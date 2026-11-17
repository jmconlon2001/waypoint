# Waypoint architecture (Phases 1-2)

Waypoint is a single-user tool that runs on one Windows laptop. It has no login, so all ports are bound to `127.0.0.1`.

```
browser ──► web (nginx :80, published 127.0.0.1:8080)
              ├─ /         Angular PWA (static files)
              └─ /api/*  ─► api (ASP.NET Core :8080, published 127.0.0.1:8081)
                              └─► db (PostgreSQL 17, volume waypoint-pgdata, published 127.0.0.1:5432)
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
| `settings` | id = 1, workday_start, workday_end, timer_warning_minutes, time_zone | A single row. Defaults: 08:45-17:30, 180 minutes, America/New_York. |

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
| Review | `GET /api/review?date=` (includes `calendarEvents`, `overlaps`, `focusBlocks`), `GET /api/reports/daily?date=` |
| Calendar | `GET /api/calendar/status`, `POST /api/calendar/sync`, `GET /api/calendar/events?from&to`, `GET /api/calendar/google/connect?origin=`, `GET /api/calendar/google/callback`, `POST /api/calendar/google/disconnect` |
| Meeting rules | `GET/POST /api/meeting-rules`, `PUT/DELETE /{id}`, `POST /test {title}` |
| Rollover | `GET /api/tasks/rollover?date=`, `POST /api/tasks/rollover {date?, skipTaskIds}` |
| Reports | `GET /api/reports/weekly?start=`, `GET /api/reports/interruptions?from&to`, `GET /api/reports/planned?from&to` |
| Aggregates | `GET /api/aggregates?from&to`, `POST /api/aggregates/recompute {from, to}` |
| Export | `GET /api/export/{time-entries\|interruptions\|calendar-events\|daily-aggregates}.csv?from&to` |

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

## Phase 2: calendar, metrics, rollover

### New tables

| Table | Columns | Notes |
|---|---|---|
| `calendar_events` | id, source (`google`/`ical`), external_id, title, started_at, ended_at, all_day, attendee_count, recurring, event_type, attendee_domains, declined, meeting_type, project_id?, synced_at | A read-only copy of the calendar for a sync window (today −35 days … +14 days). Unique on (source, external_id). |
| `meeting_rules` | id, pattern (regex), project_id, sort_order, active | First active match by sort order wins. |
| `daily_aggregates` | date (PK), task/reactive/untracked/meeting/planned-focus/unlogged minutes, interruption count/minutes, could_wait_count, task_switches, focus_blocks, longest_focus_block_minutes, planned_tasks, planned_tasks_worked, planned/unplanned task minutes, meeting_count, computed_at | Filled nightly and recomputable. Today is always computed live. |
| `task_plans` | task_id, date (composite PK) | Planned-for-day history. A task's `planned_date` moves when it rolls over; this table keeps every day it was planned. |

`settings` gains these columns:

- `calendar_source`
- `max_meeting_minutes` (default 120)
- `internal_domains`
- `google_refresh_token_protected`
- `google_account`
- `calendar_last_sync_at`
- `calendar_last_sync_error`
- `review_reminder_enabled`
- `review_reminder_time`

### Calendar import (FR-C1)

`ICalendarFeed` has two implementations:

- **`GoogleCalendarClient`**: plain `HttpClient` against Calendar API v3, with no SDK.
  - Sign-in uses the OAuth flow for installed apps with a loopback redirect and PKCE.
  - It requests `access_type=offline`, so Google returns a refresh token.
  - The refresh token is encrypted with Data Protection and kept in `settings`. Keys live in `/data/keys` (a volume).
  - The access token is cached in memory.
  - It reads `singleEvents=true`, so Google expands recurring events.
- **`ICalUrlFeed`**: downloads the secret iCal URL and parses it with `Domain/ICalParser`. The parser supports:
  - date-times in UTC, with TZID, floating, or date-only (all-day);
  - RRULE with FREQ DAILY/WEEKLY/MONTHLY/YEARLY, plus INTERVAL, COUNT, UNTIL, BYDAY (including ordinals) and BYMONTHDAY;
  - EXDATE;
  - RECURRENCE-ID overrides.

`CalendarService.SyncAsync`:

- upserts events by (source, external_id);
- deletes events in the window that have disappeared, plus events from a source that is no longer selected;
- classifies each event and applies meeting rules;
- records the last sync time and error.

`CalendarSyncWorker` runs this every `Calendar:SyncIntervalMinutes` (15 by default) while a source is selected. `POST /api/calendar/sync` runs it on demand.

### Meeting time (FR-C2, C4)

An event **counts** as meeting time unless it is any of these:

- all-day;
- declined by you;
- of a non-meeting type (`focusTime`, `outOfOffice`, `workingLocation`);
- longer than `max_meeting_minutes`.

Excluded events are returned with `counted=false` and an `exclusionReason`, and the review shows them greyed.

- Counted meetings are busy time for gap detection.
- `meetingMinutes` is the union of counted meetings clipped to the day and to *now*, so overlapping meetings count once and future meetings don't count yet.
- Rule-mapped meetings add their minutes to the project's total.
- Google `focusTime` blocks are summed as **planned focus** (`plannedFocusMinutes`), not tracked time.

**Overlap flag.** A Task or Reactive time entry that overlaps a counted meeting by at least 1 minute appears in `review.overlaps` and is flagged on the timeline. The time is probably counted twice, so you can trim one of them.

### Meeting types (FR-C3)

`CalendarRules.Classify` applies these in order; the first match wins.

| Type | Rule |
|---|---|
| Focus | Google eventType `focusTime` |
| External | Any attendee domain is not internal (equal to, or a subdomain of, an internal domain). Only applies when internal domains are configured. |
| 1:1 | Exactly 2 attendees (meeting rooms excluded) |
| Recurring | Part of a recurring series |
| Team | 3 or more attendees, one-off |
| Ad hoc | Everything else: holds and invite-less calls |

Types are stored on import, and recomputed when internal domains or rules change.

### Metrics (FR-D2-D6)

All metric logic is pure code in `Domain/Metrics.cs` and `Domain/DayMetrics.cs`, which makes it unit-testable.

| Metric | Definition |
|---|---|
| Focus block | A continuous stretch of **25 minutes or more** on one task, from Task-kind entries. Back-to-back entries of the same task (≤ 1 minute apart) are joined. Any interruption start inside the stretch splits it. |
| Task switch | The active timer changes to a different task (or to a different project/kind for entries without a task). Resuming the same task after an interruption is not a switch. |
| Heatmap | Interruption starts bucketed by **local** weekday (Mon = 0) × hour, so 09:30 lands in the 9 o'clock bucket in both EST and EDT. |
| Planned vs actual | Time on tasks planned for that day (`task_plans`) versus all other task/reactive time. The weekly view lists each planned task with its planned days, the time on those days and the time across the week. Starting a timer does **not** plan a task. |
| Could have waited | Count and share of interruptions marked "could wait", by source and by person. |

**Aggregates.** `NightlyAggregateWorker` runs:

- on startup, filling up to 35 missing past days;
- every night at 00:20 local, recomputing the previous 7 days.

You can also call:

- `POST /api/aggregates/recompute {from, to}` to rebuild a range;
- `GET /api/aggregates`, which fills missing past days on demand.

**Weekly report.** `GET /api/reports/weekly?start=` (any date; it snaps to Monday). It computes the current week live from raw data. The trend covers the previous 4 weeks and comes from `daily_aggregates`.

### Rollover and reminder (FR-T5, R4)

**Rollover.** `GET /api/tasks/rollover?date=` lists unfinished tasks on that day's Today list. `POST /api/tasks/rollover {date?, skipTaskIds}` then:

- moves the tasks to `date + 1`, recording each in `task_plans`;
- sends skipped tasks back to the backlog.

Tasks planned for a later date no longer show on today's list.

**Reminder.** `ReminderService` in the web app checks every 30 seconds whether the configured local time has passed. It shows one notification per day, through the service worker when there is one; clicking it opens `/review`. It only works while the app is open or installed.

### CSV export (FR-D7)

`GET /api/export/{dataset}.csv?from&to`, where `dataset` is `time-entries`, `interruptions`, `calendar-events` or `daily-aggregates`.

- Encoding is UTF-8 with a BOM.
- Fields follow RFC 4180 quoting.
- Text that starts with `= + - @`, tab or CR gets a `'` prefix, so it can't run as a formula.
- Times are given both as local time and as UTC.

## Not built (Phase 3)

LLM features (e.g. "summarise my week", suggested projects for gaps). These would call `MetricsService` and `ReviewService` rather than the database directly.

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
