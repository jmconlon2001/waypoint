import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';
import { ApiService, errorMessage } from './api.service';
import { InterruptionDetails, TimerState } from './models';
import { ToastService } from './toast.service';
import { formatElapsed } from './time';

/**
 * The running timer and open interruption (both live on the server, so they survive a browser
 * restart). Also owns `version`, a counter bumped after every change so pages know to reload.
 */
@Injectable({ providedIn: 'root' })
export class TimerStore {
  private readonly api = inject(ApiService);
  private readonly toast = inject(ToastService);

  readonly state = signal<TimerState | null>(null);
  /** Ticks every second (client clock corrected by the server's clock). */
  readonly now = signal(Date.now());
  readonly version = signal(0);
  /** The interruption form is open (shell renders it). */
  readonly interruptionFormOpen = signal(false);
  readonly busy = signal(false);

  private skewMs = 0;

  readonly running = computed(() => this.state()?.running ?? null);
  readonly interruption = computed(() => this.state()?.openInterruption ?? null);
  readonly pinned = computed(() => this.state()?.pinned ?? []);
  readonly warnings = computed(() => this.state()?.warnings ?? []);

  readonly elapsedMs = computed(() => {
    const r = this.running();
    return r ? this.now() - new Date(r.startedAt).getTime() : 0;
  });
  readonly elapsed = computed(() => formatElapsed(this.elapsedMs()));
  readonly interruptionElapsed = computed(() => {
    const i = this.interruption();
    return i ? formatElapsed(this.now() - new Date(i.startedAt).getTime()) : '';
  });
  /** Long-running warning computed live, so it appears without waiting for a refresh. */
  readonly overThreshold = computed(() => {
    const t = this.state()?.warningThresholdMinutes ?? 0;
    return t > 0 && this.elapsedMs() > t * 60_000;
  });

  constructor() {
    const tick = setInterval(() => this.now.set(Date.now() + this.skewMs), 1000);
    const poll = setInterval(() => this.refresh(true), 60_000);
    const onFocus = () => this.refresh(true);
    window.addEventListener('focus', onFocus);
    inject(DestroyRef).onDestroy(() => {
      clearInterval(tick);
      clearInterval(poll);
      window.removeEventListener('focus', onFocus);
    });
  }

  async refresh(quiet = false): Promise<void> {
    try {
      this.set(await this.api.getTimer());
    } catch (e) {
      if (!quiet) this.toast.error(errorMessage(e));
    }
  }

  start(taskId: number): Promise<boolean> {
    return this.act(() => this.api.startTimer({ taskId }), 'Timer started');
  }

  startProject(projectId: number): Promise<boolean> {
    return this.act(() => this.api.startTimer({ projectId, kind: 'Task' }), 'Timer started');
  }

  startPinned(slot: number): Promise<boolean> {
    if (!this.pinned().some((p) => p.slot === slot)) {
      this.toast.warning(`Nothing is pinned to ${slot}. Pin a task from the Today list.`);
      return Promise.resolve(false);
    }
    return this.act(() => this.api.startPinned(slot), 'Switched task');
  }

  stop(): Promise<boolean> {
    return this.act(() => this.api.stopTimer(), 'Timer stopped');
  }

  /** Stop if running; otherwise resume the most recent task. */
  async toggle(): Promise<void> {
    if (this.running()) {
      await this.stop();
    } else if (this.state()?.lastTask) {
      await this.start(this.state()!.lastTask!.id);
    } else {
      this.toast.warning('No recent task to resume. Start one from the Today list.');
    }
  }

  /** Pauses the timer immediately (server-side), then opens the form for the details. */
  async interrupt(): Promise<void> {
    if (this.interruption()) {
      this.interruptionFormOpen.set(true);
      return;
    }
    try {
      this.busy.set(true);
      await this.api.startInterruption(null);
      await this.refresh();
      this.interruptionFormOpen.set(true);
      this.bump();
    } catch (e) {
      this.toast.error(errorMessage(e));
    } finally {
      this.busy.set(false);
    }
  }

  async saveInterruption(details: InterruptionDetails): Promise<boolean> {
    const i = this.interruption();
    if (!i) return false;
    try {
      await this.api.updateInterruption(i.id, details);
      await this.refresh();
      this.bump();
      return true;
    } catch (e) {
      this.toast.error(errorMessage(e));
      return false;
    }
  }

  /** "Back to work": closes the interruption and resumes the paused task. */
  async backToWork(details: InterruptionDetails | null = null): Promise<boolean> {
    const i = this.interruption();
    if (!i) return false;
    if (!i.sourceType && !details?.sourceType) {
      this.interruptionFormOpen.set(true);
      this.toast.warning('Pick where the interruption came from first.');
      return false;
    }
    try {
      this.busy.set(true);
      const r = await this.api.resumeInterruption(i.id, details);
      this.interruptionFormOpen.set(false);
      await this.refresh();
      this.bump();
      const mins = Math.max(1, Math.round(r.interruption.minutes));
      this.toast.show(r.resumed ? `Back to "${r.resumed.taskTitle ?? 'work'}" after ${mins}m` : `Interruption logged (${mins}m)`);
      return true;
    } catch (e) {
      this.toast.error(errorMessage(e));
      return false;
    } finally {
      this.busy.set(false);
    }
  }

  bump(): void {
    this.version.update((v) => v + 1);
  }

  private set(s: TimerState): void {
    this.skewMs = new Date(s.serverNow).getTime() - Date.now();
    this.state.set(s);
    this.now.set(Date.now() + this.skewMs);
  }

  private async act(fn: () => Promise<TimerState>, message: string): Promise<boolean> {
    try {
      this.busy.set(true);
      this.set(await fn());
      this.bump();
      this.toast.show(message);
      return true;
    } catch (e) {
      this.toast.error(errorMessage(e));
      return false;
    } finally {
      this.busy.set(false);
    }
  }
}
