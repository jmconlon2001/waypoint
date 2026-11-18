import { Component, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService, errorMessage } from '../core/api.service';
import { TASK_STATUSES, TaskStatus, WorkTask } from '../core/models';
import { SettingsStore } from '../core/settings.store';
import { TimerStore } from '../core/timer.store';
import { ToastService } from '../core/toast.service';
import { UiStore } from '../core/ui.store';
import { addDays, formatDateShort, formatMinutes } from '../core/time';

type View = 'open' | 'today' | 'tomorrow' | 'Backlog' | 'Blocked' | 'done' | 'all';

@Component({
  selector: 'app-tasks-page',
  imports: [FormsModule],
  template: `
    <div class="page-header">
      <div>
        <h1>Tasks</h1>
        <p class="page-subtitle">Everything on your plate. Plan a task for today to see it on the Today page.</p>
      </div>
      <div class="page-actions">
        <button type="button" class="btn btn-primary" (click)="ui.taskEditor.set('new')"><span class="i i-plus"></span> New task</button>
      </div>
    </div>

    <div class="toolbar">
      <div class="segmented" role="tablist">
        @for (v of views; track v.value) {
          <button type="button" role="tab" [class.active]="view() === v.value" (click)="setView(v.value)">{{ v.label }}</button>
        }
      </div>
      <select class="form-select w-auto" [ngModel]="projectId()" (ngModelChange)="projectId.set($event); load()" aria-label="Filter by project">
        <option [ngValue]="null">All projects</option>
        @for (p of settings.projects(); track p.id) { <option [ngValue]="p.id">{{ p.name }}</option> }
      </select>
      <div class="input-icon grow">
        <span class="i i-search"></span>
        <input class="form-control" placeholder="Search title or notes" [ngModel]="q()" (ngModelChange)="search($event)" aria-label="Search tasks" />
      </div>
    </div>

    <div class="card card-flush">
      <div class="card-body">
        <div class="table-wrap">
          <table class="table table-hover">
            <thead>
              <tr><th style="width: 44px"></th><th>Task</th><th>Project</th><th>Status</th><th>Due</th><th>Planned</th><th class="text-end">Tracked</th><th></th></tr>
            </thead>
            <tbody>
              @for (t of tasks(); track t.id) {
                <tr>
                  <td><span class="priority" [class]="'priority-' + t.priority">{{ t.priority }}</span></td>
                  <td style="min-width: 240px">
                    <div class="cell-main" role="button" (click)="ui.taskEditor.set(t)">{{ t.title }}
                      @if (t.pinSlot) { <span class="pin-badge ms-1">{{ t.pinSlot }}</span> }
                    </div>
                    @if (t.notes) { <div class="cell-sub text-truncate" style="max-width: 420px">{{ t.notes }}</div> }
                  </td>
                  <td>@if (t.projectName) { <span class="project-pill" [style.--pc]="t.projectColor"><span class="project-dot"></span>{{ t.projectName }}</span> }</td>
                  <td>
                    <select class="form-select form-select-sm w-auto" [ngModel]="t.status" (ngModelChange)="setStatus(t, $event)" [attr.aria-label]="'Status of ' + t.title">
                      @for (s of statuses; track s) { <option [ngValue]="s">{{ s }}</option> }
                    </select>
                  </td>
                  <td class="cell-mono" [class.due-overdue]="isOverdue(t)">{{ fmtDate(t.dueDate) }}</td>
                  <td>
                    @if (t.status === 'Done') {
                      <span class="cell-sub">{{ fmtDate(t.plannedDate) }}</span>
                    } @else if (t.plannedDate && t.plannedDate <= settings.today()) {
                      <div class="plan-cell">
                        <button type="button" class="pill pill-accent border-0" (click)="plan(t, false)" title="Remove from today">Today <span class="i i-x"></span></button>
                        <button type="button" class="btn btn-sm btn-ghost" (click)="moveTo(t, tomorrow())" title="Move to tomorrow"><span class="i i-arrow-right"></span> Tomorrow</button>
                      </div>
                    } @else if (t.plannedDate) {
                      <div class="plan-cell">
                        <button type="button" class="pill pill-info border-0" (click)="plan(t, false)" [title]="'Planned for ' + fmtDate(t.plannedDate) + '. Click to unplan.'">
                          {{ t.plannedDate === tomorrow() ? 'Tomorrow' : fmtDate(t.plannedDate) }} <span class="i i-x"></span>
                        </button>
                        <button type="button" class="btn btn-sm btn-ghost" (click)="plan(t, true)" title="Do it today instead"><span class="i i-sunrise"></span> Today</button>
                      </div>
                    } @else {
                      <div class="plan-cell">
                        <button type="button" class="btn btn-sm btn-outline-secondary" (click)="plan(t, true)"><span class="i i-sunrise"></span> Today</button>
                        <button type="button" class="btn btn-sm btn-outline-secondary" (click)="moveTo(t, tomorrow())" title="Plan for tomorrow"><span class="i i-arrow-right"></span> Tomorrow</button>
                      </div>
                    }
                  </td>
                  <td class="cell-mono text-end">{{ t.trackedMinutes > 0 ? fmt(t.trackedMinutes) : '' }}</td>
                  <td class="td-actions">
                    @if (t.status !== 'Done') {
                      <button type="button" class="icon-btn icon-btn-sm play" [class.playing]="timer.running()?.taskId === t.id" (click)="startStop(t)" [title]="timer.running()?.taskId === t.id ? 'Stop' : 'Start timer'">
                        <span class="i" [class.i-play]="timer.running()?.taskId !== t.id" [class.i-stop]="timer.running()?.taskId === t.id"></span>
                      </button>
                    }
                    <button type="button" class="icon-btn icon-btn-sm" (click)="ui.taskEditor.set(t)" title="Edit"><span class="i i-edit"></span></button>
                  </td>
                </tr>
              } @empty {
                <tr><td colspan="8">
                  <div class="empty"><span class="i i-list"></span><h3>No tasks here</h3><p class="mb-0">Try another filter, or add one.</p></div>
                </td></tr>
              }
            </tbody>
          </table>
        </div>
      </div>
    </div>
  `,
})
export class TasksPage {
  private readonly api = inject(ApiService);
  protected readonly settings = inject(SettingsStore);
  protected readonly timer = inject(TimerStore);
  protected readonly ui = inject(UiStore);
  private readonly toast = inject(ToastService);

