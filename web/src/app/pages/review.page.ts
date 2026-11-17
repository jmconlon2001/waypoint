import { Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService, errorMessage } from '../core/api.service';
import { Gap, INTERRUPTION_SOURCES, Interruption, InterruptionSource, MEETING_TYPE_LABELS, Overlap, Review, TimeEntry, TimeEntryKind, sourceLabel } from '../core/models';
import { SettingsStore } from '../core/settings.store';
import { TimerStore } from '../core/timer.store';
import { ToastService } from '../core/toast.service';
import { UiStore } from '../core/ui.store';
import { addDays, formatClock, formatDateLong, formatDateShort, formatMinutes, localTime, zonedToUtc } from '../core/time';
import { PersonInput } from '../shared/person-input';
import { buildTimeline, ribbonSegments, TimelineItem } from './timeline';

@Component({
  selector: 'app-review-page',
  imports: [FormsModule, PersonInput],
  templateUrl: './review.page.html',
})
export class ReviewPage {
  private readonly api = inject(ApiService);
  protected readonly settings = inject(SettingsStore);
  protected readonly timer = inject(TimerStore);
  protected readonly ui = inject(UiStore);
  private readonly toast = inject(ToastService);

  protected readonly date = signal('');
  protected readonly review = signal<Review | null>(null);
  protected readonly editing = signal<number | null>(null);
  protected readonly fmt = formatMinutes;
  protected readonly sources = INTERRUPTION_SOURCES;
  protected readonly sourceLabel = sourceLabel;
  protected readonly meetingTypeLabel = MEETING_TYPE_LABELS;
  protected readonly shortDate = formatDateShort;

  /** Unfinished tasks ticked for roll-over (all by default). */
  protected readonly rollSkip = signal<Set<number>>(new Set());
  protected readonly rolling = signal(false);

  // Interruption editor state
  protected edSource: InterruptionSource | null = null;
  protected edPerson = '';
  protected edNote = '';
  protected edCouldWait: boolean | null = null;
  protected edProject: number | null = null;
  protected edStart = '';
  protected edEnd = '';

  protected readonly isToday = computed(() => this.date() === this.settings.today());
  protected readonly title = computed(() => (this.date() ? formatDateLong(this.date()) : ''));
  protected readonly items = computed<TimelineItem[]>(() => {
    const r = this.review();
    return r ? buildTimeline(r) : [];
  });
  protected readonly ribbon = computed(() => {
    const r = this.review();
    return r ? ribbonSegments(r, this.timer.now(), this.settings.tz()) : null;
  });
  protected readonly unclassified = computed(() => (this.review()?.interruptions ?? []).filter((i) => !i.sourceType && i.endedAt));
  protected readonly maxProject = computed(() => Math.max(1, ...(this.review()?.totals.byProject ?? []).map((p) => p.minutes)));
  protected readonly kindTotal = computed(() => {
    const t = this.review()?.totals;
    return t ? Math.max(1, t.taskMinutes + t.meetingMinutes + t.reactiveMinutes + t.untrackedMinutes + t.unloggedMinutes) : 1;
  });
  protected readonly allDay = computed(() => (this.review()?.meetings ?? []).filter((m) => m.isAllDay));
  protected readonly tomorrow = computed(() => (this.date() ? addDays(this.date(), 1) : ''));
  /** Next weekday: Friday's roll-over lands on Monday. */
  protected readonly nextWorkday = computed(() => {
    let d = this.tomorrow();
    if (!d) return d;
    while ([0, 6].includes(new Date(d + 'T12:00:00Z').getUTCDay())) d = addDays(d, 1);
    return d;
  });
  protected readonly rollIds = computed(() => (this.review()?.unfinished ?? []).map((t) => t.id).filter((id) => !this.rollSkip().has(id)));
  protected readonly planTotal = computed(() => {
    const p = this.review()?.plan;
    return p ? Math.max(1, p.plannedMinutes + p.unplannedTaskMinutes + p.reactiveMinutes + p.meetingMinutes) : 1;
  });

  constructor() {
    effect(() => {
      if (!this.date() && this.settings.settings()) this.date.set(this.settings.today());
    });
    effect(() => {
      this.timer.version();
      const d = this.date();
      if (d) untracked(() => this.load());
    });
  }

  async load(): Promise<void> {
    this.rollSkip.set(new Set());
    try {
      this.review.set(await this.api.getReview(this.date()));
    } catch (e) {
      this.toast.error(errorMessage(e));
    }
  }

  shift(days: number): void {
    this.editing.set(null);
    this.date.set(addDays(this.date(), days));
  }

  goToday(): void {
    this.editing.set(null);
    this.date.set(this.settings.today());
  }

  clock(iso: string | null): string {
    return iso ? formatClock(iso, this.settings.tz()) : 'now';
  }

