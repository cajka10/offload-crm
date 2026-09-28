# Offload CRM — QA Report

Owner: QA. Dev updates **Status** to `Fixed` (with a commit/task ref). QA changes it to `Verified`, or reopens it as `Open`.
Severity: **Critical** = data loss, crash, security, or a blocked core workflow · **High** = major feature broken, no workaround · **Medium** = feature partly broken, or has a workaround · **Low** = cosmetic or minor UX.
Status: `Open` · `Fixed` · `Verified` · `Won't fix` (needs PM approval + reason).

## Summary

| Phase | Open C/H | Open M/L | Fixed (awaiting verify) | Verified |
|---|---|---|---|---|
| 1 | 0 | 0 / 1 (BUG-013 deferred) | 0 | 14 |
| 2 | 1 (BUG-016, High) | 4 / 7 | 0 | 0 |
| 3 | 0 | 0 | 0 | 0 |
| 4 | 0 | 0 | 0 | 0 |

## PM triage (Phase 1 review, 2026-09-27)

Phase 1 gate: **passed with conditions.** There are no Critical/High bugs. The Medium bugs are fixed in T-38 at the start of Phase 2, and QA verifies them in the Phase 2 pass (T-22).

| ID | Decision | Rationale |
|---|---|---|
| BUG-001 | Fix now (T-38) | The same flaw would repeat for every Phase 2 enum. Fix it once with the D-20.4 rule. |
| BUG-002 | Fix now (T-38) | A trivial 500. The paging helper will be reused by all lists (D-20.2). |
| BUG-003 | Fix now (T-38) | A security issue on shared machines. Update the stamp on logout and revalidate every 1 min (D-19). |
| BUG-004 | Fix now (T-38) | Uses the same mechanism as BUG-003 (an `IsActive` check in revalidation). It must be in place before T-24 ships deactivation. |
| BUG-005 | Fix now (T-38) | A misleading "No customers yet" state, and the list pattern is being copied into 3 new lists (D-20.2). |
| BUG-006 | Fix now (T-38) | Tablet is a stated target, and the actions must be reachable. Set the pattern before Projects/Tasks lists (D-20.5). |
| BUG-007 | Fix now (T-38) | One record can break the whole list. A global CSS fix is cheap (D-20.6). |
| BUG-008 | Fix now (T-38) | D-01 promises ProblemDetails, and Phase 2 adds many endpoints. Fix it centrally once (D-20.7). |
| BUG-009 | ~~Defer to Phase 4~~ Fixed as a side effect of T-38 (D-22.7) | The routing change returns 405 + Allow. QA verifies it in T-22. |
| BUG-010 | Fix now (T-38) | UI and API disagree, and Phase 2 adds more forms. Normalise before validating and show the max length (D-19). |
| BUG-011 | Fix now (T-38) | Contradicts D-18's claim. The fix (a confirm page, no auto-submit) is small (D-19). |
| BUG-012 | Fix now (T-38) | A one-line regex fix with a real phishing vector (D-20.8). |
| BUG-013 | Defer to Phase 4 (T-36) | Internal app with a small user base. Generic messages plus counting failures for inactive users go in the hardening phase. |
| BUG-014 | Fix now (T-38) | "Most recent first" is currently wrong. The shared sort helper covers it (D-20.3). |
| BUG-015 | Fix now (T-38) | **Decision: persist sort/page/page size in the URL** for every list (D-20.1). It's cheap when done in the shared helper, and it's standard SaaS behaviour. |

## Issues

| ID | Severity | Phase | Area | Title | Status |
|---|---|---|---|---|---|
| BUG-001 | Medium | 1 | API / Customers | API accepts undefined numeric `status` values (e.g. `99`) and stores them | Verified (T-22) |
| BUG-002 | Medium | 1 | API / Customers | Large `page` number causes HTTP 500 (`OFFSET must not be negative`, int overflow) | Verified (T-22) |
| BUG-003 | Medium | 1 | Auth | Logout doesn't end the interactive session in other open tabs; they can still read and write | Verified (T-22) |
| BUG-004 | Medium | 1 | Auth | A deactivated user's existing session (cookie + circuit) keeps working | Verified (T-22) |
| BUG-005 | Medium | 1 | Customers list | Deleting the only row on the last page shows "No customers yet." and pager "26-25 of 25" | Verified (T-22) |
| BUG-006 | Medium | 1 | Customers list / responsive | At 1280/1024/768 px the Created column and the Edit/Delete actions are off-screen (horizontal scroll inside the table) | Verified (T-22) |
| BUG-007 | Medium | 1 | Layout | A long unbroken value (e.g. 200-char company name) breaks the list columns and makes the detail page scroll ~1000–1800 px sideways | Verified (T-22) |
| BUG-008 | Low | 1 | API | Malformed JSON / wrong types / missing body / unhandled errors don't return usable ProblemDetails (empty 400, or an HTML page in Production) | Verified (T-22) |
| BUG-009 | Low | 1 | API | Unsupported HTTP method on an existing route returns 404 instead of 405 | Verified (T-22) |
| BUG-010 | Low | 1 | Customer form | The UI checks max length before trimming, so it rejects values the API accepts | Verified (T-22) |
| BUG-011 | Low | 1 | Auth / security | Logout CSRF: any foreign link to `/account/logout` signs the user out | Verified (T-22) |
| BUG-012 | Low | 1 | Validation / security | E-mail validation accepts mailto header injection (`x@y.cz?cc=attacker@evil.com`) | Verified (T-22) |
| BUG-013 | Low | 1 | Auth / security | Login responses reveal account state (user enumeration via lockout; password oracle for deactivated accounts without lockout) | Open |
| BUG-014 | Low | 1 | Customers list | Descending sort puts empty values first (e.g. "Last activity" ↓ starts with customers with no activity) | Verified (T-22) |
| BUG-015 | Low | 1 | Customers list / UX | Sort, page and page size are lost when returning from a customer detail page | Verified (T-22) |
| BUG-016 | **High** | 2 | Projects list / filters | Picking a status in the Status filter starts an endless navigation loop: the list never filters and the page freezes | Open |
| BUG-017 | Medium | 2 | Projects list + API | "Active only" (default on) silently hides an explicitly selected closed status (Delivered/Cancelled): "No projects match" / `totalCount: 0` | Open |
| BUG-018 | Medium | 2 | Tasks / Kanban / responsive | The Kanban board is wider than the viewport at 1280/1024/768: the Done column, its count and the card menus are off-screen; long titles overflow cards | Open |
| BUG-019 | Medium | 2 | API / Tasks | `PATCH /api/tasks/{id}/status` without a `status` field (`{}` or a typo) returns 200 and resets the task to Todo (clears CompletedAt) | Open |
| BUG-020 | Medium | 2 | Settings / statuses | A closed, inactive status (e.g. Cancelled) can be made the default, so every new project starts Cancelled and is hidden by "Active only" | Open |
| BUG-021 | Low | 2 | Projects list / filters | A deep-link filter value that doesn't exist (`?customer=999999`, `?responsible=x`, `?status=999`) is applied but the control shows empty: "No projects match" with no visible filter | Open |
| BUG-022 | Low | 2 | Navigation | The detail-page breadcrumb ("Customers" / "Projects") links to the bare list and drops the filter/sort/page state (D-20.1) | Open |
| BUG-023 | Low | 2 | Projects list / errors | With the DB unavailable, `/projects` shows the global "Something went wrong" error boundary instead of the list's load-error state with Retry | Open |
| BUG-024 | Low | 2 | Tasks / Kanban | In Kanban view the Status filter still reads "Open tasks" while the Done column is shown; with an explicit status filter a moved card stays on the board | Open |
| BUG-025 | Low | 2 | API conventions | Projects/tasks query params differ between UI URL and API (`status` vs `statusId`, `customer` vs `customerId`, `size` vs `pageSize`, `desc` vs `descending`); unknown params are silently ignored; API tasks default to all statuses and `status=all` returns 400 | Open |
| BUG-026 | Low | 2 | Validation | Dates are not range-checked: project start `0001-01-01` / target `9999-12-31` and activity date `1900-01-01` are accepted; an activity `date` without an offset is taken as UTC | Open |
| BUG-027 | Low | 2 | Project timeline | A "Call" activity without subject shows as "Call" in the timeline (UI + `/timeline` API) instead of "Phone call" (D-19) | Open |

