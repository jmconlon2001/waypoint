# Waypoint architecture (Phases 1-2)

Waypoint is a single-user tool that runs on one Windows laptop. It has no login, so all ports are bound to `127.0.0.1`.

```
browser ──► web (nginx :80, published 127.0.0.1:8080)
              ├─ /         Angular PWA (static files)
              └─ /api/*  ─► api (ASP.NET Core :8080, published 127.0.0.1:8081)
                              ├─► db (PostgreSQL 17, volume waypoint-pgdata, published 127.0.0.1:5432)
                              └─► https://calendar.google.com (read-only iCal feed, only when configured)
```

## Principles

- **Manual input only.** There is no desktop agent and no activity monitoring. Data comes from timers you start, entries you add, and interruptions you log with one click. The goal is under 5 minutes of data entry a day.
- **The server owns the timer.** A running timer is a `time_entries` row with `ended_at IS NULL`. Closing the browser doesn't lose it, and only one runs at a time: starting a timer stops the current one.
- **UTC in the database, local time on screen.** All instants are `timestamp with time zone` (UTC). "A day", work hours and the display all use the configured IANA zone (default `America/New_York`). `planned_date` and `due_date` are local calendar dates (`date`).

## Data model

| Table | Columns | Notes |
|---|---|---|
| `projects` | id, name (unique), color, active | Colors come from a fixed set of 8 that avoids the blue/cyan/green accent hues and the warning amber. Deleting a project that has history archives it (`active = false`). |
| `tasks` | id, title, project_id?, priority 1-3, due_date?, notes?, status, planned_date?, pin_slot?, created_at, completed_at? | Status: Backlog, Today, Doing, Blocked, Done. `pin_slot` (1-5) maps to the number-key shortcuts. |
| `time_entries` | id, task_id?, project_id?, kind, started_at, ended_at?, note? | Kind: `Task` (focus), `Reactive`, `Untracked`. A project with no task is allowed. |
| `interruptions` | id, started_at, ended_at?, source_type?, person_id?, note?, could_wait?, paused_time_entry_id?, converted_task_id? | Source: WalkUp, GoogleChat, Email, Phone, MeetingOverrun, SelfDistraction. It's empty only between clicking "Interrupted" and saving the form; the UI requires it. |
| `people` | id, name (unique) | Created automatically from the interruption form's person field. |
| `settings` | id = 1, workday_start, workday_end, timer_warning_minutes, time_zone, meeting_max_minutes, review_reminder_time?, calendar_ics_url?, calendar_internal_domains?, calendar_last_sync_at?, calendar_last_sync_error? | A single row. Defaults: 08:45-17:30, 180 minutes, America/New_York, 120-minute meeting limit. |
| `calendar_events` | id, external_id (unique), title, started_at, ended_at, is_all_day, attendee_count, is_recurring, is_free, meeting_type, project_id?, synced_at | One row per occurrence (recurring events are expanded). `external_id` = iCal UID + occurrence start. `is_free` = transparent or declined by the owner. |
| `meeting_rules` | id, title_pattern, project_id | Case-insensitive "title contains". The first rule (lowest id) wins. Rules are re-applied to all stored events when they change. |
| `task_plans` | id, task_id, date | History of the Today list (unique per task and date). It's the "planned" side of planned vs. actual, kept because `tasks.planned_date` moves on roll-over. |

Enums are stored as strings. Foreign keys use `ON DELETE SET NULL`, so deleting a task or person never deletes logged time.

## Key flows

**Start a timer.** `POST /api/timer/start {taskId}` ends any running entry and closes any open interruption. It then inserts a new entry with `ended_at = null` and sets the task to `Doing`. The task that was running goes back from `Doing` to `Today`.

**Interrupted.** `POST /api/interruptions/start` ends the running entry and creates an interruption with `paused_time_entry_id` set to that entry. The UI then opens the form, and `PUT /api/interruptions/{id}` saves the source, person, note and could-wait.

**Back to work.** `POST /api/interruptions/{id}/resume` sets `ended_at` and starts a new time entry with the paused entry's task, project and kind. The interruption's duration is `ended_at - started_at`.

**Convert to task.** `POST /api/interruptions/{id}/convert` ends the interruption if it is still open. It creates a task (default status Done, titled from the note or from the source and person) and attaches a `Reactive` time entry covering the interruption.

