
# Waypoint — Requirements

Nov 17, 2026 · @Jacob

## 1. Purpose

Waypoint is a personal, locally hosted tool for my work at CFS. It tracks todos and what I'm working on, records where my time goes, and logs interruptions and distractions. It's built in VS Code and runs in Podman on my work machine. It's a personal productivity tool, not a CFS system of record, and stays out of SOX scope.

Nothing monitors the desktop. Time comes from task timers I start and stop, plus my Google Calendar. Distraction insight comes from interruptions I log with one click. The design goal is to keep that data entry under 5 minutes a day.

**Questions it must answer:**

- Where did my day/week go, by project?
- How much time was meetings vs. focus work vs. reactive work?
- What interrupts me, who or what is the source, and when does it happen?
- Did I work on what I planned to work on?

## 2. Scope

**In scope:** task list, task timers, manual time entries, interruption logging, Google Calendar import, focus/interruption metrics, daily review, daily and weekly reports.

**Out of scope:** desktop agents or activity monitoring, browser extensions, team use, multi-user auth, mobile app, cloud hosting, time billing.

## 3. Architecture

A Podman pod, reachable only from localhost:

- `api` — ASP.NET Core minimal API: tasks, timers, interruptions, metrics, Google Calendar sync
- `web` — Angular UI served by nginx, installable as a PWA so it runs in its own window
- `db` — PostgreSQL with a named volume
- `llm` (P3) — Ollama serving a small local model, reachable only by `api` inside the pod

Podman on Windows runs in a WSL2 VM. Published ports are bound to `127.0.0.1` so nothing is reachable from the network.

## 4. Functional requirements

Priority: **MVP** = phase 1, **P2** = phase 2, **P3** = later.

### 4.1 Tasks

- **FR-T1 (MVP)** Create, edit, complete and delete tasks with title, project, priority, due date, notes and status (Backlog / Today / Doing / Blocked / Done).
- **FR-T2 (MVP)** Projects are configurable (e.g., SOX/J-SOX, Origination, Extractor migration, People management, Admin) and each has a color.
- **FR-T3 (MVP)** Quick add: one text box, with shorthand for project and priority (e.g., `Review AG failover #origination !1`).
- **FR-T4 (MVP)** Plan the day: mark tasks *Today*. That becomes the "planned" side of planned vs. actual.
- **FR-T5 (P2)** Unfinished *Today* tasks roll over in the end-of-day review.
- **FR-T6 (P3)** One-way import from Google Tasks.

### 4.2 Time tracking

- **FR-M1 (MVP)** Start a timer on a task with one click. Only one timer runs at a time; starting another stops the current one.
- **FR-M2 (MVP)** Timers keep running if the browser closes. Start time is stored server-side.
- **FR-M3 (MVP)** Add or edit a time entry after the fact: task (or project only), start, end, note.
- **FR-M4 (MVP)** Warn when a timer has run over a set limit (default 3 hours) or across midnight. Likely forgotten.
- **FR-M5 (MVP)** Keyboard shortcuts for start/stop, switch to a pinned task, and log interruption.
- **FR-M6 (P2)** A "reactive work" bucket for time with no planned task (email, ad hoc requests).

### 4.3 Interruptions and distractions

- **FR-I1 (MVP)** One-click **Interrupted** button. It pauses the running timer and opens a short form: source type (walk-up, Google Chat, email, phone, meeting overrun, self-distraction), person (optional, autocomplete from past entries) and a short note. Every field except source type is optional.
- **FR-I2 (MVP)** **Back to work** resumes the paused task and records how long the interruption lasted.
- **FR-I3 (MVP)** If an interruption turns into real work, convert it to a task with its time attached.
- **FR-I4 (P2)** Flag whether an interruption was urgent or could have waited.

### 4.4 Calendar

