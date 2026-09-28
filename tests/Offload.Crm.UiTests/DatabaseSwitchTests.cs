using Microsoft.Playwright;
using Offload.Crm.UiTests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Offload.Crm.UiTests;

/// <summary>
/// D-26: Settings → Database switches the whole app between the production and the test database.
/// Requires the app to run with ConnectionStrings:Test configured (Development default). Always restores the database that was active.
/// </summary>
[Collection(UiCollection.Name)]
public sealed class DatabaseSwitchTests(PlaywrightFixture fx)
{
    [UiFact]
    public async Task Switching_database_isolates_data_marks_every_tab_and_switches_back()
    {
        await using var context = await fx.NewAdminContextAsync(1440, 900);
        var page = await context.NewPageAsync();
        var otherTab = await context.NewPageAsync();
        var errors = UiHelpers.CollectConsoleErrors(page);
        var name = UiHelpers.Unique("UI db-isolation customer");
        int? customerId = null;

        // Works from whichever database is active and always restores it (the rest of the suite needs the demo data).
        await OpenDatabaseTabAsync(page);
        var start = await ActiveAsync(page);
        var other = start == "test" ? "production" : "test";
        try
        {
            customerId = await CreateCustomerAsync(context, name);
            await otherTab.GotoAsync("/settings");
            await otherTab.GetByTestId("tab-statuses").WaitForAsync();
            await ExpectBadgeAsync(otherTab, start);

            // Switch: confirmation, reload, badge; the other open tab follows automatically.
            await SwitchAsync(page, other);
            await ExpectBadgeAsync(page, other);
            await ExpectBadgeAsync(otherTab, other, timeout: 15000);
            await page.GotoAsync("/settings");
            await page.GetByRole(AriaRole.Tab, new() { Name = "General" }).ClickAsync();
            await Expect(page.GetByTestId("app-database")).ToContainTextAsync(other == "test" ? "(Test)" : "(Production)");

            // Data created in one database is not in the other.
            Assert.Equal(0, await CountCustomersAsync(context, name));

            await OpenDatabaseTabAsync(page);
            await SwitchAsync(page, start);
            await ExpectBadgeAsync(page, start);
            Assert.Equal(1, await CountCustomersAsync(context, name));
            Assert.Empty(errors);
        }
        finally
        {
            await OpenDatabaseTabAsync(page);
            if (await ActiveAsync(page) != start)
            {
                await SwitchAsync(page, start);
            }

            if (customerId is { } id)
            {
                await context.APIRequest.DeleteAsync($"/api/customers/{id}");
            }
        }
    }

    [UiFact]
    public async Task Cancelling_the_confirmation_keeps_the_current_database()
    {
        await using var context = await fx.NewAdminContextAsync();
        var page = await context.NewPageAsync();
        await OpenDatabaseTabAsync(page);
        var start = await ActiveAsync(page);

        await page.GetByTestId($"db-switch-{(start == "test" ? "production" : "test")}").ClickAsync();
        await UiHelpers.ConfirmAsync(page, "Cancel");

        await Expect(page.GetByTestId($"db-target-{start}").GetByTestId("db-active-chip")).ToBeVisibleAsync();
        await ExpectBadgeAsync(page, start);
    }

    private static async Task<string> ActiveAsync(IPage page) =>
        await page.GetByTestId("db-target-test").GetByTestId("db-active-chip").CountAsync() > 0 ? "test" : "production";

    private static Task ExpectBadgeAsync(IPage page, string target, float? timeout = null) =>
        Expect(page.GetByTestId("test-db-badge")).ToHaveCountAsync(target == "test" ? 1 : 0, new() { Timeout = timeout });

    private static async Task OpenDatabaseTabAsync(IPage page)
    {
        await page.GotoAsync("/settings");
        await page.GetByRole(AriaRole.Tab, new() { Name = "Database" }).ClickAsync();
        await page.GetByTestId("db-targets").WaitForAsync();
    }

    private static async Task SwitchAsync(IPage page, string target)
    {
        await page.GetByTestId($"db-switch-{target}").ClickAsync();
        var label = char.ToUpperInvariant(target[0]) + target[1..];
        // Confirming switches the database and force-reloads the page.
        var reloaded = new TaskCompletionSource();
        void OnLoad(object? sender, IPage p) => reloaded.TrySetResult();
        page.Load += OnLoad;
        try
        {
            await UiHelpers.ConfirmAsync(page, $"Switch to {label}");
            await reloaded.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }
        finally
        {
            page.Load -= OnLoad;
        }
        await OpenDatabaseTabAsync(page);
        await Expect(page.GetByTestId($"db-target-{target}").GetByTestId("db-active-chip")).ToBeVisibleAsync();
    }

    private static async Task<int> CreateCustomerAsync(IBrowserContext context, string name)
    {
        var response = await context.APIRequest.PostAsync("/api/customers", new() { DataObject = new { companyName = name, status = "Lead" } });
        Assert.Equal(201, response.Status);
        return (await response.JsonAsync())!.Value.GetProperty("id").GetInt32();
    }

    private static async Task<int> CountCustomersAsync(IBrowserContext context, string name)
    {
        var response = await context.APIRequest.GetAsync($"/api/customers?search={Uri.EscapeDataString(name)}");
        Assert.Equal(200, response.Status);
        return (await response.JsonAsync())!.Value.GetProperty("totalCount").GetInt32();
    }
}
