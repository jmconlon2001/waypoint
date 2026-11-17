/** Single-key shortcuts (ignored while typing in a field). */
export const SHORTCUTS: { keys: string[]; label: string }[] = [
  { keys: ['s'], label: 'Start / stop the timer (resumes the last task)' },
  { keys: ['1', '–', '5'], label: 'Switch the timer to pinned task 1-5' },
  { keys: ['i'], label: 'Interrupted: pause and log an interruption' },
  { keys: ['b'], label: 'Back to work: resume the paused task' },
  { keys: ['q'], label: 'Quick add a task (Today page)' },
  { keys: ['n'], label: 'New task with all fields' },
  { keys: ['l'], label: 'Log time after the fact' },
  { keys: ['?'], label: 'Show this help' },
  { keys: ['Esc'], label: 'Close a dialog' },
];

export function isTypingTarget(target: EventTarget | null): boolean {
  const el = target as HTMLElement | null;
  if (!el) return false;
  const tag = el.tagName;
  return tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT' || el.isContentEditable;
}
