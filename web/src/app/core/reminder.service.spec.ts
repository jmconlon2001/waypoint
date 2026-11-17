import { TestBed } from '@angular/core/testing';
import { ReminderService } from './reminder.service';
import { SettingsStore } from './settings.store';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';

describe('ReminderService', () => {
  let shown: string[];

  beforeEach(() => {
    shown = [];
    localStorage.removeItem('wp-review-reminded');
    (globalThis as unknown as { Notification: unknown }).Notification = Object.assign(
      function (this: unknown, title: string) { shown.push(title); return { close() {} }; },
      { permission: 'granted', requestPermission: async () => 'granted' },
    );
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    TestBed.inject(SettingsStore).settings.set({
      workdayStart: '08:45', workdayEnd: '17:30', timerWarningMinutes: 180, timeZone: 'America/New_York', projectColors: [],
      calendarSource: 'None', maxMeetingMinutes: 120, internalDomains: '', reviewReminderEnabled: true, reviewReminderTime: '17:15',
    });
  });

  it('fires once per local day at or after the reminder time', () => {
    const svc = TestBed.inject(ReminderService);
    expect(svc.check(new Date('2026-11-17T22:10:00Z'))).toBe(false); // 17:10 New York
    expect(svc.check(new Date('2026-11-17T22:16:00Z'))).toBe(true);  // 17:16
    expect(svc.check(new Date('2026-11-17T22:30:00Z'))).toBe(false); // already reminded today
    expect(svc.check(new Date('2026-11-18T22:20:00Z'))).toBe(true);  // next day
  });
});
