import { Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiService, errorMessage } from '../core/api.service';
import { DayBreakdown, ExportKind, WeeklySummary } from '../core/models';
import { SettingsStore } from '../core/settings.store';
import { TimerStore } from '../core/timer.store';
import { ToastService } from '../core/toast.service';
import { addDays, formatClock, formatDateShort, formatMinutes } from '../core/time';

/** The stacked "where did the time go" series, in fixed order (identity never depends on rank). */
const KINDS = [
  { key: 'focusMinutes', label: 'Focus', css: 'var(--wp-kind-focus)' },
  { key: 'meetingMinutes', label: 'Meetings', css: 'var(--wp-kind-meeting)' },
  { key: 'reactiveMinutes', label: 'Reactive', css: 'var(--wp-kind-reactive)' },
  { key: 'untrackedMinutes', label: 'Untracked', css: 'var(--wp-kind-untracked)' },
  { key: 'unloggedMinutes', label: 'Unlogged', css: 'var(--sm-hatch)' },
] as const;
type KindKey = (typeof KINDS)[number]['key'];

interface Tip { x: number; y: number; title: string; lines: string[]; }

interface Tile { label: string; value: string; current: number; trend: number; icon: string; tint: string; foot: string; moreIsGood: boolean | null; }

@Component({
  selector: 'app-insights-page',
  imports: [RouterLink],
  template: `
    <div class="page-header">
      <div>
        <h1>Insights</h1>
        <p class="page-subtitle">{{ rangeLabel() }} · where the week went, compared with your last {{ trendLabel() }}.</p>
      </div>
      <div class="page-actions">
        <button type="button" class="icon-btn" (click)="shift(-7)" aria-label="Previous week"><span class="i i-chevron-left"></span></button>
        <button type="button" class="icon-btn" (click)="shift(7)" [disabled]="isThisWeek()" aria-label="Next week"><span class="i i-chevron-right"></span></button>
        @if (!isThisWeek()) { <button type="button" class="btn btn-outline-secondary" (click)="goThisWeek()">This week</button> }
        <div class="dropdown-wrap">
          <button type="button" class="btn btn-outline-secondary" (click)="exportOpen.set(!exportOpen())" [attr.aria-expanded]="exportOpen()">
            <span class="i i-inbox"></span> Export CSV
          </button>
          @if (exportOpen()) {
            <div class="dropdown-panel" role="menu">
              <div class="dropdown-label">This week</div>
              @for (k of exports; track k.kind) { <a role="menuitem" [href]="exportUrl(k.kind, 0)" download (click)="exportOpen.set(false)">{{ k.label }}</a> }
              <div class="dropdown-label">Last 30 days</div>
              @for (k of exports; track k.kind) { <a role="menuitem" [href]="exportUrl(k.kind, 30)" download (click)="exportOpen.set(false)">{{ k.label }}</a> }
            </div>
          }
        </div>
      </div>
    </div>

    @if (week(); as w) {
      <div class="row g-3 mb-4 row-cols-2 row-cols-md-3 tiles-6">
        @for (t of tiles(); track t.label) {
          <div class="col">
            <div class="card stat-card"><div class="card-body">
              <div class="stat-label">{{ t.label }}</div>
              <div class="stat-value">{{ t.value }}</div>
              <div class="stat-foot">
                @if (w.trendWeeks && delta(t) !== null) {
                  <span class="trend" [class.good]="trendTone(t) === 'good'" [class.bad]="trendTone(t) === 'bad'">
                    {{ delta(t)! > 0 ? '▲' : delta(t)! < 0 ? '▼' : '=' }} {{ deltaLabel(t) }}
                  </span>
                  vs. avg
                } @else { {{ t.foot }} }
              </div>
              <span class="stat-icon" [class]="'stat-icon ' + t.tint"><span class="i" [class]="t.icon"></span></span>
            </div></div>
          </div>
        }
      </div>

      <div class="row g-4">
        <div class="col-xl-8">
          <div class="card mb-4">
            <div class="card-header">
              <h2>Where the week went</h2>
              <div class="d-flex align-items-center gap-3 flex-wrap">
                <div class="segmented segmented-sm">
                  <button type="button" [class.active]="!showTable()" (click)="showTable.set(false)">Chart</button>
                  <button type="button" [class.active]="showTable()" (click)="showTable.set(true)">Table</button>
                </div>
              </div>
            </div>
            <div class="card-body">
              @if (showTable()) {
                <div class="table-responsive">
                  <table class="table table-sm mb-0 num-table">
                    <thead><tr><th>Day</th>@for (k of kinds; track k.key) { <th class="text-end">{{ k.label }}</th> }<th class="text-end">Interruptions</th><th class="text-end">Switches</th></tr></thead>
                    <tbody>
                      @for (d of w.days; track d.date) {
                        <tr>
                          <td>{{ dayName(d.date) }} {{ shortDate(d.date) }}</td>
                          @for (k of kinds; track k.key) { <td class="text-end num">{{ fmt(d[k.key]) }}</td> }
                          <td class="text-end num">{{ d.interruptionCount }}</td>
                          <td class="text-end num">{{ d.taskSwitches }}</td>
                        </tr>
                      }
                    </tbody>
                  </table>
                </div>
              } @else {
                <div class="week-chart" role="img" [attr.aria-label]="'Time by kind for each day of the week. Use the Table view for exact values.'">
                  <div class="week-axis">
                    @for (g of gridlines(); track g.value) { <span [style.bottom.%]="g.pct">{{ g.label }}</span> }
                  </div>
                  <div class="week-plot">
                    @for (g of gridlines(); track g.value) { <div class="week-grid" [style.bottom.%]="g.pct"></div> }
                    @for (d of w.days; track d.date) {
                      <div class="week-col" tabindex="0" [attr.aria-label]="dayAria(d)"
                           (mouseenter)="showDayTip($event, d)" (mousemove)="moveTip($event)" (mouseleave)="tip.set(null)"
                           (focus)="showDayTip($event, d)" (blur)="tip.set(null)">
                        <div class="week-stack" [style.height.%]="(dayTotal(d) / chartMax()) * 100">
                          @for (k of kinds; track k.key) {
                            @if (d[k.key] > 0) { <span class="seg" [style.background]="k.css" [style.flex-grow]="d[k.key]"></span> }
                          }
                        </div>
                        <div class="week-label" [class.today]="d.date === today()">{{ dayName(d.date) }}<small>{{ fmtShort(dayTotal(d)) }}</small></div>
                      </div>
                    }
                  </div>
                </div>
                <div class="legend justify-content-center mt-1">
                  @for (k of kinds; track k.key) { <span><span class="swatch-sm" [style.background]="k.css"></span>{{ k.label }}</span> }
                </div>
              }
            </div>
          </div>

          <div class="card mb-4">
            <div class="card-header"><h2>When interruptions happen</h2><span class="cell-sub">{{ w.interruptions.count }} this week · {{ fmt(w.interruptions.minutes) }}</span></div>
            <div class="card-body">
              @if (w.interruptions.count) {
                <div class="heatmap" role="img" aria-label="Interruptions by weekday and hour">
                  <div class="heat-row heat-head"><span></span>@for (h of heatHours(); track h) { <span>{{ hourLabel(h) }}</span> }</div>
                  @for (row of heatRows(); track row.day) {
                    <div class="heat-row">
                      <span class="heat-day">{{ row.day }}</span>
                      @for (c of row.cells; track c.hour) {
                        <span class="heat-cell" [attr.data-level]="c.level" tabindex="-1"
                              (mouseenter)="showTip($event, row.day + ' ' + hourLabel(c.hour) + '–' + hourLabel(c.hour + 1), [c.count + ' interruption' + (c.count === 1 ? '' : 's')])"
                              (mousemove)="moveTip($event)" (mouseleave)="tip.set(null)"></span>
                      }
                    </div>
                  }
                  <div class="heat-scale"><span>Fewer</span>@for (l of [1, 2, 3, 4, 5]; track l) { <span class="heat-cell" [attr.data-level]="l"></span> }<span>More</span></div>
                </div>
                <div class="row g-4 mt-1">
                  <div class="col-md-6">
                    <div class="nav-section-inline">By source</div>
                    @for (s of w.interruptions.bySource; track s.key) {
                      <div class="totals-row">
                        <span class="name">{{ s.label }}</span><span class="val">{{ s.count }}× · {{ fmt(s.minutes) }}</span>
                        <div class="bar"><span [style.width.%]="(s.count / maxSource()) * 100" style="--pc: var(--sm-warning)"></span></div>
                      </div>
                    }
                  </div>
                  <div class="col-md-6">
                    <div class="nav-section-inline">By person</div>
                    @for (p of w.interruptions.byPerson.slice(0, 6); track p.key) {
                      <div class="totals-row">
                        <span class="name"><span class="i i-user text-muted"></span>{{ p.label }}</span><span class="val">{{ p.count }}× · {{ fmt(p.minutes) }}</span>
                        <div class="bar"><span [style.width.%]="(p.count / maxPerson()) * 100" style="--pc: var(--sm-warning)"></span></div>
                      </div>
                    } @empty { <p class="text-muted small">No names logged. Add a person in the interruption form to see who to batch.</p> }
                    @if (w.interruptions.couldWaitCount || w.interruptions.urgentCount) {
                      <p class="small text-muted mb-0 mt-2">{{ w.interruptions.couldWaitCount }} could have waited · {{ w.interruptions.urgentCount }} urgent</p>
                    }
                  </div>
                </div>
              } @else {
                <div class="empty py-4"><span class="i i-zap"></span><h3>No interruptions logged</h3><p class="mb-0">Press <kbd>i</kbd> when someone pulls you off a task.</p></div>
              }
            </div>
          </div>
        </div>

        <div class="col-xl-4">
          <div class="card mb-4">
            <div class="card-header"><h2>By project</h2></div>
            <div class="card-body">
              @for (p of w.byProject; track p.name) {
                <div class="totals-row">
                  <span class="name"><span class="project-dot" [style.--pc]="p.color"></span>{{ p.name }}</span>
                  <span class="val">{{ fmt(p.minutes) }}</span>
                  <div class="bar"><span [style.width.%]="(p.minutes / maxProject()) * 100" [style.--pc]="p.color"></span></div>
                </div>
              } @empty { <p class="text-muted mb-0">No project time this week.</p> }
            </div>
          </div>

          <div class="card mb-4">
            <div class="card-header"><h2>Planned vs. actual</h2></div>
            <div class="card-body">
              @if (w.plannedCount) {
                <div class="d-flex align-items-baseline gap-2 mb-2">
                  <span class="stat-value">{{ w.plannedDoneCount }}/{{ w.plannedCount }}</span><span class="text-muted">planned tasks done</span>
                </div>
              }
              <div class="totals-row">
                <span class="name"><span class="project-dot" style="--pc: var(--wp-kind-focus)"></span>On planned tasks</span>
                <span class="val">{{ fmt(w.plannedMinutes) }}</span>
                <div class="bar"><span [style.width.%]="planPct(w.plannedMinutes)" style="--pc: var(--wp-kind-focus)"></span></div>
              </div>
              <div class="totals-row mb-0">
                <span class="name"><span class="project-dot" style="--pc: var(--wp-unplanned)"></span>On unplanned tasks</span>
                <span class="val">{{ fmt(w.unplannedTaskMinutes) }}</span>
                <div class="bar"><span [style.width.%]="planPct(w.unplannedTaskMinutes)" style="--pc: var(--wp-unplanned)"></span></div>
              </div>
            </div>
          </div>

          <div class="card mb-4">
            <div class="card-header"><h2>Meetings by type</h2><span class="cell-sub">{{ meetingCount() }} meetings</span></div>
            <div class="card-body">
              @for (m of w.byMeetingType; track m.key) {
                <div class="totals-row">
                  <span class="name">{{ m.label }}</span><span class="val">{{ fmt(m.minutes) }}</span>
                  <div class="bar"><span [style.width.%]="(m.minutes / maxMeetingType()) * 100" style="--pc: var(--wp-kind-meeting)"></span></div>
                </div>
              } @empty {
                <p class="text-muted mb-0">No meetings. Connect your calendar in <a routerLink="/settings">Projects &amp; settings</a> to count them automatically.</p>
              }
            </div>
          </div>

          <div class="card">
            <div class="card-header"><h2>Focus</h2></div>
            <div class="card-body">
              @if (w.longestFocusBlock; as b) {
                <div class="cell-sub">Longest focus block</div>
                <div class="d-flex align-items-baseline gap-2"><span class="stat-value">{{ fmt(b.minutes) }}</span></div>
                <div class="small text-muted">{{ b.taskTitle ?? 'Task' }} · {{ dayName(b.date) }} {{ clock(b.start) }}–{{ clock(b.end) }}</div>
              } @else {
                <p class="text-muted mb-0">No focus block this week. A focus block is 25+ minutes on one task timer with no interruption.</p>
              }
              @if (w.totals.plannedFocusMinutes) {
                <div class="small mt-2">{{ fmt(w.totals.plannedFocusMinutes) }} of Focus Time booked in the calendar.</div>
              }
            </div>
          </div>
        </div>
      </div>
    }

    @if (tip(); as t) {
      <div class="chart-tip" [style.left.px]="t.x" [style.top.px]="t.y" role="tooltip">
        <strong>{{ t.title }}</strong>
        @for (l of t.lines; track $index) { <div>{{ l }}</div> }
      </div>
    }
  `,
})
export class InsightsPage {
  private readonly api = inject(ApiService);
  private readonly settings = inject(SettingsStore);
  private readonly timer = inject(TimerStore);
  private readonly toast = inject(ToastService);