  /** One step: pick a project (or untracked) and the gap becomes a time entry. */
  async fill(gap: Gap, choice: string, kind: TimeEntryKind = 'Task'): Promise<void> {
    if (!choice) return;
    const untrackedChoice = choice === 'untracked';
    try {
      await this.api.fillGap({
        start: gap.start,
        end: gap.end,
        projectId: untrackedChoice ? null : Number(choice),
        kind: untrackedChoice ? 'Untracked' : kind,
        note: null,
      });
      this.toast.show(`Filled ${formatMinutes(gap.minutes)}`);
      this.timer.bump();
    } catch (e) {
      this.toast.error(errorMessage(e));
    }
  }

  detailGap(gap: Gap): void {
    const tz = this.settings.tz();
    this.ui.entryEditor.set({ entry: null, date: this.date(), start: localTime(gap.start, tz), end: localTime(gap.end, tz) });
  }

  editEntry(e: TimeEntry): void {
    if (!e.endedAt) {
      this.toast.warning('Stop the running timer before editing it.');
      return;
    }
    this.ui.entryEditor.set({ entry: e, date: this.date() });
  }

  async deleteEntry(e: TimeEntry): Promise<void> {
    if (!confirm(`Delete ${formatMinutes(e.minutes)} on "${e.taskTitle ?? e.projectName ?? 'untracked'}"?`)) return;
    try {
      await this.api.deleteEntry(e.id);
      this.toast.show('Time entry deleted');
      this.timer.bump();
    } catch (err) {
      this.toast.error(errorMessage(err));
    }
  }

  toggleRoll(id: number): void {
    const next = new Set(this.rollSkip());
    if (next.has(id)) next.delete(id);
    else next.add(id);
    this.rollSkip.set(next);
  }

  /** End-of-day roll-over: ticked tasks move to the next work day (or back to the backlog). */
  async rollOver(toBacklog: boolean): Promise<void> {
    const ids = this.rollIds();
    if (!ids.length) return;
    this.rolling.set(true);
    try {
      const r = await this.api.rollOver(ids, toBacklog ? null : this.nextWorkday());
      this.toast.show(toBacklog
        ? `${r.moved} task${r.moved === 1 ? '' : 's'} back to the backlog`
        : `${r.moved} task${r.moved === 1 ? '' : 's'} moved to ${formatDateShort(this.nextWorkday())}`);
      await this.timer.refresh();
      this.timer.bump();
    } catch (e) {
      this.toast.error(errorMessage(e));
    } finally {
      this.rolling.set(false);
    }
  }

  /** Opens the entry that ran during a meeting so its times can be trimmed. */
  fixOverlap(o: Overlap): void {
    const e = this.review()?.entries.find((x) => x.id === o.entryId);
    if (e) this.editEntry(e);
  }

  openInterruption(i: Interruption): void {
    if (!i.endedAt) {
      this.timer.interruptionFormOpen.set(true);
      return;
    }
    const tz = this.settings.tz();
    this.edSource = i.sourceType;
    this.edPerson = i.personName ?? '';
    this.edNote = i.note ?? '';
    this.edCouldWait = i.couldWait;
    this.edProject = null;
    this.edStart = localTime(i.startedAt, tz);
    this.edEnd = localTime(i.endedAt, tz);
    this.editing.set(this.editing() === i.id ? null : i.id);
  }

  async saveInterruption(i: Interruption): Promise<boolean> {
    if (!this.edSource) {
      this.toast.error('Pick where the interruption came from.');
      return false;
    }
    try {
      await this.api.updateInterruption(i.id, {
        sourceType: this.edSource, personId: null, personName: this.edPerson.trim() || null,
        note: this.edNote.trim() || null, couldWait: this.edCouldWait,
      });
      const tz = this.settings.tz();
      const oldStart = localTime(i.startedAt, tz);
      const oldEnd = i.endedAt ? localTime(i.endedAt, tz) : '';
      if (this.edStart !== oldStart || this.edEnd !== oldEnd) {
        const s = zonedToUtc(this.date(), this.edStart, tz);
        let e = zonedToUtc(this.date(), this.edEnd, tz);
        if (e <= s) e = zonedToUtc(addDays(this.date(), 1), this.edEnd, tz);
        await this.api.setInterruptionTimes(i.id, s.toISOString(), e.toISOString());
      }
      return true;
    } catch (err) {
      this.toast.error(errorMessage(err));
      return false;
    }
  }

  async save(i: Interruption): Promise<void> {
    if (await this.saveInterruption(i)) {
      this.toast.show('Interruption saved');
      this.editing.set(null);
      this.timer.bump();
    }
  }

  async convert(i: Interruption): Promise<void> {
    if (!(await this.saveInterruption(i))) return;
    try {
      const r = await this.api.convertInterruption(i.id, { title: null, projectId: this.edProject, status: 'Done' });
      this.toast.show(`Task "${r.task.title}" created with ${formatMinutes(r.entry.minutes)} reactive time`);
      this.editing.set(null);
      this.timer.bump();
    } catch (err) {
      this.toast.error(errorMessage(err));
    }
  }

  async deleteInterruption(i: Interruption): Promise<void> {
    if (!confirm('Delete this interruption?')) return;
    try {
      await this.api.deleteInterruption(i.id);
      this.toast.show('Interruption deleted');
      this.editing.set(null);
      this.timer.bump();
    } catch (err) {
      this.toast.error(errorMessage(err));
    }
  }
}
