# Offload CRM — Roadmap (Developer work order)

Read `docs/DECISIONS.md` first; it's binding. Task IDs are stable, so reference them in commits (`T-07: ...`).
Priority: **P0** = must have for the phase gate, **P1** = should have, **P2** = nice to have.
**Phase workflow:** Dev completes the phase tasks, then runs `dotnet build`, `dotnet test`, and `dotnet format --verify-no-changes`, and hands the phase over to QA. QA tests it and logs bugs in `docs/QA_REPORT.md`. Dev fixes Critical/High bugs, QA verifies the fixes, and then PM closes the phase in `docs/STATUS.md`.

**Definition of Done (every dev task):** builds with 0 warnings-as-errors introduced, has service-level tests for new business rules, uses migrations (no manual DB edits), shows no browser console errors on the touched pages, and has validation + error + empty + loading states for every new screen.

---

## Phase 1: Foundation + Customers & Contacts

| ID | Task | Owner | Pri |
|---|---|---|---|
| T-01 | Solution scaffold | dev | P0 |
| T-02 | Domain model (all entities) | dev | P0 |
| T-03 | DbContext, EF configurations, initial migration | dev | P0 |
| T-04 | Identity + login/logout + seeded admin | dev | P0 |
| T-05 | Seed data (statuses + demo data) | dev | P0 |
| T-06 | App shell: layout, theme, navigation | dev | P0 |
| T-07 | Customers: service + API + list page | dev | P0 |
| T-08 | Customers: create/edit/delete | dev | P0 |
| T-09 | Customer detail + Contacts CRUD | dev | P0 |
| T-10 | Test infrastructure (unit + integration) | dev | P0 |
| T-11 | UI test project + Phase 1 smoke tests | qa | P1 |
| T-12 | Phase 1 QA pass | qa | P0 |

**T-01 Solution scaffold.** AC: `Offload.Crm.sln` with the projects in D-01. Nullable is enabled, `TreatWarningsAsErrors` is on for src projects, and `.editorconfig` is present. `.config/dotnet-tools.json` has `dotnet-ef` 8.x. `.gitignore` is present and `git init` has been run. `dotnet build` succeeds.

**T-02 Domain model.** AC: entities Customer, Contact, Project, ProjectStatus, TaskItem (avoid clashing with `System.Threading.Tasks.Task`), Activity, and ApplicationUser. Enums: CustomerStatus, Priority, TaskItemStatus, ActivityType. Fields follow the requirements and D-06/D-10/D-11/D-12. All entities have `Id` (int), `CreatedAt`, and `UpdatedAt`. Customer has address fields (Street, City, PostalCode, Country). Contact has FirstName/LastName.

**T-03 DbContext & migration.** AC: `AppDbContext : IdentityDbContext<ApplicationUser>`. Max lengths and required fields are configured, with enums stored as strings. There's a unique index on normalized `RegistrationNo` (filtered, only when not null), plus indexes on the FKs and on `Customer.CompanyName` and `Project.Name`. FK delete behaviors follow D-07 (Customer→Project Restrict, Project→Task Cascade, Project→Activity SetNull, Customer→Contact Cascade, Status→Project Restrict, User→* Restrict). A timestamp interceptor sets CreatedAt/UpdatedAt. `dotnet ef database update` works on an empty DB, and the app auto-migrates in Development.

**T-04 Auth.** AC: a login page (email, password, "remember me"), with a friendly error on bad credentials and a lockout message. Logout is in the app bar. All pages and `/api/*` require auth; unauthenticated pages redirect to login and the API returns 401. The admin from D-09 is seeded, and an inactive user can't log in. There's no public registration.

**T-05 Seed.** AC: the project statuses from D-02 are always seeded (idempotently). In Development only, the demo data from D-09 is seeded, covering all statuses and priorities, some overdue tasks, and some customers without projects. Re-running the app doesn't duplicate anything.

**T-06 App shell.** AC: MudBlazor is installed and the theme uses the brand colors (D-15). There's a left drawer nav with icons for Dashboard, Customers, Projects, Tasks, Activities, and Settings, with the active item highlighted. The app bar has the "Offload CRM" brand, the user name, and logout. Pages that aren't built yet show a placeholder. There's a 404 page and an error boundary with a friendly message. The drawer is responsive at 1024px and 768px. Page titles (`<PageTitle>`) are set.

