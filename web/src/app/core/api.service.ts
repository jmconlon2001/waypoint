import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom, Observable } from 'rxjs';
import {
  ConvertResponse, DailyTotals, Interruption, InterruptionDetails, Person, Project, QuickAddResponse,
  ResumeResponse, Review, Settings, TaskInput, TaskStatus, TimeEntry, TimeEntryInput, TimeEntryKind,
  TimerState, WorkTask, SettingsInput, CalendarStatus, SyncResult, CalendarEvent, MeetingRule, WeeklySummary,
  InterruptionStats, PlannedVsActual, ExportKind,
} from './models';

/** Typed client for the Waypoint API (all calls return promises). */
@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api';

  private run<T>(o: Observable<T>): Promise<T> {
    return firstValueFrom(o);
  }

  private params(values: Record<string, string | number | boolean | null | undefined>): HttpParams {
    let p = new HttpParams();
    for (const [k, v] of Object.entries(values)) if (v !== null && v !== undefined && v !== '') p = p.set(k, String(v));
    return p;
  }

  // Settings
  getSettings() { return this.run(this.http.get<Settings>(`${this.base}/settings`)); }
  saveSettings(s: SettingsInput) { return this.run(this.http.put<Settings>(`${this.base}/settings`, s)); }

  // Projects
  getProjects(includeInactive = false) {
    return this.run(this.http.get<Project[]>(`${this.base}/projects`, { params: this.params({ includeInactive }) }));
  }
  createProject(p: { name: string; color: string | null; active: boolean | null }) { return this.run(this.http.post<Project>(`${this.base}/projects`, p)); }
  updateProject(id: number, p: { name: string; color: string | null; active: boolean | null }) { return this.run(this.http.put<Project>(`${this.base}/projects/${id}`, p)); }
  deleteProject(id: number) { return this.run(this.http.delete<Project | null>(`${this.base}/projects/${id}`)); }

  // People
  searchPeople(q: string) { return this.run(this.http.get<Person[]>(`${this.base}/people`, { params: this.params({ q }) })); }
  createPerson(name: string) { return this.run(this.http.post<Person>(`${this.base}/people`, { name })); }
  renamePerson(id: number, name: string) { return this.run(this.http.put<Person>(`${this.base}/people/${id}`, { name })); }
  deletePerson(id: number) { return this.run(this.http.delete<void>(`${this.base}/people/${id}`)); }

  // Tasks
  getTasks(filter: { view?: string; status?: TaskStatus | null; projectId?: number | null; q?: string | null }) {
    return this.run(this.http.get<WorkTask[]>(`${this.base}/tasks`, { params: this.params(filter) }));
  }
  getToday() { return this.run(this.http.get<WorkTask[]>(`${this.base}/tasks/today`)); }
  createTask(t: TaskInput) { return this.run(this.http.post<WorkTask>(`${this.base}/tasks`, t)); }
  updateTask(id: number, t: TaskInput) { return this.run(this.http.put<WorkTask>(`${this.base}/tasks/${id}`, t)); }
  deleteTask(id: number) { return this.run(this.http.delete<void>(`${this.base}/tasks/${id}`)); }
  quickAdd(text: string, today: boolean) { return this.run(this.http.post<QuickAddResponse>(`${this.base}/tasks/quick-add`, { text, today })); }
  planTask(id: number, today: boolean) { return this.run(this.http.post<WorkTask>(`${this.base}/tasks/${id}/plan`, { today })); }
  setTaskStatus(id: number, status: TaskStatus) { return this.run(this.http.post<WorkTask>(`${this.base}/tasks/${id}/status`, { status })); }
  pinTask(id: number, slot: number | null) { return this.run(this.http.post<WorkTask>(`${this.base}/tasks/${id}/pin`, { slot })); }

  // Timer
  getTimer() { return this.run(this.http.get<TimerState>(`${this.base}/timer`)); }
  startTimer(req: { taskId?: number | null; projectId?: number | null; kind?: TimeEntryKind | null; note?: string | null }) {
    return this.run(this.http.post<TimerState>(`${this.base}/timer/start`, req));
  }
  startPinned(slot: number) { return this.run(this.http.post<TimerState>(`${this.base}/timer/pinned/${slot}`, {})); }
  stopTimer() { return this.run(this.http.post<TimerState>(`${this.base}/timer/stop`, {})); }

  // Time entries
  getEntries(date: string) { return this.run(this.http.get<TimeEntry[]>(`${this.base}/time-entries`, { params: this.params({ date }) })); }
  createEntry(e: TimeEntryInput) { return this.run(this.http.post<TimeEntry>(`${this.base}/time-entries`, e)); }
  updateEntry(id: number, e: TimeEntryInput) { return this.run(this.http.put<TimeEntry>(`${this.base}/time-entries/${id}`, e)); }
  /** Resolves an overlap: this entry keeps its time, the ones it overlaps are trimmed or split. */
  keepEntry(id: number) { return this.run(this.http.post<TimeEntry>(`${this.base}/time-entries/${id}/keep`, {})); }
  deleteEntry(id: number) { return this.run(this.http.delete<void>(`${this.base}/time-entries/${id}`)); }
  fillGap(g: { start: string; end: string; projectId: number | null; kind: TimeEntryKind; note: string | null }) {
    return this.run(this.http.post<TimeEntry>(`${this.base}/time-entries/fill-gap`, g));
  }

  // Interruptions
  startInterruption(d: InterruptionDetails | null) { return this.run(this.http.post<Interruption>(`${this.base}/interruptions/start`, d ?? {})); }
  updateInterruption(id: number, d: InterruptionDetails) { return this.run(this.http.put<Interruption>(`${this.base}/interruptions/${id}`, d)); }
  setInterruptionTimes(id: number, startedAt: string, endedAt: string) {
    return this.run(this.http.put<Interruption>(`${this.base}/interruptions/${id}/times`, { startedAt, endedAt }));
  }
  resumeInterruption(id: number, d: InterruptionDetails | null) { return this.run(this.http.post<ResumeResponse>(`${this.base}/interruptions/${id}/resume`, d ?? {})); }
  convertInterruption(id: number, req: { title: string | null; projectId: number | null; status: TaskStatus | null }) {
    return this.run(this.http.post<ConvertResponse>(`${this.base}/interruptions/${id}/convert`, req));
  }
  deleteInterruption(id: number) { return this.run(this.http.delete<void>(`${this.base}/interruptions/${id}`)); }

  // Review / reports
  getReview(date: string | null) { return this.run(this.http.get<Review>(`${this.base}/review`, { params: this.params({ date }) })); }
  getDailyTotals(date: string | null) { return this.run(this.http.get<DailyTotals>(`${this.base}/reports/daily`, { params: this.params({ date }) })); }
  getWeekly(date: string | null) { return this.run(this.http.get<WeeklySummary>(`${this.base}/reports/weekly`, { params: this.params({ date }) })); }
  getInterruptionStats(from: string, to: string) {
    return this.run(this.http.get<InterruptionStats>(`${this.base}/reports/interruptions`, { params: this.params({ from, to }) }));
  }
  getPlan(date: string | null) { return this.run(this.http.get<PlannedVsActual>(`${this.base}/reports/plan`, { params: this.params({ date }) })); }
  /** A plain link (not XHR) so the browser downloads the file. */
  exportUrl(kind: ExportKind, from: string, to: string): string {
    return `${this.base}/export/${kind}.csv?${this.params({ from, to }).toString()}`;
  }
  rollOver(taskIds: number[], toDate: string | null) {
    return this.run(this.http.post<{ moved: number; toDate: string | null }>(`${this.base}/tasks/roll-over`, { taskIds, toDate }));
  }

  // Calendar
  getCalendarStatus() { return this.run(this.http.get<CalendarStatus>(`${this.base}/calendar/status`)); }
  setCalendarSource(icsUrl: string | null, internalDomains: string | null) {
    return this.run(this.http.put<CalendarStatus>(`${this.base}/calendar/source`, { icsUrl, internalDomains }));
  }
  syncCalendar() { return this.run(this.http.post<SyncResult>(`${this.base}/calendar/sync`, {})); }
  getCalendarEvents(date: string) { return this.run(this.http.get<CalendarEvent[]>(`${this.base}/calendar/events`, { params: this.params({ date }) })); }
  getMeetingRules() { return this.run(this.http.get<MeetingRule[]>(`${this.base}/calendar/rules`)); }
  createMeetingRule(titlePattern: string, projectId: number) {
    return this.run(this.http.post<MeetingRule[]>(`${this.base}/calendar/rules`, { titlePattern, projectId }));
  }
  updateMeetingRule(id: number, titlePattern: string, projectId: number) {
    return this.run(this.http.put<MeetingRule[]>(`${this.base}/calendar/rules/${id}`, { titlePattern, projectId }));
  }
  deleteMeetingRule(id: number) { return this.run(this.http.delete<MeetingRule[]>(`${this.base}/calendar/rules/${id}`)); }
}

/** Pulls the ProblemDetails title out of an HttpErrorResponse (or any error). */
export function errorMessage(err: unknown): string {
  const e = err as { error?: { title?: string; detail?: string }; message?: string; status?: number };
  if (e?.status === 0) return 'Cannot reach the Waypoint API. Is it running?';
  return e?.error?.title || e?.error?.detail || e?.message || 'Something went wrong.';
}
