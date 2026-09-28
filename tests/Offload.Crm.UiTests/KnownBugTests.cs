using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Offload.Crm.UiTests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Offload.Crm.UiTests;

/// <summary>
/// Regression tests for open bugs in docs/QA_REPORT.md. They fail on the current build and are skipped with the
/// bug ID so the suite stays green. Dev: remove the <c>Skip</c> when fixing the bug; QA verifies with it removed.
/// </summary>
[Collection(UiCollection.Name)]
public sealed class KnownBugTests(PlaywrightFixture fx)
{
    [UiFact]
    public async Task Bug001_undefined_status_value_is_rejected()
    {
        await using var context = await fx.NewAdminContextAsync();
        var create = await context.APIRequest.PostAsync("/api/customers", new() { DataObject = new { companyName = "BUG-001", status = 99 } });
        if (create.Status == 201)
        {
            await context.APIRequest.DeleteAsync($"/api/customers/{(await create.JsonAsync())!.Value.GetProperty("id").GetInt32()}");
        }

        Assert.Equal(400, create.Status);
        Assert.Equal(400, (await context.APIRequest.GetAsync("/api/customers?status=99")).Status);
    }

    [UiFact]
    public async Task Bug002_huge_page_number_returns_empty_page_not_500()
    {
        await using var context = await fx.NewAdminContextAsync();
        var response = await context.APIRequest.GetAsync("/api/customers?page=100000000");
        Assert.Equal(200, response.Status);
    }

    [UiFact]
    public async Task Bug003_other_tab_cannot_save_after_logout()
    {
        await using var context = await fx.NewAnonymousContextAsync();
        var tabA = await context.NewPageAsync();
        await UiHelpers.LoginAsync(tabA, "petr.dvorak@offload.local", "Demo123!");
        await tabA.GotoAsync("/customers");
        await Expect(tabA.GetByTestId("customer-link").First).ToBeVisibleAsync();

        var tabB = await context.NewPageAsync();
        await tabB.GotoAsync("/");
        await UiHelpers.WaitForAppAsync(tabB);
        await tabB.GetByTestId("logout").ClickAsync();
        await tabB.WaitForURLAsync(new Regex("/account/login"));

        var name = UiHelpers.Unique("BUG-003 after logout");
        await tabA.GetByTestId("customer-new").ClickAsync();
        await UiHelpers.Field(tabA, "customer-companyName").FillAsync(name);
        await tabA.GetByTestId("dialog-save").ClickAsync();
        await tabA.WaitForTimeoutAsync(2000);

        // Check with an admin session whether the logged-out tab managed to create the customer.
        await using var admin = await fx.NewAdminContextAsync();
        var found = await admin.APIRequest.GetAsync("/api/customers?search=" + Uri.EscapeDataString(name));
        var body = (await found.JsonAsync())!.Value;
        foreach (var item in body.GetProperty("items").EnumerateArray())
        {
            await admin.APIRequest.DeleteAsync($"/api/customers/{item.GetProperty("id").GetInt32()}");
        }

        Assert.Equal(0, body.GetProperty("totalCount").GetInt32());
    }

    [UiFact]
    public async Task Bug005_deleting_last_row_on_last_page_goes_to_previous_page()
    {
        await using var context = await fx.NewAdminContextAsync(1920, 1000);
        var page = await context.NewPageAsync();

        // Make sure the total is 25*n + 1 and the single row on the last page is ours ("ŽŽŽ…" sorts last).
        var total = (await (await context.APIRequest.GetAsync("/api/customers?pageSize=1")).JsonAsync())!.Value.GetProperty("totalCount").GetInt32();
        var toCreate = ((1 - total) % 25 + 25) % 25;
        toCreate = toCreate == 0 ? 25 : toCreate;
        var ids = new List<int>();
        for (var i = 0; i < toCreate; i++)
        {
            var r = await context.APIRequest.PostAsync("/api/customers", new() { DataObject = new { companyName = $"ŽŽŽ BUG-005 {i:00}" } });
            ids.Add((await r.JsonAsync())!.Value.GetProperty("id").GetInt32());
        }

        try
        {
            await page.GotoAsync("/customers");
            await page.GetByTestId("customer-link").First.WaitForAsync();
            await page.Locator(".mud-table-pagination-actions button").Last.ClickAsync();
            await Expect(page.GetByTestId("customer-link")).ToHaveCountAsync(1);
            await page.GetByTestId("customer-delete").First.ClickAsync();
            await UiHelpers.ConfirmAsync(page, "Delete");

            await Expect(page.GetByTestId("customer-link")).ToHaveCountAsync(25);
            await Expect(page.GetByTestId("empty-state")).Not.ToBeVisibleAsync();
        }
        finally
        {
            foreach (var id in ids)
            {
                await context.APIRequest.DeleteAsync($"/api/customers/{id}");
            }
        }
    }

