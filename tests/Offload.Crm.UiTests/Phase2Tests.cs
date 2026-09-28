using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Offload.Crm.UiTests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Offload.Crm.UiTests;

/// <summary>Phase 2 E2E: projects (filters, detail, timeline, delete), tasks (list + Kanban), activities, statuses.</summary>
[Collection(UiCollection.Name)]
public sealed class Phase2Tests(PlaywrightFixture fx)
{
    // ---------- helpers (API setup through the admin cookie) ----------
    private static async Task<JsonElement> PostAsync(IBrowserContext ctx, string url, object body)
    {
        var r = await ctx.APIRequest.PostAsync(url, new() { DataObject = body });
        Assert.True(r.Status is 200 or 201, $"POST {url} -> {r.Status}: {await r.TextAsync()}");
        return (await r.JsonAsync())!.Value;
    }

    private static async Task<int> CreateProjectAsync(IBrowserContext ctx, string name, int customerId = 4, int? statusId = null, string priority = "Medium") =>
        (await PostAsync(ctx, "/api/projects", new { name, customerId, statusId, priority })).GetProperty("id").GetInt32();

    private static async Task<int> CreateTaskAsync(IBrowserContext ctx, string title, int projectId, string status = "Todo") =>
        (await PostAsync(ctx, "/api/tasks", new { title, projectId, status, priority = "High" })).GetProperty("id").GetInt32();

    private static async Task<JsonElement> GetTaskAsync(IBrowserContext ctx, int id) =>
        (await (await ctx.APIRequest.GetAsync($"/api/tasks/{id}")).JsonAsync())!.Value;

    private static Task<string> CardColumnAsync(IPage page) => page.EvaluateAsync<string>(
        "() => { const c = document.querySelector(\"[data-testid='kanban-card']\"); return c ? c.closest(\"[data-testid^='kanban-column-']\").getAttribute('data-testid') : 'none'; }");

    // ---------- projects list ----------
    [UiFact]
    public async Task Projects_list_defaults_to_active_projects_and_shows_rows()
    {
        await using var context = await fx.NewAdminContextAsync(1440, 900);
        var page = await context.NewPageAsync();
        var errors = UiHelpers.CollectConsoleErrors(page);
        await page.GotoAsync("/projects");
        await Expect(page.GetByTestId("project-link").First).ToBeVisibleAsync();

        var api = (await (await context.APIRequest.GetAsync("/api/projects?pageSize=1")).JsonAsync())!.Value.GetProperty("totalCount").GetInt32();
        await Expect(page.GetByTestId("pager-info")).ToContainTextAsync($"of {api}");
        await Expect(page.Locator("input[data-testid='project-active-only']")).ToBeCheckedAsync();
        Assert.Empty(errors);
    }

    [UiFact]
    public async Task Projects_priority_and_search_filters_survive_reload_and_back_from_detail()
    {
        await using var context = await fx.NewAdminContextAsync(1440, 900);
        var page = await context.NewPageAsync();
        var name = UiHelpers.Unique("UI2 filter Critical");
        var id = await CreateProjectAsync(context, name, priority: "Critical");
        try
        {
            await page.GotoAsync("/projects");
            await page.GetByTestId("project-link").First.WaitForAsync();
            await UiHelpers.OpenSelectAsync(page, "project-priority-filter");
            await UiHelpers.PickPopoverItemAsync(page, "Critical");
            await Expect(page).ToHaveURLAsync(new Regex("priority=Critical"));
            await UiHelpers.Field(page, "project-search").FillAsync("UI2 filter");
            await Expect(page).ToHaveURLAsync(new Regex("search=UI2"));
            await Expect(page.GetByTestId("project-link")).ToHaveCountAsync(1);
            await Expect(page.GetByTestId("project-link")).ToHaveTextAsync(name);

            await page.ReloadAsync();
            await Expect(page.GetByTestId("project-link")).ToHaveCountAsync(1);
            await Expect(page.GetByTestId("project-link")).ToHaveTextAsync(name);

            await page.GetByTestId("project-link").ClickAsync();
            await Expect(page).ToHaveURLAsync(new Regex($"/projects/{id}$"));
            await page.GoBackAsync();
            await Expect(page).ToHaveURLAsync(new Regex("priority=Critical"));
            await Expect(page.GetByTestId("project-link")).ToHaveCountAsync(1);
            await Expect(page.GetByTestId("project-link")).ToHaveTextAsync(name);
        }
        finally
        {
            await context.APIRequest.DeleteAsync($"/api/projects/{id}");
        }
    }