### Issue details

#### BUG-001: API accepts undefined numeric `status` values and stores them
- **Severity:** Medium
- **Phase / Area:** 1 / API, Customers
- **Environment:** API via curl / Playwright APIRequest; Development build 2026-09-27 (no git commits yet)
- **Steps to reproduce:**
  1. Sign in (cookie) and `POST /api/customers` with `{"companyName":"QA status99","status":99}`.
  2. Open the customer list/detail in the UI, and `GET /api/customers?status=99`.
- **Expected:** 400 ProblemDetails with an error for `status` (only `Lead|Active|Inactive`, or 0–2 if numbers are allowed). The `?status=99` filter gets 400, like `?status=bogus`.
- **Actual:** 201 Created. The DB stores `Status = '99'`. The list and detail show the status chip "99", and the edit dialog's Status select holds the value 99. `GET /api/customers?status=99` returns 200 with an empty list. `JsonStringEnumConverter` and query binding accept any integer; there's no `Enum.IsDefined` check (the same will apply to Priority/TaskItemStatus/ActivityType in Phase 2).
- **Evidence:** `KnownBugTests.Bug001_undefined_status_value_is_rejected` (skipped). The QA-created customer "QA status99" is in the dev DB.
- **Status:** Verified (T-22, 2026-09-28)
- **Fix notes (dev):** Enum inputs carry `[EnumDefined]` (body) and services call `Guard.EnumDefined` on query filters, so 400 has a field error for `status` (also Priority/TaskItemStatus/ActivityType). `?status=bogus` gives a 400 ProblemDetails (BUG-008). Tests: `SharedRulesTests.Undefined_enum_values_fail_validation`, `ListConventionTests.Undefined_status_*`, `Phase2ApiTests.Undefined_enum_numbers_are_400_with_field_error`, and the un-skipped `KnownBugTests.Bug001`.

#### BUG-002: Large `page` number causes HTTP 500
- **Severity:** Medium
- **Phase / Area:** 1 / API, Customers (service paging)
- **Steps to reproduce:** `GET /api/customers?page=100000000` (signed in).
- **Expected:** 200 with an empty `items` (or the page clamped), never a 500.
- **Actual:** 500. `(page - 1) * size` overflows `int`, and PostgreSQL throws `2201X: OFFSET must not be negative`. In Development the response contains the full stack trace. In Production it's the HTML "Something went wrong" page (see BUG-008). `page=2147483647` happens to wrap to a positive offset and returns 200.
- **Evidence:** `KnownBugTests.Bug002_huge_page_number_returns_empty_page_not_500`; app log `Npgsql.PostgresException (0x80004005): 2201X`.
- **Status:** Verified (T-22, 2026-09-28)
- **Fix notes (dev):** The shared `PagingExtensions.ToPagedResultAsync` computes the offset as a long. A page past the end returns an empty page without querying. Used by all lists. Tests: `ListConventionTests.Page_beyond_the_end_returns_an_empty_page`, `Phase2ApiTests.Huge_page_numbers_return_an_empty_page` (all 4 list APIs), and `KnownBugTests.Bug002`.

#### BUG-003: Logout doesn't end the interactive session in other open tabs
- **Severity:** Medium (security)
- **Phase / Area:** 1 / Auth
- **Steps to reproduce:**
  1. Sign in and open `/customers` in tab A.
  2. Open the app in tab B (same browser) and click **Logout**. Tab B shows the login page, and `/api/customers` now returns 401.
  3. Go back to tab A (no reload), click **New customer**, fill in a name, and click **Save**.
- **Expected:** Tab A is signed out too (or at least every server action fails and redirects to login).
- **Actual:** Tab A keeps working with full rights. The customer is created and the detail page opens. The Blazor circuit keeps its authentication state until the 30-min revalidation, which checks only the security stamp, and sign-out doesn't change it. On a shared computer, "Logout" leaves live sessions behind.
- **Suggestion:** Call `UserManager.UpdateSecurityStampAsync` on logout and/or shorten the revalidation interval, or broadcast logout to the circuits.
- **Evidence:** `KnownBugTests.Bug003_other_tab_cannot_save_after_logout` (skipped); customer "QA after logout …" was created in the dev DB.
- **Status:** Verified (T-22, 2026-09-28)
- **Fix notes (dev):** Logout calls `UpdateSecurityStampAsync`. Every service call from a circuit re-validates the session (stamp + active) through `GuardedDbContextFactory`/`CircuitSessionGuard`. On failure the tab is sent to `/account/login?expired=1` and the action fails. Cookie and circuit revalidation run every 1 minute. Tests: `SessionTests.Logout_in_one_browser_ends_the_other_sessions` and `KnownBugTests.Bug003`.

