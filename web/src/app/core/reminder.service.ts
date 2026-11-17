import { Injectable, effect, inject } from '@angular/core';
import { SettingsStore } from './settings.store';
import { zonedParts } from './time';

const KEY = 'wp-review-reminded';

/**
 * FR-R4: a browser notification at the configured time reminding you to do the daily review.
 * Uses the Notification API (through the service worker when installed as a PWA). It only fires
 * while the app is open or installed and running; there is no server push.
 */
@Injectable({ providedIn: 'root' })
export class ReminderService {
  private readonly settings = inject(SettingsStore);
  private timer: ReturnType<typeof setInterval> | undefined;

  constructor() {
    effect(() => {
      const s = this.settings.settings();
      clearInterval(this.timer);
      if (!s?.reviewReminderEnabled) return;
      this.check();
      this.timer = setInterval(() => this.check(), 30_000);
    });
  }

  get supported(): boolean {
    return typeof window !== 'undefined' && 'Notification' in window;
  }

  get permission(): NotificationPermission | 'unsupported' {
    return this.supported ? Notification.permission : 'unsupported';
  }

  async requestPermission(): Promise<NotificationPermission | 'unsupported'> {
    if (!this.supported) return 'unsupported';
    return Notification.requestPermission();
  }

  /** Fires once per local day, at or after the reminder time, during the working day. */
  check(now = new Date()): boolean {
    const s = this.settings.settings();
    if (!s?.reviewReminderEnabled || this.permission !== 'granted') return false;
    const p = zonedParts(now, this.settings.tz());
    const today = `${p.year}-${String(p.month).padStart(2, '0')}-${String(p.day).padStart(2, '0')}`;
    const minutes = p.hour * 60 + p.minute;
    const [h, m] = s.reviewReminderTime.split(':').map(Number);
    if (minutes < h * 60 + m || p.hour >= 23) return false;
    if (localStorage.getItem(KEY) === today) return false;
    localStorage.setItem(KEY, today);
    void this.show('Time for your daily review', 'Fill the gaps and roll unfinished tasks to tomorrow. It takes two minutes.');
    return true;
  }

  async show(title: string, body: string): Promise<void> {
    if (this.permission !== 'granted') return;
    const options: NotificationOptions & { data?: unknown } = {
      body,
      icon: 'icons/icon-192x192.png',
      tag: 'waypoint-review',
      // Handled by the Angular service worker: clicking opens/focuses the review page.
      data: { onActionClick: { default: { operation: 'navigateLastFocusedOrOpen', url: '/review' } } },
    };
    try {
      const reg = await navigator.serviceWorker?.getRegistration();
      if (reg) {
        await reg.showNotification(title, options);
        return;
      }
    } catch { /* fall back to a page notification */ }
    const n = new Notification(title, options);
    n.onclick = () => {
      window.focus();
      window.location.assign('/review');
      n.close();
    };
  }
}
