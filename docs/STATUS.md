# Offload CRM — Status

**Current phase:** Phase 2: QA testing (T-22). Phase 3 is specified and ready to start after QA sign-off.
**Last updated:** 2026-09-28 (PM: Phase 2 review. D-21 accepted with amendments D-22; Phase 3 specified in D-23 (Dashboard), D-24 (Users), D-25 (API); T-39 added; BUG-009 fixed, pending QA verification)

Legend: `Todo` · `In progress` · `Ready for QA` · `Done` (QA verified) · `Blocked`

## Phase overview

| Phase | Scope | Status |
|---|---|---|
| 1 | Scaffold, domain, DB/migrations, seed, auth, shell, Customers + Contacts | Done (conditional: 13 Phase 1 bugs fixed in T-38, verified in T-22) |
| 2 | T-38 bug fixes, Project statuses, Projects, Tasks (+Kanban), Activities | Dev done, QA testing (T-22) |
| 3 | D-22 amendments, Dashboard, Users/Settings, polish, API completeness | Ready to start after T-22 sign-off |
| 4 | Hardening, README, regression, release | Todo |

## Tasks

| ID | Task | Owner | Status | Notes |
|---|---|---|---|---|
| T-01 | Solution scaffold | dev | Done | sln, 4 src + tests projects, global.json (8.0.x), Directory.Build.props (nullable, TreatWarningsAsErrors in src), .editorconfig, .gitignore, .gitattributes, local dotnet-ef 8.0.31, git init (nothing committed yet) |
| T-02 | Domain model | dev | Done | All entities incl. Phase 2 ones (Project, ProjectStatus, TaskItem, Activity); see D-18 |
| T-03 | DbContext + initial migration | dev | Done | Migration `InitialCreate`; enums as strings; D-07 FK behaviors; filtered unique IČO index; audit interceptor; auto-migrate in Development |
| T-04 | Identity + login | dev | Done | Static SSR login/logout, lockout 5/5 min, inactive users blocked, API 401, pages redirect with ReturnUrl |
| T-05 | Seed data | dev | Done | Statuses always (when empty), admin always, demo data in Development: 3 users, 8 customers, 13 contacts, 12 projects, 40 tasks, 30 activities |
| T-06 | App shell/theme/nav | dev | Done | MudBlazor 8.15 themed; responsive drawer (< 960px collapses); placeholders; 404 page; error boundary |
| T-07 | Customers list + API | dev | Done | Server paging/sort (all columns)/search/status filter; search+status in URL |
| T-08 | Customer create/edit/delete | dev | Done | Dialog form, inline + server field errors, delete confirm, 409 when projects exist |
| T-09 | Customer detail + Contacts | dev | Done | Tabs Overview/Contacts/Projects (empty state)/Activities (placeholder); contacts CRUD dialogs + API |
| T-10 | Test infrastructure | dev | Done | 84 tests (unit, service on Postgres `offload_crm_test` + Respawn, WebApplicationFactory API/auth). `dotnet test` green |
| T-11 | UI test project + smoke | qa | Done | `tests/Offload.Crm.UiTests` (Playwright msedge, 66 tests; skip when the app is down), README "UI / end-to-end tests" |
| T-12 | Phase 1 QA pass | qa | Done | 15 bugs logged (0 C, 0 H, 7 M, 8 L): see QA_REPORT. PM triaged: 13 fix now (T-38), 2 deferred to Phase 4 (BUG-009, BUG-013) |
| T-38 | Phase 1 bug fixes + D-20 conventions (do first) | dev | Ready for QA | 13 bugs Fixed (see QA_REPORT fix notes); shared list/paging/sort/URL-state helpers (D-21); 7 KnownBugTests un-skipped and passing; BUG-009 resolved (405): Fixed, pending QA verification (D-22.7) |
| T-13 | Project statuses (Settings) | dev | Ready for QA | Settings → Project statuses: create/edit/reorder/default/delete (blocked in use or default), API `/api/project-statuses` |
| T-14 | Projects list + API | dev | Ready for QA | D-20 list; filters status (multi), customer, priority, responsible, active only (default on); `/api/projects` |
| T-15 | Project create/edit/delete | dev | Ready for QA | Dialog with customer autocomplete, active-user picker, date/value rules; status change logs Note activity; delete confirm mentions tasks deleted / activities kept |
| T-16 | Project detail | dev | Ready for QA | Inline status, tabs Overview / Tasks (quick add, inline status, edit, delete) / Activities / Timeline |
| T-17 | Tasks list + API | dev | Ready for QA | Default open tasks; filters incl. Me/Unassigned, overdue; inline status; `PATCH /api/tasks/{id}/status` |
| T-18 | Tasks Kanban | dev | Ready for QA | `?view=kanban`; drag & drop (optimistic, reverts on error) + card menu; columns ≥ 260 px, board-only horizontal scroll |
| T-19 | Activities | dev | Ready for QA | List (newest first) with type/customer/project/author/date-range filters; dialog with D-12 rules; author read-only; `/api/activities` |
| T-20 | Customer detail tabs wiring | dev | Ready for QA | Projects tab (+ New project pre-filled), Activities feed (own + projects), last activity refreshes |
| T-21 | Phase 2 tests | dev | Ready for QA | Offload.Crm.Tests 188 green (service + API + session tests) |
| T-22 | Phase 2 QA pass | qa | Done | T-38 fixes verified (14). New: BUG-016 High (Projects status filter loop), BUG-017..020 Medium, BUG-021..027 Low. Phase 2 E2E tests added |
| T-39 | D-22 amendments (Kanban Done 30-day window, customer-change warning + Note) | dev | Todo | Phase 3, do first |
| T-23 | Dashboard | dev | Todo | Spec D-23 |
| T-24 | Users management + change own password | dev | Todo | Spec D-24; no roles in MVP (policy `ManageUsers`); `LastSignInAt` migration |
| T-25 | Settings general | dev | Todo | |
| T-26 | Detail pages polish | dev | Todo | |
| T-27 | API completeness + Swagger + /health | dev | Todo | Spec D-25 |
| T-28 | Phase 3 tests | dev | Todo | |
| T-29 | Phase 3 QA pass | qa | Todo | |
| T-30 | Validation/error audit | dev | Todo | |
| T-31 | Empty/loading/error states audit | dev | Todo | |
| T-32 | Responsive + consistency pass | dev | Todo | |
| T-33 | Logging + global exception handling | dev | Todo | |
| T-34 | Performance sanity | dev | Todo | |
| T-35 | README | dev | Todo | |
| T-36 | Bug fixing | dev | Todo | Includes deferred BUG-013 |
| T-37 | Full regression + sign-off | qa | Todo | |

