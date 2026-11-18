import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService, errorMessage } from '../core/api.service';
import { TimeEntry, TimeEntryKind, WorkTask } from '../core/models';
import { SettingsStore } from '../core/settings.store';
import { TimerStore } from '../core/timer.store';
import { ToastService } from '../core/toast.service';
import { UiStore } from '../core/ui.store';
import { addDays, localDate, localTime, zonedToUtc } from '../core/time';

/** Add or edit a time entry after the fact: task (or project only), start, end, note. */
@Component({
  selector: 'app-entry-editor',
  imports: [FormsModule],
  template: `
    <div class="sm-modal-backdrop" (click)="close()">
      <form class="sm-modal" role="dialog" aria-modal="true" aria-labelledby="entry-title" (click)="$event.stopPropagation()" (ngSubmit)="save()">
        <div class="sm-modal-header">
          <h3 id="entry-title">{{ existing ? 'Edit time entry' : 'Log time' }}</h3>
          <button type="button" class="icon-btn icon-btn-sm" (click)="close()" aria-label="Close"><span class="i i-x"></span></button>
        </div>
        <div class="sm-modal-body">
          <div class="mb-3">
            <div class="form-label">Kind</div>
            <div class="segmented">
              <button type="button" [class.active]="kind === 'Task'" (click)="kind = 'Task'">Focus / task</button>
              <button type="button" [class.active]="kind === 'Reactive'" (click)="kind = 'Reactive'">Reactive</button>
              <button type="button" [class.active]="kind === 'Untracked'" (click)="kind = 'Untracked'">Untracked</button>
            </div>
          </div>
          @if (kind !== 'Untracked') {
            <div class="row g-3 mb-3">
              <div class="col-sm-6">
                <label class="form-label" for="e-task">Task</label>
                <select id="e-task" name="task" class="form-select" [(ngModel)]="taskId" (ngModelChange)="onTask($event)">
                  <option [ngValue]="null">No task (project only)</option>
                  @for (t of tasks(); track t.id) { <option [ngValue]="t.id">{{ t.title }}</option> }
                </select>
              </div>
              <div class="col-sm-6">
                <label class="form-label" for="e-project">Project</label>
                <select id="e-project" name="project" class="form-select" [(ngModel)]="projectId">
                  <option [ngValue]="null">No project</option>
                  @for (p of settings.projects(); track p.id) { <option [ngValue]="p.id">{{ p.name }}</option> }
                </select>
              </div>
            </div>
          }
          <div class="row g-3 mb-3">
            <div class="col-sm-4">
              <label class="form-label" for="e-date">Date</label>
              <input id="e-date" name="date" type="date" class="form-control" [(ngModel)]="date" required />
            </div>
            <div class="col-6 col-sm-4">
              <label class="form-label" for="e-start">Start</label>
              <input id="e-start" name="start" type="time" class="form-control mono-input" [(ngModel)]="start" required />
            </div>
            <div class="col-6 col-sm-4">
              <label class="form-label" for="e-end">End</label>
              <input id="e-end" name="end" type="time" class="form-control mono-input" [(ngModel)]="end" required />
            </div>
          </div>
          <div class="form-text mb-3">Times are in {{ settings.tz() }}. An end before the start means the next day.</div>
          <div>
            <label class="form-label" for="e-note">Note <span class="opt">optional</span></label>
            <input id="e-note" name="note" class="form-control" [(ngModel)]="note" />
          </div>
        </div>
        <div class="sm-modal-footer">
          @if (existing) {
            <button type="button" class="btn btn-ghost me-auto text-danger" (click)="remove()" [disabled]="saving()"><span class="i i-trash"></span> Delete</button>
          }
          <button type="button" class="btn btn-outline-secondary" (click)="close()">Cancel</button>
          <button type="submit" class="btn btn-primary" [disabled]="saving()">Save</button>
        </div>
      </form>
    </div>
  `,
})
export class EntryEditor implements OnInit {
  protected readonly settings = inject(SettingsStore);
  private readonly ui = inject(UiStore);
  private readonly api = inject(ApiService);
  private readonly timer = inject(TimerStore);
  private readonly toast = inject(ToastService);

  protected readonly tasks = signal<WorkTask[]>([]);
  protected readonly saving = signal(false);
  protected existing: TimeEntry | null = null;
  protected kind: TimeEntryKind = 'Task';
  protected taskId: number | null = null;
  protected projectId: number | null = null;
  protected date = '';
  protected start = '';
  protected end = '';
  protected note = '';

  async ngOnInit(): Promise<void> {
    const req = this.ui.entryEditor();
    if (!req) return;
    const tz = this.settings.tz();
    this.date = req.date;
    if (req.entry) {
      const e = req.entry;
      this.existing = e;
      this.kind = e.kind;
      this.taskId = e.taskId;
      this.projectId = e.projectId;
      this.date = localDate(new Date(e.startedAt), tz);
      this.start = localTime(e.startedAt, tz);
      this.end = e.endedAt ? localTime(e.endedAt, tz) : localTime(new Date(), tz);
      this.note = e.note ?? '';
    } else {
      this.start = req.start ?? '';
      this.end = req.end ?? localTime(new Date(), tz);
    }
    try {
      const open = await this.api.getTasks({ view: 'all' });
      // Keep the selected task in the list even if it's old/done.
      this.tasks.set(open.filter((t) => t.status !== 'Done' || t.id === this.taskId).slice(0, 300));
    } catch {
      this.tasks.set([]);
    }
  }

  onTask(id: number | null): void {
    const t = this.tasks().find((x) => x.id === id);
    if (t?.projectId) this.projectId = t.projectId;
  }

  async save(): Promise<void> {
    if (!this.date || !this.start || !this.end) {
      this.toast.error('Date, start and end are required.');
      return;
    }
    const tz = this.settings.tz();
    const startUtc = zonedToUtc(this.date, this.start, tz);
    let endUtc = zonedToUtc(this.date, this.end, tz);
    if (endUtc <= startUtc) endUtc = zonedToUtc(addDays(this.date, 1), this.end, tz);
    const input = {
      taskId: this.kind === 'Untracked' ? null : this.taskId,
      projectId: this.kind === 'Untracked' ? null : this.projectId,
      kind: this.kind,
      startedAt: startUtc.toISOString(),
      endedAt: endUtc.toISOString(),
      note: this.note.trim() || null,
    };
    this.saving.set(true);
    try {
      const saved = this.existing ? await this.api.updateEntry(this.existing.id, input) : await this.api.createEntry(input);
      this.toast.show(saved.adjusted?.length ? `Time saved. ${saved.adjusted.join('; ')}.` : 'Time saved');
      this.ui.entryEditor.set(null);
      this.timer.bump();
    } catch (e) {
      this.toast.error(errorMessage(e));
    } finally {
      this.saving.set(false);
    }
  }

  async remove(): Promise<void> {
    if (!this.existing || !confirm('Delete this time entry?')) return;
    try {
      await this.api.deleteEntry(this.existing.id);
      this.toast.show('Time entry deleted');
      this.ui.entryEditor.set(null);
      this.timer.bump();
    } catch (e) {
      this.toast.error(errorMessage(e));
    }
  }

  close(): void {
    this.ui.entryEditor.set(null);
  }
}
