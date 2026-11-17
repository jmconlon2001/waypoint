import { Component, DestroyRef, ElementRef, computed, effect, inject, signal, untracked, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiService, errorMessage } from '../core/api.service';
import { DailyTotals, WorkTask } from '../core/models';
import { SettingsStore } from '../core/settings.store';
import { TimerStore } from '../core/timer.store';
import { ToastService } from '../core/toast.service';
import { UiStore } from '../core/ui.store';
import { formatDateLong, formatDateShort, formatMinutes } from '../core/time';

@Component({
  selector: 'app-today-page',
  imports: [FormsModule, RouterLink],
  templateUrl: './today.page.html',
})
export class TodayPage {
  private readonly api = inject(ApiService);
  protected readonly timer = inject(TimerStore);
  protected readonly settings = inject(SettingsStore);
  protected readonly ui = inject(UiStore);
  private readonly toast = inject(ToastService);

  protected readonly tasks = signal<WorkTask[]>([]);
  protected readonly totals = signal<DailyTotals | null>(null);
  protected readonly loading = signal(true);
  protected readonly adding = signal(false);
  protected quickText = '';

  private readonly quickInput = viewChild<ElementRef<HTMLInputElement>>('quick');

  protected readonly fmt = formatMinutes;
  protected readonly fmtDate = formatDateShort;
  protected readonly todayLabel = computed(() => formatDateLong(this.settings.today()));
  protected readonly open = computed(() => this.tasks().filter((t) => t.status !== 'Done'));
  protected readonly done = computed(() => this.tasks().filter((t) => t.status === 'Done'));
  protected readonly maxProject = computed(() => Math.max(1, ...(this.totals()?.byProject ?? []).map((p) => p.minutes)));
  protected readonly freeSlot = computed(() => {
    const used = new Set(this.tasks().map((t) => t.pinSlot).filter((s) => s != null));
    for (let s = 1; s <= 5; s++) if (!used.has(s)) return s;
    return null;
  });

  constructor() {
    effect(() => {
      this.timer.version();
      untracked(() => this.load());
    });
    effect(() => {
      if (this.ui.focusQuickAdd() > 0) setTimeout(() => this.quickInput()?.nativeElement.focus());
    });
    const poll = setInterval(() => this.loadTotals(), 60_000);
    inject(DestroyRef).onDestroy(() => clearInterval(poll));
  }

  async load(): Promise<void> {
    try {
      const [tasks] = await Promise.all([this.api.getToday(), this.loadTotals()]);
      this.tasks.set(tasks);
    } catch (e) {
      this.toast.error(errorMessage(e));
    } finally {
      this.loading.set(false);
    }
  }

  private async loadTotals(): Promise<void> {
    try {
      this.totals.set(await this.api.getDailyTotals(null));
    } catch { /* shown by load() */ }
  }

  async quickAdd(): Promise<void> {
    const text = this.quickText.trim();
    if (!text) return;
    this.adding.set(true);
    try {
      const r = await this.api.quickAdd(text, true);
      this.quickText = '';
      if (r.warning) this.toast.warning(r.warning);
      else this.toast.show(`Added "${r.task.title}"`);
      this.timer.bump();
    } catch (e) {
      this.toast.error(errorMessage(e));
    } finally {
      this.adding.set(false);
    }
  }

  isRunning(t: WorkTask): boolean {
    return this.timer.running()?.taskId === t.id;
  }

  async startStop(t: WorkTask): Promise<void> {
    if (this.isRunning(t)) await this.timer.stop();
    else await this.timer.start(t.id);
  }

  async toggleDone(t: WorkTask): Promise<void> {
    await this.mutate(() => this.api.setTaskStatus(t.id, t.status === 'Done' ? 'Today' : 'Done'), t.status === 'Done' ? 'Reopened' : 'Done. Nice.');
    this.timer.refresh(true);
  }

  async togglePin(t: WorkTask): Promise<void> {
    const slot = t.pinSlot ? null : this.freeSlot();
    if (!t.pinSlot && slot == null) {
      this.toast.warning('All 5 pin slots are taken. Unpin a task first.');
      return;
    }
    await this.mutate(() => this.api.pinTask(t.id, slot), slot ? `Pinned to key ${slot}` : 'Unpinned');
    this.timer.refresh(true);
  }

  async unplan(t: WorkTask): Promise<void> {
    await this.mutate(() => this.api.planTask(t.id, false), 'Moved back to the backlog');
  }

  async setBlocked(t: WorkTask): Promise<void> {
    await this.mutate(() => this.api.setTaskStatus(t.id, t.status === 'Blocked' ? 'Today' : 'Blocked'), t.status === 'Blocked' ? 'Unblocked' : 'Marked blocked');
  }

  isOverdue(t: WorkTask): boolean {
    return !!t.dueDate && t.dueDate < this.settings.today() && t.status !== 'Done';
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
