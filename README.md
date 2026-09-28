# Offload CRM

Internal CRM / project-management web app. .NET 8 Blazor Web App (Interactive Server) + MudBlazor,
EF Core 8 + PostgreSQL, ASP.NET Core Identity. See `docs/` for the roadmap, decisions and status.

## Prerequisites

- .NET SDK 8.0.x (pinned by `global.json`)
- PostgreSQL (dev default: `localhost:5432`, user `postgres`; the password is set in user secrets, see below)

## Run (Development)

One-time setup: the connection strings (with the database password) are .NET user secrets, so they never end up
in the repository:

```bash
dotnet user-secrets set "ConnectionStrings:Default" "Host=localhost;Port=5432;Database=offload_crm_prod;Username=postgres;Password=<your password>" --project src/Offload.Crm.Web
dotnet user-secrets set "ConnectionStrings:Test" "Host=localhost;Port=5432;Database=offload_crm;Username=postgres;Password=<your password>" --project src/Offload.Crm.Web
```

The integration tests use the same secret (database `offload_crm_test`), or `OFFLOAD_TEST_CONNECTION` if set.

```bash
dotnet tool restore                      # local dotnet-ef 8.x
dotnet run --project src/Offload.Crm.Web --launch-profile http
```

- URL: **http://localhost:5080**
- Login: **`admin@offload.local` / `Admin123!`** (change it after first login; override with `Seed__AdminEmail` / `Seed__AdminPassword`)
- Demo users (Development only): `jana.novakova@offload.local`, `petr.dvorak@offload.local` / `Demo123!` (role: User, test DB only)
- Regional settings (D-28): Slovak formatting (sk-SK, `dd.MM.yyyy`, `1 250,50 €`), currency EUR, time zone Europe/Bratislava; UI texts in English.
- Roles (D-27): **Administrator** (users, project statuses, database switch) and **User** (everything else). Change a role in Settings → Users → Edit.

In Development the app creates/migrates both databases (production `offload_crm_prod` and test `offload_crm`, see below) and seeds project statuses and the admin on startup; demo data goes only into the test database (demo data only when there are no customers; disable with `Seed__DemoData=false`).
Other environments migrate only with `Database__MigrateOnStartup=true`.

Configuration (env vars override `appsettings*.json`):

| Setting | Default |
|---|---|
| `ConnectionStrings__Default` | Production DB. Dev: user secret, e.g. `Host=localhost;Port=5432;Database=offload_crm_prod;Username=postgres;Password=<your password>` |
| `ConnectionStrings__Test` | Test DB (optional; enables the switch). Dev: `...;Database=offload_crm;...` |
| `Database__SelectionFile` | `App_Data/active-database.json` (content root) — remembers the active DB |
| `Database__MigrateOnStartup` | `false` (always on in Development) |
| `Seed__AdminEmail` / `Seed__AdminPassword` | `admin@offload.local` / `Admin123!` |
| `Seed__DemoData` | `true` (Development only) |

### Production vs. test database (D-26)

The app works with one of two databases, chosen in **Settings → Database**:

- **Production**: `offload_crm_prod` (`ConnectionStrings:Default`)
- **Test**: `offload_crm` (`ConnectionStrings:Test`). `offload_crm_prod` was created as an exact copy of it on 2026-09-28.

The switch is app-wide (all users), survives restarts, checks that the target DB is reachable first, and reloads every
open tab. While the test DB is active, an orange **TEST DATABASE** badge is shown in the app bar. Without a `Test`
connection string the switch is hidden and the app always uses production. Both databases are migrated on startup;
`dotnet ef database update` only migrates the currently selected one.

The integration tests use their own throwaway database `offload_crm_test`; the UI tests run against whichever
database the running app has selected (they switch to Test and back in `DatabaseSwitchTests`).

### Fresh database

```bash
psql -U postgres -c "DROP DATABASE IF EXISTS offload_crm WITH (FORCE);"
dotnet run --project src/Offload.Crm.Web --launch-profile http   # recreates + seeds
# or without running the app:
dotnet ef database update --project src/Offload.Crm.Infrastructure --startup-project src/Offload.Crm.Web
```

New migration: `dotnet ef migrations add <Name> --project src/Offload.Crm.Infrastructure --startup-project src/Offload.Crm.Web --output-dir Persistence/Migrations`

## Test

```bash
dotnet test
```

