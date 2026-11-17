// Pure helpers for the weekly page (unit-tested).
import { addDays } from '../core/time';

export const WEEKDAYS = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];

/** Monday of the week containing a "yyyy-MM-dd" date. */
export function mondayOf(date: string): string {
  const [y, m, d] = date.split('-').map(Number);
  const dow = new Date(Date.UTC(y, m - 1, d)).getUTCDay(); // 0 = Sunday
  return addDays(date, -((dow + 6) % 7));
}

export interface HeatmapView {
  hours: number[];
  rows: { label: string; cells: { hour: number; count: number; v: number }[] }[];
  max: number;
}

/**
 * Shapes the [weekday][hour] grid for display: always shows 07-19, widened to any hour with data;
 * v is the intensity 0-1 relative to the busiest cell.
 */
export function heatmapView(grid: number[][], minHour = 7, maxHour = 19): HeatmapView {
  let lo = minHour;
  let hi = maxHour;
  let max = 0;
  grid.forEach((row) => row.forEach((c, h) => {
    if (c > 0) { lo = Math.min(lo, h); hi = Math.max(hi, h); max = Math.max(max, c); }
  }));
  const hours = Array.from({ length: hi - lo + 1 }, (_, i) => lo + i);
  return {
    hours,
    max,
    rows: WEEKDAYS.map((label, d) => ({
      label,
      cells: hours.map((h) => {
        const count = grid[d]?.[h] ?? 0;
        return { hour: h, count, v: count && max ? Math.max(0.12, count / max) : 0 };
      }),
    })),
  };
}

/** "9a", "12p", "3p" */
export function hourLabel(h: number): string {
  if (h === 0) return '12a';
  if (h === 12) return '12p';
  return h < 12 ? `${h}a` : `${h - 12}p`;
}

/** Share as a whole percentage (0 when the total is 0). */
export function percent(part: number, total: number): number {
  return total > 0 ? Math.round((part / total) * 100) : 0;
}