**T-07 Customers list.** AC: `ICustomerService` has List (search, status filter, sort, and paging), Get, Create, Update, and Delete. The API offers `GET/POST /api/customers`, `GET/PUT/DELETE /api/customers/{id}`, with ProblemDetails for errors. The list page shows Company, RegNo, City, Status chip, Email, Phone, Projects count, Last activity (D-06), and Created. Search follows D-16. There's a status filter, and all shown columns can be sorted, with server-side paging. There's an empty state and a "no results" state.

**T-08 Customer create/edit/delete.** AC: the form has all fields and the D-12 validation (inline messages). A duplicate RegNo shows a clear field error. Delete asks for confirmation. Deleting a customer with projects is blocked (D-07) and the reason is shown in a snackbar. Success snackbars are shown. Pressing Cancel discards changes.

**T-09 Customer detail + contacts.** AC: `/customers/{id}` has a header with the key info, status, and actions (Edit, Delete). Tabs: Overview (all fields, with clickable email/phone/website), Contacts (table + add/edit/delete dialogs with validation), Projects (the project list, filled in once Phase 2 is done; show an empty state until then), and Activities (placeholder until Phase 2). An invalid ID shows a "not found" state. There's a contacts API at `/api/customers/{id}/contacts` plus `PUT/DELETE /api/contacts/{id}`.

**T-10 Test infrastructure.** AC: an xUnit project with a fixture that creates/migrates `offload_crm_test` and resets data between tests (Respawn or truncate). It has tests for the customer validation rules, RegNo uniqueness, the delete-blocked rule, and last-activity computation. There's at least one API integration test via `WebApplicationFactory` (401 without auth, 200 with auth). `dotnet test` is green.

**T-11 UI tests (QA).** AC: the `tests/Offload.Crm.UiTests` project with Playwright using msedge, a documented way to start the app for tests, and smoke tests for login, navigation to every menu item, and the customer CRUD happy path.

**T-12 QA pass.** AC: the Phase 1 QA focus has been executed, bugs logged, and the test run recorded in QA_REPORT.

**QA focus, Phase 1:**
- Log in with a wrong password, lockout after 5 attempts, deep links while logged out, `/api/customers` without a cookie, and logout followed by the back button.
- Customer form: empty/whitespace-only name, a 201-char name, invalid email/URL, a website without `https://`, a duplicate RegNo (including different case/spaces), and Czech diacritics (ř, ž, ů) in names and in search.
- Delete a customer that has projects (use seeded data) and one without. Cancel in the confirm dialog.
- Contacts: add/edit/delete, validation, and contacts appearing in customer search.
- List: sort by every column, paging boundaries, a search with no results, and the filter + search combination.
- Detail page for a non-existent ID, `/customers/abc`, and an unknown route.
- Layout at 1920, 1280, 1024 (tablet), and 768px. Check the console for errors, and check double-click on Save (duplicate creates?).
- Restart the app: no duplicate seeds, and migrations run cleanly on a fresh DB.

---

## Phase 2: Projects, Tasks, Activities

| ID | Task | Owner | Pri |
|---|---|---|---|
| T-38 | Phase 1 bug fixes + shared list/API conventions (D-20) — do FIRST | dev | P0 |
| T-13 | Project statuses management (Settings) | dev | P0 |
| T-14 | Projects: service + API + list | dev | P0 |
| T-15 | Projects: create/edit/delete + status change | dev | P0 |
| T-16 | Project detail page (overview, tasks, activities) | dev | P0 |
| T-17 | Tasks: service + API + list view | dev | P0 |
| T-18 | Tasks: Kanban view | dev | P1 |
| T-19 | Activities: service + API + list + create/edit | dev | P0 |
| T-20 | Wire customer detail Projects/Activities tabs | dev | P0 |
| T-21 | Tests for Phase 2 rules | dev | P0 |
| T-22 | Phase 2 QA pass + UI tests | qa | P0 |

