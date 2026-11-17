import { Component, HostListener, OnInit, computed, inject } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet, NavigationEnd } from '@angular/router';
import { filter } from 'rxjs';
import { errorMessage } from './core/api.service';
import { SettingsStore } from './core/settings.store';
import { ThemeService } from './core/theme.service';
import { TimerStore } from './core/timer.store';
import { ToastService } from './core/toast.service';
import { UiStore } from './core/ui.store';
import { ReminderService } from './core/reminder.service';
import { isTypingTarget } from './shortcuts';
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
  // Started here so the daily-review reminder runs on every page.
  private readonly reminder = inject(ReminderService);

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