#### BUG-004: A deactivated user's existing session keeps working
- **Severity:** Medium (security; becomes visible with Users management, T-24)
- **Phase / Area:** 1 / Auth
- **Steps to reproduce:**
  1. Sign in as `jana.novakova@offload.local` (curl or browser).
  2. In the DB: `update "AspNetUsers" set "IsActive"=false where "Email"='jana.novakova@offload.local'`.
  3. With the same cookie, `GET /api/customers` and `GET /customers`.
- **Expected:** Requests from a deactivated user are rejected (401 / redirect to login) quickly.
- **Actual:** 200 for both. A new sign-in is correctly blocked ("Your account is deactivated…"). The cookie's `SecurityStampValidator` checks only the stamp (not `IsActive`), and the cookie slides for 14 days. The circuit revalidation checks `IsActive` only every 30 minutes.
- **Suggestion:** When deactivating (T-24), also call `UpdateSecurityStampAsync`, and check `IsActive` in cookie validation (`OnValidatePrincipal`) with a short `SecurityStampValidatorOptions.ValidationInterval`.
- **Status:** Verified (T-22, 2026-09-28)
- **Fix notes (dev):** `AppSecurityStampValidator` rejects inactive users when the cookie is validated (1-minute interval). The circuit revalidation and the per-action guard check `IsActive` too. Test: `SessionTests.Deactivated_user_session_is_rejected` (interval forced to 0). T-24 must also update the stamp when deactivating.

#### BUG-005: Deleting the only row on the last page shows the "No customers yet." empty state
- **Severity:** Medium
- **Phase / Area:** 1 / Customers list
- **Steps to reproduce:**
  1. Have 26 customers (25 per page), where the one on page 2 has no projects.
  2. `/customers`, go to page 2, and click **Delete** on that row, then **Delete**.
- **Expected:** The list goes back to page 1 and shows 25 rows. The pager shows "1-25 of 25".
- **Actual:** The table stays on the now-nonexistent page 2. It shows the **"No customers yet."** empty state with a "New customer" button, which falsely suggests the DB is empty, and the pager shows **"26-25 of 25"**. The same happens when a search/filter change or a delete from the detail page empties the current page.
- **Evidence:** `KnownBugTests.Bug005_deleting_last_row_on_last_page_goes_to_previous_page` (skipped).
- **Status:** Verified (T-22, 2026-09-28)
- **Fix notes (dev):** All lists run on `ListPageBase`. When a load returns an empty page and total > 0, it navigates to the last existing page. The empty-DB state is only shown when the table really has no rows (a separate unfiltered check). Test: `KnownBugTests.Bug005`.

#### BUG-006: Customer list actions are off-screen at 1280/1024/768 px
- **Severity:** Medium (UX/responsive; the D-15 target widths)
- **Phase / Area:** 1 / Customers list
- **Environment:** Edge headless, viewports 1280×800, 1024×768, 768×1024, with seed data only (`/customers?search=s.r.o`)
- **Steps to reproduce:** Open `/customers` at 1280 px width.
- **Expected:** All columns, or at least the row actions (Edit/Delete), are visible without scrolling at the supported widths. Lower-priority columns hide or collapse, or the actions are pinned.
- **Actual:** The table is wider than its container (by 227 px at 1280, 483 px at 1024, and 507 px at 768). "Created" and the Edit/Delete buttons are only reachable by horizontally scrolling inside the table, and there's no visual hint that it scrolls. Company names wrap onto 3 lines while E-mail/Phone take a lot of space. At 1920 px it fits. (Page-level scrolling is fine; only the table scrolls.)
- **Evidence:** Screenshots `%TEMP%\qa-list-normal-1280.png`, `qa-list-normal-768.png`; `KnownBugTests.Bug006_…` (skipped).
- **Status:** Verified (T-22, 2026-09-28)
- **Fix notes (dev):** Lists use responsive columns: `hide-lt-1440/1280/960/600` classes. Secondary columns (E-mail, Phone, Created, VAT, …) hide at <= 1280 px. The actions column is always visible, and names clamp to 2 lines with a tooltip. Test: `KnownBugTests.Bug006` (1280, 1024). My smoke runs check that no list/detail page scrolls horizontally at 1024/768.

#### BUG-007: A long unbroken value breaks the layouts
- **Severity:** Medium
- **Phase / Area:** 1 / Customers list + detail
- **Steps to reproduce:** Create a customer whose company name is 200 characters without spaces (valid per D-12), e.g. `WWWW…W`. Open `/customers` and the customer's detail page at 1280 px.
- **Expected:** The text wraps or ellipsizes, and the page never scrolls horizontally.
- **Actual:**
  - List: the Company column grows to ~2000 px, so IČO/City/Status/…/Actions are all pushed off-screen for **every** row. One record makes the list unusable.
  - Detail: the H1 wraps correctly, but the **breadcrumb** item doesn't wrap. The whole page scrolls horizontally by 990 px (1280 wide), 1246 px (1024), and 1837 px in another run.
  - The same applies to long e-mails/URLs (no `word-break` on the link cells).
- **Evidence:** `%TEMP%\qa-detail-long-1280.png`, `%TEMP%\qa-list-1280.png`; `KnownBugTests.Bug007_…` (skipped).
- **Status:** Verified (T-22, 2026-09-28)
- **Fix notes (dev):** Global CSS: table cells, breadcrumbs, headings and links use `overflow-wrap:anywhere`, the breadcrumbs wrap, and the main content has `min-width:0`. Headers and chips never break mid-word (chips ellipsize). Test: `KnownBugTests.Bug007`.

#### BUG-008: Bad API requests / unhandled errors don't return usable ProblemDetails
- **Severity:** Low
- **Phase / Area:** 1 / API (D-01: "Errors come back as RFC 7807 ProblemDetails")
- **Steps to reproduce (run the app with `ASPNETCORE_ENVIRONMENT=Production`, `Database__MigrateOnStartup` not needed):**
  1. `POST /api/customers` with body `{"companyName": ` (malformed), `{"companyName":123}` (wrong type), `[]`, `null`, or no body.
  2. `GET /api/customers?status=bogus` / `?page=abc`.
  3. `GET /api/customers?page=100000000` (a server error, BUG-002).
