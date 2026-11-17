import { Review } from '../core/models';
import { buildTimeline, ribbonSegments } from './timeline';

function review(): Review {
  return {
    date: '2026-11-17',
    timeZone: 'America/New_York',
    dayStart: '2026-11-17T05:00:00Z',
    dayEnd: '2026-11-18T05:00:00Z',
    workStart: '2026-11-17T13:45:00Z',
    workEnd: '2026-11-17T22:30:00Z',
    entries: [
      { id: 1, taskId: 1, taskTitle: 'Memo', projectId: 1, projectName: 'SOX/J-SOX', projectColor: '#e11d48', kind: 'Task',
        startedAt: '2026-11-17T14:00:00Z', endedAt: '2026-11-17T15:00:00Z', note: null, minutes: 60 },
    ],
    interruptions: [
      { id: 1, startedAt: '2026-11-17T15:00:00Z', endedAt: '2026-11-17T15:10:00Z', sourceType: 'WalkUp', personId: null, personName: null,
        note: null, couldWait: null, pausedTimeEntryId: 1, pausedTaskId: 1, pausedTaskTitle: 'Memo', convertedTaskId: null, minutes: 10 },
    ],
    gaps: [{ start: '2026-11-17T13:45:00Z', end: '2026-11-17T14:00:00Z', minutes: 15 }],
    totals: { date: '2026-11-17', taskMinutes: 60, reactiveMinutes: 0, untrackedMinutes: 0, trackedMinutes: 60, interruptionCount: 1,
      interruptionMinutes: 10, unloggedMinutes: 15, byProject: [] },
  };
}

describe('timeline', () => {
  it('orders entries, interruptions and gaps by start time', () => {
    expect(buildTimeline(review()).map((i) => i.type)).toEqual(['gap', 'entry', 'interruption']);
  });

  it('lays segments out within the work-hours window', () => {
    const rb = ribbonSegments(review(), Date.parse('2026-11-17T23:00:00Z'), 'America/New_York');
    for (const s of rb.segments) {
      expect(s.left).toBeGreaterThanOrEqual(0);
      expect(s.left + s.width).toBeLessThanOrEqual(100.0001);
    }
    const entry = rb.segments.find((s) => s.kind === 'entry')!;
    const gap = rb.segments.find((s) => s.kind === 'gap')!;
    expect(gap.left).toBeLessThan(entry.left);
    expect(rb.ticks[0].label).toBe('8 AM');
  });
});
