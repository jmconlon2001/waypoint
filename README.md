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

Not built yet (hooks only): Google Calendar import, metrics beyond daily totals, LLM features. See [docs/architecture.md](docs/architecture.md).

## Prerequisites

| Tool | Used for |
|---|---|
| [Podman Desktop](https://podman-desktop.io) (Podman + a WSL2 machine, with the compose extension) | Running the pod |
| [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) | API development and tests |
| [Node.js 22 LTS](https://nodejs.org) (npm 10) | Web development and tests |
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
dotnet test api/Waypoint.slnx           # xUnit: quick-add parser, gaps/totals, timer/interruption flows, migration snapshot check
cd web; npx ng test --watch=false       # Vitest: time-zone helpers, timeline layout, shell
```

### Database migrations

Migrations are applied automatically when the API starts. To change the model:

```powershell
dotnet tool install --global dotnet-ef   # once
cd api/src/Waypoint.Api
dotnet ef migrations add <Name> --output-dir Data/Migrations
```

> **Note:** the initial migration (`Data/Migrations/20261117170000_InitialCreate*`) and the model snapshot were written by hand, because the EF tooling could not be run when Phase 1 was built.
>
> The test `MigrationTests.Model_has_no_pending_changes` checks the snapshot against the model.
>
> If that test fails on a fresh clone with an empty database:
>
> 1. Delete `Data/Migrations`.
> 2. Run `dotnet ef migrations add InitialCreate --output-dir Data/Migrations`.
> 3. Drop the volume with `podman compose down -v`.

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
│  │  └─ Integrations/              # Phase 2+ hooks (calendar import interface)
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
│        ├─ pages/                  # Today, Tasks, Review (+ timeline layout), Projects & settings
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
