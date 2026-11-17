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
      interruptionMinutes: 10, unloggedMinutes: 15, byProject: [], meetingMinutes: 30, meetingCount: 1, plannedFocusMinutes: 0,
      focusBlockCount: 1, focusBlockMinutes: 60, longestFocusBlockMinutes: 60, taskSwitches: 0 },
    meetings: [
      { id: 9, title: 'Standup', startedAt: '2026-11-17T16:00:00Z', endedAt: '2026-11-17T16:30:00Z', isAllDay: false, attendeeCount: 4,
        isRecurring: true, isFree: false, meetingType: 'Team', projectId: null, projectName: null, projectColor: null,
        countsAsMeeting: true, isPlannedFocus: false, excludedReason: null, minutes: 30 },
      { id: 10, title: 'OOO', startedAt: '2026-11-17T05:00:00Z', endedAt: '2026-11-18T05:00:00Z', isAllDay: true, attendeeCount: 0,
        isRecurring: false, isFree: false, meetingType: 'AdHoc', projectId: null, projectName: null, projectColor: null,
        countsAsMeeting: false, isPlannedFocus: false, excludedReason: 'All-day', minutes: 1440 },
    ],
    overlaps: [],
    focusBlocks: [],
    unfinished: [],
    plan: { date: '2026-11-17', planned: [], plannedCount: 0, plannedDoneCount: 0, plannedMinutes: 0, unplannedTaskMinutes: 0,
      reactiveMinutes: 0, meetingMinutes: 30, unplanned: [] },
  };
}

describe('timeline', () => {
  it('orders entries, interruptions and gaps by start time', () => {
    expect(buildTimeline(review()).map((i) => i.type)).toEqual(['gap', 'entry', 'interruption', 'meeting']);
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

  it('draws counted meetings on the ribbon but leaves all-day events out', () => {
    const rb = ribbonSegments(review(), Date.parse('2026-11-17T23:00:00Z'), 'America/New_York');
    const meetings = rb.segments.filter((s) => s.kind === 'meeting');
    expect(meetings.length).toBe(1);
    expect(meetings[0].title).toContain('Standup');
    expect(buildTimeline(review()).some((i) => i.type === 'meeting' && i.meeting.isAllDay)).toBe(false);
  });
});
