# Offload CRM — Decisions

Owner: PM. Each decision has an ID (D-xx). If Dev or QA needs to deviate, propose the change to the PM, and the PM records it here.

## D-01 Stack & architecture
- **.NET 8**, one solution `Offload.Crm.sln`.
- **UI:** ASP.NET Core Blazor Web App, **Interactive Server** render mode (set globally), **MudBlazor** (latest 7.x/8.x compatible with .NET 8). No JS toolchain is available (no Node), so there are no npm/SPA frameworks.
- **Data:** EF Core 8 + Npgsql, PostgreSQL. Schema changes go through **migrations only** (no `EnsureCreated`). Local tool manifest (`.config/dotnet-tools.json`) with `dotnet-ef` 8.x.
- **Auth:** ASP.NET Core Identity (cookie auth) with `ApplicationUser : IdentityUser` (+ `FullName`, `IsActive`). Every page requires auth except Login.
- **Layers (keep it simple):**
  - `src/Offload.Crm.Domain`: entities, enums, and no dependencies.
  - `src/Offload.Crm.Application`: service interfaces + implementations, DTOs, validation (FluentValidation or DataAnnotations; pick one and use it everywhere), `Result`/exception types. It depends on Domain and on an `IAppDbContext` abstraction, **or** it is merged into Infrastructure if that turns out simpler. Dev decides; the rule is that the UI and API only call services and never the DbContext directly.
  - `src/Offload.Crm.Infrastructure`: `AppDbContext`, EF configurations, migrations, seeding, Identity stores.
  - `src/Offload.Crm.Web`: Blazor components, layout, theme, minimal APIs under `/api/*`, and DI composition.
  - `tests/Offload.Crm.Tests` (xUnit): unit tests for services/validation, plus integration tests against the Postgres DB `offload_crm_test` (recreated with migrations per test run).
  - `tests/Offload.Crm.UiTests` (xUnit + Microsoft.Playwright, `Channel="msedge"`). QA owns this project.
- **REST API:** minimal APIs, `/api/customers`, `/api/contacts`, `/api/projects`, `/api/tasks`, `/api/activities`, `/api/project-statuses`, `/api/users` (read). They use the same services and the same validation. Auth is the same cookie (API keys/tokens are deferred). Errors come back as RFC 7807 `ProblemDetails` (400 validation with field errors, 404, 409 conflict).
- **Concurrency:** last write wins for the MVP. `UpdatedAt` is kept on all main entities.
- **Config:** `appsettings.json` (no secrets), `appsettings.Development.json` (no connection strings; locally they are .NET user secrets of the Web project, see README), env vars override (`ConnectionStrings__Default`, `Seed__AdminEmail`, `Seed__AdminPassword`). The test connection string comes from the env var `OFFLOAD_TEST_CONNECTION`, falling back to the user-secret `ConnectionStrings:Default` with DB `offload_crm_test`.
- **Startup:** in Development, the app applies pending migrations and seeds on startup. In other environments this is controlled by config `Database:MigrateOnStartup` (default false).

## D-02 Configurable project statuses
- Table `ProjectStatuses`: `Id`, `Name` (unique, required, max 50), `Color` (hex, e.g. `#0F5132`), `SortOrder` (int), `IsActive` (the status "counts as an active project"), `IsClosed` (a terminal status such as Delivered/Cancelled), `IsDefault` (exactly one; used for new projects).
- Seed data (order, flags):

| # | Name | IsActive | IsClosed |
|---|------|----------|----------|
| 1 | Lead | yes | no |
| 2 | Analysis | yes | no |
| 3 | Proposal | yes | no |
| 4 | Development | yes | no |
| 5 | Testing | yes | no |
| 6 | Delivered | no | yes |
| 7 | Maintenance | yes | no |
| 8 | Cancelled | no | yes |

  Lead is the default.
- Settings supports create, rename, recolor, reorder (up/down or a sort number), toggling the flags, and setting the default.
- **Delete is blocked when any project uses the status** (409 + a message saying how many projects use it). The default status can't be deleted either. `IsActive` and `IsClosed` can't both be true (validation).
- **Task statuses are a fixed enum** in the MVP (`Todo`, `InProgress`, `Blocked`, `Done`). Making them configurable would complicate the Kanban for little value. This is deferred.

## D-03 "Active" definitions
- **Active project** = a project whose status has `IsActive = true`. (Amended in Phase 1 review: there is no archive flag in the MVP, per D-07/D-19. "Archiving" a project = moving it to a closed status such as Delivered or Cancelled.)
- **Active customer** = `Customer.Status == Active`. The dashboard shows this count.
- **Open task** = status not `Done`.
- **Overdue** = due/target date < today and not Done/closed.
- **Upcoming deadlines** (dashboard) = open tasks with a due date, and active projects with a target date, both within the next 14 days (overdue included and shown highlighted).