**Daily review.** `GET /api/review?date=` returns for that local day:

- entries and interruptions clipped to the day;
- **gaps**: parts of the work hours, up to now, that are covered by neither an entry nor an interruption (gaps under 2 minutes are ignored);
- totals.

`POST /api/time-entries/fill-gap {start, end, projectId | kind: Untracked}` fills a gap in one step.

**Reports.** `GET /api/reports/daily?date=` returns:

- minutes by kind (focus/task, reactive, untracked);
- tracked total;
- interruption count and minutes;
- unlogged gap minutes;
- minutes by project.

Interruption time is reported on its own line. A converted interruption also shows up as reactive time.

**Timer warnings.** `GET /api/timer` returns `warnings`:

- `long-running` when the timer has run past the threshold;
- `crossed-midnight` when the timer started on an earlier local day.

The UI also shows the threshold warning live.

## API surface

See [`api.http`](api.http) for runnable examples. In Development, the OpenAPI document is at `/openapi/v1.json`.

| Area | Endpoints |
|---|---|
| Health | `GET /api/health` |
| Settings | `GET/PUT /api/settings` |
| Projects | `GET /api/projects?includeInactive=`, `POST`, `PUT /{id}`, `DELETE /{id}` |
| People | `GET /api/people?q=`, `POST`, `PUT /{id}`, `DELETE /{id}` |
| Tasks | `GET /api/tasks?view=open\|today\|done\|all&status=&projectId=&q=`, `GET /api/tasks/today`, `GET/PUT/DELETE /{id}`, `POST /api/tasks`, `POST /quick-add`, `POST /{id}/plan`, `POST /{id}/status`, `POST /{id}/pin` |
| Timer | `GET /api/timer`, `POST /start`, `POST /pinned/{slot}`, `POST /stop` |
| Time entries | `GET /api/time-entries?date=`, `POST`, `PUT/DELETE /{id}`, `POST /fill-gap` |
| Interruptions | `GET /api/interruptions?date=`, `GET /{id}`, `POST /start`, `PUT /{id}`, `PUT /{id}/times`, `POST /{id}/resume`, `POST /{id}/convert`, `DELETE /{id}` |
| Review | `GET /api/review?date=` |
| Reports | `GET /api/reports/daily?date=`, `GET /api/reports/weekly?date=` (any day in the week), `GET /api/reports/interruptions?from=&to=`, `GET /api/reports/plan?date=` |
| Export | `GET /api/export/{time-entries\|interruptions\|meetings\|daily}.csv?from=&to=` |
| Roll-over | `POST /api/tasks/roll-over` |
| Calendar | `GET /api/calendar/status`, `PUT /source`, `POST /sync`, `GET /events?date=`, `GET/POST /rules`, `PUT/DELETE /rules/{id}` |

Errors are RFC 7807 problem responses, and `title` holds a message the user can read.

## Quick add syntax

`Review AG failover #origination !1` creates a task with:

- **Title:** "Review AG failover"
- **Project:** Origination
- **Priority:** 1

Parsing happens on the server (`Domain/QuickAddParser.cs`):

- The first `#token` names a project. It matches on letters and digits only, case-insensitive, trying an exact match, then a unique prefix, then a unique substring. So `#sox`, `#jsox` and `#sox/j-sox` all find "SOX/J-SOX", and `#extractor` finds "Extractor migration".
- `!1`-`!3` sets the priority.
- An unknown or ambiguous project still adds the task, with a warning.

**Overlapping time entries.** The entry being saved wins (`TimeEntryService.CarveAsync`). Each entry it overlaps is:

- trimmed, if the overlap is at one end;
- split into before and after pieces, if the new entry sits inside it. A running timer split this way keeps running from the end of the new entry;
- removed, if it's fully covered.

Leftover pieces under 1 minute are dropped. On an edit, only time the entry newly covers carves, so changing a note never touches neighbours. The save response lists what changed (`adjusted`), and the UI shows it as a toast. Overlaps that already exist are listed in the review (`entryOverlaps`) with a one-click `POST /api/time-entries/{id}/keep`.

## Phase 2: calendar and metrics

**Calendar sync.** `CalendarService.SyncAsync`:

1. downloads the secret iCal address (`HttpCalendarFeed`: HTTPS, `calendar.google.com` only, no redirects, 30 s timeout);
2. expands it with Ical.Net (`IcsParser`), applying RRULE, EXDATE and RECURRENCE-ID overrides and dropping cancelled events;
3. replaces the stored events in a rolling window, 35 days back and 14 days ahead. Events outside the window are kept for the metrics.

