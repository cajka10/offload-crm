using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Offload.Crm.UiTests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Offload.Crm.UiTests;

[Collection(UiCollection.Name)]
public sealed class CustomerTests(PlaywrightFixture fx)
{
    [UiFact]
    public async Task Customer_list_shows_seeded_rows_and_pager()
    {
        await using var context = await fx.NewAdminContextAsync();
        var page = await context.NewPageAsync();
        var errors = UiHelpers.CollectConsoleErrors(page);

        await page.GotoAsync("/customers");

        await Expect(page.GetByTestId("customer-link").First).ToBeVisibleAsync();
        await Expect(page.Locator(".mud-table-pagination")).ToBeVisibleAsync();
        Assert.Empty(errors);
    }

    [UiFact]
    public async Task Customer_crud_happy_path_create_edit_search_delete()
    {
        await using var context = await fx.NewAdminContextAsync();
        var page = await context.NewPageAsync();
        var errors = UiHelpers.CollectConsoleErrors(page);
        var name = UiHelpers.Unique("UI Test Žluťoučký kůň");

        // Create
        await page.GotoAsync("/customers");
        await page.GetByTestId("customer-new").ClickAsync();
        var dialog = UiHelpers.Dialog(page);
        await Expect(dialog).ToBeVisibleAsync();
        await UiHelpers.Field(page, "customer-companyName").FillAsync(name);
        await UiHelpers.Field(page, "customer-email").FillAsync("ui-test@example.cz");
        await UiHelpers.Field(page, "customer-website").FillAsync("example.cz");
        await UiHelpers.Field(page, "customer-city").FillAsync("Brno");
        await page.GetByTestId("dialog-save").ClickAsync();

        // Lands on the detail page
        await Expect(page).ToHaveURLAsync(new Regex("/customers/\\d+$"));
        await Expect(page.GetByTestId("customer-name")).ToHaveTextAsync(name);
        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "https://example.cz" })).ToBeVisibleAsync();
        var detailUrl = page.Url;

        // Edit
        await page.GetByTestId("customer-edit").ClickAsync();
        await Expect(UiHelpers.Dialog(page)).ToBeVisibleAsync();
        await UiHelpers.Field(page, "customer-city").FillAsync("Ostrava");
        await page.GetByTestId("dialog-save").ClickAsync();
        await Expect(UiHelpers.Dialog(page)).Not.ToBeVisibleAsync();
        await Expect(page.GetByText("Ostrava").First).ToBeVisibleAsync();

        // Search in the list (diacritics, different case)
        await page.GotoAsync("/customers?search=" + Uri.EscapeDataString(name.ToUpperInvariant()));
        await Expect(page.GetByTestId("customer-link")).ToHaveCountAsync(1);
        await Expect(page.GetByTestId("customer-link")).ToHaveTextAsync(name);

        // Delete from the detail page
        await page.GotoAsync(detailUrl);
        await page.GetByTestId("customer-delete").ClickAsync();
        await UiHelpers.ConfirmAsync(page, "Delete");
        await Expect(page).ToHaveURLAsync(UiTestSettings.BaseUrl + "/customers");
        await Expect(page.GetByText($"Customer \"{name}\" deleted.")).ToBeVisibleAsync();

        await page.GotoAsync(detailUrl);
        await Expect(page.GetByText("Customer not found")).ToBeVisibleAsync();
        Assert.Empty(errors);
    }

    [UiFact]
    public async Task Create_customer_validation_shows_inline_errors()
    {
        await using var context = await fx.NewAdminContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync("/customers");
        await page.GetByTestId("customer-new").ClickAsync();
        await Expect(UiHelpers.Dialog(page)).ToBeVisibleAsync();

        await UiHelpers.Field(page, "customer-companyName").FillAsync("   ");
        await UiHelpers.Field(page, "customer-email").FillAsync("not-an-email");
        await UiHelpers.Field(page, "customer-website").FillAsync("not a url");
        await UiHelpers.Field(page, "customer-phone").FillAsync("call me");
        await page.GetByTestId("dialog-save").ClickAsync();

        var dialog = UiHelpers.Dialog(page);
        await Expect(dialog.GetByText("Company name is required.")).ToBeVisibleAsync();
        await Expect(dialog.GetByText("Enter a valid e-mail address.")).ToBeVisibleAsync();
        await Expect(dialog.GetByText("Enter a valid web address, e.g. https://example.com.")).ToBeVisibleAsync();
        await Expect(dialog.GetByText("Phone may contain only digits, spaces and + ( ) -.")).ToBeVisibleAsync();
        await Expect(page.GetByTestId("form-error-summary")).ToBeVisibleAsync();

        await page.GetByTestId("dialog-cancel").ClickAsync();
        await Expect(UiHelpers.Dialog(page)).Not.ToBeVisibleAsync();
    }

    [UiFact]
    public async Task Duplicate_registration_no_shows_field_error()
    {
        await using var context = await fx.NewAdminContextAsync();
        var page = await context.NewPageAsync();

        // Seeded customer "Česká logistika a.s." has IČO 63998530; enter it with spaces.
        await page.GotoAsync("/customers");
        await page.GetByTestId("customer-new").ClickAsync();
        await UiHelpers.Field(page, "customer-companyName").FillAsync(UiHelpers.Unique("UI dup IČO"));
        await UiHelpers.Field(page, "customer-registrationNo").FillAsync(" 639 985 30 ");
        await page.GetByTestId("dialog-save").ClickAsync();

        await Expect(UiHelpers.Dialog(page).GetByText("A customer with this registration no. (IČO) already exists.")).ToBeVisibleAsync();
        await page.GetByTestId("dialog-cancel").ClickAsync();
    }

    [UiFact]
    public async Task Deleting_customer_with_projects_is_blocked_with_message()
    {
        await using var context = await fx.NewAdminContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync("/customers?search=" + Uri.EscapeDataString("Česká logistika"));
        await Expect(page.GetByTestId("customer-link")).ToHaveCountAsync(1);
        await page.GetByTestId("customer-delete").First.ClickAsync();
        await UiHelpers.ConfirmAsync(page, "Delete");

        await Expect(page.GetByText(new Regex("Customer has \\d+ projects?\\. Delete or reassign them first\\."))).ToBeVisibleAsync();
        await Expect(page.GetByTestId("customer-link")).ToHaveCountAsync(1);
    }

    [UiFact]
    public async Task Cancel_in_delete_confirmation_keeps_customer()
    {
        await using var context = await fx.NewAdminContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync("/customers?search=" + Uri.EscapeDataString("Moravské vinařství"));
        await Expect(page.GetByTestId("customer-link")).ToHaveCountAsync(1);
        await page.GetByTestId("customer-delete").First.ClickAsync();
        await UiHelpers.ConfirmAsync(page, "Cancel");

        await Expect(page.GetByTestId("customer-link")).ToHaveCountAsync(1);
    }

    [UiFact]
    public async Task Search_with_no_results_shows_no_results_state_and_clear_filters()
    {
        await using var context = await fx.NewAdminContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync("/customers?search=zzz-no-such-customer-" + Guid.NewGuid().ToString("N")[..6]);
        await Expect(page.GetByTestId("no-results")).ToBeVisibleAsync();

        await page.GetByRole(AriaRole.Button, new() { Name = "Clear filters" }).ClickAsync();
        await Expect(page.GetByTestId("customer-link").First).ToBeVisibleAsync();
        await Expect(page).ToHaveURLAsync(UiTestSettings.BaseUrl + "/customers");
    }

    [UiFact]
    public async Task Html_in_customer_fields_is_rendered_as_text()
    {
        await using var context = await fx.NewAdminContextAsync();
        var page = await context.NewPageAsync();
        var dialogs = 0;
        page.Dialog += async (_, d) =>
        {
            dialogs++;
            await d.DismissAsync();
        };
        var name = "<img src=x onerror=alert(1)><b>UI XSS</b> " + Guid.NewGuid().ToString("N")[..6];

        await page.GotoAsync("/customers");
        await page.GetByTestId("customer-new").ClickAsync();
        await UiHelpers.Field(page, "customer-companyName").FillAsync(name);
        await UiHelpers.Field(page, "customer-notes").FillAsync("<script>alert(2)</script>");
        await page.GetByTestId("dialog-save").ClickAsync();

        await Expect(page.GetByTestId("customer-name")).ToHaveTextAsync(name);
        Assert.Equal(0, await page.Locator("h1 img, h1 b").CountAsync());

        // Delete it again (also exercises the confirmation + snackbar with HTML in the name).
        await page.GetByTestId("customer-delete").ClickAsync();
        await UiHelpers.ConfirmAsync(page, "Delete");
        await Expect(page).ToHaveURLAsync(UiTestSettings.BaseUrl + "/customers");
        await page.WaitForTimeoutAsync(500);
        Assert.Equal(0, dialogs);
    }

    [UiFact]
    public async Task Status_filter_and_search_are_kept_in_the_url()
    {
        await using var context = await fx.NewAdminContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync("/customers?status=Active");
        await Expect(page.GetByTestId("customer-link").First).ToBeVisibleAsync();
        var chips = await page.GetByTestId("customer-status").AllInnerTextsAsync();
        Assert.NotEmpty(chips);
        Assert.All(chips, c => Assert.Equal("Active", c.Trim()));

        await UiHelpers.Field(page, "customer-search").FillAsync("a");
        await Expect(page).ToHaveURLAsync(new Regex("search=a"));
        await Expect(page).ToHaveURLAsync(new Regex("status=Active"));
    }
}
