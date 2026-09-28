using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Offload.Crm.UiTests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Offload.Crm.UiTests;

[Collection(UiCollection.Name)]
public sealed class ContactTests(PlaywrightFixture fx)
{
    [UiFact]
    public async Task Contacts_add_edit_delete_and_customer_search_by_contact_name()
    {
        await using var context = await fx.NewAdminContextAsync();
        var page = await context.NewPageAsync();
        var errors = UiHelpers.CollectConsoleErrors(page);
        var company = UiHelpers.Unique("UI Contacts Co");
        var lastName = "Řezníčková" + Guid.NewGuid().ToString("N")[..5];

        // Fresh customer for the test
        await page.GotoAsync("/customers");
        await page.GetByTestId("customer-new").ClickAsync();
        await UiHelpers.Field(page, "customer-companyName").FillAsync(company);
        await page.GetByTestId("dialog-save").ClickAsync();
        await Expect(page.GetByTestId("customer-name")).ToHaveTextAsync(company);
        var detailUrl = page.Url;

        // Contacts tab: empty state
        await page.GetByRole(AriaRole.Tab, new() { NameRegex = new Regex("^Contacts") }).ClickAsync();
        await Expect(page.GetByTestId("contacts-empty")).ToBeVisibleAsync();

        // Validation: last name required
        await page.GetByTestId("contact-new").ClickAsync();
        await UiHelpers.Field(page, "contact-firstName").FillAsync("Eliška");
        await UiHelpers.Field(page, "contact-email").FillAsync("bad@");
        await page.GetByTestId("dialog-save").ClickAsync();
        await Expect(UiHelpers.Dialog(page).GetByText("Last name is required.")).ToBeVisibleAsync();
        await Expect(UiHelpers.Dialog(page).GetByText("Enter a valid e-mail address.")).ToBeVisibleAsync();

        // Add
        await UiHelpers.Field(page, "contact-lastName").FillAsync(lastName);
        await UiHelpers.Field(page, "contact-email").FillAsync("eliska@example.cz");
        await page.GetByTestId("dialog-save").ClickAsync();
        await Expect(page.GetByTestId("contact-name")).ToHaveTextAsync($"Eliška {lastName}");
        await Expect(page.GetByRole(AriaRole.Tab, new() { Name = "Contacts (1)" })).ToBeVisibleAsync();

        // Edit
        await page.GetByTestId("contact-edit").ClickAsync();
        await UiHelpers.Field(page, "contact-position").FillAsync("CTO");
        await page.GetByTestId("dialog-save").ClickAsync();
        await Expect(page.GetByTestId("contacts-table").GetByText("CTO")).ToBeVisibleAsync();

        // Customer search finds the customer by the contact's name ("Last First" too)
        await page.GotoAsync("/customers?search=" + Uri.EscapeDataString($"{lastName} Eliška"));
        await Expect(page.GetByTestId("customer-link")).ToHaveTextAsync(company);

        // Delete contact
        await page.GotoAsync(detailUrl);
        await page.GetByRole(AriaRole.Tab, new() { NameRegex = new Regex("^Contacts") }).ClickAsync();
        await page.GetByTestId("contact-delete").ClickAsync();
        await UiHelpers.ConfirmAsync(page, "Delete");
        await Expect(page.GetByTestId("contacts-empty")).ToBeVisibleAsync();

        // Clean up the customer
        await page.GetByTestId("customer-delete").ClickAsync();
        await UiHelpers.ConfirmAsync(page, "Delete");
        await Expect(page).ToHaveURLAsync(UiTestSettings.BaseUrl + "/customers");
        Assert.Empty(errors);
    }
}