    [UiTheory]
    [InlineData(1280)]
    [InlineData(1024)]
    public async Task Bug006_row_actions_are_visible_without_horizontal_scrolling(int width)
    {
        await using var context = await fx.NewAdminContextAsync(width, 900);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/customers?search=s.r.o");
        await page.GetByTestId("customer-edit").First.WaitForAsync(new() { State = WaitForSelectorState.Attached });

        var right = await page.EvaluateAsync<double>("() => document.querySelector(\"[data-testid='customer-edit']\").getBoundingClientRect().right");
        Assert.True(right <= width, $"Edit button ends at {right}px, viewport is {width}px.");
    }

    [UiFact]
    public async Task Bug007_long_unbroken_name_does_not_overflow_detail_page()
    {
        await using var context = await fx.NewAdminContextAsync();
        var created = await context.APIRequest.PostAsync("/api/customers", new() { DataObject = new { companyName = new string('W', 200) } });
        var id = (await created.JsonAsync())!.Value.GetProperty("id").GetInt32();
        try
        {
            var page = await context.NewPageAsync();
            await page.GotoAsync($"/customers/{id}");
            await page.GetByTestId("customer-name").WaitForAsync();
            var overflow = await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - document.documentElement.clientWidth");
            Assert.True(overflow <= 1, $"Page scrolls horizontally by {overflow}px.");
        }
        finally
        {
            await context.APIRequest.DeleteAsync($"/api/customers/{id}");
        }
    }

    [UiFact]
    public async Task Bug011_foreign_link_to_logout_does_not_sign_out()
    {
        await using var context = await fx.NewAdminContextAsync();
        var evil = await context.NewPageAsync();
        await evil.SetContentAsync($"<a id='l' href='{UiTestSettings.BaseUrl}/account/logout'>win a prize</a>");
        await evil.ClickAsync("#l");
        await evil.WaitForTimeoutAsync(2500);

        Assert.Equal(200, (await context.APIRequest.GetAsync("/api/customers")).Status);
    }

    [UiFact]
    public async Task Bug016_status_multiselect_filters_the_list_without_looping()
    {
        await using var context = await fx.NewAdminContextAsync(1440, 900);
        var page = await context.NewPageAsync();
        var navigations = 0;
        page.FrameNavigated += (_, f) =>
        {
            if (f == page.MainFrame)
            {
                navigations++;
            }
        };
        var statuses = (await (await context.APIRequest.GetAsync("/api/project-statuses")).JsonAsync())!.Value.EnumerateArray().ToList();
        var lead = statuses.First(s => s.GetProperty("isDefault").GetBoolean());
        var expected = (await (await context.APIRequest.GetAsync($"/api/projects?statusId={lead.GetProperty("id").GetInt32()}")).JsonAsync())!.Value.GetProperty("totalCount").GetInt32();

        await page.GotoAsync("/projects");
        await page.GetByTestId("project-link").First.WaitForAsync();
        await UiHelpers.OpenSelectAsync(page, "project-status-filter");
        await page.Locator(".mud-popover-open .mud-list-item").Filter(new() { HasText = lead.GetProperty("name").GetString()! }).First.ClickAsync();
        await page.Keyboard.PressAsync("Escape");
        await page.WaitForTimeoutAsync(1500);
        navigations = 0;
        await page.WaitForTimeoutAsync(2000);

        Assert.True(navigations <= 1, $"{navigations} navigations in 2 s after picking one status (loop).");
        await Expect(page).ToHaveURLAsync(new Regex($"status={lead.GetProperty("id").GetInt32()}"));
        await Expect(page.GetByTestId("project-link")).ToHaveCountAsync(expected);
    }

