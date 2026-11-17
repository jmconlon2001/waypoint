# Waypoint

A personal work tracker for one person on one laptop. Waypoint tracks:

- **todos**,
- **what you are working on**, with task timers,
- **interruptions**,

so the end of the day can answer *where did the time go?*

There is no desktop agent and no activity monitoring. Everything comes from:

- a timer you start,
- an entry you add after the fact,
- a one-click "Interrupted" button.

The target is under five minutes of data entry a day. All data stays on the machine: Waypoint runs in Podman, and every port is bound to `127.0.0.1`.

## What's in Phase 1

- **Tasks:**
  - Fields: title, project, priority 1-3, due date, notes, status (Backlog / Today / Doing / Blocked / Done), planned date.
  - Configurable projects with colors. Seeded with SOX/J-SOX, Origination, Extractor migration, People management and Admin.
  - **Quick add** shorthand: `Review AG failover #origination !1`, parsed on the server.
  - Mark tasks for **Today**.
- **Time tracking:**
  - One-click timers. Only one runs at a time, and it is stored on the server, so it survives closing the browser.
  - Add or edit entries after the fact: a task or just a project, start, end, note, and kind (task, reactive or untracked).
  - Warnings when a timer runs longer than 3 h (configurable) or crosses midnight.
- **Interruptions:**
  - **Interrupted** pauses the timer and opens a short form with source, optional person (autocomplete) and optional note.
  - **Back to work** resumes the paused task and records how long the interruption took.
  - Any interruption can be **converted into a task**, with its time attached.
- **Daily review:**
  - A timeline of entries, interruptions and **gaps** (untracked time within work hours, 08:45-17:30 by default).
  - Fill a gap in one step: pick a project or "untracked".
- **Reports:** daily totals by project and by kind (focus, reactive, untracked), plus an interruptions total.
- **Keyboard shortcuts:**

  | Key | Action |
  |---|---|
  | `s` | Start/stop the timer |
  | `1`-`5` | Switch to a pinned task |
  | `i` | Interrupted |
  | `b` | Back to work |
  | `q` | Quick add |
  | `n` | New task |
  | `l` | Log time |
  | `?` | Help |

- **Light and dark themes.** Follows the OS, or saves your choice. The look is borrowed from Keyport, recolored blue/green.

### Phase 2

- **Calendar (read-only):**
  - Google Calendar, signed in with OAuth, scope `calendar.readonly`. Or, as a fallback, a secret iCal URL.
  - Syncs every 15 minutes, plus a **Sync now** button.
- **Meetings count as time automatically.**
  - All-day events, declined events and events longer than 2 hours (configurable) don't count. They still show on the review timeline as greyed context.
  - A task timer that overlaps a counted meeting is flagged on the review.
  - Gap detection treats counted meetings as tracked time.
- **Meeting types:** 1:1, team, external (based on a list of internal domains), recurring, ad hoc. Google *focus time* counts as planned focus, not as a meeting.
- **Meeting rules:** a title regex maps a meeting to a project.
- **Metrics:**
  - focus blocks (25+ continuous minutes on one task with no interruption);
  - task switches;
  - interruptions by source, by person and by weekday × hour (heatmap);
  - "could have waited" counts;
  - planned vs actual.
- **Weekly page:** time by project; meetings vs focus vs reactive vs untracked; top interruption sources and people; longest focus block; trend against the previous 4 weeks; planned vs actual.
- **CSV export** for a date range: time entries, interruptions, calendar events and daily totals.
- **Daily totals:** stored nightly in `daily_aggregates`, and recomputable for any range.
- **End-of-day rollover:** unfinished Today tasks move to tomorrow in one click, with a per-task opt-out.
- **Review reminder:** an optional browser notification at a time you pick.

Not built (Phase 3): LLM features. See [docs/architecture.md](docs/architecture.md).

## Prerequisites

