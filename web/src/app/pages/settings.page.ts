import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService, errorMessage } from '../core/api.service';
import { CalendarStatus, MeetingRule, Person, Project } from '../core/models';
import { SettingsStore } from '../core/settings.store';
import { ThemeService } from '../core/theme.service';
import { TimerStore } from '../core/timer.store';
import { ToastService } from '../core/toast.service';
import { formatClock } from '../core/time';

interface ProjectDraft { id: number | null; name: string; color: string; active: boolean; }

@Component({
  selector: 'app-settings-page',
  imports: [FormsModule],
  template: `
    <div class="page-header">
      <div>
        <h1>Projects &amp; settings</h1>
        <p class="page-subtitle">Projects, work hours, your calendar and the people who interrupt you.</p>
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

        <div class="card card-flush mb-4">
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

        <form class="card mb-4" (ngSubmit)="saveCalendar()">
          <div class="card-header">
            <h2>Google Calendar</h2>
            @if (calendar()?.configured) {
              @if (calendar()?.lastError) { <span class="pill pill-danger"><span class="i i-alert"></span> Sync failed</span> }
              @else { <span class="pill pill-success">Connected</span> }
            } @else { <span class="pill pill-muted">Off</span> }
          </div>
          <div class="card-body">
            <p class="small text-muted">
              Meetings are counted automatically from your calendar, read-only. In Google Calendar open
              <strong>Settings → your calendar → Integrate calendar</strong> and copy the
              <strong>Secret address in iCal format</strong>. It stays on this machine and is never shown again.
            </p>
            <label class="form-label" for="c-url">Secret iCal address</label>
            <input id="c-url" name="url" type="password" class="form-control mono-input" [(ngModel)]="icsUrl" autocomplete="off"
                   [placeholder]="calendar()?.addressHint ? 'Saved: ' + calendar()?.addressHint : 'https://calendar.google.com/calendar/ical/…/basic.ics'" />
            <label class="form-label mt-3" for="c-dom">Internal email domains <span class="opt">optional</span></label>
            <input id="c-dom" name="domains" class="form-control" [(ngModel)]="internalDomains" placeholder="cfs.com" />
            <div class="form-text">Meetings with anyone outside these count as external. Empty: your calendar's own domain.</div>
            @if (calendar(); as c) {
              @if (c.configured) {
                <div class="small mt-3">
                  @if (c.lastSyncAt) { Last synced {{ syncedLabel(c.lastSyncAt) }} · {{ c.eventCount }} events · every {{ c.syncIntervalMinutes }} min }
                  @else { Not synced yet. }
                </div>
                @if (c.lastError) { <div class="small text-danger mt-1"><span class="i i-alert"></span> {{ c.lastError }}</div> }
              }
            }
          </div>
          <div class="sm-modal-footer" style="border-radius: 0 0 var(--sm-radius) var(--sm-radius)">
            @if (calendar()?.configured) {
              <button type="button" class="btn btn-ghost text-danger me-auto" (click)="disconnectCalendar()">Disconnect</button>
              <button type="button" class="btn btn-outline-secondary" (click)="syncNow()" [disabled]="syncing()">
                <span class="i i-refresh"></span> {{ syncing() ? 'Syncing…' : 'Sync now' }}
              </button>
            }
            <button type="submit" class="btn btn-primary" [disabled]="syncing()">Save</button>
          </div>
        </form>

        <div class="card card-flush mb-4">
          <div class="card-header"><h2>Meeting rules</h2><span class="cell-sub">Title contains → project</span></div>
          <div class="card-body">
            <form class="d-flex gap-2 p-3 border-bottom" (ngSubmit)="addRule()">
              <input class="form-control" name="pattern" [(ngModel)]="rulePattern" placeholder="e.g. SOX" aria-label="Text in the meeting title" maxlength="200" />
              <select class="form-select w-auto" name="ruleProject" [(ngModel)]="ruleProject" aria-label="Project">
                <option [ngValue]="null" disabled>Project…</option>
                @for (p of settings.projects(); track p.id) { <option [ngValue]="p.id">{{ p.name }}</option> }
              </select>
              <button type="submit" class="btn btn-primary" [disabled]="!rulePattern.trim() || ruleProject == null">Add</button>
            </form>
            <table class="table">
              <tbody>
                @for (r of rules(); track r.id) {
                  <tr>
                    <td><code>{{ r.titlePattern }}</code></td>
                    <td><span class="project-pill" [style.--pc]="r.projectColor"><span class="project-dot"></span>{{ r.projectName }}</span></td>
                    <td class="cell-sub num">{{ r.matchCount }} meeting{{ r.matchCount === 1 ? '' : 's' }}</td>
                    <td class="td-actions"><button type="button" class="icon-btn icon-btn-sm danger" (click)="deleteRule(r)" title="Delete rule"><span class="i i-trash"></span></button></td>
                  </tr>
                } @empty {
                  <tr><td><p class="text-muted m-0 small">No rules. Meetings without a project show as "Meetings (no project)". The first matching rule wins.</p></td></tr>
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
                <label class="form-label" for="s-meet">Longest meeting (minutes)</label>
                <input id="s-meet" name="meet" type="number" min="15" max="1440" class="form-control mono-input" [(ngModel)]="meetingMaxMinutes" />
                <div class="form-text">Longer events (holds, OOO, trainings) don't count as meeting time.</div>
              </div>
              <div class="col-sm-6">
                <label class="form-label" for="s-future">Future entries (minutes)</label>
                <input id="s-future" name="future" type="number" min="0" max="1440" class="form-control mono-input" [(ngModel)]="futureEntryMaxMinutes" />
                <div class="form-text">How far ahead a logged entry may end, e.g. a block you're about to start. 0 = none. Default 240.</div>
              </div>
              <div class="col-sm-6">
                <label class="form-label" for="s-remind">Daily review reminder</label>
                <input id="s-remind" name="remind" type="time" class="form-control mono-input" [(ngModel)]="reviewReminderTime" />
                <div class="form-text">Leave empty for no reminder. Shows while Waypoint is open.</div>
                @if (reviewReminderTime && notificationState() === 'default') {
                  <button type="button" class="btn btn-link btn-sm p-0" (click)="allowNotifications()">Also show a desktop notification</button>
                }
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

  protected readonly draft = signal<ProjectDraft | null>(null);
  protected readonly people = signal<Person[]>([]);
  protected readonly renaming = signal<number | null>(null);
  protected renameText = '';
  protected workdayStart = '08:45';
  protected workdayEnd = '17:30';
  protected timerWarningMinutes = 180;
  protected timeZone = 'America/New_York';
  protected meetingMaxMinutes = 120;
  protected reviewReminderTime = '';
  protected futureEntryMaxMinutes = 240;
  protected readonly calendar = signal<CalendarStatus | null>(null);
  protected readonly rules = signal<MeetingRule[]>([]);
  protected readonly syncing = signal(false);
  protected readonly notificationState = signal<string>('Notification' in window ? Notification.permission : 'unsupported');
  protected icsUrl = '';
  protected internalDomains = '';
  protected rulePattern = '';
  protected ruleProject: number | null = null;
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
        this.meetingMaxMinutes = s.meetingMaxMinutes;
        this.reviewReminderTime = s.reviewReminderTime ?? '';
        this.futureEntryMaxMinutes = s.futureEntryMaxMinutes;
      }
      this.people.set(await this.api.searchPeople(''));
      const [c, r] = await Promise.all([this.api.getCalendarStatus(), this.api.getMeetingRules()]);
      this.calendar.set(c);
      this.internalDomains = c.internalDomains ?? '';
      this.rules.set(r);
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
        meetingMaxMinutes: Number(this.meetingMaxMinutes) || 120, reviewReminderTime: this.reviewReminderTime || null,
        futureEntryMaxMinutes: Math.max(0, Math.round(Number(this.futureEntryMaxMinutes) || 0)),
      });
      this.settings.settings.set(s);
      await this.timer.refresh(true);
      this.timer.bump();
      this.toast.show('Settings saved');
    } catch (e) {
      this.toast.error(errorMessage(e));
    }
  }

  async allowNotifications(): Promise<void> {
    if (!('Notification' in window)) return;
    this.notificationState.set(await Notification.requestPermission());
  }

  syncedLabel(iso: string): string {
    const mins = Math.round((Date.now() - Date.parse(iso)) / 60000);
    return mins < 1 ? 'just now' : mins < 60 ? `${mins} min ago` : formatClock(iso, this.settings.tz());
  }

  /** Saves the address (if typed) and domains; a new address syncs straight away. */
  async saveCalendar(): Promise<void> {
    const url = this.icsUrl.trim();
    try {
      this.calendar.set(await this.api.setCalendarSource(url || null, this.internalDomains.trim() || null));
      this.icsUrl = '';
      if (url) await this.syncNow();
      else this.toast.show('Calendar settings saved');
    } catch (e) {
      this.toast.error(errorMessage(e));
    }
  }

  async syncNow(): Promise<void> {
    this.syncing.set(true);
    try {
      const r = await this.api.syncCalendar();
      this.calendar.set(r.status);
      this.rules.set(await this.api.getMeetingRules());
      this.timer.bump();
      this.toast.show(`Calendar synced: ${r.total} events (${r.added} new, ${r.removed} removed)`);
    } catch (e) {
      this.toast.error(errorMessage(e));
      this.calendar.set(await this.api.getCalendarStatus().catch(() => this.calendar()));
    } finally {
      this.syncing.set(false);
    }
  }

  async disconnectCalendar(): Promise<void> {
    if (!confirm('Disconnect the calendar? Meetings already imported are kept.')) return;
    try {
      this.calendar.set(await this.api.setCalendarSource('', this.internalDomains.trim() || null));
      this.toast.show('Calendar disconnected');
    } catch (e) {
      this.toast.error(errorMessage(e));
    }
  }

  async addRule(): Promise<void> {
    if (this.ruleProject == null) return;
    try {
      this.rules.set(await this.api.createMeetingRule(this.rulePattern.trim(), this.ruleProject));
      this.rulePattern = '';
      this.timer.bump();
    } catch (e) {
      this.toast.error(errorMessage(e));
    }
  }

  async deleteRule(r: MeetingRule): Promise<void> {
    try {
      this.rules.set(await this.api.deleteMeetingRule(r.id));
      this.timer.bump();
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
}
