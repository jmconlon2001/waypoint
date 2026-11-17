import { Injectable, signal } from '@angular/core';
import { TimeEntry, WorkTask } from './models';

/** Shell-level modal state shared between pages. */
@Injectable({ providedIn: 'root' })
export class UiStore {
  /** Task editor: a task to edit, 'new' for a blank one, or null when closed. */
  readonly taskEditor = signal<WorkTask | 'new' | null>(null);
  /** Time entry editor: an entry, a prefilled draft, or null. */
  readonly entryEditor = signal<{ entry: TimeEntry | null; date: string; start?: string; end?: string } | null>(null);
  readonly shortcutsOpen = signal(false);
  readonly navOpen = signal(false);
  /** Bumped to ask the Today page to focus quick add. */
  readonly focusQuickAdd = signal(0);
}