## D-04 Customer statuses
Enum: `Lead`, `Active`, `Inactive`. The default for a new customer is `Lead`. The status is changed manually, with no automatic transitions in the MVP.

## D-05 Priority
One enum for projects and tasks: `Low`, `Medium`, `High`, `Critical`. The default is `Medium`. Each value has a colored chip (Critical = red, High = orange #E8912D, Medium = blue/grey, Low = grey).

## D-06 "Last activity" (Customer)
It's computed and not stored: `max(Activity.Date)` over activities that are linked to the customer directly **or** through one of its projects. If there are none, it shows "—". It's calculated in the query (a subquery/projection), and it's sortable in the customer list.

## D-07 Delete semantics
- **Customer:** hard delete is **blocked while it has projects** (409, "Customer has N projects. Delete or reassign them first."). If it has none, delete cascades to its contacts and to the activities linked only to the customer. The user sees a confirmation dialog.
- **Contact:** hard delete, with confirmation.
- **Project:** hard delete with confirmation. It cascades to its tasks. Its activities are **kept**: `ProjectId` is set to null, and the customer link remains.
- **Task / Activity:** hard delete with confirmation.
- **Project status:** see D-02.
- **User:** never deleted. Users are deactivated instead (`IsActive=false`, login blocked). A deactivated user stays shown on historical records and can't be picked for new assignments.
- There is no soft-delete/archive in the MVP. Setting a customer to `Inactive` or a project to Cancelled/Delivered is how records get "archived".

## D-08 Attachments
**Deferred** (not in the MVP). The domain is structured so that a `Document` entity (ProjectId, file name, storage path, size, uploaded by/at) can be added later. For now, links can go in the Notes fields.

## D-09 Users & auth
- Identity with email as the username. Password policy: min 8 chars, at least one digit, and at least one uppercase letter. Lockout after 5 failed attempts for 5 minutes.
- Seeded admin: **`admin@offload.local` / `Admin123!`**, which can be overridden via `Seed:AdminEmail`/`Seed:AdminPassword`. The README tells the owner to change it.
- Roles: *(superseded by D-24: no roles in the MVP)* a single role, `Admin`. All users have full access in the MVP. Only admins can manage users in Settings, and in practice every seeded or created user is an Admin until we need finer control.
- Settings → Users: list, create (email, full name, initial password), edit name, deactivate/reactivate, reset password. No email sending.
- Dev seed (Development only): 2 extra users, around 8 customers, contacts, around 12 projects across all statuses, around 40 tasks, and around 30 activities, so that QA and the dashboard have realistic data. It runs idempotently, only when the DB has no customers. It can be turned off with `Seed:DemoData=false`.
- "Responsible person" (project), "Assigned person" (task), and "Author" (activity, set automatically to the current user and not editable) are FKs to `AspNetUsers`, nullable except Author.

## D-10 Money & currency
- `EstimatedValue` / `ActualValue`: `decimal(18,2)`, nullable, and >= 0.
- Currency: **CZK** as a single app-wide setting (`App:Currency`, default `CZK`). There's no per-project currency in the MVP. Values display as `1 250 000 Kč`.

## D-11 Locale, dates, language
- UI language: **English** (the requirements are written in English). The code keeps strings in one place so that Czech localization can be added later.
- Culture for formatting: **cs-CZ**. Dates display as `dd.MM.yyyy`, date-times as `dd.MM.yyyy HH:mm`, and numbers as `1 234,50`. Date pickers use the same format.
- Storage: `timestamp with time zone` in UTC for timestamps (`CreatedAt`, `UpdatedAt`, `CompletedAt`, `Activity.Date`), and `date` (`DateOnly`) for StartDate, TargetDate, and DueDate. Times display in the local server time zone (Europe/Prague).

## D-12 Validation rules
General: trim strings, and treat empty strings as null for optional fields. Errors appear inline under the field, and the Save button reports what failed. The API returns 400 ProblemDetails with field errors.

| Entity | Rules |
|---|---|
| Customer | CompanyName required, max 200. RegistrationNo (IČO) optional, max 20, **unique among customers when present** (case-insensitive, trimmed). VatId optional, max 20. Email valid format. Website: a valid http(s) URL (if the scheme is missing, prepend `https://`). Phone max 30 and chars `+0-9 ()-`. Address: Street, City, PostalCode, Country (default "Czech Republic"), each optional, max 200. Notes max 4000. |
| Contact | FirstName + LastName: LastName required (max 100), FirstName optional. Position max 100. Email format. Phone as for Customer. CustomerId required and existing. |
| Project | Name required, max 200. CustomerId required. StatusId required (default status). Priority required. TargetDate >= StartDate when both are set. Values >= 0 and <= 999 999 999 999. Description max 4000, Notes max 4000. |
| Task | Title required, max 200. ProjectId **required** (all tasks belong to a project in the MVP). Status and Priority required. DueDate optional. Assignee must be an active user. |
| Activity | Date required (default now) and not more than 1 year in the future. Type required. Description required, max 4000. **At least one of Customer/Project required.** If Project is set, Customer is auto-set to the project's customer, and a mismatch is rejected. |
| ProjectStatus | Name required, unique, max 50. Color a valid hex. See D-02. |

## D-13 Task lifecycle
- `CompletedAt` is set automatically when status becomes `Done` and cleared when it leaves `Done`.
- `CreatedAt`/`UpdatedAt` are set by the DbContext (SaveChanges interceptor) on all entities.

## D-14 Timeline
The project detail page has a **Timeline** tab. It's a chronological feed (newest first) that merges the project's activities, task completions (from `CompletedAt`), and project creation. There's **no status-change audit table** in the MVP. When a project's status changes, the service auto-creates an Activity of type `Note` ("Status changed: Proposal → Development") authored by the current user. That gives a timeline and history at almost no cost. The customer detail page shows the same kind of feed for the customer's activities, including those of its projects.

## D-15 UX conventions
- The MudBlazor theme uses primary `#0F5132` and secondary `#E8912D`, with a light theme only in the MVP. The layout is a left nav drawer (Dashboard, Customers, Projects, Tasks, Activities, Settings), an app bar with the current user and logout, and a responsive drawer (collapses on screens < 960px).
- Lists use a MudDataGrid/MudTable with server-side paging (25 per page), search, filters, and sortable columns. Filter/search state is kept in the URL query string where it's cheap to do so.
- Create/edit happens in dialogs for small entities (Contact, Task, Activity, Status) and in dialogs or pages for Customer/Project (Dev's choice, but consistent).
- Every destructive action shows a confirmation dialog. Success and error messages appear as snackbars. Empty states show a message and a primary action button. Loading shows skeletons or a progress bar. Unhandled errors show a friendly error boundary and are logged.
- The Tasks page has a List/Kanban toggle. Kanban has 4 columns by status, and cards can change status by drag & drop (MudDropContainer) **and** through a status menu on the card (for accessibility and testability).

## D-16 Search
Search is a simple case-insensitive `ILIKE` over the main text fields (Customer: name, RegNo, VAT, email, and contact names; Project: name and customer name; Task: title). There's no full-text search in the MVP.

## D-17 Out of scope for the MVP
Attachments, email integration, notifications, per-role permissions, multi-currency, localization to Czech, audit log, import/export, dark mode, and custom fields.

## D-18 Implementation decisions from Phase 1 (Dev; reviewed by PM, see D-19)
These extend or clarify earlier decisions. PM: please confirm or push back.
- **Layers (D-01):** `Offload.Crm.Application` is a separate project. Services depend on `IAppDbContextFactory`, which creates a **short-lived DbContext per service call**. A circuit-scoped DbContext in Blazor Server would be shared across concurrent UI events and keep stale tracked entities. The UI and API only call services.
- **ApplicationUser:** it lives in Domain, so Domain references `Microsoft.Extensions.Identity.Stores` (for the `IdentityUser` base class only). It keeps Identity's **string (GUID) key**. All other entities use `int` ids. It has `FullName`, `IsActive`, `CreatedAt`, and `UpdatedAt`.
- **Validation:** DataAnnotations everywhere (on the input DTOs, shared by the forms and the API), plus custom attributes `EmailFormat`, `PhoneChars`, and `HttpUrl`. Rules that need the DB (unique IČO) run in the services. The services throw `AppValidationException` (400, field errors keyed by camelCase property names in the API), `NotFoundException` (404), and `ConflictException` (409). The browser's native form validation is turned off (`novalidate`), so our inline messages are shown instead.
- **IČO uniqueness:** there's a `Customers.RegistrationNoNormalized` column (whitespace removed, upper-cased, set by the entity) with a unique index filtered to NOT NULL. `"255 966 41"` and `"25596641"` count as duplicates. The display value keeps the user's formatting (trimmed).
- **Search (D-16):** implemented as `lower(col) LIKE '%term%'` (LINQ `ToLower().Contains`). That's equivalent to ILIKE and doesn't tie the Application layer to Npgsql. Contact search matches both "First Last" and "Last First".
- **Activity:** has an optional `Subject` (max 200). `ActivityType` = Call, Email, Meeting, Note, Other. The DB check constraint `CK_Activities_CustomerOrProject` enforces D-12's "at least one of customer/project".
- **No `IsArchived` on Project:** D-03 mentions "not archived", but D-07 says there's no archive in the MVP. So "active project" = its status `IsActive`. Adding the column later is a trivial migration.
- **Render mode:** Interactive Server with **prerendering disabled** (no double data loads). `/account/*` (login/logout) are **static SSR** pages because they must set or clear the auth cookie. Logout: the app bar navigates to `/account/logout`, which renders an antiforgery-protected form that submits itself (POST), so a third-party link can't log users out.
- **Unknown routes:** page requests re-execute to `/status/{code}` (a friendly "Page not found"). The browser still gets HTTP 404, so the console shows the standard "Failed to load resource: 404" line for that request. That's expected. Unknown `/api/*` routes return a 404 ProblemDetails.
- **Caching:** every non-static response is sent with `Cache-Control: no-cache, no-store`, so "logout, then Back" doesn't show cached pages.
- **Login messages:** wrong credentials show "Invalid e-mail or password.". "Deactivated" is shown only when the password is correct, so it doesn't leak account state. Lockout shows a message with the 5-minute wait.
- **Customer create/edit:** dialogs (the same pattern as Contact/Task/Activity, D-15). Search + status filter are kept in the URL (`?search=&status=`); sort/page aren't.
- **Seeding:** project statuses are seeded only when the table is empty, so user edits survive restarts. The demo seed adds users `jana.novakova@offload.local` and `petr.dvorak@offload.local` with password `Demo123!`.
- **Dev port:** `http://localhost:5080` (launch profile `http`). HTTPS redirection/HSTS apply only outside Development.

## D-19 PM review of D-18 (Phase 1 review, 2026-09-27)
All of D-18 is **accepted**, with these rulings and amendments:
- **D-03 vs D-07 conflict:** resolved in favour of D-07. There's no `IsArchived` in the MVP, and **active project = status `IsActive`**. D-03 has been amended. A closed status (Delivered/Cancelled) is the archive mechanism. The Projects list's "Active only" filter (T-14) uses exactly this rule.
- **DbContext per call, Application project, string user ids, DataAnnotations, and `RegistrationNoNormalized`:** accepted.
- **Validation order:** normalise first (trim, empty→null), then validate, in both the UI and the API (BUG-010). Single-line inputs show `MaxLength` via `maxlength`/`Counter`.
- **Activity:** accepted. `Subject` is optional. The enum value `Call` **displays as "Phone call"** (the requirement's wording). The auto status-change activity (D-14) uses Type = `Note`, with Subject "Status changed" and Description "Status changed: {old} → {new}".
- **Logout:** amended (BUG-011). A GET to `/account/logout` shows a "Sign out?" confirmation page with a button and **no auto-submit script**. The app-bar Logout button posts the form directly (same-origin + antiforgery). On logout, the server calls `UpdateSecurityStampAsync` (BUG-003).
- **Session revalidation:** amended (BUG-003/004). The cookie `OnValidatePrincipal` and the circuit `RevalidatingServerAuthenticationStateProvider` check **both the security stamp and `IsActive`**, with an interval of **1 minute**. Deactivating a user (T-24) also updates the stamp.
- **Login messages:** a generic message for every failure is the target (BUG-013, deferred to Phase 4). Until then, D-18's current behaviour stands.
- **Unknown-route 404 console line:** accepted as expected, and it's not a bug.
- **Seeding statuses only when the table is empty, and the demo users with `Demo123!`:** accepted (Development only).

## D-20 Shared list & API conventions (apply to all lists from Phase 2 on; retrofit Customers)
1. **URL state:** search, filters, **sort, sort direction, page and page size** live in the query string (resolves BUG-015). Back and the breadcrumb return to the same view.
2. **Paging:** `page`/`pageSize` are clamped in the service (page >= 1, 1 <= pageSize <= 100), and the offset is computed in `long` or guarded (BUG-002). If the current page is beyond the last page after a delete or filter change, the list jumps to the last existing page (BUG-005). There are 3 distinct states: *empty DB* ("No X yet" + create), *no results* ("No matches" + clear filters), and *loading*.
3. **Sorting:** nulls always sort last, in both directions (BUG-014). Enum columns sort in declaration (lifecycle) order: Customer status Lead→Active→Inactive, Priority Low→Critical, Task status Todo→Done. Project status sorts by `SortOrder`.
4. **Enums:** every enum input (body and query) is checked with `Enum.IsDefined`. Undefined values give 400 with a field error (BUG-001). This applies to CustomerStatus, Priority, TaskItemStatus, and ActivityType.
5. **Responsive tables:** at <= 1280 px, secondary columns hide (e.g. Created, Phone, E-mail, VAT). The **row actions are always visible without horizontal scroll** at 1280/1024/768 (use a compact kebab menu, or make the row clickable plus a kebab). The primary name column wraps at max 2 lines (ellipsis + tooltip) (BUG-006).
6. **Long text:** a global `overflow-wrap: anywhere` applies to table cells, chips, breadcrumbs, headers and links. No page may scroll horizontally (BUG-007).
7. **API errors:** every `/api/*` error is `application/problem+json`. Malformed JSON or a wrong type returns 400 with a readable `detail` and the JSON path. Unhandled errors return a 500 ProblemDetails without a stack trace outside Development, and 4xx client errors aren't logged as `fail` (BUG-008).
8. **E-mail format:** a stricter regex that rejects `? < > , ; " ( ) [ ] \` and whitespace, with a single `@` (BUG-012). Links are always built from validated values.

## D-21 Implementation decisions from T-38 and Phase 2 (Dev; reviewed by PM, see D-22)
- **Shared list infrastructure (D-20):**
  - Service side: `SortMap<T>`/`SortBy` (nulls last, enum declaration order, stable `Id` tie-breaker) and `ToPagedResultAsync` (long offset; a page past the end returns an empty page).
  - UI side: `ListPageBase<T>` (the URL is the single source of truth, the jump to the last existing page, and the empty-DB / no-results / loading / error states), plus `ListState`, `SortHeader` and `ListPager`.
  - List URL keys: `search, sort, desc, page, size`, plus filters (`status, customer, project, priority, responsible, assignee, active, overdue, type, author, from, to, view`). Defaults are left out of the URL.
- **Tables:** lists use `MudSimpleTable` with our own sort headers and pager, instead of `MudTable` server data. That gives full control over URL state and the out-of-range page handling. The pager keeps MudTable's CSS class names.
- **Sessions (D-19):**
  - Every service call from a Blazor circuit re-validates the session (security stamp + `IsActive`) before it opens a DbContext. If that fails, the call throws `SessionExpiredException` (the API maps it to 401) and the tab is sent to `/account/login?expired=1`.
  - Logout ends *all* sessions of that user: other tabs are stopped on their next action, and other browsers within ≤ 1 min.
  - The login page doesn't bounce an expired session back into the app.
- **API routing:** the `/api/{**path}` catch-all was removed. Unknown `/api` routes return 401 for anonymous callers and 404 ProblemDetails for signed-in users. A wrong verb returns 405 with `Allow`, and a wrong content type returns 415. All of these are problem+json via `UseStatusCodePages` in the API branch. (This happens to resolve BUG-009, which is still formally deferred.)
- **Forms:** `FormValidator` replaces `DataAnnotationsValidator`. It normalises and then validates, and it also shows server-side field errors. `IValidatableObject` rules (e.g. target ≥ start) are evaluated even when an attribute failed, so every error shows at once. Single-line inputs show a `Counter`, but there's no hard `maxlength`, because that would truncate pasted, padded values.
- **Project statuses:**
  - Names are unique case-insensitively. Colors are `#RRGGBB`.
  - The default can't be un-flagged directly; you set another status as the default instead (atomic switch).
  - Reorder is move up/down, which also renumbers `SortOrder` 1..n.
  - API: `POST /api/project-statuses/{id}/move?direction=up|down` and `POST /api/project-statuses/{id}/default`.
- **Projects:**
  - A status change through the inline select, the edit dialog or `PATCH /api/projects/{id}/status` logs the D-19 Note activity authored by the current user.
  - Changing a project's customer also moves its activities' `CustomerId`, so D-12's consistency holds.
  - Money values allow at most 2 decimals.
  - "Overdue" for projects = target < today and the status is not closed.
  - The timeline is available at `GET /api/projects/{id}/timeline`.
- **Assignees:** a *newly* assigned responsible person or assignee must be active. Keeping an already assigned, since-deactivated user is allowed, so old records stay editable. Pickers list only active users (plus the record's current user, marked "(inactive)"). Filters list everyone.
- **Tasks:**
  - The list defaults to open tasks (`status` absent). `status=all` and single statuses are also available.
  - The Kanban shows all 4 columns and ignores the implicit "open" default (otherwise Done would always be empty). An explicitly chosen status or other filters still apply.
  - The board is capped at 500 cards, with a notice.
  - The default list sort is due date ascending, with no due date last.
  - `assignee=me|unassigned|{userId}`.
  - Multi-value API filters use repeated keys (`?status=Todo&status=Blocked`, `?statusId=1&statusId=2`).
- **Activities:**
  - The default sort is newest first.
  - Search covers subject + description (extends D-16).
  - The date range filter uses local (Europe/Prague) whole days (`from`/`to` = `yyyy-MM-dd`).
  - A customer's feed includes its projects' activities (D-06).
  - A missing date defaults to now.
- **Settings:** the page has tabs. "Project statuses" is built (T-13). "Users" is a placeholder until T-24.
- **Test infrastructure:** QA's `PlaywrightFixture` signs the admin in fresh for every context. A logout now changes the user's security stamp, so a cached admin cookie would stop working mid-run.

## D-22 PM review of D-21 (Phase 2 review, 2026-09-28)
D-21 is **accepted** except for the amendments below. The shared list infrastructure, `MudSimpleTable`, `FormValidator` (with a counter instead of a hard `maxlength`), the assignee rules, the API routing (401/404/405/415), and the task and activity filter details are all accepted as written.
1. **The Kanban ignores the implicit "open tasks" default:** accepted. **Amendment:** by default, the Done column shows only tasks **completed in the last 30 days**. Its footer shows "N older done tasks: view in list" and links to `/tasks?status=Done`. If the user explicitly picks `status=Done` or `status=all`, the 30-day window doesn't apply. Without the window, the Done column grows forever and would eventually hit the cap.
2. **Logout ends all of the user's sessions:** accepted. It's an internal tool that people use on shared machines, so security wins over convenience. The login page shows "You were signed out." for `?expired=1`. The README documents this (T-35).
3. **A status can't lose the default flag directly:** accepted. This keeps the invariant that exactly one status is the default. The UI marks the default status with a "Default" chip, and the other rows have a "Set as default" action.
4. **Changing a project's customer moves its activities:** accepted, because D-12's consistency rule requires it. **Amendment:** when the customer changes, the edit dialog shows a warning ("N activities will move to {new customer}") before saving. The service also logs a Note activity, "Customer changed: {old} → {new}", in the same way as a status change (D-19).
5. **The Kanban is capped at 500 cards:** accepted as a safety limit. The notice says how many cards are hidden and asks the user to narrow the filters. With amendment 1 it shouldn't trigger on realistic data.
6. **Dev's change to QA's `PlaywrightFixture`:** accepted. It's a test-only compatibility fix that the D-19 security change forced. **Rule going forward:** Dev may change QA-owned test infrastructure only when a product change requires it, and must record the change in STATUS/QA_REPORT. QA re-confirms the fixture in T-22.
7. **BUG-009 (405):** it was resolved as a side effect of the routing change. It's marked **Fixed, pending QA verification** and is no longer deferred to T-36.

## D-23 Dashboard (T-23)
**Principle:** every number on the dashboard comes from **the same query predicate** as the list it links to. That predicate is a shared filter/specification in the Application layer, so a KPI and its click-through list can never disagree. "Today" is computed in Europe/Prague. The data loads once per visit (no auto-refresh), and there's a Refresh button.

**Layout (desktop):** a KPI row, then a two-column grid.
- The left column holds Projects by status and Upcoming deadlines.
- The right column holds My open tasks and Recent activities.
- Recently updated sits at the bottom.

Below 960 px everything stacks into one column, and the KPI cards wrap two or three per row.

| Widget | Content | Rule | Click-through |
|---|---|---|---|
| KPI: Active customers | count | `Customer.Status == Active` | `/customers?status=Active` |
| KPI: Active projects | count | the status is `IsActive` (D-03) | `/projects` (active only is the default) |
| KPI: Open tasks | count | status ≠ Done | `/tasks` |
| KPI: Overdue tasks | count, shown in red when > 0 | open and `DueDate` < today | `/tasks?overdue=true` |
| KPI: Pipeline value | a CZK sum (D-10) | the sum of `EstimatedValue` over active projects, where null counts as 0 | `/projects?sort=estimatedValue&desc=true` |
| Projects by status | one horizontal bar per status in `SortOrder` and the status color, including zero counts | all projects | `/projects?status={id}` **with "active only" turned off**. Otherwise closed statuses would show an empty list. |
| Upcoming deadlines | at most 10 rows, overdue first and then by date. Each row has a task/project icon, title, project/customer, date, person, and a label: "Overdue" (red), "Today" (orange), or "in N days" | open tasks with `DueDate` <= today+14, and projects in a non-closed status with `TargetDate` <= today+14 | the task's project or the project itself. "View all overdue" goes to `/tasks?overdue=true` |
| My open tasks | at most 10 rows: title, project, priority chip, and due date (red when overdue) | assignee = the current user and the task is open. Sorted by due date ascending, with no due date last | the task's project. "View all" goes to `/tasks?assignee=me` |
| Recent activities | the 10 newest: type icon, the subject (or the first 80 characters of the description), customer/project, author, and a relative date | `Date <= now` (future-dated activities are excluded) | the project if there is one, otherwise the customer. "View all" goes to `/activities` |
| Recently updated | the top 5 customers and the top 5 projects by `UpdatedAt`: name, status chip, and "updated 2 h ago" | none | the detail page |

**States:**
- Each widget has its own loading skeleton.
- Each widget has its own plain-text empty state, for example "Nothing due in the next 14 days."
- If one widget fails, it shows an inline error with a Retry button, and the rest of the page still renders.
- On an empty DB, the KPIs show 0 and a welcome panel offers "Add your first customer".

**API:** `GET /api/dashboard` returns the same DTO for the current user.

**Performance:** at most about 8 DB round trips and no N+1 queries. The page loads in under 1 s with the demo data.

## D-24 Users & roles (T-24)
- **Roles: none in the MVP.** Every active user has full access, including Settings → Users. For a small internal team, roles would add UI and test cost with no current need.
  - Preparation for later: all user-management actions go through a named authorization policy, `ManageUsers`. For now it only requires an authenticated, active user, so adding an Admin role later is a one-line policy change.
  - The seeded `Admin` role (D-09) may stay in the DB unused. This supersedes D-09's role wording.
- **Guard rails:** you can't deactivate yourself (the service returns 409, and the UI hides the action on your own row). At least one active user must always exist. Users are never deleted (D-07).
- **Users list (Settings → Users tab):**
  - Columns: Full name, E-mail, Status chip, Created, and Last sign-in. `LastSignInAt` is set on a successful login and needs a small migration.
  - Search by name or e-mail, with a status filter of All, Active or Inactive (default All).
  - Sort: active users first, then by name. There's no paging, because the user base is small.
  - Row actions: Edit, Reset password, and Deactivate or Reactivate.
- **Create user (dialog):**
  - Fields: Full name (required, max 100), E-mail (required, the D-20.8 format, unique case-insensitively, max 256), and Password + Confirm (the D-09 policy).
  - New users are created active, with the e-mail treated as confirmed. No e-mail is sent.
  - Identity errors are mapped to field errors, for example "Password must contain a digit" shows under Password.
- **Edit user (dialog):** Full name and E-mail. Changing the e-mail also changes the login username, which must stay unique. Status and password are not edited here.
- **Deactivate:**
  - A confirmation dialog explains the effect: "{name} will be signed out within a minute and can't sign in. Their N open tasks and M active projects stay assigned. Reassign them manually."
  - The service sets `IsActive=false` and updates the security stamp, so all of their sessions end (D-19).
  - The user is removed from pickers but still shows on existing records as "(inactive)" (D-21).
- **Reactivate:** confirmation, then `IsActive=true`. Any lockout is cleared.
- **Reset password (dialog):** new password + confirm, following the policy. It updates the security stamp, which ends all of that user's sessions, and clears any lockout. It works for inactive users too. No e-mail is sent, and the admin passes the password on in person.
- **Change own password:** the app-bar user menu has a "Change password" dialog with the current password, the new password and a confirmation. On success, the user's other sessions end and the current session stays signed in (the cookie is re-issued). This is P0, because the README tells the owner to change the seeded admin password.
- **API:** read-only in the MVP: `GET /api/users` (`?active=true|false&search=`) and `GET /api/users/{id}`, returning id, fullName, email, isActive and lastSignInAt. User writes are **UI-only**, because they're security-sensitive and no integration needs them. Password hashes and security stamps are never exposed.

## D-25 API conventions for T-27
- **List endpoints:** they all take `page`, `pageSize` (clamped per D-20.2), `search`, `sort` and `descending`, plus entity filters that use the same names as the UI URL keys (D-21). They all return the same envelope: `{ items, page, pageSize, totalCount }`.
- **Errors:** as per D-20.7 and D-21 (400/401/404/405/409/415 as problem+json).
- **OpenAPI:** Swashbuckle, with `/swagger` served in Development only and returning 404 in any other environment. Every endpoint declares its success type and its `ProducesProblem` codes, and endpoints are grouped by tag per entity.
- **Health:** `GET /health` is anonymous and checks the DB. It returns 200 `Healthy` or 503. It's there for later hosting and monitoring.
- **Auth:** the same cookie as the UI. API keys and tokens are still deferred (D-17).

## D-26 Production and test database with a runtime switch (PM request, 2026-09-28)
- **Request:** the existing database becomes the **test** database; an exact copy is the **production** database; a toggle in Settings switches between them so the PM can test safely.
- **Databases:** production `offload_crm_prod` (`ConnectionStrings:Default`), created with `CREATE DATABASE … TEMPLATE offload_crm`; test `offload_crm` (`ConnectionStrings:Test`). Integration tests keep their own `offload_crm_test`.
- **Scope of the switch: app-wide, not per user.** Identity (users, passwords, security stamps) lives in each database, and session validation runs in fresh DI scopes, so a per-browser choice would need auth to span two databases. An app-wide switch is simple and predictable for an internal tool. Consequence: when someone switches, **everyone** works on that database. Mitigations: a confirmation dialog, an always-visible orange "TEST DATABASE" badge, and every open tab reloads immediately.
- **Mechanics:** `DatabaseSelector` (singleton) holds both connection strings and the current target, persisted to `App_Data/active-database.json`. `SwitchableDbContextFactory` creates each context for the current target. Blazor circuits record their database when they open; `CircuitSessionGuard` reloads a stale circuit before any data access, and `MainLayout` reloads on the `Changed` event.
- **Safety:** before switching, the target DB must be reachable, otherwise an error is shown and nothing changes. Connection strings and passwords are never shown, only database names. The switch uses the `ManageUsers` policy (all active users in the MVP; admin-only once roles exist).
- **Sessions:** after a switch, a user stays signed in if the same user with the same security stamp exists in the other database (true right after the copy). Users created or changed in only one database are signed out on switch; this is expected.
- **Disable in real production:** omit `ConnectionStrings:Test`. The switch is then hidden and a stale selection file is ignored.
- **Production cleared (2026-09-28, PM request):** all customers, contacts, projects, tasks and activities, plus every user except `admin@offload.local`, were removed from `offload_crm_prod`. Project statuses and the admin are kept. Backup taken before clearing: `pg_dump -Fc` (`offload_crm_prod-before-clear.dump`). **Demo data is now seeded only into the Test database** when a test DB is configured, so production is never refilled on startup.

## D-27 Roles: Administrator and User (PM request, 2026-09-28; replaces the "no roles in MVP" part of D-24)
- **Two roles.** *Administrator* = member of the Identity role `Admin`. *User* = any active user without it. No read-only role yet.
- **Administrators only:**
  - Settings → Users: list, create, edit (including the role), deactivate/reactivate, reset passwords. This also covers `GET /api/users`.
  - Settings → Database (the switch, D-26).
  - Changing project statuses: create, edit, delete, reorder, set default, via both the UI and `/api/project-statuses`.
- **Everyone:** customers, contacts, projects, tasks, activities, the dashboard, reading project statuses, Settings → General, and changing their own password.
- **Enforcement is server-side:**
  - The `Admin` policy checks the user's active flag and role in the database on every call, not the cookie claims, so granting or revoking the role applies immediately without re-login.
  - `UserAdminService`, `AdminProjectStatusService` (a wrapper around the status service) and the database switch all check it, and the API returns 403 ProblemDetails.
  - The UI additionally hides what a User can't use: the Database and Users tabs, and the status edit buttons ("Only administrators can change them").
- **Safeguards:**
  - You can't remove your own Admin role (409; the checkbox is disabled for yourself), and the check runs before any other edit is saved.
  - The last active administrator can't be demoted or deactivated.
  - On startup, if a database has no active administrator, the seeded admin (`Seed:AdminEmail`) gets the role back.
- **Seeding:** `admin@offload.local` is an Administrator. The demo users (Jana, Petr) are now ordinary Users; their Admin role, left over from the MVP seeder, was removed from the test DB.

## D-28 Slovak regional settings and euro (PM request, 2026-09-28; replaces the culture and currency parts of D-10/D-11)
- **Culture:** `sk-SK` for all number and date formatting (`AppCulture` in `Offload.Crm.Web/Ui` is the single source; `Program.cs` sets it as the thread default).
  - Dates stay `dd.MM.yyyy` and date-times `dd.MM.yyyy HH:mm`, as used in Slovakia. Date pickers use the same explicit format. Calendar month and day names follow sk-SK.
  - Decimal comma, and a non-breaking space between thousands (`1 234,50`).
- **Currency:** **EUR**, app-wide. Amounts display as `1 250 000 €`, or `1 250,50 €` when there are cents, with a non-breaking space before `€` so the symbol never wraps onto its own line. Value fields are labelled "Estimated value (€)" and "Actual value (€)". The `App:Currency` setting was removed: the currency is now a code constant, not configuration.
- **Time zone:** `Europe/Bratislava`. It has the same rules as Prague, so no stored or displayed time changes.
- **UI language:** unchanged, English (D-11).
- **Existing amounts are not converted.** Values are stored as plain numbers. The test DB's demo projects were entered as CZK amounts and now display with €. Production is empty, so it's unaffected.