  protected readonly kinds = KINDS;
  protected readonly fmt = formatMinutes;
  protected readonly shortDate = formatDateShort;
  protected readonly exports: { kind: ExportKind; label: string }[] = [
    { kind: 'time-entries', label: 'Time entries' },
    { kind: 'interruptions', label: 'Interruptions' },
    { kind: 'meetings', label: 'Meetings' },
    { kind: 'daily', label: 'Daily totals' },
  ];

  protected readonly anchor = signal('');
  protected readonly week = signal<WeeklySummary | null>(null);
  protected readonly showTable = signal(false);
  protected readonly exportOpen = signal(false);
  protected readonly tip = signal<Tip | null>(null);

  protected readonly today = computed(() => (this.settings.settings() ? this.settings.today() : ''));
  protected readonly isThisWeek = computed(() => {
    const w = this.week();
    const t = this.today();
    return !!w && t >= w.weekStart && t <= w.weekEnd;
  });
  protected readonly rangeLabel = computed(() => {
    const w = this.week();
    return w ? `${formatDateShort(w.weekStart)} – ${formatDateShort(w.weekEnd)}` : '';
  });
  protected readonly trendLabel = computed(() => {
    const n = this.week()?.trendWeeks ?? 0;
    return n === 0 ? 'weeks (no history yet)' : n === 1 ? 'week' : `${n} weeks`;
  });