- **FR-C1 (P2)** Read-only import of Google Calendar events: title, start, end, attendee count, recurring flag. Sync every 15 minutes plus on demand.
- **FR-C2 (P2)** Meetings count as time automatically, with no data entry. Events longer than 2 hours (e.g., all-day blocks, holds, OOO, trainings) are excluded and don't count as meeting time; the threshold is configurable. A running timer that overlaps a meeting is flagged in the review.
- **FR-C3 (P2)** Meeting type assigned automatically: 1:1, team, external, recurring, ad hoc. Map a meeting to a project with a title rule (e.g., "SOX" → SOX/J-SOX).
- **FR-C4 (P2)** Focus Time calendar events count as planned focus.

### 4.5 Daily review

- **FR-R1 (MVP)** End-of-day timeline: meetings, time entries and interruptions on one view, with **gaps** (untracked time during work hours) highlighted.
- **FR-R2 (MVP)** Fill a gap in one step: pick a project or "untracked" and it becomes a time entry.
- **FR-R3 (MVP)** Work hours are configurable (e.g., 8:45–5:30) so gap detection ignores the rest of the day.
- **FR-R4 (P2)** Optional daily reminder at a set time to do the review.

### 4.6 Metrics and reports

- **FR-D1 (MVP)** Daily totals by project, and meetings vs. focus vs. reactive vs. untracked.
- **FR-D2 (P2)** Focus block: ≥ 25 minutes on one task timer with no interruption logged.
- **FR-D3 (P2)** Interruptions per day, by source type, by person, and by hour of day (heatmap).
- **FR-D4 (P2)** Task switches per day: how often the active timer changed.
- **FR-D5 (P2)** Weekly summary: time by project; meetings vs. focus vs. reactive; top interruption sources and people; longest focus block; trend vs. the previous 4 weeks.
- **FR-D6 (P2)** Planned vs. actual: *Today* tasks against time actually spent.
- **FR-D7 (P2)** CSV export of time entries, interruptions and daily aggregates.

### 4.7 LLM features

The LLM suggests and the user confirms: nothing it produces is saved without a click. The app works fully with the LLM turned off. Listed in order of value for effort.

