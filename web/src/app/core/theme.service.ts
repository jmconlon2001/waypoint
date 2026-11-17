import { Injectable, signal } from '@angular/core';

export type Theme = 'light' | 'dark';
const KEY = 'wp-theme';

/** Light/dark via Bootstrap's data-bs-theme. Follows the OS until the user picks one (saved in localStorage). */
@Injectable({ providedIn: 'root' })
export class ThemeService {
  readonly theme = signal<Theme>((document.documentElement.getAttribute('data-bs-theme') as Theme) || 'light');

  constructor() {
    const mq = window.matchMedia?.('(prefers-color-scheme: dark)');
    mq?.addEventListener?.('change', (e) => {
      if (!this.saved()) this.apply(e.matches ? 'dark' : 'light');
    });
  }

  toggle(): void {
    const next: Theme = this.theme() === 'dark' ? 'light' : 'dark';
    try { localStorage.setItem(KEY, next); } catch { /* private mode */ }
    this.apply(next);
  }

  /** Forget the saved choice and follow the OS again. */
  followSystem(): void {
    try { localStorage.removeItem(KEY); } catch { /* ignore */ }
    this.apply(window.matchMedia?.('(prefers-color-scheme: dark)').matches ? 'dark' : 'light');
  }

  saved(): Theme | null {
    try { return localStorage.getItem(KEY) as Theme | null; } catch { return null; }
  }

  private apply(t: Theme): void {
    document.documentElement.setAttribute('data-bs-theme', t);
    this.theme.set(t);
  }
}