  protected readonly views: { value: View; label: string }[] = [
    { value: 'open', label: 'Open' },
    { value: 'today', label: 'Today' },
    { value: 'tomorrow', label: 'Tomorrow' },
    { value: 'Backlog', label: 'Backlog' },
    { value: 'Blocked', label: 'Blocked' },
    { value: 'done', label: 'Done' },
    { value: 'all', label: 'All' },
  ];
  protected readonly statuses = TASK_STATUSES;
  protected readonly view = signal<View>('open');
  protected readonly projectId = signal<number | null>(null);
  protected readonly q = signal('');
  protected readonly tasks = signal<WorkTask[]>([]);
  protected readonly fmt = formatMinutes;
  protected readonly fmtDate = formatDateShort;
  protected readonly tomorrow = () => addDays(this.settings.today(), 1);
  private searchTimer: ReturnType<typeof setTimeout> | undefined;

  constructor() {
    effect(() => {
      this.timer.version();
      untracked(() => this.load());
    });
  }

  setView(v: View): void {
    this.view.set(v);
    this.load();
  }

  search(q: string): void {
    this.q.set(q);
    clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => this.load(), 200);
  }

  async load(): Promise<void> {
    const v = this.view();
    const isStatus = v === 'Backlog' || v === 'Blocked';
    try {
      let list = await this.api.getTasks({
        view: isStatus || v === 'tomorrow' ? 'open' : v,
        status: isStatus ? (v as TaskStatus) : null,
        projectId: this.projectId(),
        q: this.q() || null,
      });
      // The "today" view comes from the Today list; apply the filters client-side.
      if (v === 'today') {
        const pid = this.projectId();
        const q = this.q().trim().toLowerCase();
        list = list.filter((t) => (pid == null || t.projectId === pid) && (!q || t.title.toLowerCase().includes(q)));
      }
      if (v === 'tomorrow') list = list.filter((t) => t.plannedDate === this.tomorrow());
      this.tasks.set(list);
    } catch (e) {
      this.toast.error(errorMessage(e));
    }
  }

  isOverdue(t: WorkTask): boolean {
    return !!t.dueDate && t.dueDate < this.settings.today() && t.status !== 'Done';
  }

  async setStatus(t: WorkTask, status: TaskStatus): Promise<void> {
    await this.mutate(() => this.api.setTaskStatus(t.id, status), `Marked ${status}`);
    this.timer.refresh(true);
  }

  async plan(t: WorkTask, today: boolean): Promise<void> {
    await this.mutate(() => this.api.planTask(t.id, today), today ? 'Planned for today' : 'Removed from today');
  }

  /** Plans the task for a later day (it shows up on the Today list then). */
  async moveTo(t: WorkTask, date: string): Promise<void> {
    await this.mutate(() => this.api.rollOver([t.id], date), `Moved to ${date === this.tomorrow() ? 'tomorrow' : formatDateShort(date)}`);
  }

  async startStop(t: WorkTask): Promise<void> {
    if (this.timer.running()?.taskId === t.id) await this.timer.stop();
    else await this.timer.start(t.id);
  }

  private async mutate(fn: () => Promise<unknown>, message: string): Promise<void> {
    try {
      await fn();
      this.toast.show(message);
      this.timer.bump();
    } catch (e) {
      this.toast.error(errorMessage(e));
    }
  }
}