`CalendarSyncWorker` runs a sync every 15 minutes while an address is saved. `POST /api/calendar/sync` runs one on demand. A failure is saved in `settings.calendar_last_sync_error` and shown on the settings page.

**Meeting rules** (`Domain/Meetings`). Whether an event counts is decided when it's read, so changing the threshold also applies to past days.

- **Counts as meeting:** timed, busy, not Focus Time, and no longer than `meeting_max_minutes`.
- **Planned focus:** a timed event whose title starts with "Focus" ("Focus time" is Google's default).
- **Meeting type:**
  - Focus;
  - External: any attendee outside the internal domains. These default to the calendar owner's domain, taken from `X-WR-CALNAME`;
  - 1:1: two people;
  - Team: three or more, recurring;
  - Recurring: on your own;
  - otherwise Ad hoc.
  - Room and group calendars aren't counted as people.

**Totals with meetings** (`DailySummary.Build`).

- Overlapping meetings count once.
- Meeting time wins over time entries: an entry only counts for the part outside meetings, so the kinds add up to wall-clock time. The overlap is reported (`ReviewDto.Overlaps`) so a timer left running can be trimmed.
- Meetings later today don't count yet.
- Meetings close gaps, but Focus Time doesn't.

**Metrics.**

- **Focus block:** back-to-back entries on the same task (stopped and restarted within 2 min) form a run. Any time covered by an interruption or meeting is cut out, and pieces of at least 25 minutes count.
- **Task switches:** consecutive entries with a different (task, project, kind).
- **Planned vs. actual:**
  - *planned* = the day's `task_plans` rows, with the time spent on those tasks;
  - *unplanned* = focus time on other tasks or project-only entries;
  - reactive work and meetings are shown separately.
  - Starting a timer no longer plans a task.

**Reports are computed on request.** `ReportService` loads a range once and builds each day from the raw rows. There is no `DailyAggregate` table and no nightly job, so editing an old entry changes the reports immediately. A weekly summary with its 4-week trend reads 35 days, which is small for one person.

- **Weekly trend:** averages only the previous weeks that have anything logged.
- **Days with nothing logged** (days off, days before you started using Waypoint) count 0 unlogged in weekly totals and CSV. The daily review still shows their gaps.

**Roll-over.** `POST /api/tasks/roll-over {taskIds, toDate | null}` stops their timers and moves the tasks:

- with a date: to that date, as Backlog. They reappear on the Today list on that date;
- with null: back to the backlog.

Today's `task_plans` rows stay as history either way. Taking a task off today's list the same day (`plan` with `today: false`) removes today's row.

**CSV.** `GET /api/export/{time-entries|interruptions|meetings|daily}.csv?from=&to=`. The default range is the last 30 days and the maximum is 366 days. The files are UTF-8 with a BOM so Excel reads them correctly, and cells starting with `= + - @` are prefixed with `'`.

**Reminder.** `settings.review_reminder_time` is checked by the web shell once a minute. It fires at most once a day (`wp-review-reminded` in localStorage) and only while the app is open.

## Hooks for later phases (not built)

- **LLM features (Phase 3):** these would call the existing services (`ReportService`, `ReviewService`) rather than the database directly. FR-L4's read-only query functions map onto `ReportService` methods.
- **Google Calendar API source:** `ICalendarFeed` is the seam if an OAuth client is approved later.

## Frontend

- Angular 22 with standalone components and signals, zoneless. Bootstrap 5.3 is used for the grid, forms and utilities.
- The design system lives in `web/src/styles.scss`. It is ported from Keyport, keeps the `--sm-*` variable names, and uses a blue/green palette.
- `public/theme.js` applies the saved theme (`wp-theme` in localStorage) or the OS preference before first paint.
- State:
  - `TimerStore`: the running timer and open interruption, plus a `version` counter that pages watch to reload.
  - `SettingsStore`: settings and projects.
  - `UiStore`: the shell modals.
- Keyboard shortcuts are handled in `app.ts`:
  - `s`: start/stop
  - `1-5`: pinned tasks
  - `i`: interrupted
  - `b`: back to work
  - `q`: quick add
  - `n`: new task
  - `l`: log time
  - `?`: help
