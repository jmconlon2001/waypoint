import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { ApiService, errorMessage } from '../core/api.service';
import { CalendarSource, CalendarStatus, MeetingRule, Person, Project } from '../core/models';
import { ReminderService } from '../core/reminder.service';
import { addDays, formatClock } from '../core/time';
import { SettingsStore } from '../core/settings.store';
import { ThemeService } from '../core/theme.service';
import { TimerStore } from '../core/timer.store';
import { ToastService } from '../core/toast.service';

interface ProjectDraft { id: number | null; name: string; color: string; active: boolean; }

@Component({
  selector: 'app-settings-page',
  imports: [FormsModule],
  template: `
    <div class="page-header">
      <div>
        <h1>Projects &amp; settings</h1>
        <p class="page-subtitle">Projects, work hours and the people who interrupt you.</p>
      </div>
    </div>

    <div class="row g-4">
      <div class="col-xl-7">
        <div class="card card-flush mb-4">
          <div class="card-header">
            <h2>Projects</h2>
            <button type="button" class="btn btn-sm btn-primary" (click)="newProject()"><span class="i i-plus"></span> Add project</button>
          </div>
          <div class="card-body">
            @if (draft(); as d) {
              <form class="p-3 border-bottom" (ngSubmit)="saveProject()">
                <div class="row g-3 align-items-end">
                  <div class="col-md-5">
                    <label class="form-label" for="p-name">Name</label>
                    <input id="p-name" name="name" class="form-control" [(ngModel)]="d.name" required maxlength="100" autofocus />
                  </div>
                  <div class="col-md-7">
                    <div class="form-label">Color</div>
                    <div class="swatches">
                      @for (c of settings.palette(); track c) {
                        <button type="button" class="swatch" [class.selected]="d.color === c" [style.--pc]="c" (click)="d.color = c" [attr.aria-label]="'Color ' + c"></button>
                      }
                    </div>
                  </div>
                </div>
                <div class="d-flex gap-2 mt-3 align-items-center">
                  <div class="form-check form-switch m-0">
                    <input class="form-check-input" type="checkbox" id="p-active" name="active" [(ngModel)]="d.active" />
                    <label class="form-check-label" for="p-active">Active</label>
                  </div>
                  <span class="flex-grow-1"></span>
                  <button type="button" class="btn btn-sm btn-outline-secondary" (click)="draft.set(null)">Cancel</button>
                  <button type="submit" class="btn btn-sm btn-primary" [disabled]="!d.name.trim()">Save</button>
                </div>
              </form>
            }
            <table class="table">
              <thead><tr><th>Project</th><th>Status</th><th></th></tr></thead>
              <tbody>
                @for (p of settings.allProjects(); track p.id) {
                  <tr>
                    <td><span class="project-pill" [style.--pc]="p.color"><span class="project-dot"></span>{{ p.name }}</span></td>
                    <td>@if (p.active) { <span class="pill pill-success">Active</span> } @else { <span class="pill pill-muted">Archived</span> }</td>
                    <td class="td-actions">
                      <button type="button" class="icon-btn icon-btn-sm" (click)="editProject(p)" title="Edit"><span class="i i-edit"></span></button>
                      <button type="button" class="icon-btn icon-btn-sm danger" (click)="deleteProject(p)" title="Delete (archives if it has tasks or time)"><span class="i i-trash"></span></button>
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        </div>

        <div class="card mb-4">
          <div class="card-header">
            <h2><span class="i i-calendar"></span> Calendar</h2>
            @if (calendar(); as c) { <span class="cell-sub">{{ c.eventCount }} events · syncs every {{ c.syncIntervalMinutes }} min</span> }
          </div>
          <div class="card-body">
            <div class="form-label">Source</div>
            <div class="segmented mb-3">
              <button type="button" [class.active]="calendarSource === 'None'" (click)="setSource('None')">Off</button>
              <button type="button" [class.active]="calendarSource === 'Google'" (click)="setSource('Google')">Google Calendar</button>
              <button type="button" [class.active]="calendarSource === 'ICal'" (click)="setSource('ICal')">iCal URL</button>
            </div>
            @if (calendar(); as c) {
              @if (calendarSource === 'Google') {
                @if (!c.googleConfigured) {
                  <div class="alert alert-warning">Set <code>GOOGLE_CLIENT_ID</code> and <code>GOOGLE_CLIENT_SECRET</code> in <code>deploy/.env</code> (a "Desktop app" OAuth client) and restart. See the README.</div>
                } @else if (c.googleConnected) {
                  <div class="d-flex align-items-center gap-2 flex-wrap mb-3">
                    <span class="pill pill-success"><span class="i i-check"></span> Connected</span>
                    <span class="cell-main">{{ c.googleAccount ?? 'Google account' }}</span>
                    <span class="flex-grow-1"></span>
                    <button type="button" class="btn btn-sm btn-ghost text-danger" (click)="disconnectGoogle()">Disconnect</button>
                  </div>
                } @else {
                  <a class="btn btn-primary mb-3" [href]="googleConnectUrl"><span class="i i-link"></span> Connect Google Calendar</a>
                  <p class="form-text mt-0">Read-only access (calendar.readonly). Your Workspace admin may need to allow the app.</p>
                }
              }
              @if (calendarSource === 'ICal') {
                @if (c.iCalConfigured) {
                  <p class="mb-3"><span class="pill pill-success"><span class="i i-check"></span> iCal URL configured</span></p>
                } @else {
                  <div class="alert alert-warning">Set <code>CALENDAR_ICAL_URL</code> in <code>deploy/.env</code> to your calendar's "secret address in iCal format" and restart.</div>
                }
              }
              @if (calendarSource !== 'None') {
                <div class="d-flex align-items-center gap-2 flex-wrap mb-3">
                  <button type="button" class="btn btn-outline-secondary btn-sm" (click)="syncNow()" [disabled]="syncing()"><span class="i i-refresh"></span> {{ syncing() ? 'Syncing…' : 'Sync now' }}</button>
                  <span class="cell-sub">Last sync: {{ c.lastSyncAt ? clock(c.lastSyncAt) : 'never' }}</span>
                </div>
                @if (c.lastSyncError) { <div class="alert alert-danger py-2">{{ c.lastSyncError }}</div> }
              }
            }
            <div class="row g-3">
              <div class="col-sm-5">
                <label class="form-label" for="c-max">Longest counted meeting (min)</label>
                <input id="c-max" type="number" min="0" max="1440" class="form-control mono-input" [(ngModel)]="maxMeetingMinutes" />
                <div class="form-text">Longer events and all-day events show as context only. 0 = no limit.</div>
              </div>
              <div class="col-sm-7">
                <label class="form-label" for="c-dom">Internal e-mail domains</label>
                <input id="c-dom" class="form-control" [(ngModel)]="internalDomains" placeholder="cfs.canon.com, canon.com" />
                <div class="form-text">Any other attendee domain makes a meeting "External". Subdomains count as internal.</div>
              </div>
            </div>
            <div class="mt-3 text-end"><button type="button" class="btn btn-primary btn-sm" (click)="saveSettings()">Save calendar settings</button></div>
          </div>
        </div>

        <div class="card card-flush mb-4">
          <div class="card-header">
            <h2>Meeting rules</h2>
            <span class="cell-sub">title pattern → project, first match wins</span>
          </div>
          <div class="card-body">
            <form class="p-3 border-bottom d-flex gap-2 flex-wrap align-items-end" (ngSubmit)="addRule()">
              <div class="flex-grow-1">
                <label class="form-label" for="r-pattern">Title pattern (regex, case-insensitive)</label>
                <input id="r-pattern" name="pattern" class="form-control mono-input" [(ngModel)]="rulePattern" placeholder="^SOX|walkthrough" />
              </div>
              <div>
                <label class="form-label" for="r-project">Project</label>
                <select id="r-project" name="project" class="form-select" [(ngModel)]="ruleProject">
                  @for (p of settings.projects(); track p.id) { <option [ngValue]="p.id">{{ p.name }}</option> }
                </select>
              </div>
              <button type="submit" class="btn btn-primary" [disabled]="!rulePattern.trim() || ruleProject == null"><span class="i i-plus"></span> Add</button>
            </form>
            <table class="table">
              <tbody>
                @for (r of rules(); track r.id; let first = $first; let last = $last) {
                  <tr [class.text-muted]="!r.active">
                    <td><code>{{ r.pattern }}</code></td>
                    <td><span class="project-pill" [style.--pc]="r.projectColor"><span class="project-dot"></span>{{ r.projectName }}</span></td>
                    <td class="td-actions">
                      <button type="button" class="icon-btn icon-btn-sm" (click)="moveRule(r, -1)" [disabled]="first" title="Move up"><span class="i i-chevron-left" style="transform: rotate(90deg)"></span></button>
                      <button type="button" class="icon-btn icon-btn-sm" (click)="moveRule(r, 1)" [disabled]="last" title="Move down"><span class="i i-chevron-right" style="transform: rotate(90deg)"></span></button>
                      <button type="button" class="icon-btn icon-btn-sm" (click)="toggleRule(r)" [title]="r.active ? 'Disable' : 'Enable'"><span class="i" [class.i-check]="!r.active" [class.i-x]="r.active"></span></button>
                      <button type="button" class="icon-btn icon-btn-sm danger" (click)="deleteRule(r)" title="Delete"><span class="i i-trash"></span></button>
                    </td>
                  </tr>
                } @empty {
                  <tr><td class="text-muted">No rules yet. Example: <code>origination|AG failover</code> → Origination.</td></tr>
                }
              </tbody>
            </table>
            <div class="p-3 border-top d-flex gap-2 align-items-center flex-wrap">
              <input class="form-control form-control-sm flex-grow-1" style="max-width: 320px" [(ngModel)]="ruleTest" (ngModelChange)="testRule()" placeholder="Try a meeting title…" aria-label="Test a meeting title" />
              @if (ruleTest.trim()) { <span class="cell-sub">→ {{ ruleTestResult() ?? 'no project' }}</span> }
            </div>
          </div>
        </div>

        <div class="card card-flush">
          <div class="card-header"><h2>People</h2><span class="cell-sub">Added automatically from interruptions</span></div>
          <div class="card-body">
            <table class="table">
              <tbody>
                @for (p of people(); track p.id) {
                  <tr>
                    <td>
                      @if (renaming() === p.id) {
                        <input class="form-control form-control-sm" [(ngModel)]="renameText" (keydown.enter)="rename(p)" (keydown.escape)="renaming.set(null)" aria-label="Name" />
                      } @else {
                        <span class="cell-main"><span class="i i-user text-muted me-1"></span>{{ p.name }}</span>
                      }
                    </td>
                    <td class="td-actions">
                      @if (renaming() === p.id) {
                        <button type="button" class="btn btn-sm btn-primary" (click)="rename(p)">Save</button>
                      } @else {
                        <button type="button" class="icon-btn icon-btn-sm" (click)="startRename(p)" title="Rename"><span class="i i-edit"></span></button>
                        <button type="button" class="icon-btn icon-btn-sm danger" (click)="deletePerson(p)" title="Delete"><span class="i i-trash"></span></button>
                      }
                    </td>
                  </tr>
                } @empty {
                  <tr><td><p class="text-muted m-0">Nobody yet. Names you type in the interruption form show up here.</p></td></tr>
                }
              </tbody>
            </table>
          </div>
        </div>
      </div>

      <div class="col-xl-5">
        <form class="card mb-4" (ngSubmit)="saveSettings()">
          <div class="card-header"><h2>Work day</h2></div>
          <div class="card-body">
            <div class="row g-3">
              <div class="col-6">
                <label class="form-label" for="s-start">Work starts</label>
                <input id="s-start" name="start" type="time" class="form-control mono-input" [(ngModel)]="workdayStart" required />
              </div>
              <div class="col-6">
                <label class="form-label" for="s-end">Work ends</label>
                <input id="s-end" name="end" type="time" class="form-control mono-input" [(ngModel)]="workdayEnd" required />
              </div>
              <div class="col-12 form-text mt-1">Gaps in the daily review are found within these hours.</div>
              <div class="col-sm-6">
                <label class="form-label" for="s-warn">Long-timer warning (minutes)</label>
                <input id="s-warn" name="warn" type="number" min="0" max="1440" class="form-control mono-input" [(ngModel)]="timerWarningMinutes" />
                <div class="form-text">0 turns it off. Default 180.</div>
              </div>
              <div class="col-sm-6">
                <label class="form-label" for="s-tz">Time zone</label>
                <input id="s-tz" name="tz" class="form-control" [(ngModel)]="timeZone" list="tz-list" />
                <datalist id="tz-list">
                  @for (z of zones; track z) { <option [value]="z"></option> }
                </datalist>
              </div>
            </div>
          </div>
          <div class="sm-modal-footer" style="border-radius: 0 0 var(--sm-radius) var(--sm-radius)">
            <button type="submit" class="btn btn-primary">Save settings</button>
          </div>
        </form>

        <div class="card">
          <div class="card-header"><h2>Appearance</h2></div>
          <div class="card-body d-flex gap-2 flex-wrap align-items-center">
            <button type="button" class="btn btn-outline-secondary" (click)="theme.toggle()">
              <span class="i i-moon theme-dark-hide"></span><span class="i i-sun theme-light-hide"></span>
              Switch to {{ theme.theme() === 'dark' ? 'light' : 'dark' }}
            </button>
            <button type="button" class="btn btn-ghost" (click)="theme.followSystem()" [disabled]="!theme.saved()">Follow system</button>
            <span class="cell-sub">{{ theme.saved() ? 'Saved choice: ' + theme.saved() : 'Following your OS setting' }}</span>
          </div>
        </div>

        <div class="card mt-4">
          <div class="card-header"><h2><span class="i i-bell"></span> Review reminder</h2></div>
          <div class="card-body">
            <div class="d-flex gap-3 align-items-end flex-wrap">
              <div class="form-check form-switch m-0">
                <input class="form-check-input" type="checkbox" id="rem-on" [(ngModel)]="reminderEnabled" (ngModelChange)="saveSettings()" />
                <label class="form-check-label" for="rem-on">Remind me to review the day</label>
              </div>
              <div>
                <label class="form-label" for="rem-time">At</label>
                <input id="rem-time" type="time" class="form-control mono-input" [(ngModel)]="reminderTime" (change)="saveSettings()" />
              </div>
            </div>
            <div class="d-flex gap-2 align-items-center flex-wrap mt-3">
              @switch (notifyPermission()) {
                @case ('granted') { <span class="pill pill-success">Notifications allowed</span> }
                @case ('denied') { <span class="pill pill-danger">Blocked in the browser</span> }
                @case ('unsupported') { <span class="pill pill-muted">Not supported here</span> }
                @default { <button type="button" class="btn btn-sm btn-outline-secondary" (click)="allowNotifications()">Allow notifications</button> }
              }
              <button type="button" class="btn btn-sm btn-ghost" (click)="testNotification()" [disabled]="notifyPermission() !== 'granted'">Send a test</button>
            </div>
            <p class="form-text mb-0 mt-2">Works while Waypoint is open in a tab or installed as an app (no server push).</p>
          </div>
        </div>

        <div class="card mt-4">
          <div class="card-header"><h2>Daily totals</h2></div>
          <div class="card-body">
            <p class="cell-sub mt-0">Daily totals are stored nightly for trends and export. Recompute after fixing old entries or changing meeting settings.</p>
            <div class="d-flex gap-2 align-items-end flex-wrap">
              <div><label class="form-label" for="rc-from">From</label><input id="rc-from" type="date" class="form-control" [(ngModel)]="recomputeFrom" /></div>
              <div><label class="form-label" for="rc-to">To</label><input id="rc-to" type="date" class="form-control" [(ngModel)]="recomputeTo" /></div>
              <button type="button" class="btn btn-outline-secondary" (click)="recompute()" [disabled]="recomputing()"><span class="i i-refresh"></span> Recompute</button>
            </div>
          </div>
        </div>
      </div>
    </div>
  `,
})
export class SettingsPage implements OnInit {
  private readonly api = inject(ApiService);
  protected readonly settings = inject(SettingsStore);
  protected readonly theme = inject(ThemeService);
  private readonly timer = inject(TimerStore);
  private readonly toast = inject(ToastService);
  private readonly reminder = inject(ReminderService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  protected readonly calendar = signal<CalendarStatus | null>(null);
  protected readonly rules = signal<MeetingRule[]>([]);
  protected readonly syncing = signal(false);
  protected readonly recomputing = signal(false);
  protected readonly ruleTestResult = signal<string | null>(null);
  protected readonly notifyPermission = signal<NotificationPermission | 'unsupported'>(this.reminder.permission);
  protected calendarSource: CalendarSource = 'None';
  protected maxMeetingMinutes = 120;
  protected internalDomains = '';
  protected reminderEnabled = false;
  protected reminderTime = '17:15';
  protected rulePattern = '';
  protected ruleProject: number | null = null;
  protected ruleTest = '';
  protected recomputeFrom = '';
  protected recomputeTo = '';
  protected readonly googleConnectUrl = this.api.googleConnectUrl(window.location.origin);
  private ruleTestTimer: ReturnType<typeof setTimeout> | undefined;

  protected readonly draft = signal<ProjectDraft | null>(null);
  protected readonly people = signal<Person[]>([]);
  protected readonly renaming = signal<number | null>(null);
  protected renameText = '';
  protected workdayStart = '08:45';
  protected workdayEnd = '17:30';
  protected timerWarningMinutes = 180;
  protected timeZone = 'America/New_York';
  protected readonly zones = (Intl as unknown as { supportedValuesOf?: (k: string) => string[] }).supportedValuesOf?.('timeZone')
    ?? ['America/New_York', 'America/Chicago', 'America/Denver', 'America/Los_Angeles', 'Asia/Tokyo', 'Europe/London', 'UTC'];

  async ngOnInit(): Promise<void> {
    try {
      await this.settings.load();
      const s = this.settings.settings();
      if (s) {
        this.workdayStart = s.workdayStart;
        this.workdayEnd = s.workdayEnd;
        this.timerWarningMinutes = s.timerWarningMinutes;
        this.timeZone = s.timeZone;
        this.calendarSource = s.calendarSource;
        this.maxMeetingMinutes = s.maxMeetingMinutes;
        this.internalDomains = s.internalDomains;
        this.reminderEnabled = s.reviewReminderEnabled;
        this.reminderTime = s.reviewReminderTime;
      }
      this.ruleProject = this.settings.projects()[0]?.id ?? null;
      this.recomputeTo = addDays(this.settings.today(), -1);
      this.recomputeFrom = addDays(this.recomputeTo, -29);
      const [people, calendar, rules] = await Promise.all([this.api.searchPeople(''), this.api.getCalendarStatus(), this.api.getMeetingRules()]);
      this.people.set(people);
      this.calendar.set(calendar);
      this.rules.set(rules);
      this.handleOAuthReturn();
    } catch (e) {
      this.toast.error(errorMessage(e));
    }
  }

  newProject(): void {
    const palette = this.settings.palette();
    const used = new Set(this.settings.allProjects().map((p) => p.color));
    this.draft.set({ id: null, name: '', color: palette.find((c) => !used.has(c)) ?? palette[0] ?? '#64748b', active: true });
  }

  editProject(p: Project): void {
    this.draft.set({ id: p.id, name: p.name, color: p.color, active: p.active });
  }

  async saveProject(): Promise<void> {
    const d = this.draft();
    if (!d) return;
    try {
      const body = { name: d.name.trim(), color: d.color, active: d.active };
      if (d.id == null) await this.api.createProject(body);
      else await this.api.updateProject(d.id, body);
      this.draft.set(null);
      await this.settings.refreshProjects();
      this.timer.bump();
      this.toast.show('Project saved');
    } catch (e) {
      this.toast.error(errorMessage(e));
    }
  }

  async deleteProject(p: Project): Promise<void> {
    if (!confirm(`Delete "${p.name}"? If it has tasks or time it will be archived instead.`)) return;
    try {
      const archived = await this.api.deleteProject(p.id);
      await this.settings.refreshProjects();
      this.timer.bump();
      this.toast.show(archived ? `"${p.name}" has history, so it was archived` : 'Project deleted');
    } catch (e) {
      this.toast.error(errorMessage(e));
    }
  }

  async saveSettings(): Promise<void> {
    try {
      const s = await this.api.saveSettings({
        workdayStart: this.workdayStart, workdayEnd: this.workdayEnd,
        timerWarningMinutes: Number(this.timerWarningMinutes) || 0, timeZone: this.timeZone.trim(),
        calendarSource: this.calendarSource, maxMeetingMinutes: Number(this.maxMeetingMinutes) || 0,
        internalDomains: this.internalDomains, reviewReminderEnabled: this.reminderEnabled, reviewReminderTime: this.reminderTime,
      });
      this.internalDomains = s.internalDomains;
      this.calendar.set(await this.api.getCalendarStatus());
      this.settings.settings.set(s);
      await this.timer.refresh(true);
      this.timer.bump();
      this.toast.show('Settings saved');
    } catch (e) {
      this.toast.error(errorMessage(e));
    }
  }

  startRename(p: Person): void {
    this.renameText = p.name;
    this.renaming.set(p.id);
  }

  async rename(p: Person): Promise<void> {
    try {
      await this.api.renamePerson(p.id, this.renameText);
      this.renaming.set(null);
      this.people.set(await this.api.searchPeople(''));
    } catch (e) {
      this.toast.error(errorMessage(e));
    }
  }

  async deletePerson(p: Person): Promise<void> {
    if (!confirm(`Delete ${p.name}? Their interruptions are kept without a name.`)) return;
    try {
      await this.api.deletePerson(p.id);
      this.people.set(await this.api.searchPeople(''));
    } catch (e) {
      this.toast.error(errorMessage(e));
    }
  }

  // ---- Calendar ----

  private handleOAuthReturn(): void {
    const q = this.route.snapshot.queryParamMap;
    const status = q.get('calendar');
    if (!status) return;
    if (status === 'connected') this.toast.show('Google Calendar connected');
    else this.toast.error(q.get('message') ?? 'Google sign-in failed');
    this.router.navigate([], { queryParams: {}, replaceUrl: true });
  }

  async setSource(source: CalendarSource): Promise<void> {
    this.calendarSource = source;
    await this.saveSettings();
  }

  async syncNow(): Promise<void> {
    this.syncing.set(true);
    try {
      const r = await this.api.syncCalendar();
      this.toast.show(`Calendar synced: ${r.added} new, ${r.updated} updated, ${r.removed} removed`);
      this.timer.bump();
    } catch (e) {
      this.toast.error(errorMessage(e));
    } finally {
      this.syncing.set(false);
      this.calendar.set(await this.api.getCalendarStatus().catch(() => this.calendar()));
    }
  }

  async disconnectGoogle(): Promise<void> {
    if (!confirm('Disconnect Google Calendar? Imported events are removed; you can connect again any time.')) return;
    try {
      await this.api.disconnectGoogle();
      this.calendar.set(await this.api.getCalendarStatus());
      this.timer.bump();
      this.toast.show('Google Calendar disconnected');
    } catch (e) {
      this.toast.error(errorMessage(e));
    }
  }

  clock(iso: string): string {
    return formatClock(iso, this.settings.tz());
  }

  // ---- Meeting rules ----

  async addRule(): Promise<void> {
    if (this.ruleProject == null) return;
    try {
      await this.api.createMeetingRule({ pattern: this.rulePattern.trim(), projectId: this.ruleProject, sortOrder: null, active: true });
      this.rulePattern = '';
      await this.reloadRules('Rule added; existing meetings re-mapped');
    } catch (e) {
      this.toast.error(errorMessage(e));
    }
  }

  async toggleRule(r: MeetingRule): Promise<void> {
    try {
      await this.api.updateMeetingRule(r.id, { pattern: r.pattern, projectId: r.projectId, sortOrder: r.sortOrder, active: !r.active });
      await this.reloadRules(r.active ? 'Rule disabled' : 'Rule enabled');
    } catch (e) {
      this.toast.error(errorMessage(e));
    }
  }

  async moveRule(r: MeetingRule, direction: -1 | 1): Promise<void> {
    const list = this.rules();
    const i = list.findIndex((x) => x.id === r.id);
    const other = list[i + direction];
    if (!other) return;
    try {
      // Swap orders (ties broken by renumbering both).
      const a = other.sortOrder === r.sortOrder ? r.sortOrder + direction : other.sortOrder;
      await this.api.updateMeetingRule(r.id, { pattern: r.pattern, projectId: r.projectId, sortOrder: a, active: r.active });
      await this.api.updateMeetingRule(other.id, { pattern: other.pattern, projectId: other.projectId, sortOrder: r.sortOrder, active: other.active });
      await this.reloadRules(null);
    } catch (e) {
      this.toast.error(errorMessage(e));
    }
  }

  async deleteRule(r: MeetingRule): Promise<void> {
    if (!confirm(`Delete the rule "${r.pattern}"?`)) return;
    try {
      await this.api.deleteMeetingRule(r.id);
      await this.reloadRules('Rule deleted');
    } catch (e) {
      this.toast.error(errorMessage(e));
    }
  }

  testRule(): void {
    clearTimeout(this.ruleTestTimer);
    this.ruleTestTimer = setTimeout(async () => {
      const title = this.ruleTest.trim();
      if (!title) { this.ruleTestResult.set(null); return; }
      try {
        this.ruleTestResult.set((await this.api.testMeetingRules(title)).projectName);
      } catch {
        this.ruleTestResult.set(null);
      }
    }, 200);
  }

  private async reloadRules(message: string | null): Promise<void> {
    this.rules.set(await this.api.getMeetingRules());
    this.timer.bump();
    if (message) this.toast.show(message);
  }

  // ---- Reminder ----

  async allowNotifications(): Promise<void> {
    this.notifyPermission.set(await this.reminder.requestPermission());
  }

  async testNotification(): Promise<void> {
    await this.reminder.show('Waypoint test', 'Notifications work. You will get the review reminder at ' + this.reminderTime + '.');
  }

  // ---- Aggregates ----

  async recompute(): Promise<void> {
    if (!this.recomputeFrom || !this.recomputeTo) return;
    this.recomputing.set(true);
    try {
      const r = await this.api.recomputeAggregates(this.recomputeFrom, this.recomputeTo);
      this.toast.show(`Recomputed ${r.days} day${r.days === 1 ? '' : 's'}`);
      this.timer.bump();
    } catch (e) {
      this.toast.error(errorMessage(e));
    } finally {
      this.recomputing.set(false);
    }
  }
}