**Phase 2 clarifications (PM, Phase 1 review):**
- Build the Projects, Tasks and Activities lists on the **D-20 conventions** from the start: URL state including sort/page/size, NULLS LAST, clamped paging, the empty / no-results / out-of-range states, responsive columns with actions always visible, and long-text wrapping. QA will re-run the BUG-001/002/005/006/007/014/015 scenarios against every new list and endpoint.
- **Active project** = the status has `IsActive` (D-03 amended, D-19). There's no archive flag. The "Active only" filter is on by default.
- Validate every Phase 2 enum (Priority, TaskItemStatus, ActivityType) with `Enum.IsDefined` in the body and the query (D-20.4).
- ActivityType `Call` displays as "Phone call". The auto status-change activity follows D-19.
- Assignee/responsible pickers list active users only. Existing records with an inactive user show the name plus "(inactive)".
- On tablet (<= 1024 px), the Kanban may scroll horizontally **inside the board only**. Each column needs a minimum width of about 260 px, and the page itself must not scroll sideways.
- The Phase 2 API endpoints follow D-20.7 (ProblemDetails for all errors). 405 handling (BUG-009) isn't required yet.

**T-38 Phase 1 bug fixes + shared conventions.** AC: fix BUG-001, 002, 003, 004, 005, 006, 007, 008, 010, 011, 012, 014, and 015 as described in QA_REPORT / D-19 / D-20. Put the paging/sorting/URL-state logic in reusable helpers (a service-side paging/sort helper plus a list-state component or helper in the UI) so that Phase 2 lists reuse it. Un-skip the matching `KnownBugTests`, which must now pass, and add service/API tests for the enum, paging and sort-nulls-last rules. Do it before T-14.

**T-13 Statuses.** AC: Settings → Project statuses: a list showing color swatch, name, order, and flags. You can create, edit, reorder, and set the default. Delete is blocked when a status is in use or is the default (D-02), with a clear message. Validation: unique name, hex color, and not both Active and Closed. There's an API at `/api/project-statuses`. The status dropdowns elsewhere show statuses in `SortOrder`.

**T-14 Projects list.** AC: `/projects` shows Name, Customer, Status chip (colored), Priority chip, Responsible, Start, Target (highlighted when overdue per D-03), Est. value (CZK), and Open tasks count. Search follows D-16. Filters: status (multi-select), customer, priority, responsible, and "active only" (default on). All columns can be sorted, with paging. There's a full REST API.

**T-15 Project create/edit/delete.** AC: the form has all fields and the D-12 validation (target >= start, values >= 0). Customer is picked with an autocomplete. Responsible is picked from active users. The status defaults to the default status. A status change auto-creates a Note activity (D-14). Delete asks for confirmation that mentions the tasks will be deleted and the activities kept (D-07). Creating a project from the customer detail page pre-fills the customer.

