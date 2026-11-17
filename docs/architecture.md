# Waypoint architecture (Phase 1)

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
| Review | `GET /api/review?date=`, `GET /api/reports/daily?date=` |

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

## Hooks for later phases (not built)

- **Google Calendar import:** `Integrations/ICalendarImportSource.cs` defines the interface. Meetings would come in as proposed time entries on the review page.
- **Metrics beyond daily totals:** these would build on `Domain/DailySummary` across date ranges.
- **LLM features:** these would call the existing services (review, time entries) rather than the database directly.

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
