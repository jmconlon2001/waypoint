import { Component, DestroyRef, HostListener, OnInit, computed, inject } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet, NavigationEnd } from '@angular/router';
import { filter } from 'rxjs';
import { errorMessage } from './core/api.service';
import { SettingsStore } from './core/settings.store';
import { ThemeService } from './core/theme.service';
import { TimerStore } from './core/timer.store';
import { ToastService } from './core/toast.service';
import { UiStore } from './core/ui.store';
import { isTypingTarget } from './shortcuts';
import { localTime } from './core/time';
import { InterruptionForm } from './shared/interruption-form';
import { TaskEditor } from './shared/task-editor';
import { EntryEditor } from './shared/entry-editor';
import { ShortcutsHelp } from './shared/shortcuts-help';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, InterruptionForm, TaskEditor, EntryEditor, ShortcutsHelp],
  templateUrl: './app.html',
})
export class App implements OnInit {
  protected readonly timer = inject(TimerStore);
  protected readonly settings = inject(SettingsStore);
  protected readonly theme = inject(ThemeService);
  protected readonly toast = inject(ToastService);
  protected readonly ui = inject(UiStore);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly anyModalOpen = computed(
    () => this.timer.interruptionFormOpen() || this.ui.taskEditor() !== null || this.ui.entryEditor() !== null || this.ui.shortcutsOpen());

  constructor() {
    this.router.events.pipe(filter((e) => e instanceof NavigationEnd)).subscribe(() => this.ui.navOpen.set(false));
  }

  async ngOnInit(): Promise<void> {
    try {
      await this.settings.load();
    } catch (e) {
      this.toast.error(errorMessage(e));
    }
    await this.timer.refresh(true);
    this.startReviewReminder();
  }

  /**
   * Daily review reminder (FR-R4): once a day, at or after the configured time, a toast (and a desktop
   * notification if allowed). Only while Waypoint is open; nothing runs in the background.
   */
  private startReviewReminder(): void {
    const key = 'wp-review-reminded';
    const check = () => {
      const at = this.settings.settings()?.reviewReminderTime;
      if (!at) return;
      const today = this.settings.today();
      if (localStorage.getItem(key) === today || localTime(new Date(), this.settings.tz()) < at) return;
      localStorage.setItem(key, today);
      if (this.router.url.startsWith('/review')) return;
      this.toast.show('Time for the daily review: fill the gaps and roll over what is left.');
      if ('Notification' in window && Notification.permission === 'granted') {
        const n = new Notification('Waypoint: daily review', { body: 'Fill the gaps and roll over what is left.', tag: 'wp-review' });
        n.onclick = () => { window.focus(); this.router.navigateByUrl('/review'); n.close(); };
      }
    };
    check();
    const id = setInterval(check, 60_000);
    this.destroyRef.onDestroy(() => clearInterval(id));
  }

  @HostListener('document:keydown', ['$event'])
  onKey(e: KeyboardEvent): void {
    if (e.key === 'Escape') {
      if (this.ui.shortcutsOpen()) this.ui.shortcutsOpen.set(false);
      else if (this.ui.entryEditor()) this.ui.entryEditor.set(null);
      else if (this.ui.taskEditor()) this.ui.taskEditor.set(null);
      else if (this.timer.interruptionFormOpen()) this.timer.interruptionFormOpen.set(false);
      return;
    }
    if (e.ctrlKey || e.metaKey || e.altKey || isTypingTarget(e.target) || this.anyModalOpen()) return;

    switch (e.key) {
      case 's': case 'S': this.timer.toggle(); break;
      case 'i': case 'I': this.timer.interrupt(); break;
      case 'b': case 'B': this.timer.backToWork(); break;
      case '1': case '2': case '3': case '4': case '5': this.timer.startPinned(Number(e.key)); break;
      case 'n': case 'N': this.ui.taskEditor.set('new'); break;
      case 'l': case 'L': this.ui.entryEditor.set({ entry: null, date: this.settings.today() }); break;
      case 'q': case 'Q':
        if (this.router.url !== '/') this.router.navigateByUrl('/');
        this.ui.focusQuickAdd.update((v) => v + 1);
        break;
      case '?': this.ui.shortcutsOpen.set(true); break;
      default: return;
    }
    e.preventDefault();
  }
}