    [UiFact]
    public async Task Bug017_selecting_a_closed_status_shows_its_projects()
    {
        await using var context = await fx.NewAdminContextAsync(1440, 900);
        var statuses = (await (await context.APIRequest.GetAsync("/api/project-statuses")).JsonAsync())!.Value.EnumerateArray().ToList();
        var closed = statuses.First(s => s.GetProperty("isClosed").GetBoolean() && s.GetProperty("projectCount").GetInt32() > 0);
        var id = closed.GetProperty("id").GetInt32();

        var api = (await (await context.APIRequest.GetAsync($"/api/projects?statusId={id}")).JsonAsync())!.Value.GetProperty("totalCount").GetInt32();
        Assert.True(api > 0, "GET /api/projects?statusId={closed} returns 0 because activeOnly defaults to true.");

        var page = await context.NewPageAsync();
        await page.GotoAsync($"/projects?status={id}");
        await page.WaitForTimeoutAsync(1500);
        await Expect(page.GetByTestId("project-link").First).ToBeVisibleAsync();
    }

    [UiTheory]
    [InlineData(1280)]
    [InlineData(1024)]
    [InlineData(768)]
    public async Task Bug018_all_kanban_columns_are_visible(int width)
    {
        await using var context = await fx.NewAdminContextAsync(width, 900);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/tasks?view=kanban");
        await page.GetByTestId("kanban-card").First.WaitForAsync();
        var right = await page.EvaluateAsync<double>("() => document.querySelector(\"[data-testid='kanban-column-Done']\").getBoundingClientRect().right");
        Assert.True(right <= width, $"Done column ends at {right}px, viewport is {width}px.");
    }

    [UiFact]
    public async Task Bug019_patch_status_without_status_is_rejected()
    {
        await using var context = await fx.NewAdminContextAsync();
        var project = (await (await context.APIRequest.PostAsync("/api/projects", new() { DataObject = new { name = "BUG-019", customerId = 4 } })).JsonAsync())!.Value.GetProperty("id").GetInt32();
        try
        {
            var task = (await (await context.APIRequest.PostAsync("/api/tasks", new() { DataObject = new { title = "BUG-019", projectId = project, status = "Done" } })).JsonAsync())!.Value.GetProperty("id").GetInt32();
            var r = await context.APIRequest.PatchAsync($"/api/tasks/{task}/status", new() { DataObject = new { } });
            var after = (await (await context.APIRequest.GetAsync($"/api/tasks/{task}")).JsonAsync())!.Value.GetProperty("status").GetString();
            Assert.Equal(400, r.Status);
            Assert.Equal("Done", after);
        }
        finally
        {
            await context.APIRequest.DeleteAsync($"/api/projects/{project}");
        }
    }

    [UiFact]
    public async Task Bug020_closed_status_cannot_be_default()
    {
        await using var context = await fx.NewAdminContextAsync();
        var statuses = (await (await context.APIRequest.GetAsync("/api/project-statuses")).JsonAsync())!.Value.EnumerateArray().ToList();
        var def = statuses.First(s => s.GetProperty("isDefault").GetBoolean()).GetProperty("id").GetInt32();
        var closed = statuses.First(s => s.GetProperty("isClosed").GetBoolean()).GetProperty("id").GetInt32();
        var r = await context.APIRequest.PostAsync($"/api/project-statuses/{closed}/default");
        if (r.Status == 200)
        {
            await context.APIRequest.PostAsync($"/api/project-statuses/{def}/default"); // restore
        }

        Assert.InRange(r.Status, 400, 409);
    }
}
