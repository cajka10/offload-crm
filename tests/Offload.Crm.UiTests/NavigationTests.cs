using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Offload.Crm.UiTests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Offload.Crm.UiTests;

[Collection(UiCollection.Name)]
public sealed class NavigationTests(PlaywrightFixture fx)
{
    [UiTheory]
    [InlineData("nav-dashboard", "/", "Dashboard")]
    [InlineData("nav-customers", "/customers", "Customers")]
    [InlineData("nav-projects", "/projects", "Projects")]
    [InlineData("nav-tasks", "/tasks", "Tasks")]
    [InlineData("nav-activities", "/activities", "Activities")]
    [InlineData("nav-settings", "/settings", "Settings")]
    public async Task Every_menu_item_navigates_to_its_page_without_console_errors(string navTestId, string path, string heading)
    {
        await using var context = await fx.NewAdminContextAsync();
        var page = await context.NewPageAsync();
        var errors = UiHelpers.CollectConsoleErrors(page);

        // Start somewhere else so the click really navigates.
        await page.GotoAsync(path == "/customers" ? "/settings" : "/customers");
        await UiHelpers.WaitForAppAsync(page);

        await page.GetByTestId(navTestId).ClickAsync();

        await Expect(page).ToHaveURLAsync(UiTestSettings.BaseUrl + path);
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = heading, Level = 1 })).ToBeVisibleAsync();
        await Expect(page).ToHaveTitleAsync(new Regex(Regex.Escape(heading) + " · Offload CRM"));
        await Expect(page.GetByTestId(navTestId).Locator("a")).ToHaveClassAsync(new Regex(@"\bactive\b"));
        Assert.Empty(errors);
    }

    [UiTheory]
    [InlineData("/this-route-does-not-exist")]
    [InlineData("/customers/abc")]
    [InlineData("/customers/1/nope")]
    public async Task Unknown_route_shows_friendly_not_found_page(string path)
    {
        await using var context = await fx.NewAdminContextAsync();
        var page = await context.NewPageAsync();

        var response = await page.GotoAsync(path);

        Assert.Equal(404, response!.Status);
        await Expect(page.GetByTestId("not-found")).ToBeVisibleAsync();
        await Expect(page.GetByText("Page not found")).ToBeVisibleAsync();
    }

    [UiFact]
    public async Task Nonexistent_customer_id_shows_not_found_state()
    {
        await using var context = await fx.NewAdminContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync("/customers/987654321");

        await Expect(page.GetByTestId("not-found")).ToBeVisibleAsync();
        await Expect(page.GetByText("Customer not found")).ToBeVisibleAsync();
    }

    [UiTheory]
    [InlineData(1920, 1080)]
    [InlineData(1280, 800)]
    [InlineData(1024, 768)]
    [InlineData(768, 1024)]
    public async Task Customers_page_has_no_horizontal_page_overflow_at_common_widths(int width, int height)
    {
        await using var context = await fx.NewAdminContextAsync(width, height);
        var page = await context.NewPageAsync();
        var errors = UiHelpers.CollectConsoleErrors(page);

        await page.GotoAsync("/customers");
        await Expect(page.GetByTestId("customer-link").First).ToBeVisibleAsync();

        var overflow = await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - document.documentElement.clientWidth");
        Assert.True(overflow <= 1, $"Page scrolls horizontally by {overflow}px at {width}px.");
        await Expect(page.GetByTestId("customer-new")).ToBeInViewportAsync();
        Assert.Empty(errors);
    }

    [UiFact]
    public async Task Drawer_is_collapsed_below_960px_and_opens_with_menu_button()
    {
        await using var context = await fx.NewAdminContextAsync(768, 1024);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/customers");
        await UiHelpers.WaitForAppAsync(page);

        await page.GetByTestId("nav-toggle").ClickAsync();
        await Expect(page.GetByTestId("nav-projects")).ToBeInViewportAsync();
        await page.GetByTestId("nav-projects").ClickAsync();
        await Expect(page).ToHaveURLAsync(UiTestSettings.BaseUrl + "/projects");
    }
}