| Tool | Used for |
|---|---|
| [Podman Desktop](https://podman-desktop.io) (Podman + a WSL2 machine, with the compose extension) | Running the pod |
| [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) | API development and tests |
| [Node.js 22 LTS](https://nodejs.org), 22.22.3 or later (npm 10) | Web development and tests (Angular 22 requirement) |
| VS Code (optional) | The repo includes `.vscode/` with recommended extensions, tasks and launch configs |

## Run it in Podman (day-to-day use)

From PowerShell in the repo:

```powershell
cd deploy
./start.ps1 -Build      # first run, or after pulling changes
./start.ps1             # later starts
```

The script:

1. starts the Podman machine if needed,
2. runs `podman compose up -d`,
3. waits for the health check,
4. opens **http://127.0.0.1:8080**.

You can also run the steps by hand:

```powershell
cd deploy
podman compose up -d --build
podman compose ps
podman compose logs -f api
podman compose down          # stop (data is kept in the waypoint-pgdata volume)
```

What runs:

| Container | Port (localhost only) | Purpose |
|---|---|---|
| `waypoint-web` | `127.0.0.1:8080` | nginx serving the Angular PWA and proxying `/api` |
| `waypoint-api` | `127.0.0.1:8081` | ASP.NET Core API. Applies EF Core migrations and seeds defaults at startup |
| `waypoint-db` | `127.0.0.1:5432` | PostgreSQL 17, data in the named volume `waypoint-pgdata` |

To change ports or the DB password, copy `deploy/.env.example` to `deploy/.env`. The default password is fine for a localhost-only database, but you can set your own.

On macOS/Linux, `deploy/start.sh` and `deploy/backup.sh` do the same jobs. They use Podman, or Docker if Podman isn't installed.

## Calendar setup

Pick **one** source in **Projects & settings → Calendar**.

### Option A: Google Calendar API (recommended)

Google Calendar gives Waypoint the most information: focus-time events, whether you declined a meeting, and attendee lists.

1. Open [Google Cloud console](https://console.cloud.google.com/) and create a project (e.g. "Waypoint"), or pick an existing one.
2. Go to **APIs & Services → Library**, search for **Google Calendar API** and click **Enable**.
3. Go to **Google Auth Platform → Branding / Audience** (the "OAuth consent screen").
   - **User type:** choose **Internal** if the project belongs to your Google Workspace organization. Internal needs no verification, and tokens don't expire.
   - If you can only choose **External**, set the publishing status to **Testing** and add yourself as a test user. In Testing mode Google expires the refresh token after **7 days**, so you will have to click Connect again weekly. Publish the app (no verification is needed for personal use) to avoid that.
4. Under **Data Access**, add the scope `https://www.googleapis.com/auth/calendar.readonly`.
5. Go to **Clients → Create client** (or **Credentials → Create credentials → OAuth client ID**) and set **Application type** to **Desktop app**. Name it "Waypoint" and copy the **Client ID** and **Client secret**.
   - Desktop-app clients accept any loopback redirect, so there's nothing to register. Waypoint redirects to `http://127.0.0.1:8080/api/calendar/google/callback`, or `http://localhost:4200/...` in development.
6. Put the values in `deploy/.env`:
   ```
   GOOGLE_CLIENT_ID=1234-abc.apps.googleusercontent.com
   GOOGLE_CLIENT_SECRET=GOCSPX-...
   ```
   For `dotnet run`, set the environment variables `Google__ClientId` and `Google__ClientSecret` instead. You can also use `dotnet user-secrets`.
7. Run `./start.ps1` again.
8. In Waypoint, open **Settings → Calendar → Google Calendar → Connect**, sign in and allow read-only access.

**Workspace admin approval.** CFS's Google Workspace admin may restrict third-party access to Google services. If sign-in shows "Access blocked" or "This app is blocked", ask the admin to look in **Admin console → Security → Access and data control → API controls → App access control**. There they can mark your OAuth client ID as **Trusted**, or allow access to the Calendar service. Give them the Client ID from step 5.

**Where the token is kept.** The refresh token is stored in the database, encrypted with ASP.NET Core Data Protection. The keys live in the `waypoint-keys` volume (`/data/keys`). If you delete that volume, Waypoint can no longer decrypt the token; click Connect again.

### Option B: secret iCal URL (fallback)

1. In Google Calendar, open **Settings → Settings for my calendars →** *your calendar* **→ Integrate calendar**, and copy the **Secret address in iCal format**. Treat it like a password.
2. Set `CALENDAR_ICAL_URL=` in `deploy/.env`, or `Calendar__ICalUrl` for `dotnet run`. Then restart Waypoint and choose **iCal URL** in Settings.

The iCal feed has some limits:
- It has no focus-time events and no declined status, so declined meetings still count.
- Some organizations disable secret addresses.
- Waypoint's parser expands common recurrence rules (daily, weekly, monthly, yearly, with BYDAY, EXDATE and moved occurrences); rarer rules fall back to the first occurrence.

### Meeting settings

| Setting | Effect |
|---|---|
| **Longest counted meeting** (default 120 min) | Longer events show as context only. All-day events never count. |
| **Internal e-mail domains** (e.g. `cfs.canon.com, canon.com`) | Any attendee outside these domains (subdomains are fine) makes the meeting *External*. |
| **Meeting rules** | A case-insensitive regex on the title maps the meeting to a project, so the time shows under that project. The first match wins. Use the tester box to check a title. |

## Other Phase 2 features

| Feature | How it works |
|---|---|
| **Review reminder** | In Settings, turn it on, pick a time, then click **Allow notifications**. It fires once a day while Waypoint is open in a tab or installed as an app (Edge/Chrome: *Install Waypoint* in the address bar). There is no server push. |
| **End-of-day rollover** | The Daily review for today shows **Wrap up the day**. Ticked tasks move to tomorrow; unticked ones go back to the backlog. |
| **Daily totals** | Stored every night at 00:20 local for the previous 7 days. Missing days are filled on startup. **Settings → Daily totals → Recompute** rebuilds any range. |
| **CSV export** | On the Weekly page, or at `GET /api/export/{time-entries\|interruptions\|calendar-events\|daily-aggregates}.csv?from=YYYY-MM-DD&to=YYYY-MM-DD`. Files are UTF-8 with a BOM so Excel reads them. Text cells that start with `= + - @` get a leading `'` so they can't run as formulas. |

### Backups

```powershell
cd deploy
./backup.ps1                 # writes deploy/backups/waypoint-yyyyMMdd-HHmmss.dump, prunes dumps older than 30 days
```

Restore a dump into the running container:

```powershell
podman exec -i waypoint-db pg_restore -U waypoint -d waypoint --clean --if-exists < backups/waypoint-20261117-180000.dump
```

To run it daily, add a Windows Task Scheduler job that runs `pwsh -File <repo>\deploy\backup.ps1`.

## Run it locally for development

Start only the database in Podman, then run the API and the web dev server on the host:

```powershell
podman compose -f deploy/compose.yaml up -d db

# API on http://localhost:5080 (applies migrations on start)
dotnet run --project api/src/Waypoint.Api --launch-profile http

# Web on http://localhost:4200 (proxies /api to :5080, see web/proxy.conf.json)
cd web
npm ci
npm start
```

In VS Code:

- Run the task **db: start (podman)**.
- Then launch **Full stack (API + Web)**, or start **API (.NET)** and **Web (ng serve + Edge)** separately.
- `docs/api.http` has ready-made requests for the REST Client extension.

The API's connection string is `ConnectionStrings:Waypoint` in `api/src/Waypoint.Api/appsettings.json`. It defaults to the containerized DB on `localhost:5432`.

### Tests

```powershell
dotnet test api/Waypoint.slnx           # xUnit: quick-add, gaps/totals, timer/interruptions, calendar parsing & classification,
                                        #        metrics (focus blocks, switches, heatmap, overlaps), rollover, CSV, migration snapshot
cd web; npx ng test --watch=false       # Vitest: time-zone helpers, timeline layout, weekly helpers, reminder, shell
```

### Database migrations

Migrations are applied automatically when the API starts. To change the model:

```powershell
dotnet tool install --global dotnet-ef   # once
cd api/src/Waypoint.Api
dotnet ef migrations add <Name> --output-dir Data/Migrations
```

> **Note:** both migrations (`InitialCreate` and `Phase2CalendarMetrics`) and the model snapshot were written by hand, because the EF tooling could not be run in the sandbox where Phases 1 and 2 were built.
>
> The test `MigrationTests.Model_has_no_pending_changes` checks the snapshot against the model.
>
> If that test fails on a fresh clone with an empty database:
>
> - **On a fresh clone with an empty database:**
>   1. Delete `Data/Migrations`.
>   2. Run `dotnet ef migrations add InitialCreate --output-dir Data/Migrations`.
>   3. Drop the volume with `podman compose down -v`.
> - **On an existing Phase 1 database:** add a corrective migration with `dotnet ef migrations add FixSnapshot`, and review it before applying.

## Project structure

```
waypoint/
├─ api/
│  ├─ Waypoint.slnx
│  ├─ Dockerfile
│  ├─ src/Waypoint.Api/
│  │  ├─ Program.cs                 # DI, JSON, error handling, migrate + seed at startup
│  │  ├─ Endpoints/ApiEndpoints.cs  # minimal API routes under /api
│  │  ├─ Contracts/Dtos.cs          # request/response records
│  │  ├─ Services/                  # Task, Timer, Interruption, TimeEntry, Review, Project, People, Settings
│  │  ├─ Domain/                    # pure logic: QuickAddParser, Intervals/gaps, DailySummary, LocalTime, TimerWarnings, ProjectPalette
│  │  ├─ Data/                      # DbContext (snake_case), entities, migrations, seeder
│  │  ├─ Integrations/              # calendar feeds: Google Calendar client (OAuth + API), iCal URL
│  │  └─ Workers/                   # background jobs: calendar sync (15 min), nightly daily aggregates
│  └─ tests/Waypoint.Api.Tests/     # xUnit tests (EF InMemory for service flows)
├─ web/
│  ├─ Dockerfile, nginx.conf        # production image: nginx serves the build, proxies /api
│  ├─ proxy.conf.json               # dev server proxy to the API
│  ├─ public/theme.js               # pre-paint light/dark (localStorage "wp-theme")
│  └─ src/
│     ├─ styles.scss                # design system (Keyport port, --sm-* variables, blue/green palette)
│     └─ app/
│        ├─ app.ts / app.html       # shell: sidebar, topbar timer chip, modals, keyboard shortcuts
│        ├─ core/                   # API client, models, stores (timer, settings, UI), time-zone helpers
│        ├─ pages/                  # Today, Tasks, Review (+ timeline layout), Weekly (+ helpers), Projects & settings
│        └─ shared/                 # interruption form, task editor, time-entry editor, person autocomplete
├─ deploy/
│  ├─ compose.yaml                  # db + api + web, ports on 127.0.0.1 only
│  ├─ start.ps1 / start.sh          # start the pod and open the app
│  ├─ backup.ps1 / backup.sh        # pg_dump to deploy/backups/waypoint-<timestamp>.dump
│  └─ .env.example
├─ docs/
│  ├─ architecture.md               # data model, flows, API surface, later-phase hooks
│  └─ api.http                      # REST Client examples
└─ .vscode/                         # extensions, tasks, launch configs
```
