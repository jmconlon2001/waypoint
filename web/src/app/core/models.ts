// Wire types for the Waypoint API. DateTime strings are UTC ISO 8601; DateOnly strings are "yyyy-MM-dd".

export type TaskStatus = 'Backlog' | 'Today' | 'Doing' | 'Blocked' | 'Done';
export type TimeEntryKind = 'Task' | 'Reactive' | 'Untracked';
export type InterruptionSource = 'WalkUp' | 'GoogleChat' | 'Email' | 'Phone' | 'MeetingOverrun' | 'SelfDistraction';

export const TASK_STATUSES: TaskStatus[] = ['Backlog', 'Today', 'Doing', 'Blocked', 'Done'];

export const INTERRUPTION_SOURCES: { value: InterruptionSource; label: string; icon: string }[] = [
  { value: 'WalkUp', label: 'Walk-up', icon: 'i-user' },
  { value: 'GoogleChat', label: 'Google Chat', icon: 'i-message' },
  { value: 'Email', label: 'Email', icon: 'i-mail' },
  { value: 'Phone', label: 'Phone', icon: 'i-phone' },
  { value: 'MeetingOverrun', label: 'Meeting overrun', icon: 'i-hourglass' },
  { value: 'SelfDistraction', label: 'Self-distraction', icon: 'i-smartphone' },
];

export function sourceLabel(s: InterruptionSource | null | undefined): string {
  return INTERRUPTION_SOURCES.find((x) => x.value === s)?.label ?? 'Unclassified';
}

export interface Project {
  id: number;
  name: string;
  color: string;
  active: boolean;
}

export interface Person {
  id: number;
  name: string;
}

export interface WorkTask {
  id: number;
  title: string;
  projectId: number | null;
  projectName: string | null;
  projectColor: string | null;
  priority: number;
  dueDate: string | null;
  notes: string | null;
  status: TaskStatus;
  plannedDate: string | null;
  pinSlot: number | null;
  createdAt: string;
  completedAt: string | null;
  trackedMinutes: number;
}

export interface TaskInput {
  title: string;
  projectId: number | null;
  priority: number;
  dueDate: string | null;
  notes: string | null;
  status: TaskStatus | null;
  plannedDate: string | null;
}

export interface QuickAddResponse {
  task: WorkTask;
  warning: string | null;
}

export interface TimeEntry {
  id: number;
  taskId: number | null;
  taskTitle: string | null;
  projectId: number | null;
  projectName: string | null;
  projectColor: string | null;
  kind: TimeEntryKind;
  startedAt: string;
  endedAt: string | null;
  note: string | null;
  minutes: number;
}

export interface TimeEntryInput {
  taskId: number | null;
  projectId: number | null;
  kind: TimeEntryKind;
  startedAt: string;
  endedAt: string;
  note: string | null;
}

export interface Interruption {
  id: number;
  startedAt: string;
  endedAt: string | null;
  sourceType: InterruptionSource | null;
  personId: number | null;
  personName: string | null;
  note: string | null;
  couldWait: boolean | null;
  pausedTimeEntryId: number | null;
  pausedTaskId: number | null;
  pausedTaskTitle: string | null;
  convertedTaskId: number | null;
  minutes: number;
}

export interface InterruptionDetails {
  sourceType: InterruptionSource | null;
  personId: number | null;
  personName: string | null;
  note: string | null;
  couldWait: boolean | null;
}

export interface PinnedTask {
  slot: number;
  taskId: number;
  title: string;
  projectColor: string | null;
}

export interface TimerState {
  running: TimeEntry | null;
  warnings: string[];
  openInterruption: Interruption | null;
  lastTask: { id: number; title: string } | null;
  pinned: PinnedTask[];
  warningThresholdMinutes: number;
  serverNow: string;
}

export interface ResumeResponse {
  interruption: Interruption;
  resumed: TimeEntry | null;
}

export interface ConvertResponse {
  interruption: Interruption;
  task: WorkTask;
  entry: TimeEntry;
}

export interface Settings {
  workdayStart: string;
  workdayEnd: string;
  timerWarningMinutes: number;
  timeZone: string;
  projectColors: string[];
  meetingMaxMinutes: number;
  reviewReminderTime: string | null;
}

export type SettingsInput = Omit<Settings, 'projectColors'>;

export interface Gap {
  start: string;
  end: string;
  minutes: number;
}

export interface ProjectTotal {
  projectId: number | null;
  name: string;
  color: string | null;
  minutes: number;
}

export interface DailyTotals {
  date: string;
  taskMinutes: number;
  reactiveMinutes: number;
  untrackedMinutes: number;
  trackedMinutes: number;
  interruptionCount: number;
  interruptionMinutes: number;
  unloggedMinutes: number;
  byProject: ProjectTotal[];
  meetingMinutes: number;
  meetingCount: number;
  plannedFocusMinutes: number;
  focusBlockCount: number;
  focusBlockMinutes: number;
  longestFocusBlockMinutes: number;
  taskSwitches: number;
}