## Quality gate (release)

- [ ] `dotnet build` succeeds
- [ ] `dotnet test` green
- [ ] `dotnet format --verify-no-changes` clean
- [ ] Migrations apply to a fresh DB
- [ ] App starts; login works
- [ ] CRUD works for all entities
- [ ] Navigation works
- [ ] Validation works (UI + API)
- [ ] Error handling works
- [ ] No browser console errors
- [ ] No broken UI states (empty/loading/error)
- [ ] QA tested major workflows + edge cases
- [ ] 0 open Critical/High bugs
- [ ] Regression run completed

## Risks / open questions

- Playwright with `Channel="msedge"` depends on the installed Edge version. If it fails, QA falls back to `playwright.ps1 install` (this needs network access).
- MudBlazor drag & drop on the Kanban must also be testable via the card status menu (D-15).
- (Phase 1 review) There are no git commits yet. Dev should commit the Phase 1 baseline before starting T-38, and after that make one commit per task ID.
- (Dev, T-38/Phase 2) Per the user's instruction, no git commits were made; the working tree holds Phase 1 + T-38 + Phase 2.
- (Phase 2 review, 2026-09-28) The product owner reported a glitch in the Projects list filtering, and QA is investigating it as top priority in T-22. It **blocks Phase 3 sign-off**, because the dashboard click-throughs (D-23) depend on the Projects filters. The fix must come with a regression test.
- (Phase 2 review) The working tree is still uncommitted (user instruction). The risk of losing work grows with each phase, so the PM recommends that the user allow a baseline commit.
