import { addDays, formatElapsed, formatMinutes, localDate, localTime, zonedToUtc } from './time';

describe('time helpers', () => {
  const tz = 'America/New_York';

  it('converts wall-clock time in the zone to UTC (EST and EDT)', () => {
    expect(zonedToUtc('2026-11-17', '08:45', tz).toISOString()).toBe('2026-11-17T13:45:00.000Z');
    expect(zonedToUtc('2026-07-01', '08:45', tz).toISOString()).toBe('2026-07-01T12:45:00.000Z');
  });

  it('round-trips local date and time', () => {
    const d = new Date('2026-11-18T04:30:00Z'); // 23:30 on the 17th in New York
    expect(localDate(d, tz)).toBe('2026-11-17');
    expect(localTime(d, tz)).toBe('23:30');
  });

  it('adds days across month ends', () => {
    expect(addDays('2026-11-30', 1)).toBe('2026-12-01');
    expect(addDays('2026-03-01', -1)).toBe('2026-02-28');
  });

  it('formats durations', () => {
    expect(formatElapsed(65_000)).toBe('1:05');
    expect(formatElapsed(3_725_000)).toBe('1:02:05');
    expect(formatMinutes(125)).toBe('2h 05m');
    expect(formatMinutes(44.6)).toBe('45m');
  });
});