  protected readonly chartMax = computed(() => {
    const max = Math.max(60, ...(this.week()?.days ?? []).map((d) => this.dayTotal(d)));
    return Math.ceil(max / 120) * 120;
  });
  protected readonly gridlines = computed(() => {
    const max = this.chartMax();
    const step = max > 720 ? 240 : 120;
    const lines = [];
    for (let v = 0; v <= max; v += step) lines.push({ value: v, pct: (v / max) * 100, label: `${v / 60}h` });
    return lines;
  });

  protected readonly tiles = computed<Tile[]>(() => {
    const w = this.week();
    if (!w) return [];
    const t = w.totals, a = w.trendAverage;
    const tile = (label: string, cur: number, avg: number, icon: string, tint: string, foot: string, moreIsGood: boolean | null, value?: string): Tile =>
      ({ label, value: value ?? formatMinutes(cur), current: cur, trend: avg, icon, tint, foot, moreIsGood });
    return [
      tile('Focus', t.focusMinutes, a.focusMinutes, 'i-target', '', `${t.focusBlockCount} focus block${t.focusBlockCount === 1 ? '' : 's'}`, true),
      tile('Meetings', t.meetingMinutes, a.meetingMinutes, 'i-users', 'tint-pink', 'From your calendar', false),
      tile('Reactive', t.reactiveMinutes, a.reactiveMinutes, 'i-inbox', 'tint-cyan', 'Unplanned asks', false),
      tile('Interruptions', t.interruptionCount, a.interruptionCount, 'i-zap', 'tint-warning', formatMinutes(t.interruptionMinutes), false, String(t.interruptionCount)),
      tile('Switches', t.taskSwitches, a.taskSwitches, 'i-refresh', 'tint-muted', 'Timer changes', false, String(t.taskSwitches)),
      tile('Unlogged', t.unloggedMinutes, a.unloggedMinutes, 'i-hourglass', 'tint-muted', 'Gaps in work hours', false),
    ];
  });