    [UiFact]
    public async Task Projects_status_deep_link_filters_and_shows_the_selection()
    {
        await using var context = await fx.NewAdminContextAsync(1440, 900);
        var page = await context.NewPageAsync();
        var statuses = (await (await context.APIRequest.GetAsync("/api/project-statuses")).JsonAsync())!.Value.EnumerateArray().ToList();
        var lead = statuses.First(s => s.GetProperty("isDefault").GetBoolean());
        var leadId = lead.GetProperty("id").GetInt32();
        var expected = (await (await context.APIRequest.GetAsync($"/api/projects?statusId={leadId}")).JsonAsync())!.Value.GetProperty("totalCount").GetInt32();

        await page.GotoAsync($"/projects?status={leadId}");
        await page.WaitForTimeoutAsync(1500);
        await Expect(UiHelpers.Field(page, "project-status-filter")).ToHaveValueAsync(lead.GetProperty("name").GetString()!);
        await Expect(page.GetByTestId("project-link")).ToHaveCountAsync(expected);
    }

    [UiFact]
    public async Task Projects_customer_filter_via_autocomplete_updates_url_and_rows()
    {
        await using var context = await fx.NewAdminContextAsync(1440, 900);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/projects");
        await page.GetByTestId("project-link").First.WaitForAsync();
        var picker = UiHelpers.Field(page, "project-customer-filter");
        await picker.FillAsync("Česká");
        await page.Locator(".mud-popover-open .mud-list-item").First.ClickAsync();
        await Expect(page).ToHaveURLAsync(new Regex("customer=\\d+"));
        var customerId = Regex.Match(page.Url, "customer=(\\d+)").Groups[1].Value;
        var expected = (await (await context.APIRequest.GetAsync($"/api/projects?customerId={customerId}")).JsonAsync())!.Value.GetProperty("totalCount").GetInt32();
        await Expect(page.GetByTestId("project-link")).ToHaveCountAsync(expected);
    }

    // ---------- project detail ----------
    [UiFact]
    public async Task Inline_status_change_creates_timeline_entry()
    {
        await using var context = await fx.NewAdminContextAsync(1440, 900);
        var page = await context.NewPageAsync();
        var id = await CreateProjectAsync(context, UiHelpers.Unique("UI2 timeline"));
        try
        {
            await page.GotoAsync($"/projects/{id}");
            await page.GetByTestId("project-name").WaitForAsync();
            await UiHelpers.OpenSelectAsync(page, "project-status-inline");
            await UiHelpers.PickPopoverItemAsync(page, "Analysis");
            await page.WaitForTimeoutAsync(1000);

            await page.GetByRole(AriaRole.Tab, new() { Name = "Timeline" }).ClickAsync();
            await Expect(page.GetByTestId("timeline-item").Filter(new() { HasText = "Status changed: Lead → Analysis" })).ToBeVisibleAsync();
            var project = (await (await context.APIRequest.GetAsync($"/api/projects/{id}")).JsonAsync())!.Value;
            Assert.Equal("Analysis", project.GetProperty("statusName").GetString());
        }
        finally
        {
            await context.APIRequest.DeleteAsync($"/api/projects/{id}");
        }
    }

    [UiFact]
    public async Task Deleting_project_deletes_tasks_and_keeps_activities_on_the_customer()
    {
        await using var context = await fx.NewAdminContextAsync(1440, 900);
        var page = await context.NewPageAsync();
        var name = UiHelpers.Unique("UI2 delete");
        var id = await CreateProjectAsync(context, name);
        var taskId = await CreateTaskAsync(context, "UI2 task of deleted project", id);
        var activityId = (await PostAsync(context, "/api/activities", new { type = "Note", description = "UI2 kept activity", projectId = id })).GetProperty("id").GetInt32();

        await page.GotoAsync($"/projects/{id}");
        await page.GetByTestId("project-delete").First.ClickAsync();
        await Expect(page.Locator(".mud-dialog").Last).ToContainTextAsync("1 task will be deleted too");
        await UiHelpers.ConfirmAsync(page, "Delete");
        await Expect(page).ToHaveURLAsync(new Regex("/projects$"));

        Assert.Equal(404, (await context.APIRequest.GetAsync($"/api/projects/{id}")).Status);
        Assert.Equal(404, (await context.APIRequest.GetAsync($"/api/tasks/{taskId}")).Status);
        var activity = (await (await context.APIRequest.GetAsync($"/api/activities/{activityId}")).JsonAsync())!.Value;
        Assert.Equal(JsonValueKind.Null, activity.GetProperty("projectId").ValueKind);
        Assert.Equal(4, activity.GetProperty("customerId").GetInt32());
        await context.APIRequest.DeleteAsync($"/api/activities/{activityId}");
    }

