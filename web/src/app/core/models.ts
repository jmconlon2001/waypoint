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

export type CalendarSource = 'None' | 'Google' | 'ICal';
export type MeetingType = 'OneOnOne' | 'Team' | 'External' | 'Recurring' | 'AdHoc' | 'Focus';

export const MEETING_TYPE_LABELS: Record<MeetingType, string> = {
  OneOnOne: '1:1', Team: 'Team', External: 'External', Recurring: 'Recurring', AdHoc: 'Ad hoc', Focus: 'Focus time',
};

export interface Settings {
  workdayStart: string;
  workdayEnd: string;
  timerWarningMinutes: number;
  timeZone: string;
  projectColors: string[];
  calendarSource: CalendarSource;
  maxMeetingMinutes: number;
  internalDomains: string;
  reviewReminderEnabled: boolean;
  reviewReminderTime: string;
}

export type SettingsInput = Omit<Settings, 'projectColors'>;

export interface CalendarEvent {
  id: number;
  source: string;
  title: string;
  startedAt: string;
  endedAt: string;
  allDay: boolean;
  attendeeCount: number;
  recurring: boolean;
  eventType: string;
  meetingType: MeetingType;
  projectId: number | null;
  projectName: string | null;
  projectColor: string | null;
  counted: boolean;
  exclusionReason: string | null;
  plannedFocus: boolean;
  minutes: number;
}

export interface MeetingOverlap {
  timeEntryId: number;
  calendarEventId: number;
  eventTitle: string;
  start: string;
  end: string;
  minutes: number;
}

export interface FocusBlock {
  taskId: number;
  taskTitle: string | null;
  start: string;
  end: string;
  minutes: number;
}

export interface CalendarStatus {
  source: CalendarSource;
  googleConfigured: boolean;
  googleConnected: boolean;
  googleAccount: string | null;
  iCalConfigured: boolean;
  lastSyncAt: string | null;
  lastSyncError: string | null;
  eventCount: number;
  syncIntervalMinutes: number;
}

export interface SyncResult {
  source: CalendarSource;
  added: number;
  updated: number;
  removed: number;
  total: number;
  syncedAt: string;
}

export interface MeetingRule {
  id: number;
  pattern: string;
  projectId: number;
  projectName: string | null;
  projectColor: string | null;
  sortOrder: number;
  active: boolean;
}

export interface RolloverResult {
  toDate: string;
  rolled: number;
  unplanned: number;
}

export interface DailyAggregate {
  date: string;
  taskMinutes: number;
  reactiveMinutes: number;
  untrackedMinutes: number;
  meetingMinutes: number;
  plannedFocusMinutes: number;
  unloggedMinutes: number;
  interruptionCount: number;
  interruptionMinutes: number;
  couldWaitCount: number;
  taskSwitches: number;
  focusBlocks: number;
  longestFocusBlockMinutes: number;
  plannedTasks: number;
  plannedTasksWorked: number;
  plannedTaskMinutes: number;
  unplannedTaskMinutes: number;
  meetingCount: number;
  computedAt: string;
}

export interface NamedCount {
  key: string;
  label: string;
  count: number;
  minutes: number;
  couldWaitCount: number;
}

export interface InterruptionStats {
  from: string;
  to: string;
  count: number;
  minutes: number;
  couldWaitCount: number;
  bySource: NamedCount[];
  byPerson: NamedCount[];
  heatmap: number[][];
  byDay: NamedCount[];
}

export interface WeekTrend {
  weekStart: string;
  meetings: number;
  focus: number;
  reactive: number;
  untracked: number;
  unlogged: number;
  interruptions: number;
  focusBlocks: number;
  taskSwitches: number;
}

export interface PlannedTask {
  taskId: number;
  title: string;
  projectName: string | null;
  projectColor: string | null;
  status: TaskStatus;
  plannedDates: string[];
  minutesOnPlannedDays: number;
  minutesInRange: number;
}

export interface WeeklyReport {
  weekStart: string;
  weekEnd: string;
  days: DailyAggregate[];
  byProject: ProjectTotal[];
  kinds: { meetings: number; focus: number; reactive: number; untracked: number; unlogged: number; plannedFocus: number };
  interruptions: InterruptionStats;
  longestFocusBlock: FocusBlock | null;
  focusBlocks: number;
  taskSwitches: number;
  trend: WeekTrend[];
  plannedVsActual: PlannedTask[];
  plannedTaskMinutes: number;
  unplannedTaskMinutes: number;
}

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
  couldWaitCount: number;
  taskSwitches: number;
  focusBlocks: number;
  longestFocusBlockMinutes: number;
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
  calendarEvents: CalendarEvent[];
  overlaps: MeetingOverlap[];
  focusBlocks: FocusBlock[];
}
