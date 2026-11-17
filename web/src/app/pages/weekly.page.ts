import { DecimalPipe } from '@angular/common';
import { Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ChartConfiguration } from 'chart.js';
import { ApiService, errorMessage } from '../core/api.service';
import { WeeklyReport } from '../core/models';
import { SettingsStore } from '../core/settings.store';
import { TimerStore } from '../core/timer.store';
import { ToastService } from '../core/toast.service';
import { addDays, formatClock, formatDateShort, formatMinutes } from '../core/time';
import { ChartView, KIND_COLORS } from '../shared/chart';
import { heatmapView, hourLabel, mondayOf, percent, WEEKDAYS } from './weekly';

const EXPORTS = [
  { value: 'time-entries', label: 'Time entries' },
  { value: 'interruptions', label: 'Interruptions' },
  { value: 'calendar-events', label: 'Calendar events' },
  { value: 'daily-aggregates', label: 'Daily totals' },
];

@Component({
  selector: 'app-weekly-page',
  imports: [FormsModule, ChartView, DecimalPipe],
  templateUrl: './weekly.page.html',
})
export class WeeklyPage {
  private readonly api = inject(ApiService);
  protected readonly settings = inject(SettingsStore);
  private readonly timer = inject(TimerStore);
  private readonly toast = inject(ToastService);

  protected readonly weekStart = signal('');
  protected readonly report = signal<WeeklyReport | null>(null);
  protected readonly loading = signal(false);
  protected readonly fmt = formatMinutes;
  protected readonly fmtDate = formatDateShort;
  protected readonly hourLabel = hourLabel;
  protected readonly percent = percent;
  protected readonly exports = EXPORTS;
  protected exportDataset = 'time-entries';
  protected exportFrom = '';
  protected exportTo = '';

  protected readonly isThisWeek = computed(() => this.weekStart() === mondayOf(this.settings.today()));
  protected readonly title = computed(() => {
    const r = this.report();
    return r ? `${formatDateShort(r.weekStart)} – ${formatDateShort(r.weekEnd)}` : '';
  });
  protected readonly heatmap = computed(() => heatmapView(this.report()?.interruptions.heatmap ?? []));
  protected readonly tracked = computed(() => {
    const k = this.report()?.kinds;
    return k ? k.meetings + k.focus + k.reactive + k.untracked : 0;
  });

  protected readonly dailyChart = computed<ChartConfiguration | null>(() => {
    const r = this.report();
    if (!r) return null;
    const labels = r.days.map((d) => WEEKDAYS[(new Date(d.date + 'T00:00:00Z').getUTCDay() + 6) % 7]);
    const h = (v: number) => Math.round((v / 60) * 10) / 10;
    const ds = (label: string, key: 'meetingMinutes' | 'taskMinutes' | 'reactiveMinutes' | 'untrackedMinutes' | 'unloggedMinutes', color: string) =>
      ({ label, data: r.days.map((d) => h(d[key])), backgroundColor: color, borderRadius: 4, stack: 'day' });
    return {
      type: 'bar',
      data: {
        labels,
        datasets: [
          ds('Meetings', 'meetingMinutes', KIND_COLORS.meetings),
          ds('Focus / task', 'taskMinutes', KIND_COLORS.focus),
          ds('Reactive', 'reactiveMinutes', KIND_COLORS.reactive),
          ds('Untracked', 'untrackedMinutes', KIND_COLORS.untracked),
          ds('Unlogged', 'unloggedMinutes', KIND_COLORS.unlogged),
        ],
      },
      options: {
        plugins: { legend: { position: 'bottom', labels: { boxWidth: 12 } }, tooltip: { callbacks: { label: (c) => `${c.dataset.label}: ${c.parsed.y} h` } } },
        scales: { x: { stacked: true, grid: { display: false } }, y: { stacked: true, title: { display: true, text: 'hours' } } },
      },
    } as ChartConfiguration;
  });

  protected readonly projectChart = computed<ChartConfiguration | null>(() => {
    const r = this.report();
    if (!r || r.byProject.length === 0) return null;
    return {
      type: 'doughnut',
      data: {
        labels: r.byProject.map((p) => p.name),
        datasets: [{ data: r.byProject.map((p) => Math.round(p.minutes)), backgroundColor: r.byProject.map((p) => p.color ?? '#94a3b8'), borderWidth: 2 }],
      },
      options: {
        cutout: '62%',
        plugins: { legend: { position: 'bottom', labels: { boxWidth: 12 } }, tooltip: { callbacks: { label: (c) => `${c.label}: ${formatMinutes(Number(c.raw))}` } } },
      },
    } as ChartConfiguration;
  });

  protected readonly trendChart = computed<ChartConfiguration | null>(() => {
    const r = this.report();
    if (!r) return null;
    const h = (v: number) => Math.round((v / 60) * 10) / 10;
    const line = (label: string, key: 'meetings' | 'focus' | 'reactive' | 'untracked', color: string) =>
      ({ label, data: r.trend.map((t) => h(t[key])), borderColor: color, backgroundColor: color, tension: 0.3, pointRadius: 3, yAxisID: 'y' });
    return {
      type: 'line',
      data: {
        labels: r.trend.map((t) => formatDateShort(t.weekStart)),
        datasets: [
          line('Meetings', 'meetings', KIND_COLORS.meetings),
          line('Focus / task', 'focus', KIND_COLORS.focus),
          line('Reactive', 'reactive', KIND_COLORS.reactive),
          line('Untracked', 'untracked', KIND_COLORS.untracked),
          { type: 'bar', label: 'Interruptions', data: r.trend.map((t) => t.interruptions), backgroundColor: 'rgba(245, 158, 11, .35)', borderColor: KIND_COLORS.interruptions, borderWidth: 1, yAxisID: 'y2', borderRadius: 4 },
        ],
      },
      options: {
        plugins: { legend: { position: 'bottom', labels: { boxWidth: 12 } } },
        scales: {
          x: { grid: { display: false } },
          y: { title: { display: true, text: 'hours' }, beginAtZero: true },
          y2: { position: 'right', beginAtZero: true, grid: { display: false }, title: { display: true, text: 'interruptions' }, ticks: { precision: 0 } },
        },
      },
    } as ChartConfiguration;
  });

  constructor() {
    effect(() => {
      if (!this.weekStart() && this.settings.settings()) this.weekStart.set(mondayOf(this.settings.today()));
    });
    effect(() => {
      this.timer.version();
      const w = this.weekStart();
      if (w) untracked(() => this.load());
    });
  }

  async load(): Promise<void> {
    this.loading.set(true);
    try {
      const r = await this.api.getWeekly(this.weekStart());
      this.report.set(r);
      this.exportFrom = r.weekStart;
      this.exportTo = r.weekEnd;
    } catch (e) {
      this.toast.error(errorMessage(e));
    } finally {
      this.loading.set(false);
    }
  }

  shift(weeks: number): void {
    this.weekStart.set(addDays(this.weekStart(), 7 * weeks));
  }

  thisWeek(): void {
    this.weekStart.set(mondayOf(this.settings.today()));
  }

  clock(iso: string): string {
    return formatClock(iso, this.settings.tz());
  }

  exportHref(): string {
    return this.api.exportUrl(this.exportDataset, this.exportFrom || this.weekStart(), this.exportTo || addDays(this.weekStart(), 6));
  }
}