Integration tests drop and recreate the `offload_crm_test` database with migrations on each run
(override with env `OFFLOAD_TEST_CONNECTION`). Stop the running app first on Windows, otherwise the build
can't overwrite the locked Web assembly.

### UI / end-to-end tests (`tests/Offload.Crm.UiTests`)

Playwright for .NET (xUnit) tests that drive a **running** app in headless Microsoft Edge (`Channel = "msedge"`,
so no Playwright browser download is needed; Edge must be installed). They don't start the app themselves.
When the app isn't reachable they are reported as **Skipped**, so a plain `dotnet test` stays green.

```bash
# terminal 1: start the app (Development DB with demo seed data)
dotnet run --project src/Offload.Crm.Web --launch-profile http

# terminal 2: run only the UI tests (the project doesn't reference the Web project, so no DLL lock)
dotnet test tests/Offload.Crm.UiTests
```

| Env var | Default | Purpose |
|---|---|---|
| `OFFLOAD_UI_BASEURL` | `http://localhost:5080` | App under test |
| `OFFLOAD_UI_HEADED` | (unset) | `1` = show the browser window |
| `OFFLOAD_UI_CHANNEL` | `msedge` | Browser channel (`chrome`, `msedge`, …) |
| `OFFLOAD_UI_ADMIN_EMAIL` / `OFFLOAD_UI_ADMIN_PASSWORD` | `admin@offload.local` / `Admin123!` | Admin login |

Notes:
- The tests need the demo seed data (e.g. "Česká logistika a.s." with projects, "Moravské vinařství Šťastný",
  users `jana.novakova@…`/`petr.dvorak@…`). They create their own records with unique names and delete them again.
- `KnownBugTests.cs` holds regression tests for open bugs in `docs/QA_REPORT.md`; they are skipped with the bug ID.
  Remove the `Skip` when fixing the bug.
- Don't run the UI tests while `dotnet test` (whole solution) rebuilds the Web project; stop the app before a full
  solution build/test on Windows.

## API

Minimal APIs under `/api` (same cookie auth as the UI; 401 without it). Errors are RFC 7807 ProblemDetails
(400 with field `errors`, 404, 409).

- `GET /api/customers?search=&status=Lead|Active|Inactive&sort=&descending=&page=&pageSize=`
  (sort: `companyName`, `registrationNo`, `city`, `status`, `email`, `phone`, `projectCount`, `lastActivity`, `created`)
- `GET|PUT|DELETE /api/customers/{id}`, `POST /api/customers`
- `GET|POST /api/customers/{id}/contacts`, `GET|PUT|DELETE /api/contacts/{id}`
- `GET|POST /api/project-statuses`, `GET|PUT|DELETE /api/project-statuses/{id}`, `POST /api/project-statuses/{id}/move?direction=up|down`, `POST /api/project-statuses/{id}/default`
- `GET|POST /api/projects` (filters `statusId` (repeatable), `customerId`, `priority`, `responsibleUserId`, `activeOnly`), `GET|PUT|DELETE /api/projects/{id}`, `PATCH /api/projects/{id}/status` `{ "statusId": 3 }`, `GET /api/projects/{id}/timeline`
- `GET|POST /api/tasks` (filters `status` (repeatable), `priority`, `assignee=me|unassigned|{userId}`, `projectId`, `customerId`, `overdue`), `GET|PUT|DELETE /api/tasks/{id}`, `PATCH /api/tasks/{id}/status` `{ "status": "Done" }`
- `GET|POST /api/activities` (filters `type`, `customerId`, `projectId`, `authorId`, `from`, `to`), `GET|PUT|DELETE /api/activities/{id}`

All lists accept `search, sort, descending, page, pageSize` (pageSize 1–100) and return `{ items, totalCount, page, pageSize }`.

## Project structure

```
src/Offload.Crm.Domain          entities + enums (all phases' schema)
src/Offload.Crm.Application     services, DTOs, DataAnnotations validation, exceptions
src/Offload.Crm.Infrastructure  AppDbContext, EF configurations, migrations, seeding
src/Offload.Crm.Web             Blazor UI (MudBlazor), minimal API, auth, DI
tests/Offload.Crm.Tests         xUnit: unit, service (Postgres) and API integration tests
tests/Offload.Crm.UiTests       xUnit + Playwright (msedge): E2E/smoke tests against a running app
```