- **FR-L1 (P3, S) Natural-language entry.** Type a sentence and it's parsed into a task, time entry or interruption, shown as a filled form to confirm. Examples: "spent 45 min on AG failover testing after standup", "Raj walked up about the Computershare file, 15 min, could have waited".
- **FR-L2 (P3, M) Gap-fill suggestions in the daily review.** For each untracked gap, suggest a project and note from the surrounding calendar events, the tasks touched that day and past patterns. Accept, edit or skip each one.
- **FR-L3 (P3, S) Weekly narrative.** A short written summary of the week from the aggregates: where time went, what changed vs. the 4-week trend, and up to 3 specific suggestions (e.g., a recurring meeting that's mostly overrun, a person whose interruptions could be batched).
- **FR-L4 (P3, M) Ask your data.** Questions in plain English, e.g., "how much time did SOX take in October?" or "who interrupts me most on Mondays?". The LLM answers by calling a fixed set of read-only query functions (time by project/period, interruptions by source/person/hour, planned vs. actual). It never writes SQL directly.
- **FR-L5 (P3, S) Status draft.** Draft a weekly status update for my manager from completed tasks and time by project, as editable text to copy.
- **FR-L6 (P3, M) Plan my day.** Suggest a *Today* list from due dates, priorities, open time on the calendar and how long similar tasks took before. Shows the reasoning in one line per task.
- **FR-L7 (P3, M) Interruption themes.** Group interruption notes into recurring asks (e.g., "report access requests", "deploy status questions") so the repeat ones can be documented, delegated or given office hours.

**LLM guardrails and design**

- **NFR-L1 Local model only.** Use `Microsoft.Extensions.AI` (`IChatClient`) against an Ollama container in the pod. No prompt or data leaves the machine, and no cloud LLM provider is configured. The abstraction keeps the model swappable.
- **NFR-L2 Minimum data.** Send only what a feature needs. FR-L3 and FR-L5 use aggregates and task titles, never raw notes.
- **NFR-L3 Structured output.** Parsing and suggestion features (FR-L1, FR-L2, FR-L6) return JSON validated against a schema. Invalid output is discarded and the user sees the plain form.
- **NFR-L4 Read-only access.** FR-L4 functions run under a read-only database role.
- **NFR-L5 Local log.** Every LLM call is logged (feature, prompt, response, latency, tokens) in an `LlmCall` table for debugging and prompt tuning.
- **NFR-L6 Performance.** Use a small instruct model (roughly 3–8B parameters, quantized) that fits in laptop RAM. On CPU it will be slow. FR-L1 responds in under 5 seconds or falls back to the plain form; the summaries and plan run in the background.
- **NFR-L7 Policy.** Cloud LLM providers are out of scope, so task titles, notes and colleague names never leave the machine. Model weights are pulled once at setup (\`ollama pull\`); confirm the download is allowed on the CFS network.

### 4.8 Look and feel

Mirror Keyport (`jmconlon2001/keyport`, `wwwroot/app.css`): same layout, typography, spacing and components. The one change is the accent palette: a **blue-to-green** gradient instead of Keyport's indigo-to-purple. Port Keyport's CSS variables into Angular's global `styles.scss` and keep the `--sm-*` token names so the two apps stay easy to compare.

**Layout (same as Keyport)**

- Fixed dark sidebar, 268px wide, vertical gradient `#0b1220` → `#111a2e`. Brand mark (gradient tile, 34px, 10px radius) plus "Waypoint" in bold with a small muted subtitle.
- Sidebar nav: section labels in small caps-style uppercase; active item gets a horizontal gradient wash and a 2px inset accent bar on the left.
- Sticky 60px topbar with a frosted-glass background (`backdrop-filter: saturate(180%) blur(14px)`), a pill chip for status, and round icon buttons.
- Page header with title, muted subtitle and right-aligned actions.
- Cards: 14px radius, 1px border, soft two-layer shadow; hover lifts 2px. Stat tiles with a tinted icon square in the top-right corner.
- Light and dark themes via Bootstrap 5's `data-bs-theme`, following the OS preference with a saved override (same pre-paint script as Keyport's `theme.js`, key renamed `wp-theme`).

**Typography (same as Keyport)**

- UI: Inter, falling back to Segoe UI Variable / system fonts; body 0.9375rem.
- Numbers, durations and timers: JetBrains Mono (falling back to Cascadia Code), so time values line up.

**Palette changes (blue/green replaces indigo/purple)**

| Token                        | Keyport                                   | Waypoint                                                          |
| ---------------------------- | ----------------------------------------- | ----------------------------------------------------------------- |
| `--sm-accent`              | `#6366f1`                               | `#2563eb` (blue-600)                                            |
| `--sm-accent-2`            | `#8b5cf6`                               | `#10b981` (emerald-500)                                         |
| `--sm-gradient`            | `135deg, #6366f1 → #8b5cf6 → #a855f7` | `135deg, #2563eb 0% → #0891b2 50% → #10b981 100%`             |
| `--sm-accent-soft` (light) | `rgba(99,102,241,.12)`                  | `rgba(37,99,235,.12)`                                           |
| `--sm-accent-soft` (dark)  | `rgba(129,140,248,.16)`                 | `rgba(56,189,248,.16)`                                          |
| Active nav wash              | indigo → violet                          | `rgba(37,99,235,.28)` → `rgba(16,185,129,.12)`               |
| Active nav bar               | `#818cf8`                               | `#38bdf8` (sky-400)                                             |
| Link hover (light)           | `#4f46e5`                               | `#1d4ed8`                                                       |
| Links (dark)                 | `#a5b4fc` / hover `#c7d2fe`           | `#7dd3fc` / hover `#bae6fd`                                   |
| Brand-mark glow              | `rgba(99,102,241,.45)`                  | `rgba(8,145,178,.45)`                                           |
| `--sm-success`             | `#10b981`                               | `#16a34a` (green-600, so success doesn't blend into the accent) |

Neutrals, borders, shadows, warning `#f59e0b`, danger `#ef4444` and info `#3b82f6` stay as in Keyport.

**Waypoint-specific uses of the palette**

- The running-timer chip in the topbar uses the gradient and shows the elapsed time in mono.
- Project colors on the timeline come from a fixed 8-color set that avoids the accent hues, so the gradient stays reserved for the active timer and primary actions.
- Interruptions on the timeline use the warning color; untracked gaps use a hatched muted fill.

## 5. Non-functional requirements

- **NFR-1 Data stays local.** The only outbound call is the read-only Google Calendar API (P2) and, if used, Google Tasks. LLM calls go only to the local model container (see 4.7). No telemetry.
- **NFR-2 Network exposure.** All ports bound to `127.0.0.1`. No login needed in the MVP, because only localhost can reach it.
- **NFR-3 Low friction.** Start/stop a timer and log an interruption each take one click or one shortcut. The UI loads in under 1 second.
- **NFR-4 Retention.** Data kept indefinitely; the volume is small.
- **NFR-5 Backup.** A script that exports the database (`pg_dump`) to a dated file.
- **NFR-6 Startup.** The pod starts with `podman compose up -d` or a startup script at login.
- **NFR-7 Policy.** Confirm with InfoSec that a Google OAuth client for calendar read access is allowed before P2.

## 6. Technology choices

- **API:** .NET 10, ASP.NET Core minimal API, EF Core, a background service for calendar sync and nightly aggregation.
- **Web:** Angular PWA on Bootstrap 5 (matching Keyport; see 4.8) with a charting library for the timeline, stacked bars and heatmap.
- **DB:** PostgreSQL (time-range queries and window functions make the metrics simple). SQLite is a fallback if fewer containers matter more.
- **Containers:** `compose.yaml` run with `podman compose`. Separate Dockerfiles for api and web.
- **Google integration:** OAuth desktop client with `calendar.readonly` scope. The Workspace admin may need to allow the client. Fallback: the calendar's secret iCal URL, if the admin hasn't disabled it.
- **Repo layout:** `/api`, `/web`, `/deploy` (compose, scripts), `/docs`.
- **LLM (P3):** Ollama container with a named volume for model weights; `Microsoft.Extensions.AI` Ollama client in `api`. Not exposed on a host port. Set the model with a config value and test 2–3 candidates on FR-L1 parsing before choosing.

## 7. Data model (initial)

- **Project** — id, name, color, active
- **Task** — id, project\_id, title, status, priority, due\_date, notes, planned\_date, created\_at, completed\_at
- **TimeEntry** — id, task\_id (nullable), project\_id, kind (task / reactive / untracked), started\_at, ended\_at (null while running), note
- **Interruption** — id, started\_at, ended\_at, source\_type, person, note, could\_wait, paused\_time\_entry\_id, converted\_task\_id
- **Person** — id, name (for interruption autocomplete)
- **CalendarEvent** (P2) — id, google\_id, title, started\_at, ended\_at, attendee\_count, is\_recurring, meeting\_type, project\_id
- **MeetingRule** (P2) — id, title\_pattern, project\_id
- **DailyAggregate** — date, project\_id, kind, minutes, interruptions, switches, focus\_blocks

## 8. Phases

- **Phase 1 – MVP (\~1–2 weekends, M):** tasks and quick add, timers, manual entries, interruption logging, daily review with gap fill, daily totals.
- **Phase 2 – Insight (\~2 weekends, M):** calendar import, focus/interruption/switch metrics, weekly summary, planned vs. actual, CSV export.
- **Phase 3 – LLM and extras (\~2–3 weekends, M):** LLM features in section 4.7 (start with FR-L1 and FR-L3), Google Tasks import, review reminder polish.

Use the MVP for 2 weeks before starting Phase 2, so the metrics are built on real habits.

## 9. Success criteria

- Data entry stays under 5 minutes a day, including the review.
- Untracked time during work hours is under 15% after 2 weeks.
- The weekly summary answers "where did my week go" on one screen.
- At least one concrete change gets made from the data (e.g., office hours for walk-ups, a recurring meeting cut, a protected focus block).

## 10. Open questions

- Is Podman Desktop already approved on the CFS image, or does it need a request?
- InfoSec position on a Google OAuth client for calendar read access (P2).
- PostgreSQL vs. SQLite: keep Postgres unless running fewer containers matters more.