**T-16 Project detail.** AC: `/projects/{id}` has a header with status (changeable inline), priority, customer link, dates, values, and responsible. Tabs: Overview, Tasks (a list of the project's tasks with quick add, status change, and edit), Activities (list + add), and Timeline (D-14). Not-found state.

**T-17 Tasks list.** AC: `/tasks` shows Title, Project (link), Customer, Status, Priority, Assignee, Due (overdue highlighted), and Created. Filters: status, priority, assignee (including "Me" and "Unassigned"), project, and overdue. Default filter: open tasks. The status changes inline from the list. Create/edit uses a dialog, and CompletedAt follows D-13. Delete asks for confirmation. There's an API at `/api/tasks`, including `PATCH /api/tasks/{id}/status`.

**T-18 Kanban.** AC: a List/Kanban toggle on `/tasks` (remembered in the URL). There are 4 columns with counts. Dragging a card changes its status (it's persisted, and on failure it reverts with an error snackbar). The card menu also changes status. The Kanban respects the same filters. Cards show the title, project, assignee, priority, and due date.

**T-19 Activities.** AC: `/activities` is a list, newest first. Filters: type, customer, project, author, and date range. Create/edit uses a dialog with the D-12 rules (at least one of customer/project; the project implies its customer). Author is the current user and read-only. Delete asks for confirmation. There's an API at `/api/activities`. Activities can be added from the customer and project detail pages, with the context pre-filled.

**T-20 Customer detail tabs.** AC: the Projects tab shows the customer's projects (with status and a link) and a "New project" action. The Activities tab shows the feed of the customer's own and its projects' activities (D-06/D-14), with an "Add activity" action. Last activity updates after an activity is added.

**T-21 Tests.** AC: tests for status delete-block, status flags validation, the project date/value rules, CompletedAt set/clear, activity customer/project consistency, project delete cascade/SetNull, the auto status-change activity, and the API 400/404/409 shapes.

**QA focus, Phase 2:**
- Statuses: delete one in use, delete the default, rename one in use (the change shows everywhere), reorder, both flags on, duplicate name, and a new status appearing in the filters and Kanban.
- Projects: target < start, negative/huge/decimal values, CZK formatting, no customer, a deactivated responsible user, a status change creating a timeline entry, and deletion with tasks + activities (verify the result in the DB/UI).
- Tasks: Done → CompletedAt set, back to In Progress → cleared. Kanban drag between all columns, a drag while the server errors (stop the DB?), filters + Kanban, the overdue highlight, "Me" filter, and a long title/description wrapping.
- Activities: no customer and no project, a project belonging to a different customer (via the API), a future date > 1 year, and the author not being editable.
- Cross-page consistency: a change on the detail page is reflected in the lists, and last activity updates.
- Regression: the full Phase 1 focus again.

---

## Phase 3: Dashboard, Settings, API completeness, polish

**Start condition:** QA has signed off T-22 (Phase 2), including the product owner's reported **Projects list filtering glitch**. The dashboard's status click-throughs depend on the Projects filters working. Binding specs: D-22 (Kanban and customer-change amendments), D-23 (Dashboard), D-24 (Users), D-25 (API).

| ID | Task | Owner | Pri |
|---|---|---|---|
| T-39 | D-22 amendments: Kanban Done 30-day window, customer-change warning + Note activity | dev | P1 |
| T-23 | Dashboard | dev | P0 |
| T-24 | Settings → Users + change own password | dev | P0 |
| T-25 | Settings → General (read-only app info) | dev | P2 |
| T-26 | Detail pages polish | dev | P1 |
| T-27 | REST API completeness + OpenAPI + health | dev | P1 |
| T-28 | Tests for Phase 3 rules | dev | P0 |
| T-29 | Phase 3 QA pass + UI tests | qa | P0 |

Suggested order: T-39 (small), T-24 (the session rules are still fresh), T-23, T-26, T-27, T-25, with T-28 written alongside each task.

**T-39 D-22 amendments.** AC:
- The Kanban's Done column shows only tasks completed in the last 30 days when no status filter is set, and its footer links to `/tasks?status=Done`.
- Changing a project's customer shows a warning with the number of activities that will move, and logs the "Customer changed" Note activity.
- Both behaviours have tests.

**T-23 Dashboard.** AC:
- Exactly the widgets, rules, click-throughs, layout and states in D-23.
- Each KPI equals the `totalCount` of its click-through list, which uses the shared predicate. A test proves this for every KPI.
- The status bars link with "active only" off.
- Deadline boundaries: yesterday is overdue, today shows "Today", today+14 is included and today+15 is excluded.
- A future-dated activity doesn't appear in Recent activities.
- `GET /api/dashboard` returns the same numbers.
- On an empty DB the page shows zeros and the welcome panel, with no errors.
- One widget throwing doesn't break the page.
- Responsive at 1280/1024/768 with no horizontal page scroll. No console errors.
- Loads in < 1 s with the demo data (measured with EF logging, at most about 8 queries).

**T-24 Users.** AC:
- Everything in D-24: list with search, status filter and sort; the create, edit, deactivate/reactivate, reset password and change own password dialogs; the self-deactivate guard; and the `LastSignInAt` migration.
- The `ManageUsers` policy is applied to the Users tab and to its service methods.
- Deactivating a user or resetting their password ends that user's sessions within ≤ 1 min, in both the UI and the API.
- After change own password, the current session stays signed in and the user's other sessions end.
- Identity errors show under the matching field.
- The deactivate confirmation shows the correct open-task and active-project counts.
- `GET /api/users` and `GET /api/users/{id}` never expose hashes or stamps.

**T-25 Settings → General.** AC:
- A read-only tab showing the app version (assembly informational version), environment, currency (`App:Currency`), culture/time zone, and DB server version.
- No secrets shown. In particular, no connection string.

**T-26 Detail pages polish.** AC:
- **Customer detail:** a summary card showing projects (total and active), open tasks across its projects, pipeline value (active projects' estimated value), and last activity. Header quick actions: New project, Log activity, Add contact.
- **Project detail:** the header shows task progress (done/total, with a progress bar), the days until the target date or "N days overdue" (red), and quick actions Add task, Log activity and Change status. The Timeline is complete per D-14: project created, status and customer changes (Note activities), other activities, and task completions. It's newest first, and each entry has an icon, date/time and author.
- **All detail pages:** breadcrumbs (Section › Item) that restore the list state the user came from (D-20.1). Long names wrap (D-20.6).
- Every count on these cards uses the same predicates as D-23.

**T-27 API completeness.** AC:
- Per D-25, every entity (customers, contacts, projects, project statuses, tasks, activities) has list/get/create/update/delete, plus the existing special endpoints (task/project status PATCH, status move/default, project timeline).
- `/api/dashboard`, `/api/users` (read-only) and `/health` exist.
- The list envelope and paging parameters are consistent everywhere.
- Swagger is at `/swagger` in Development only (404 in Production), with success types and problem codes documented for every endpoint.
- An endpoint inventory table is added to the README (T-35 reuses it).

**T-28 Tests.** AC, covering:
- Dashboard KPI and list parity for all 5 KPIs, the deadline boundaries, the future activity exclusion, and the empty DB.
- User rules: duplicate e-mail, weak password, the self-deactivate guard, and that a deactivated user can't sign in and their session ends.
- Reset password and change own password, including their effect on sessions.
- The `ManageUsers` policy is applied.
- The D-22 amendments.
- API contract tests: the list envelope on every list endpoint, 401 anonymous, 405 on a wrong verb, and Swagger returning 404 in Production.
- `dotnet test` green.

**T-29 QA pass.** AC: the Phase 3 QA focus has been executed, the bugs are logged, T-22 regressions have been re-run, and the UI suite has been extended with the dashboard and users flows.

**QA focus, Phase 3:**
- **Dashboard:**
  - Compare every KPI with its click-through list, then change data (close a project, complete a task, deactivate a customer) and re-check.
  - Deadline boundaries: yesterday, today, +14, +15.
  - A future-dated activity.
  - Every status bar click-through, including closed statuses.
  - An empty DB (`Seed:DemoData=false` on a fresh DB).
  - Tablet widths.
- **Users:**
  - Create with a weak password, a duplicate e-mail (including different case), or an invalid e-mail.
  - Edit an e-mail to one that already exists.
  - Deactivate a user who has tasks and projects, and check the counts in the dialog.
  - Deactivate while that user is signed in in another browser.
  - Try to deactivate yourself.
  - Reactivate a locked-out user.
  - Reset a password and sign in with it.
  - Change your own password with a wrong current password, and check that your other session ends.
  - Check the inactive markers in pickers and on records.
- **Kanban:** Done shows only the last 30 days, and the footer link works.
- **Customer change on a project:** the warning count, the activities moving, and the timeline entry.
- **API:** every endpoint with an invalid body, unknown IDs, no auth and a wrong verb. Confirm Swagger is absent in Production and `/health` works when the DB is down.
- **Regression:** Phases 1–2 (the automated UI suite plus manual spot checks of the list URL state).

---

## Phase 4: Hardening & release

| ID | Task | Owner | Pri |
|---|---|---|---|
| T-30 | Validation/error handling audit (all forms + API) | dev | P0 |
| T-31 | Empty/loading/error states audit on every page | dev | P0 |
| T-32 | Responsive pass (1280/1024/768) + UI consistency (spacing, typography) | dev | P1 |
| T-33 | Logging (Serilog or built-in to console + file), global exception handler, no stack traces shown to users | dev | P0 |
| T-34 | Performance sanity: no N+1 on lists (verified with EF logging), indexes, AsNoTracking for reads | dev | P1 |
| T-35 | README: prerequisites, setup, config/env vars, migrations, run, test, default login, project structure | dev | P0 |
| T-36 | Fix all Open Critical/High bugs; triage Medium/Low; deferred from Phase 1: BUG-013 (generic login messages) | dev | P0 |
| T-37 | Full regression + release sign-off report | qa | P0 |

AC for T-30..T-35 are the task titles, verified by QA. **Release gate:** fresh clone → README steps → the app runs. `dotnet build` and `dotnet test` are green, and `dotnet format --verify-no-changes` is clean. 0 Open Critical/High bugs, and all quality-gate items from the brief are checked in STATUS.md.

**QA focus, Phase 4:**
- Fresh-DB install following only the README.
- Kill the DB mid-session: is the error friendly, and does the app recover once the DB is back?
- Long text everywhere, special characters, HTML/script injection in text fields (it must render as text), and SQL-like input in search.
- Two browser tabs editing the same record (last write wins, no crash).
- Browser console clean on every page. Tablet width on every page.
- Full regression of all previous QA focus lists, plus rerun of all automated tests.