    // ---------- tasks / Kanban ----------
    [UiFact]
    public async Task Kanban_drag_across_all_columns_persists_status_and_completed_at()
    {
        await using var context = await fx.NewAdminContextAsync(1920, 1000);
        var page = await context.NewPageAsync();
        var errors = UiHelpers.CollectConsoleErrors(page);
        var tag = UiHelpers.Unique("UI2kanban").Replace(" ", "");
        var projectId = await CreateProjectAsync(context, "UI2 kanban project " + tag);
        var taskId = await CreateTaskAsync(context, tag, projectId);
        try
        {
            await page.GotoAsync($"/tasks?view=kanban&search={tag}");
            await Expect(page.GetByTestId("kanban-card")).ToHaveCountAsync(1);
            foreach (var target in new[] { "InProgress", "Blocked", "Done", "Todo", "Done" })
            {
                await page.GetByTestId("kanban-card").First.DragToAsync(page.Locator($"[data-testid='kanban-column-{target}'] .kanban-zone"));
                await page.WaitForTimeoutAsync(800);
                Assert.Equal($"kanban-column-{target}", await CardColumnAsync(page));
                var task = await GetTaskAsync(context, taskId);
                Assert.Equal(target, task.GetProperty("status").GetString());
                Assert.Equal(target == "Done", task.GetProperty("completedAt").ValueKind != JsonValueKind.Null);
            }

            // Card menu (accessible alternative to drag & drop)
            await page.GetByTestId("kanban-card-menu").First.ClickAsync();
            await page.Locator("[data-testid='kanban-move-InProgress']").First.ClickAsync();
            await page.WaitForTimeoutAsync(800);
            Assert.Equal("InProgress", (await GetTaskAsync(context, taskId)).GetProperty("status").GetString());
            Assert.Equal(JsonValueKind.Null, (await GetTaskAsync(context, taskId)).GetProperty("completedAt").ValueKind);
            Assert.Empty(errors);
        }
        finally
        {
            await context.APIRequest.DeleteAsync($"/api/projects/{projectId}");
        }
    }

    [UiFact]
    public async Task Kanban_respects_priority_filter_and_view_is_kept_in_url()
    {
        await using var context = await fx.NewAdminContextAsync(1920, 1000);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/tasks?view=kanban&priority=Critical");
        await page.GetByTestId("kanban").WaitForAsync();
        await page.WaitForTimeoutAsync(1000);
        var chips = await page.Locator("[data-testid='kanban-card'] [data-testid='priority-chip']").AllInnerTextsAsync();
        Assert.NotEmpty(chips);
        Assert.All(chips, c => Assert.Equal("Critical", c.Trim()));
        await page.ReloadAsync();
        await Expect(page.GetByTestId("kanban")).ToBeVisibleAsync();
    }

    [UiFact]
    public async Task Task_list_default_shows_only_open_tasks()
    {
        await using var context = await fx.NewAdminContextAsync(1440, 900);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/tasks?size=100");
        await page.GetByTestId("task-row").First.WaitForAsync();
        var chips = await page.Locator("[data-testid='task-row'] [data-testid='task-status-chip'], [data-testid='task-row'] [data-testid='task-status-select']").AllInnerTextsAsync();
        Assert.DoesNotContain(chips, c => c.Trim() == "Done");
    }

    // ---------- statuses ----------
    [UiFact]
    public async Task Deleting_a_status_in_use_is_blocked_with_message()
    {
        await using var context = await fx.NewAdminContextAsync(1440, 900);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/settings");
        var row = page.GetByTestId("status-row").Filter(new() { HasText = "Development" });
        await row.GetByTestId("status-delete").ClickAsync();
        await UiHelpers.ConfirmAsync(page, "Delete");
        await Expect(page.Locator(".mud-snackbar")).ToContainTextAsync("can't be deleted");
        await Expect(page.GetByTestId("status-row").Filter(new() { HasText = "Development" })).ToHaveCountAsync(1);
    }

    [UiFact]
    public async Task Status_rules_via_api_duplicate_name_both_flags_and_default()
    {
        await using var context = await fx.NewAdminContextAsync();
        var dup = await context.APIRequest.PostAsync("/api/project-statuses", new() { DataObject = new { name = " lead ", color = "#112233" } });
        Assert.Equal(400, dup.Status);
        var both = await context.APIRequest.PostAsync("/api/project-statuses", new() { DataObject = new { name = "UI2 both", color = "#112233", isActive = true, isClosed = true } });
        Assert.Equal(400, both.Status);
        var statuses = (await (await context.APIRequest.GetAsync("/api/project-statuses")).JsonAsync())!.Value.EnumerateArray().ToList();
        var def = statuses.First(s => s.GetProperty("isDefault").GetBoolean()).GetProperty("id").GetInt32();
        Assert.Equal(409, (await context.APIRequest.DeleteAsync($"/api/project-statuses/{def}")).Status);
    }

