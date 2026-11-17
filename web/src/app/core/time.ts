// Time helpers. The API stores UTC; the UI shows times in the configured IANA time zone
// (default America/New_York), regardless of the browser's own zone.

export const DEFAULT_TZ = 'America/New_York';

interface Parts { year: number; month: number; day: number; hour: number; minute: number; second: number; }

const partsCache = new Map<string, Intl.DateTimeFormat>();
function partsFormatter(tz: string): Intl.DateTimeFormat {
  let f = partsCache.get(tz);
  if (!f) {
    f = new Intl.DateTimeFormat('en-US', {
      timeZone: tz, hourCycle: 'h23', year: 'numeric', month: '2-digit', day: '2-digit',
      hour: '2-digit', minute: '2-digit', second: '2-digit',
    });
    partsCache.set(tz, f);
  }
  return f;
}

/** Wall-clock parts of an instant in a time zone. */
export function zonedParts(date: Date, tz: string): Parts {
  const out: Record<string, number> = {};
  for (const p of partsFormatter(tz).formatToParts(date)) if (p.type !== 'literal') out[p.type] = Number(p.value);
  return { year: out['year'], month: out['month'], day: out['day'], hour: out['hour'] % 24, minute: out['minute'], second: out['second'] };
}

/** Offset of the zone from UTC at an instant, in milliseconds (e.g. -5h for EST). */
function offsetMs(date: Date, tz: string): number {
  const p = zonedParts(date, tz);
  const asUtc = Date.UTC(p.year, p.month - 1, p.day, p.hour, p.minute, p.second);
  return asUtc - Math.floor(date.getTime() / 1000) * 1000;
}

const pad = (n: number) => String(n).padStart(2, '0');

/** "yyyy-MM-dd" for the local date of an instant in the zone. */
export function localDate(date: Date, tz: string): string {
  const p = zonedParts(date, tz);
  return `${p.year}-${pad(p.month)}-${pad(p.day)}`;
}

/** "HH:mm" for the local time of an instant in the zone. */
export function localTime(date: Date | string, tz: string): string {
  const p = zonedParts(typeof date === 'string' ? new Date(date) : date, tz);
  return `${pad(p.hour)}:${pad(p.minute)}`;
}

/** UTC instant for a wall-clock date ("yyyy-MM-dd") and time ("HH:mm") in the zone. */
export function zonedToUtc(date: string, time: string, tz: string): Date {
  const [y, m, d] = date.split('-').map(Number);
  const [hh, mm] = time.split(':').map(Number);
  const guess = Date.UTC(y, m - 1, d, hh, mm);
  let result = guess - offsetMs(new Date(guess), tz);
  // Re-check once: the offset can differ at the corrected instant around DST changes.
  const second = guess - offsetMs(new Date(result), tz);
  if (second !== result) result = second;
  return new Date(result);
}

/** Adds days to a "yyyy-MM-dd" date. */
export function addDays(date: string, days: number): string {
  const [y, m, d] = date.split('-').map(Number);
  const t = new Date(Date.UTC(y, m - 1, d + days));
  return `${t.getUTCFullYear()}-${pad(t.getUTCMonth() + 1)}-${pad(t.getUTCDate())}`;
}

/** "1:05:09" style elapsed time (always shows minutes and seconds). */
export function formatElapsed(ms: number): string {
  const total = Math.max(0, Math.floor(ms / 1000));
  const h = Math.floor(total / 3600);
  const m = Math.floor((total % 3600) / 60);
  const s = total % 60;
  return h > 0 ? `${h}:${pad(m)}:${pad(s)}` : `${m}:${pad(s)}`;
}

/** "2h 05m" / "45m" / "0m" for durations in minutes. */
export function formatMinutes(minutes: number | null | undefined): string {
  const total = Math.max(0, Math.round(minutes ?? 0));
  const h = Math.floor(total / 60);
  const m = total % 60;
  return h > 0 ? `${h}h ${pad(m)}m` : `${m}m`;
}

/** "Tue, Nov 17" for a "yyyy-MM-dd" date. */
export function formatDateLong(date: string): string {
  const [y, m, d] = date.split('-').map(Number);
  return new Intl.DateTimeFormat('en-US', { weekday: 'long', month: 'long', day: 'numeric', timeZone: 'UTC' })
    .format(new Date(Date.UTC(y, m - 1, d)));
}

export function formatDateShort(date: string | null): string {
  if (!date) return '';
  const [y, m, d] = date.split('-').map(Number);
  return new Intl.DateTimeFormat('en-US', { month: 'short', day: 'numeric', timeZone: 'UTC' }).format(new Date(Date.UTC(y, m - 1, d)));
}

/** "9:05 AM" in the zone. */
export function formatClock(iso: string, tz: string): string {
  return new Intl.DateTimeFormat('en-US', { timeZone: tz, hour: 'numeric', minute: '2-digit' }).format(new Date(iso));
}
