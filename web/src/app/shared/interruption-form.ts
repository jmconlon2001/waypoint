import { Component, HostListener, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService, errorMessage } from '../core/api.service';
import { INTERRUPTION_SOURCES, InterruptionDetails, InterruptionSource } from '../core/models';
import { SettingsStore } from '../core/settings.store';
import { TimerStore } from '../core/timer.store';
import { ToastService } from '../core/toast.service';
import { formatClock } from '../core/time';
import { isTypingTarget } from '../shortcuts';
import { PersonInput } from './person-input';

/** The short form opened by "Interrupted". The timer is already paused when this shows. */
@Component({
  selector: 'app-interruption-form',
  imports: [FormsModule, PersonInput],
  template: `
    <div class="sm-modal-backdrop" (click)="close()">
      <div class="sm-modal" role="dialog" aria-modal="true" aria-labelledby="int-title" (click)="$event.stopPropagation()">
        <div class="sm-modal-header">
          <div>
            <h3 id="int-title"><span class="i i-zap text-warning"></span> Interrupted <span class="num text-muted ms-1">{{ timer.interruptionElapsed() }}</span></h3>
            <div class="cell-sub mt-1">
              Since {{ startedAt() }}
              @if (timer.interruption()?.pausedTaskTitle; as t) { · paused <strong>{{ t }}</strong> } @else { · no timer was running }
            </div>
          </div>
          <button type="button" class="icon-btn icon-btn-sm" (click)="close()" aria-label="Close"><span class="i i-x"></span></button>
        </div>
        <div class="sm-modal-body">
          <div class="mb-3">
            <div class="form-label">Where did it come from? <span class="req">*</span></div>
            <div class="type-picker">
              @for (s of sources; track s.value; let n = $index) {
                <button type="button" class="type-option" [class.selected]="source() === s.value" (click)="source.set(s.value)">
                  <span class="type-icon"><span class="i" [class]="s.icon"></span></span> {{ s.label }}
                  <span class="key">{{ n + 1 }}</span>
                </button>
              }
            </div>
          </div>
          <div class="mb-3">
            <label class="form-label" for="person-input">Person <span class="opt">optional</span></label>
            <app-person-input [(value)]="person" />
          </div>
          <div class="mb-3">
            <label class="form-label" for="int-note">Note <span class="opt">optional</span></label>
            <textarea id="int-note" class="form-control" rows="2" [(ngModel)]="note" placeholder="What was it about?"></textarea>
          </div>
          <div>
            <div class="form-label">Could it have waited? <span class="opt">optional</span></div>
            <div class="segmented">
              <button type="button" [class.active]="couldWait() === true" (click)="couldWait.set(couldWait() === true ? null : true)">Yes</button>
              <button type="button" [class.active]="couldWait() === false" (click)="couldWait.set(couldWait() === false ? null : false)">No</button>
              <button type="button" [class.active]="couldWait() === null" (click)="couldWait.set(null)">Not sure</button>
            </div>
          </div>
        </div>
        <div class="sm-modal-footer">
          <button type="button" class="btn btn-ghost me-auto" (click)="convert()" [disabled]="!source() || saving()" title="End the interruption and keep it as a (done) task with its time">
            <span class="i i-check"></span> Save as task
          </button>
          <button type="button" class="btn btn-outline-secondary" (click)="save()" [disabled]="!source() || saving()">Save, still interrupted</button>
          <button type="button" class="btn btn-primary" (click)="back()" [disabled]="!source() || saving()">
            <span class="i i-corner-down-right"></span> Back to work
          </button>
        </div>
      </div>
    </div>
  `,
})
export class InterruptionForm implements OnInit {
  protected readonly timer = inject(TimerStore);
  private readonly settings = inject(SettingsStore);
  private readonly api = inject(ApiService);
  private readonly toast = inject(ToastService);

  protected readonly sources = INTERRUPTION_SOURCES;
  protected readonly source = signal<InterruptionSource | null>(null);
  protected readonly couldWait = signal<boolean | null>(null);
  protected readonly saving = signal(false);
  protected person = '';
  protected note = '';

  protected readonly startedAt = computed(() => {
    const i = this.timer.interruption();
    return i ? formatClock(i.startedAt, this.settings.tz()) : '';
  });

  ngOnInit(): void {
    const i = this.timer.interruption();
    if (!i) return;
    this.source.set(i.sourceType);
    this.couldWait.set(i.couldWait);
    this.person = i.personName ?? '';
    this.note = i.note ?? '';
  }

  @HostListener('document:keydown', ['$event'])
  onKey(e: KeyboardEvent): void {
    if (isTypingTarget(e.target) || e.ctrlKey || e.metaKey || e.altKey) return;
    const n = Number(e.key);
    if (n >= 1 && n <= this.sources.length) {
      this.source.set(this.sources[n - 1].value);
      e.preventDefault();
    } else if (e.key === 'Enter' && this.source()) {
      this.back();
      e.preventDefault();
    }
  }

  private details(): InterruptionDetails {
    return {
      sourceType: this.source(),
      personId: null,
      personName: this.person.trim() || null,
      note: this.note.trim() || null,
      couldWait: this.couldWait(),
    };
  }

  async save(): Promise<void> {
    this.saving.set(true);
    if (await this.timer.saveInterruption(this.details())) {
      this.timer.interruptionFormOpen.set(false);
      this.toast.show('Interruption saved. Press b when you are back.');
    }
    this.saving.set(false);
  }

  async back(): Promise<void> {
    this.saving.set(true);
    await this.timer.backToWork(this.details());
    this.saving.set(false);
  }

  async convert(): Promise<void> {
    const i = this.timer.interruption();
    if (!i) return;
    this.saving.set(true);
    try {
      await this.api.updateInterruption(i.id, this.details());
      const r = await this.api.convertInterruption(i.id, { title: null, projectId: null, status: 'Done' });
      this.timer.interruptionFormOpen.set(false);
      await this.timer.refresh();
      this.timer.bump();
      this.toast.show(`Saved as task "${r.task.title}". Start a timer when you are back.`);
    } catch (e) {
      this.toast.error(errorMessage(e));
    } finally {
      this.saving.set(false);
    }
  }

  close(): void {
    this.timer.interruptionFormOpen.set(false);
  }
}