    // ---------- activities ----------
    [UiFact]
    public async Task Activity_rules_via_api_mismatch_future_and_author()
    {
        await using var context = await fx.NewAdminContextAsync();
        var projectId = await CreateProjectAsync(context, UiHelpers.Unique("UI2 act"), customerId: 4);
        try
        {
            Assert.Equal(400, (await context.APIRequest.PostAsync("/api/activities", new() { DataObject = new { type = "Note", description = "x" } })).Status);
            Assert.Equal(400, (await context.APIRequest.PostAsync("/api/activities", new() { DataObject = new { type = "Note", description = "x", customerId = 1, projectId } })).Status);
            Assert.Equal(400, (await context.APIRequest.PostAsync("/api/activities", new() { DataObject = new { type = "Note", description = "x", customerId = 4, date = DateTime.UtcNow.AddYears(2) } })).Status);
            var created = await PostAsync(context, "/api/activities", new { type = "Call", description = "UI2 author", projectId, authorId = "someone-else" });
            Assert.Equal(4, created.GetProperty("customerId").GetInt32());
            Assert.Equal("Administrator", created.GetProperty("authorName").GetString());
        }
        finally
        {
            await context.APIRequest.DeleteAsync($"/api/projects/{projectId}");
        }
    }

    [UiFact]
    public async Task Html_in_phase2_text_fields_renders_as_text()
    {
        await using var context = await fx.NewAdminContextAsync(1440, 900);
        var page = await context.NewPageAsync();
        var alerts = 0;
        page.Dialog += async (_, d) =>
        {
            alerts++;
            await d.DismissAsync();
        };
        var projectId = await CreateProjectAsync(context, "<img src=x onerror=alert(1)> UI2 xss " + Guid.NewGuid().ToString("N")[..5]);
        await PostAsync(context, "/api/tasks", new { title = "<img src=x onerror=alert(2)> UI2 xss task", projectId, description = "<script>alert(3)</script>" });
        await PostAsync(context, "/api/activities", new { type = "Note", subject = "<b>s</b>", description = "<img src=x onerror=alert(4)>", projectId });
        try
        {
            foreach (var url in new[] { "/projects?search=UI2%20xss", $"/projects/{projectId}", "/tasks?search=UI2%20xss", "/tasks?view=kanban&search=UI2%20xss", "/activities" })
            {
                await page.GotoAsync(url);
                await page.WaitForTimeoutAsync(1200);
                Assert.Equal(0, await page.Locator(".mud-main-content img[src='x']").CountAsync());
            }

            await page.GotoAsync($"/projects/{projectId}");
            foreach (var tab in new[] { "Timeline" })
            {
                await page.GetByRole(AriaRole.Tab, new() { Name = tab }).ClickAsync();
                await page.WaitForTimeoutAsync(600);
                Assert.Equal(0, await page.Locator(".mud-main-content img[src='x']").CountAsync());
            }

            Assert.Equal(0, alerts);
        }
        finally
        {
            await context.APIRequest.DeleteAsync($"/api/projects/{projectId}");
        }
    }

    // ---------- layout / console ----------
    [UiTheory]
    [InlineData(1280)]
    [InlineData(1024)]
    [InlineData(768)]
    public async Task Phase2_pages_have_no_page_overflow_hidden_row_actions_or_console_errors(int width)
    {
        await using var context = await fx.NewAdminContextAsync(width, 900);
        var page = await context.NewPageAsync();
        var errors = UiHelpers.CollectConsoleErrors(page);
        foreach (var url in new[] { "/projects", "/projects?active=false", "/projects/1", "/tasks", "/activities", "/settings", "/customers/1" })
        {
            await page.GotoAsync(url);
            await page.WaitForTimeoutAsync(1200);
            var overflow = await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - document.documentElement.clientWidth");
            Assert.True(overflow <= 1, $"{url} scrolls horizontally by {overflow}px at {width}px.");
            var hidden = await page.EvaluateAsync<int>("() => [...document.querySelectorAll(\"[data-testid$='-edit'],[data-testid$='-delete']\")].filter(e => { const r = e.getBoundingClientRect(); return r.width > 0 && r.right > window.innerWidth + 1; }).length");
            Assert.True(hidden == 0, $"{url}: {hidden} row actions are off-screen at {width}px.");
        }

        Assert.Empty(errors);
    }
}