- **Expected:** `application/problem+json` with a meaningful title/detail (e.g. "The request body is not valid JSON." and the failing path `$.companyName`), and a JSON 500 ProblemDetails for unhandled API errors.
- **Actual:** Production: 1 and 2 return **400 with an empty body**. 3 returns **the HTML error page** (`text/html`, with the page title "**Not found** · Offload CRM" even though it's a 500). Development: the title is the exception type name (`Microsoft.AspNetCore.Http.BadHttpRequestException`) plus a stack trace, and each such 4xx is logged as `fail: … unhandled exception`.
- **Status:** Verified (T-22, 2026-09-28)
- **Fix notes (dev):** `ApiErrorMiddleware` in the `/api` branch, with `ThrowOnBadRequest`: bad JSON or a wrong type returns a 400 whose detail includes the JSON path, a missing body returns "A JSON request body is required.", and unparsable query values return a 400 with a readable detail. These are logged as Information, not fail. Unhandled errors return a 500 ProblemDetails with no stack trace outside Development. `UseStatusCodePages` in the API branch turns empty 401/404/405 responses into problem+json. Tests: `Phase2ApiTests.Malformed_*`, `Missing_body_*`, `Unauthenticated_api_error_is_problem_json` (run in the Production environment).

#### BUG-009: Unsupported method returns 404 instead of 405
- **Severity:** Low
- **Phase / Area:** 1 / API
- **Steps:** `PATCH /api/customers/1`, `POST /api/contacts`, `GET /api/contacts`.
- **Expected:** 405 Method Not Allowed (with an `Allow` header) for existing resources.
- **Actual:** 404 ProblemDetails "Not Found". The catch-all `api.Map("/{**path}")` swallows method mismatches, so a client can't tell a wrong verb from a wrong URL.
- **Status:** Verified (T-22, 2026-09-28)
- **Dev note (not claimed as fixed; deferred per triage):** as a side effect of T-38, the `/api/{**path}` catch-all was removed, so a wrong verb on an existing route now returns 405 ProblemDetails with an `Allow` header. Anonymous requests to unknown `/api` routes still get 401. QA can verify this whenever it is convenient.

#### BUG-010: The customer form checks max length before trimming (UI ≠ API)
- **Severity:** Low
- **Phase / Area:** 1 / Customer (and Contact) dialogs
- **Steps:** New customer, with Company name = two spaces + 199 × "M" + three spaces, then Save.
- **Expected:** Saved (D-12: trim, then validate), the same as the API does (the API accepted `"  " + 200×"B" + "  "`).
- **Actual:** "Company name can be at most 200 characters." The dialog validates the raw DataAnnotations before `Normalize()`. Similarly, a whitespace-padded IČO can hit the 20-char limit only in the UI. There's also no `maxlength`/counter on single-line inputs (only Notes shows `x / 4000`), so users learn about the limits only after saving.
- **Status:** Verified (T-22, 2026-09-28)
- **Fix notes (dev):** The new `FormValidator` (it replaces `DataAnnotationsValidator` in every dialog) normalises the model first (trim, empty → null, https:// prefix) and then validates, the same as the services do. Single-line inputs show a `Counter` with the max length. There is deliberately no hard `maxlength`, because that would truncate pasted, padded values before trimming.

#### BUG-011: Logout CSRF
- **Severity:** Low (security, nuisance)
- **Phase / Area:** 1 / Auth (D-18 says "a third-party link can't log users out")
- **Steps:** While signed in, click a link on any other site (or a page opened with `window.open`) that points to `http://localhost:5080/account/logout`.
- **Expected:** The user stays signed in (a GET shows a "Sign out?" confirmation, or the logout requires a same-origin POST initiated by the app).
- **Actual:** The GET page auto-submits its own antiforgery-protected form with `<script>document.getElementById('logout-form').submit()</script>`, so the user is signed out (API → 401). The antiforgery token doesn't help, because the attacker never needs it.
- **Evidence:** `KnownBugTests.Bug011_foreign_link_to_logout_does_not_sign_out` (skipped).
- **Status:** Verified (T-22, 2026-09-28)
- **Fix notes (dev):** A GET to `/account/logout` now shows a "Sign out?" confirmation with a button and no script. The app-bar Logout button posts that form directly (antiforgery token). Tests: `SessionTests.Get_on_logout_does_not_sign_out` and `KnownBugTests.Bug011`.

#### BUG-012: E-mail validation accepts mailto header injection
- **Severity:** Low (security)
- **Phase / Area:** 1 / Validation (Customer + Contact e-mail)
- **Steps:** Save a customer with E-mail `sales@firma.cz?cc=attacker@evil.com` (also accepted: `x@y.cz?subject=<b>`).
- **Expected:** "Enter a valid e-mail address." (`?`, `<`, `>`, `,`, `;` aren't valid in an address).
- **Actual:** Accepted. The list/detail render `mailto:sales@firma.cz?cc=attacker@evil.com`, so any colleague who clicks it silently CCs the attacker. The regex `^[^@\s]+@[^@\s]+\.[^@\s.]{2,}$` allows almost any character.
- **Status:** Verified (T-22, 2026-09-28)
- **Fix notes (dev):** Stricter regex: the local part cannot contain whitespace or `@ ? < > , ; " ( ) [ ] \ :`, the domain must be made of labels, and the TLD must have 2+ letters. Test: `SharedRulesTests.Email_rejects_header_injection_and_invalid_characters` (14 cases) plus valid cases.

#### BUG-013: Login responses reveal account state
- **Severity:** Low (security)
- **Phase / Area:** 1 / Auth
- **Steps / Actual:**
  1. 5 wrong passwords for an **existing** e-mail → "Your account is locked…". For a non-existent e-mail it's always "Invalid e-mail or password.", so an attacker can enumerate valid accounts.
  2. For a **deactivated** user, failed attempts aren't counted (Identity returns `NotAllowed` before checking the password). The special "Your account is deactivated" message appears only when the password is correct, which gives an attacker an unlimited, lockout-free password oracle for deactivated accounts. That matters because people reuse passwords.
- **Expected:** The same generic message in all cases (or a generic "can't sign in" for locked/inactive), and failed attempts counted for inactive accounts too.
- **Status:** Open

#### BUG-014: Descending sort puts empty values first
- **Severity:** Low
- **Phase / Area:** 1 / Customers list + API (`sort=…&descending=true`)
- **Steps:** Customers list, click "Last activity" twice (descending) or call `GET /api/customers?sort=lastActivity&descending=true`.
- **Expected:** The most recent activity first, with customers without activity at the end (NULLS LAST), and the same for IČO/City/E-mail/Phone.
- **Actual:** Customers with "—" (null) come first (PostgreSQL's default `NULLS FIRST` for DESC), so "most recent first" starts with customers that have no activity. Also, the Status column sorts alphabetically (Active, Inactive, Lead) rather than in lifecycle order (Lead → Active → Inactive), which is minor.
- **Status:** Verified (T-22, 2026-09-28)
- **Fix notes (dev):** The shared `SortMap`/`SortBy` sorts nulls last in both directions. Enum columns sort in declaration (lifecycle) order: customer status Lead→Active→Inactive, priority, task status. Project status sorts by `SortOrder`. Tests: `ListConventionTests.Nulls_sort_last_in_both_directions`, `Empty_text_columns_sort_last_descending`, `Status_sorts_in_lifecycle_order`, and `ProjectServiceTests.Every_sort_column_works_and_nulls_are_last`.

#### BUG-015: List state is lost when returning from a customer detail page
- **Severity:** Low (UX; D-18 keeps only search/status in the URL)
- **Phase / Area:** 1 / Customers list
- **Steps:** Sort by City, go to page 2 (or set 50 rows/page), open a customer, then press Back (or the "Customers" breadcrumb).
- **Expected:** The same sort/page/page size as before (standard SaaS behavior; store them in the URL like search/status).
- **Actual:** The list resets to page 1, sorted by Company, 25 rows.
- **Status:** Verified (T-22, 2026-09-28)
- **Fix notes (dev):** Search, filters, sort, direction, page and page size all live in the URL (`?search=&status=&sort=&desc=true&page=&size=`), and defaults are omitted. Back and the breadcrumb return to the same view. The same applies to the Projects, Tasks (+ `view=kanban`) and Activities lists.

#### BUG-016: Projects Status filter loops endlessly and never filters (the product owner's "filtering glitch")
- **Severity:** High (a core list filter is unusable and the tab freezes; the only workaround is typing `?status=<id>` into the URL)
- **Phase / Area:** 2 / Projects list (`ProjectListPage` status `MudSelect MultiSelection` + `ListPageBase`)
- **Environment:** Development, Edge headless 1440×900, seed data
- **Steps to reproduce:**
  1. Open `/projects` (10 active projects).
  2. Open the **Status** filter and tick **Lead** (any status).
- **Expected:** The URL becomes `/projects?status=1`, and the list shows the 2 Lead projects. Ticking a second status adds it.
- **Actual:**
  - The URL flips between `/projects` and `/projects?status=1` **~200 times per second** (641–647 navigations measured in 3 s, and it keeps going after the dropdown is closed).
  - The checkbox flickers on and off. The label alternates between "Lead" and empty.
  - The table **never filters**: it always shows all 10 rows ("1–10 of 10").
  - Every navigation starts a new list load, which cancels the previous one.
  - The page becomes unresponsive: a click on the Priority filter timed out after 8 s. The loop only stops on a full reload or when you navigate away.
  - With the dropdown open, the URL visibly lags one click behind (after Lead then Analysis it showed `?status=1`).
- **Not affected:** Deep links work (`/projects?status=1` → 2 rows, label "Lead"; `?status=1,2` → 4 rows, label "Lead, Analysis", no loop). Priority, Responsible, Customer, Search, "Active only", sort, paging and page size all update the URL once and filter correctly.
- **Likely cause (for dev):** `SelectedValues="_statusIds"` receives a **new** `HashSet` on every `OnStateLoaded`, while `SelectedValuesChanged` → `SetFilter` → `NavigateTo` round-trips through the URL. MudSelect then re-raises `SelectedValuesChanged` with the stale or new set, and the two states chase each other. `ListPageBase.Change` also clones the last *loaded* `State`, so a change made before the previous navigation lands is based on stale state.
- **Evidence:** `KnownBugTests.Bug016_status_multiselect_filters_the_list_without_looping` (skipped; fails with "485 navigations in 2 s").
- **Status:** Open

#### BUG-017: "Active only" silently hides an explicitly selected closed status
- **Severity:** Medium
- **Phase / Area:** 2 / Projects list + `GET /api/projects`
- **Steps:**
  1. `/projects?status=6` (Delivered), with "Active only" on by default.
  2. `/projects?status=1,6,8`.
  3. `GET /api/projects?statusId=6`.
- **Expected:** Choosing a closed status explicitly shows those projects. Either an explicit status selection overrides "Active only", or selecting a closed status turns "Active only" off, or the closed statuses are disabled in the dropdown while the switch is on.
- **Actual:**
  1. "No projects match your search or filters." The Status field shows "Delivered" and the switch shows on, and the user has to realise the two conflict.
  2. Only the 2 Lead projects are shown; Delivered and Cancelled are silently dropped while the label says "3 statuses".
  3. `totalCount: 0`, because the API's `activeOnly` defaults to `true` as well (with `&activeOnly=false` it returns 1).
- **Evidence:** `KnownBugTests.Bug017_selecting_a_closed_status_shows_its_projects` (skipped).
- **Status:** Open

#### BUG-018: The Kanban board doesn't fit at 1280/1024/768
- **Severity:** Medium (D-20.5: actions must be reachable without horizontal scroll at these widths)
- **Steps:** `/tasks?view=kanban` at 1280×900 (also 1024 and 768).
- **Expected:** All 4 columns visible (narrower columns, wrapping, or a clear horizontal-scroll affordance), with card menus reachable.
- **Actual:**
  - The board has fixed-width columns. The Done column ends at 1324 px (1280 and 1024 viewports) and at 1092 px (768). At 1024 the Done column is not visible at all.
  - 12 (1280) and 25 (1024/768) card menus are outside the viewport. There's no visible scrollbar hint.
  - A 200-character unbroken title overflows its card, and the text runs past the card edge (D-20.6).
- **Evidence:** `%TEMP%\qa2-tasks-view-kanban-1280.png`, `qa2-tasks-view-kanban-1024.png`; `KnownBugTests.Bug018_all_kanban_columns_are_visible` (skipped, 3 widths).
- **Status:** Open

#### BUG-019: `PATCH /api/tasks/{id}/status` without a status resets the task to Todo
- **Severity:** Medium (silent data change via the API)
- **Steps:**
  1. Set a task to Done.
  2. `PATCH /api/tasks/{id}/status` with the body `{}`, or with a typo such as `{"state":"Done"}`.
- **Expected:** 400 with an error for the `status` field (it's required).
- **Actual:** 200. The task becomes **Todo** and `CompletedAt` is cleared. The non-nullable enum defaults to `Todo` (0) when the field is missing. `PATCH /api/projects/{id}/status` with `{}` correctly returns 400.
- **Evidence:** `KnownBugTests.Bug019_patch_status_without_status_is_rejected` (skipped).
- **Status:** Open

#### BUG-020: A closed status can be made the default
- **Severity:** Medium
- **Steps:**
  1. `POST /api/project-statuses/8/default` (Cancelled: IsActive=false, IsClosed=true), or use "Make default" in Settings.
  2. Create a project without choosing a status.
- **Expected:** 400/409 "The default status must be an open (non-closed) status", or at least a warning.
- **Actual:** 200. New projects are created as **Cancelled**, and immediately disappear from the default Projects list ("Active only"), which looks like data loss to the user.
- **Evidence:** `KnownBugTests.Bug020_closed_status_cannot_be_default` (skipped; it restores the original default).
- **Status:** Open

#### BUG-021: An unknown deep-link filter value is applied but invisible
- **Severity:** Low
- **Steps:** `/projects?customer=999999`, `/projects?responsible=nobody`, `/projects?status=999`.
- **Expected:** The unknown value is ignored or shown (e.g. "Unknown customer"), and it can be cleared.
- **Actual:** "No projects match…", while the Customer/Responsible/Status controls are empty, so nothing on screen explains the empty result. (The "Clear filters" button does work.) The same happens after a customer referenced in a bookmarked URL is deleted. By contrast, `?status=abc` and `?priority=bogus` are silently ignored.
- **Status:** Open

#### BUG-022: The detail breadcrumb drops the list state
- **Severity:** Low (D-20.1: "Back and the breadcrumb return to the same view")
- **Steps:** `/customers?sort=city&desc=true&size=50` (or `/projects?priority=High&sort=target&desc=true`), open a record, then click the "Customers"/"Projects" breadcrumb.
- **Expected:** You return to the same filtered/sorted list. Browser Back does this correctly.
- **Actual:** The breadcrumb href is `/customers` or `/projects`, so filters, sort and page size are reset.
- **Status:** Open

#### BUG-023: With the DB down, the Projects list shows the global error boundary
- **Severity:** Low (T-31)
- **Steps:** Make the DB unavailable (`ALTER DATABASE offload_crm ALLOW_CONNECTIONS false` + terminate its connections), then open `/projects`.
- **Expected:** The list's own "The list could not be loaded… try again" state (`load-error`), as the Customers list does.
- **Actual:** "Something went wrong. An unexpected error occurred while showing this page." (error boundary). `OnInitializedAsync` loads the statuses outside the list's try/catch.
- **Also checked:** A Kanban drag while the DB was down **reverts the card and shows "The status could not be changed. Please try again."**, and the board works again once the DB is back. In Production, the API returns a 500 ProblemDetails "An unexpected error occurred." without a stack trace.
- **Status:** Open

#### BUG-024: Kanban status filter label and stale cards
- **Severity:** Low
- **Steps / Actual:**
  1. `/tasks?view=kanban`: the Status filter reads **"Open tasks"**, but the board shows the Done column with Done cards (D-21 says Kanban ignores the implicit default; the label should read e.g. "All statuses").
  2. `/tasks?view=kanban&status=Done`, then drag a card to To do: the card stays on the board in "To do", even though the filter says Done, until reload.
- **Status:** Open

#### BUG-025: API and UI list parameters are inconsistent
- **Severity:** Low (API usability)
- **Actual:**
  - UI URLs use `status=1,2`, `customer`, `responsible`, `size`, `desc`. The API uses repeated `statusId`, `customerId`, `responsibleUserId`, `pageSize`, `descending`.
  - Unknown API parameters are silently ignored (`GET /api/projects?status=1` returns all projects).
  - `GET /api/tasks` defaults to all statuses while the UI defaults to open tasks, and `?status=all` returns 400 "Failed to bind parameter".
  - The 415 response has `title: "Bad Request"`.
- **Status:** Open

#### BUG-026: Dates aren't range-checked
- **Severity:** Low
- **Actual:**
  - Projects accept start `0001-01-01` and target `9999-12-31`, and activities accept `1900-01-01`, so typos like `0226` for `2026` go through.
  - `POST /api/activities` with `"date":"2026-09-28T10:00:00"` (no offset) is stored as 10:00 **UTC** (12:00 Prague). This should be documented, or the date should be treated as local time (D-11).
- **Status:** Open

#### BUG-027: Timeline shows "Call" instead of "Phone call"
- **Severity:** Low (D-19)
- **Steps:** Add an activity of type Call without a subject to a project, then open the project's Timeline tab (or `GET /api/projects/{id}/timeline`).
- **Actual:** The title is "Call". `ProjectService` builds the title as `a.Subject ?? a.Type.ToString()`, which bypasses `Labels.Of`.
- **Status:** Open

## Verification of T-38 fixes (QA, 2026-09-28)

I reproduced my original steps independently (curl/API, Playwright, DB) rather than relying on dev tests.

| Bug | Result | How it was verified |
|---|---|---|
| BUG-001 | Verified | `status:99` → 400 `errors.status`. `?status=99` → 400 listing the allowed values. Project `priority:7`, task `status:9`, activity `type:42` all → 400 with a field error. |
| BUG-002 | Verified | `page=100000000` and `page=2147483647&pageSize=100` → 200 with an empty page on customers, projects, tasks and activities. |
| BUG-003 | Verified | Two tabs: logout in B, then Save in A → A goes to login, and no customer is created (`KnownBugTests.Bug003`, unchanged). A curl admin cookie also dies when the UI suite logs out as admin (the stamp rotates). |
| BUG-004 | Verified | Cookie: the deactivated user's API call → 401 within ≤ 65 s, and the page → 302 to login. Circuit: clicking "New project" right after deactivation → immediate redirect to `/account/login?expired=1`. After reactivation the old cookie stays invalid (a new sign-in is needed). |
| BUG-005 | Verified | `KnownBugTests.Bug005` (Customers). Projects/Tasks/Activities use the same `ListPageBase` logic, and `?page=3` beyond the end jumps to the last page. |
| BUG-006 | Verified for all lists | Customers, Projects, Tasks list, Activities and Settings at 1280/1024/768: 0 page overflow and 0 row actions off-screen. **Not true for the Kanban board → BUG-018.** |
| BUG-007 | Verified for lists/detail | A 200-char unbroken customer/project/task name gives 0 px page overflow on the list and detail pages. Kanban cards still overflow (BUG-018). |
| BUG-008 | Verified | Production instance: malformed JSON → 400 `detail … at '$.companyName'`; missing body → "A JSON request body is required."; DB down → 500 problem+json "An unexpected error occurred." with no stack. A bad query value → 400 problem+json (the detail still shows the .NET type `Nullable<int>`; cosmetic). |
| BUG-009 | Verified | `PATCH /api/customers/1` → 405 + `Allow: DELETE, GET, PUT`. Unknown `/api/nope` → 401 anonymous / 404 signed-in. `POST /api/contacts` → 404, which is correct because the collection route doesn't exist. |
| BUG-010 | Verified | UI: "  QA2 " + 194 × M + three spaces → saved (the customer was created). |
| BUG-011 | Verified | GET `/account/logout` shows the "Sign out?" page with a form and **no auto-submit script**. The API is still 200 afterwards. A POST without the antiforgery token → 400. The foreign link test `KnownBugTests.Bug011` passes. |
| BUG-012 | Verified | `sales@firma.cz?cc=attacker@evil.com` and `a@b.cz,c@d.cz` → 400. `first.last+tag@sub.firma.cz` is accepted. |
| BUG-014 | Verified | `lastActivity`/`city` ascending and descending → nulls last. Customer status sorts Lead→Active→Inactive, and project status sorts by SortOrder. |
| BUG-015 | Verified (Back) | `/customers?sort=city&desc=true&size=50` → detail → Back restores the URL and page size 50. The same works on Projects. The breadcrumb does not restore it → BUG-022. |

**Review of Dev's change to `PlaywrightFixture` (fresh admin sign-in per context):** accepted, because it's correct and robust. Since D-19 any admin logout rotates the stamp and kills every admin cookie. Trade-offs:
- It adds about 1 s per test context.
- The real root cause was that `AuthTests.Logout_then_back…` logged out the **admin**, which also ends every *human* admin session while the suite runs. I observed this: my curl admin session got 401 after a suite run. I changed that test to use the demo user `petr.dvorak@…`. The logout tests that remain (`Bug003`, `Logout_then_back…`) now only affect `petr`.

## What was verified OK (Phase 2)

- **Project filters (other than status):** Priority, Responsible (includes inactive users), Customer autocomplete, and search (diacritics) each update the URL once and filter correctly. Combined with sort/paging, they survive reload and Back from the detail page. `?size=1&page=2`, `?size=7` and `?page=3` beyond the end all work, and a filter change resets to page 1. "Clear filters" resets everything, including "Active only" → on. API filters match the UI results.
- **Statuses:** duplicate names in a different case or with padding → 400. Both flags → 400. Invalid/3-digit/CSS-injected colors → 400. A 51-char name → 400. Deleting a status in use → 409 with "N projects use it", and the UI shows it in a snackbar. Deleting the default → 409. Un-flagging the default → 400. Setting a new default switches it atomically. Move up at the top is a no-op, a bogus direction → 400, and a missing id → 404. A rename shows up immediately in project lists. Turning a status's IsActive off removes its projects from "Active only".
- **Projects:** target < start → 400 (field `targetDate`). Negative value, 3 decimals, > 999 999 999 999, and `1e30` → 400. The maximum value and 0.01 are accepted. `1 250 000,5 Kč` formatting works. A missing or non-existent customer, an invalid status, or an unknown/inactive responsible user → 400. Changing the customer moves the project's activities to the new customer. A status change (inline, PATCH) creates a "Status changed: Lead → Development" Note in the timeline, and PATCH with `{}` → 400. **Delete with tasks + activities:** the confirmation states the counts; the tasks are deleted and the activities are kept with `ProjectId = null` on the customer (checked in the DB).
- **Tasks:** CompletedAt is set on Done, kept on Done → Done, can't be overridden via PUT, and is cleared when leaving Done (API + Kanban). **Kanban drag through To do → In progress → Blocked → Done → To do → Done → In progress** is persisted each time, the counts update, and the card menu offers the other 3 statuses. A drag while the DB was down reverts with an error snackbar. Priority filter + Kanban works. The list defaults to open tasks. `assignee=me`, multi-status and overdue filters work via the API.
- **Activities:** no customer/project → 400. A project from a different customer → 400. A date more than 1 year ahead → 400, and 364 days ahead is OK. A spoofed `authorId` is ignored (the author is the current user). A blank description → 400. A project-only activity gets the project's customer.
- **Security:** HTML/script in project name/description/notes, task title/description, activity subject/description and status name renders as text in the lists, detail, Kanban, timeline, confirm dialogs and snackbars. No `img` injection and no alert at 1280/1024/768.
- **Confirmation dialogs** on every delete: task, activity, status, project (list + detail, with task/activity counts), customer, contact.
- **Console:** 0 errors on all Phase 1+2 pages and detail tabs at 1280/1024/768 (and during the Kanban drags).

## What was verified OK (Phase 1)

- Login/logout, "Remember me" (a session cookie without it), a friendly error for a wrong password or unknown user, inline "Enter your e-mail/password" validation, lockout after 5 attempts (5 min), deactivated-user sign-in blocked, and no public registration.
- **No open redirect:** `ReturnUrl=https://evil.com`, `//evil.com`, `/\evil.com`, `/%09/evil.com`, `/%0A/evil.com`, `/%20/evil.com`, and `javascript:` all end on `http://localhost:5080/…`. A deep link while logged out goes to login and back to the same URL (with the query).
- Back after logout doesn't show cached pages (`Cache-Control: no-store`). The API gets 401 (not a 302) without a cookie, including unknown `/api/*` routes.
- The API returns 404 ProblemDetails for unknown/negative/non-numeric IDs and routes (customers, contacts, contacts of a missing customer). A PUT to a missing ID gets 404 (not 400). 409 with a message for deleting a customer with projects. 400 with camelCase field errors for whitespace-only/201-char names, a bad e-mail/URL/phone, a 21-char IČO, and 5 MB notes. Negative/zero page/pageSize is normalized, and pageSize > 100 is capped. Unknown `sort` falls back to Company.
- Duplicate IČO: `QA777001`, ` qa777001 `, `QA 777 001`, tab/NBSP-separated and fullwidth variants are all rejected. A whitespace-only IČO becomes null. **10 parallel POSTs with the same new IČO → exactly 1×201 + 9×400** (no 500). 10 parallel DELETEs → 1×204 + 9×404.
- HTML/script in every text field (name, city, notes, contact position, website with quotes) renders as text in the list, detail H1, breadcrumb, `<title>`, snackbars and confirm dialogs, with no script execution. `javascript:`-style websites are rejected. SQL-like search (`' OR 1=1--`, `%`, `_`) matches literally.
- Czech diacritics: create/edit/search is case-insensitive for ř/ž/ů/Ř/Ž/Ů. Contact names are searchable as "First Last" and "Last First".
- Double-click on Save, Enter pressed twice, and parallel submits don't create duplicates. Cancel/Esc discards dialog edits, and Cancel in the delete confirmation keeps the record.
- The detail page shows a "Customer not found" state for `/customers/0`, `/-1` and `/987654321`. `/customers/abc`, `/customers/2147483648` and unknown routes show the friendly 404 page (HTTP 404). All tabs render. The Projects tab shows the count and the Activities placeholder is shown (Phase 2).
- No browser console errors on Dashboard, Customers, the customer detail (all tabs), Projects, Tasks, Activities, Settings, or the dialogs. The only console line is the documented `Failed to load resource: 404` on 404 pages (D-18).
- Drawer: collapses below 960 px and opens with the menu button. All 6 menu items navigate, set `<PageTitle>` and highlight the active item.
- `dotnet build` (0 warnings), `dotnet test` green. **Fresh DB:** the app on an empty DB (`offload_crm_qafresh`) migrates and seeds 8 customers / 13 contacts / 12 projects / 40 tasks / 30 activities / 8 statuses / 3 users. **A restart doesn't duplicate anything.** `dotnet ef database update` on an empty DB applies `InitialCreate` cleanly.

## Test runs

| Date | Phase | Build/commit | Scope (manual/automated) | Result (pass/fail counts) | Notes |
|---|---|---|---|---|---|
| 2026-09-27 | 1 | Working tree 2026-09-27 (no git commits yet), Development, Edge (msedge channel, headless) | **Automated:** `dotnet build`; `dotnet test` (whole sln, app stopped); new `tests/Offload.Crm.UiTests` (Playwright, 66 tests: auth, navigation, 404, responsive, customer CRUD, validation, duplicate IČO, delete-block, contacts CRUD, XSS rendering, API black-box incl. concurrency). **Manual/exploratory:** curl API fuzzing (malformed JSON, wrong types, huge payloads, enum/paging abuse, verbs), open-redirect matrix, CSRF logout, lockout, inactive user, multi-tab logout, fresh-DB migration + restart, Production-mode error handling, screenshots at 1920/1280/1024/768. | Build OK (0 warnings). Offload.Crm.Tests 84/84 passed. UiTests with the app running: **59 passed, 0 failed, 7 skipped** (= `KnownBugTests`, which fail when un-skipped). Without the app (plain `dotnet test`): 0 failed, all UI tests skipped (39 reported; theories count once). | 15 bugs logged: 0 Critical, 0 High, 7 Medium, 8 Low. No Critical/High blockers for the Phase 1 gate; PM to triage Medium. QA test data was removed from the dev DB afterwards (back to the 8 seeded customers / 13 contacts). The UI suite cleans up after itself. |
| 2026-09-27 | 1–2 (Dev, T-38 + T-13..T-21) | Working tree (no commits), Development, fresh `offload_crm`, Edge headless | **Automated:** `dotnet test` (whole sln): Offload.Crm.Tests **188 passed**; UiTests with the app running: **67 passed, 0 failed, 0 skipped** (the 7 `KnownBugTests` for BUG-001/002/003/005/006/007/011 are un-skipped, and their assertions are unchanged). Dev Playwright smoke of the Phase 2 pages (statuses, projects, project detail/timeline, tasks list + Kanban drag/menu, activities, customer tabs, no horizontal overflow at 1024/768): all passed, 0 console errors. | Infrastructure change in QA's suite: `PlaywrightFixture` now signs the admin in fresh for every context. Since D-19, the admin logout in `AuthTests` changes the admin's security stamp, which would invalidate a cached admin cookie within about 1 min and break the later tests. No test assertions were modified. |
| 2026-09-28 | 1 (retest) + 2 (T-22) | Working tree 2026-09-28 (no commits), Development + a Production instance on :5091 for error handling, Edge headless | **Retest:** all 14 T-38 fixes plus BUG-009, reproduced with the original steps (curl/API, Playwright, DB toggles for deactivation and DB-down). **Phase 2 exploratory:** project filters (each filter alone/combined, URL round-trip, deep links, Back/breadcrumb, clear, EF SQL log to spot extra loads), statuses, projects, tasks + Kanban (drags across all columns, card menu, DB down via `ALLOW_CONNECTIONS false`, filters, 1280/1024/768), activities, sessions (2 tabs, deactivation, foreign logout link), XSS in every new field, confirmation dialogs, console. **Automated:** +16 Phase 2 E2E test cases (`Phase2Tests.cs`: 13 facts + a 3-width layout theory) and +5 known-bug tests (BUG-016..020, 7 cases). | `dotnet build` OK (0 warnings). Offload.Crm.Tests **188/188 passed**. UiTests with the app running: **88 total: 83 passed, 0 failed, 5 skipped** (the BUG-016..020 known-bug tests; all 7 cases fail when un-skipped). Run 3× green. Without the app: 58 skipped, 0 failed. | 14 fixed bugs **Verified**. New: 1 High (BUG-016), 4 Medium (BUG-017..020), 7 Low (BUG-021..027). **BUG-016 is the product owner's filtering glitch.** QA data was removed from the dev DB again (12 projects, 8 customers, 8 statuses; Lead is the default). `AuthTests` logout now uses the demo user. |