  protected readonly heatHours = computed(() => {
    const s = this.settings.settings();
    const start = s ? Number(s.workdayStart.slice(0, 2)) : 8;
    const end = s ? Number(s.workdayEnd.slice(0, 2)) + 1 : 18;
    const heat = this.week()?.interruptions.heatmap ?? [];
    let lo = start, hi = end;
    heat.forEach((row) => row.forEach((c, h) => { if (c) { lo = Math.min(lo, h); hi = Math.max(hi, h + 1); } }));
    return Array.from({ length: hi - lo }, (_, i) => lo + i);
  });
  protected readonly heatRows = computed(() => {
    const heat = this.week()?.interruptions.heatmap ?? [];
    const max = Math.max(1, ...heat.flat());
    const names = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];
    return heat
      .map((row, i) => ({
        day: names[i],
        total: row.reduce((x, y) => x + y, 0),
        cells: this.heatHours().map((h) => ({ hour: h, count: row[h] ?? 0, level: row[h] ? Math.max(1, Math.ceil((row[h] / max) * 5)) : 0 })),
      }))
      // Weekends only when something happened.
      .filter((r, i) => i < 5 || r.total > 0);
  });

  protected readonly maxProject = computed(() => Math.max(1, ...(this.week()?.byProject ?? []).map((p) => p.minutes)));
  protected readonly maxSource = computed(() => Math.max(1, ...(this.week()?.interruptions.bySource ?? []).map((p) => p.count)));
  protected readonly maxPerson = computed(() => Math.max(1, ...(this.week()?.interruptions.byPerson ?? []).map((p) => p.count)));
  protected readonly maxMeetingType = computed(() => Math.max(1, ...(this.week()?.byMeetingType ?? []).map((p) => p.minutes)));
  protected readonly meetingCount = computed(() => (this.week()?.byMeetingType ?? []).reduce((n, m) => n + m.count, 0));

  constructor() {
    effect(() => {
      if (!this.anchor() && this.settings.settings()) this.anchor.set(this.settings.today());
    });
    effect(() => {
      this.timer.version();
      const a = this.anchor();
      if (a) untracked(() => this.load());
    });
  }

  async load(): Promise<void> {
    try {
      this.week.set(await this.api.getWeekly(this.anchor()));
    } catch (e) {
      this.toast.error(errorMessage(e));
    }
  }

  shift(days: number): void {
    this.anchor.set(addDays(this.anchor(), days));
  }

  goThisWeek(): void {
    this.anchor.set(this.settings.today());
  }

  exportUrl(kind: ExportKind, lastDays: number): string {
    const w = this.week();
    if (!w) return '#';
    if (lastDays) {
      const to = this.today();
      return this.api.exportUrl(kind, addDays(to, -(lastDays - 1)), to);
    }
    return this.api.exportUrl(kind, w.weekStart, w.weekEnd);
  }

  dayTotal(d: DayBreakdown): number {
    return KINDS.reduce((n, k) => n + d[k.key as KindKey], 0);
  }

  dayName(date: string): string {
    return new Intl.DateTimeFormat('en-US', { weekday: 'short', timeZone: 'UTC' }).format(new Date(date + 'T12:00:00Z'));
  }

  dayAria(d: DayBreakdown): string {
    return `${this.dayName(d.date)}: ` + KINDS.map((k) => `${k.label} ${formatMinutes(d[k.key as KindKey])}`).join(', ');
  }

  fmtShort(minutes: number): string {
    return minutes ? `${(minutes / 60).toFixed(minutes >= 600 ? 0 : 1)}h` : '';
  }

  hourLabel(h: number): string {
    const hh = h % 24;
    return hh === 0 ? '12a' : hh < 12 ? `${hh}a` : hh === 12 ? '12p' : `${hh - 12}p`;
  }

  clock(iso: string): string {
    return formatClock(iso, this.settings.tz());
  }

  planPct(v: number): number {
    const w = this.week();
    return w ? (v / Math.max(1, w.plannedMinutes, w.unplannedTaskMinutes)) * 100 : 0;
  }

  delta(t: Tile): number | null {
    return Number.isFinite(t.current - t.trend) ? Math.round(t.current - t.trend) : null;
  }

  deltaLabel(t: Tile): string {
    const d = Math.abs(this.delta(t) ?? 0);
    return t.value.includes('m') ? formatMinutes(d) : String(d);
  }

  trendTone(t: Tile): 'good' | 'bad' | null {
    const d = this.delta(t) ?? 0;
    if (t.moreIsGood === null || d === 0) return null;
    return (d > 0) === t.moreIsGood ? 'good' : 'bad';
  }

  showDayTip(e: Event, d: DayBreakdown): void {
    const lines = KINDS.filter((k) => d[k.key as KindKey] > 0).map((k) => `${k.label}: ${formatMinutes(d[k.key as KindKey])}`);
    if (d.interruptionCount) lines.push(`Interruptions: ${d.interruptionCount}`);
    if (d.focusBlockCount) lines.push(`Focus blocks: ${d.focusBlockCount} (longest ${formatMinutes(d.longestFocusBlockMinutes)})`);
    this.showTip(e, `${this.dayName(d.date)} ${formatDateShort(d.date)}`, lines.length ? lines : ['Nothing logged']);
  }

  showTip(e: Event, title: string, lines: string[]): void {
    const pos = this.position(e);
    this.tip.set({ ...pos, title, lines });
  }

  moveTip(e: MouseEvent): void {
    const t = this.tip();
    if (t) this.tip.set({ ...t, ...this.position(e) });
  }

  private position(e: Event): { x: number; y: number } {
    if (e instanceof MouseEvent) return { x: Math.min(e.clientX + 14, window.innerWidth - 240), y: e.clientY + 14 };
    const r = (e.target as HTMLElement).getBoundingClientRect();
    return { x: Math.min(r.left, window.innerWidth - 240), y: r.top + 12 };
  }
}

