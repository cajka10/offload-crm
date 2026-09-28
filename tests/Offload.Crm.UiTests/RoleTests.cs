using Microsoft.Playwright;
using Offload.Crm.UiTests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Offload.Crm.UiTests;

/// <summary>D-27: what an Administrator vs. a User sees in Settings (the server also enforces it, see Offload.Crm.Tests).</summary>
[Collection(UiCollection.Name)]
public sealed class RoleTests(PlaywrightFixture fx)
{
    [UiFact]
    public async Task User_sees_read_only_statuses_and_no_admin_tabs()
    {
        await using var context = await fx.NewAnonymousContextAsync();
        var page = await context.NewPageAsync();
        var errors = UiHelpers.CollectConsoleErrors(page);
        await UiHelpers.LoginAsync(page, "petr.dvorak@offload.local", "Demo123!");

        await page.GotoAsync("/settings");
        await Expect(page.GetByTestId("statuses-readonly")).ToBeVisibleAsync();
        await Expect(page.GetByTestId("status-row").First).ToBeVisibleAsync();
        await Expect(page.GetByTestId("status-new")).ToHaveCountAsync(0);
        await Expect(page.GetByTestId("status-edit")).ToHaveCountAsync(0);
        await Expect(page.GetByTestId("status-delete")).ToHaveCountAsync(0);
        await Expect(page.GetByRole(AriaRole.Tab, new() { Name = "General" })).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Tab, new() { Name = "Users" })).ToHaveCountAsync(0);
        await Expect(page.GetByRole(AriaRole.Tab, new() { Name = "Database" })).ToHaveCountAsync(0);

        // The API refuses too, not just the UI.
        Assert.Equal(403, (await context.APIRequest.GetAsync("/api/users")).Status);
        Assert.Empty(errors);
    }

    [UiFact]
    public async Task Admin_sees_roles_and_cant_remove_their_own_admin_role()
    {
        await using var context = await fx.NewAdminContextAsync(1440, 900);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/settings");
        await Expect(page.GetByTestId("status-new")).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Tab, new() { Name = "Users" }).ClickAsync();

        var me = page.GetByTestId("user-row").Filter(new() { HasText = "(you)" });
        await Expect(me.GetByTestId("user-role")).ToHaveTextAsync("Administrator");
        await Expect(page.GetByTestId("user-row").Filter(new() { HasText = "petr.dvorak@offload.local" }).GetByTestId("user-role")).ToHaveTextAsync("User");

        await me.GetByTestId("user-edit").ClickAsync();
        await Expect(UiHelpers.Field(page, "user-isAdmin")).ToBeDisabledAsync();
        await Expect(page.GetByTestId("user-dialog")).ToContainTextAsync("You can't remove the administrator role from your own account.");
    }
}
