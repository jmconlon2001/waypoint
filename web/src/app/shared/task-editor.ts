import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService, errorMessage } from '../core/api.service';
import { TASK_STATUSES, TaskInput, TaskStatus, WorkTask } from '../core/models';
import { SettingsStore } from '../core/settings.store';
import { TimerStore } from '../core/timer.store';
import { ToastService } from '../core/toast.service';
import { UiStore } from '../core/ui.store';

/** Create / edit a task with all fields. */
@Component({
  selector: 'app-task-editor',
  imports: [FormsModule],
  template: `
    <div class="sm-modal-backdrop" (click)="close()">
      <form class="sm-modal" role="dialog" aria-modal="true" aria-labelledby="task-title" (click)="$event.stopPropagation()" (ngSubmit)="save()">
        <div class="sm-modal-header">
          <h3 id="task-title">{{ existing ? 'Edit task' : 'New task' }}</h3>
          <button type="button" class="icon-btn icon-btn-sm" (click)="close()" aria-label="Close"><span class="i i-x"></span></button>
        </div>
        <div class="sm-modal-body">
          <div class="mb-3">
            <label class="form-label" for="t-title">Title <span class="req">*</span></label>
            <input id="t-title" name="title" class="form-control" [(ngModel)]="form.title" required maxlength="300" autofocus />
          </div>
          <div class="row g-3 mb-3">
            <div class="col-sm-6">
              <label class="form-label" for="t-project">Project</label>
              <select id="t-project" name="project" class="form-select" [(ngModel)]="form.projectId">
                <option [ngValue]="null">No project</option>
                @for (p of settings.projects(); track p.id) { <option [ngValue]="p.id">{{ p.name }}</option> }
              </select>
            </div>
            <div class="col-sm-6">
              <div class="form-label">Priority</div>
              <div class="segmented">
                @for (p of [1, 2, 3]; track p) {
                  <button type="button" [class.active]="form.priority === p" (click)="form.priority = p">{{ p === 1 ? 'High' : p === 2 ? 'Normal' : 'Low' }}</button>
                }
              </div>
            </div>
            <div class="col-sm-6">
              <label class="form-label" for="t-status">Status</label>
              <select id="t-status" name="status" class="form-select" [(ngModel)]="form.status">
                @for (s of statuses; track s) { <option [ngValue]="s">{{ s }}</option> }
              </select>
            </div>
            <div class="col-sm-6">
              <label class="form-label" for="t-due">Due date</label>
              <input id="t-due" name="due" type="date" class="form-control" [(ngModel)]="form.dueDate" />
            </div>
            <div class="col-sm-6">
              <label class="form-label" for="t-planned">Planned for</label>
              <div class="d-flex gap-2">
                <input id="t-planned" name="planned" type="date" class="form-control" [(ngModel)]="form.plannedDate" />
                <button type="button" class="btn btn-outline-secondary btn-sm" (click)="form.plannedDate = settings.today()">Today</button>
              </div>
            </div>
          </div>
          <div>
            <label class="form-label" for="t-notes">Notes</label>
            <textarea id="t-notes" name="notes" class="form-control" rows="3" [(ngModel)]="form.notes"></textarea>
          </div>
        </div>
        <div class="sm-modal-footer">
          @if (existing) {
            <button type="button" class="btn btn-ghost me-auto text-danger" (click)="remove()" [disabled]="saving()"><span class="i i-trash"></span> Delete</button>
          }
          <button type="button" class="btn btn-outline-secondary" (click)="close()">Cancel</button>
          <button type="submit" class="btn btn-primary" [disabled]="saving() || !form.title.trim()">{{ existing ? 'Save' : 'Add task' }}</button>
        </div>
      </form>
    </div>
  `,
})
export class TaskEditor implements OnInit {
  protected readonly settings = inject(SettingsStore);
  private readonly ui = inject(UiStore);
  private readonly api = inject(ApiService);
  private readonly timer = inject(TimerStore);
  private readonly toast = inject(ToastService);

  protected readonly statuses = TASK_STATUSES;
  protected readonly saving = signal(false);
  protected existing: WorkTask | null = null;
  protected form: TaskInput & { status: TaskStatus } = {
    title: '', projectId: null, priority: 2, dueDate: null, notes: null, status: 'Backlog', plannedDate: null,
  };

  ngOnInit(): void {
    const t = this.ui.taskEditor();
    if (t && t !== 'new') {
      this.existing = t;
      this.form = {
        title: t.title, projectId: t.projectId, priority: t.priority, dueDate: t.dueDate, notes: t.notes,
        status: t.status, plannedDate: t.plannedDate,
      };
    }
  }

  async save(): Promise<void> {
    this.saving.set(true);
    const input: TaskInput = { ...this.form, dueDate: this.form.dueDate || null, plannedDate: this.form.plannedDate || null };
    try {
      if (this.existing) await this.api.updateTask(this.existing.id, input);
      else await this.api.createTask(input);
      this.toast.show(this.existing ? 'Task saved' : 'Task added');
      this.done();
    } catch (e) {
      this.toast.error(errorMessage(e));
    } finally {
      this.saving.set(false);
    }
  }

  async remove(): Promise<void> {
    if (!this.existing || !confirm(`Delete "${this.existing.title}"? Its logged time is kept (without the task link).`)) return;
    try {
      await this.api.deleteTask(this.existing.id);
      this.toast.show('Task deleted');
      this.done();
    } catch (e) {
      this.toast.error(errorMessage(e));
    }
  }

  private done(): void {
    this.ui.taskEditor.set(null);
    this.timer.refresh(true);
    this.timer.bump();
  }

  close(): void {
    this.ui.taskEditor.set(null);
  }
}
