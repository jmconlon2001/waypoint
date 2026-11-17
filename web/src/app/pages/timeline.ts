import { Gap, Interruption, Review, TimeEntry } from '../core/models';
import { formatClock } from '../core/time';

export type TimelineItem =
  | { type: 'entry'; start: string; end: string | null; entry: TimeEntry }
  | { type: 'interruption'; start: string; end: string | null; interruption: Interruption }
  | { type: 'gap'; start: string; end: string; gap: Gap };

/** Entries, interruptions and gaps of a day in chronological order. */
export function buildTimeline(r: Review): TimelineItem[] {
  const items: TimelineItem[] = [
    ...r.entries.map((e) => ({ type: 'entry' as const, start: e.startedAt, end: e.endedAt, entry: e })),
    ...r.interruptions.map((i) => ({ type: 'interruption' as const, start: i.startedAt, end: i.endedAt, interruption: i })),
    ...r.gaps.map((g) => ({ type: 'gap' as const, start: g.start, end: g.end, gap: g })),
  ];
  const order = { entry: 0, interruption: 1, gap: 2 };
  return items.sort((a, b) => a.start.localeCompare(b.start) || order[a.type] - order[b.type]);
}

export interface RibbonSegment {
  kind: 'entry' | 'interruption' | 'gap' | 'now';
  left: number;
  width: number;
  color: string | null;
  entryKind?: string;
  title: string;
}

export interface Ribbon {
  segments: RibbonSegment[];
  ticks: { label: string; left: number }[];
}

/**
 * Positions for the day ribbon. The window is the work hours, widened to include anything logged
 * outside them (clipped to the day).
 */
export function ribbonSegments(r: Review, nowMs: number, tz: string): Ribbon {
  const dayStart = Date.parse(r.dayStart);
  const dayEnd = Date.parse(r.dayEnd);
  const clamp = (t: number) => Math.min(dayEnd, Math.max(dayStart, t));
  const endOf = (iso: string | null) => clamp(iso ? Date.parse(iso) : Math.min(nowMs, dayEnd));

  let from = Date.parse(r.workStart);
  let to = Date.parse(r.workEnd);
  for (const x of [...r.entries.map((e) => [e.startedAt, e.endedAt] as const), ...r.interruptions.map((i) => [i.startedAt, i.endedAt] as const)]) {
    from = Math.min(from, clamp(Date.parse(x[0])));
    to = Math.max(to, endOf(x[1]));
  }
  // Round the window out to whole hours for readable ticks.
  const hour = 3_600_000;
  const offset = from - Date.parse(new Date(from).toISOString().slice(0, 13) + ':00:00Z');
  from -= offset % hour;
  to = from + Math.ceil((to - from) / hour) * hour;
  const span = Math.max(1, to - from);
  const pct = (t: number) => ((t - from) / span) * 100;

  const segments: RibbonSegment[] = [];
  for (const g of r.gaps) {
    const s = Date.parse(g.start), e = Date.parse(g.end);
    segments.push({ kind: 'gap', left: pct(s), width: pct(e) - pct(s), color: null, title: `Unlogged ${formatClock(g.start, tz)}–${formatClock(g.end, tz)}` });
  }
  for (const en of r.entries) {
    const s = clamp(Date.parse(en.startedAt)), e = endOf(en.endedAt);
    segments.push({
      kind: 'entry', left: pct(s), width: pct(e) - pct(s), color: en.projectColor, entryKind: en.kind.toLowerCase(),
      title: `${en.taskTitle ?? en.projectName ?? 'Untracked'} ${formatClock(en.startedAt, tz)}–${en.endedAt ? formatClock(en.endedAt, tz) : 'now'}`,
    });
  }
  for (const i of r.interruptions) {
    const s = clamp(Date.parse(i.startedAt)), e = endOf(i.endedAt);
    segments.push({ kind: 'interruption', left: pct(s), width: pct(e) - pct(s), color: null, title: `Interruption ${formatClock(i.startedAt, tz)}–${i.endedAt ? formatClock(i.endedAt, tz) : 'now'}` });
  }
  if (nowMs > from && nowMs < to) segments.push({ kind: 'now', left: pct(nowMs), width: 0, color: null, title: 'Now' });

  const hours = Math.round(span / hour);
  const step = hours > 12 ? 3 : hours > 6 ? 2 : 1;
  const ticks: { label: string; left: number }[] = [];
  for (let h = 0; h <= hours; h += step) {
    const t = from + h * hour;
    ticks.push({ label: formatClock(new Date(t).toISOString(), tz).replace(':00', ''), left: pct(t) });
  }
  return { segments, ticks };
}