export interface Review {
  date: string;
  timeZone: string;
  dayStart: string;
  dayEnd: string;
  workStart: string;
  workEnd: string;
  entries: TimeEntry[];
  interruptions: Interruption[];
  gaps: Gap[];
  totals: DailyTotals;
  meetings: CalendarEvent[];
  overlaps: Overlap[];
  focusBlocks: FocusBlock[];
  unfinished: WorkTask[];
  plan: PlannedVsActual;
}

// ---- Phase 2 ----

export type MeetingType = 'OneOnOne' | 'Team' | 'External' | 'Recurring' | 'AdHoc' | 'Focus';

export const MEETING_TYPE_LABELS: Record<MeetingType, string> = {
  OneOnOne: '1:1', Team: 'Team', External: 'External', Recurring: 'Recurring', AdHoc: 'Ad hoc', Focus: 'Focus time',
};

export interface CalendarEvent {
  id: number;
  title: string;
  startedAt: string;
  endedAt: string;
  isAllDay: boolean;
  attendeeCount: number;
  isRecurring: boolean;
  isFree: boolean;
  meetingType: MeetingType;
  projectId: number | null;
  projectName: string | null;
  projectColor: string | null;
  countsAsMeeting: boolean;
  isPlannedFocus: boolean;
  excludedReason: string | null;
  minutes: number;
}

export interface CalendarStatus {
  configured: boolean;
  addressHint: string | null;
  internalDomains: string | null;
  lastSyncAt: string | null;
  lastError: string | null;
  eventCount: number;
  syncIntervalMinutes: number;
}

export interface SyncResult {
  added: number;
  updated: number;
  removed: number;
  total: number;
  syncedAt: string;
  status: CalendarStatus;
}

export interface MeetingRule {
  id: number;
  titlePattern: string;
  projectId: number;
  projectName: string;
  projectColor: string;
  matchCount: number;
}

export interface Overlap {
  entryId: number;
  title: string;
  meetingId: number;
  meetingTitle: string;
  minutes: number;
}

export interface FocusBlock {
  start: string;
  end: string;
  minutes: number;
  taskId: number;
  taskTitle: string | null;
}

export interface PlannedTask {
  taskId: number;
  title: string;
  projectName: string | null;
  projectColor: string | null;
  status: TaskStatus;
  done: boolean;
  minutes: number;
}

export interface PlannedVsActual {
  date: string;
  planned: PlannedTask[];
  plannedCount: number;
  plannedDoneCount: number;
  plannedMinutes: number;
  unplannedTaskMinutes: number;
  reactiveMinutes: number;
  meetingMinutes: number;
  unplanned: { taskId: number | null; title: string; projectColor: string | null; minutes: number }[];
}

export interface CountMinutes {
  key: string;
  label: string;
  count: number;
  minutes: number;
}

export interface InterruptionStats {
  from: string;
  to: string;
  count: number;
  minutes: number;
  couldWaitCount: number;
  urgentCount: number;
  bySource: CountMinutes[];
  byPerson: CountMinutes[];
  byHour: number[];
  /** [weekday 0 = Monday .. 6 = Sunday][hour 0-23] */
  heatmap: number[][];
  byDay: CountMinutes[];
}

export interface DayBreakdown {
  date: string;
  focusMinutes: number;
  reactiveMinutes: number;
  untrackedMinutes: number;
  meetingMinutes: number;
  unloggedMinutes: number;
  plannedFocusMinutes: number;
  interruptionCount: number;
  interruptionMinutes: number;
  taskSwitches: number;
  focusBlockCount: number;
  longestFocusBlockMinutes: number;
}

export interface PeriodTotals {
  focusMinutes: number;
  reactiveMinutes: number;
  untrackedMinutes: number;
  meetingMinutes: number;
  unloggedMinutes: number;
  plannedFocusMinutes: number;
  interruptionCount: number;
  interruptionMinutes: number;
  taskSwitches: number;
  focusBlockCount: number;
}

export interface WeeklySummary {
  weekStart: string;
  weekEnd: string;
  timeZone: string;
  days: DayBreakdown[];
  totals: PeriodTotals;
  trendAverage: PeriodTotals;
  trendWeeks: number;
  byProject: ProjectTotal[];
  byMeetingType: CountMinutes[];
  longestFocusBlock: { date: string; start: string; end: string; minutes: number; taskTitle: string | null } | null;
  interruptions: InterruptionStats;
  plannedCount: number;
  plannedDoneCount: number;
  plannedMinutes: number;
  unplannedTaskMinutes: number;
}

export type ExportKind = 'time-entries' | 'interruptions' | 'meetings' | 'daily';
