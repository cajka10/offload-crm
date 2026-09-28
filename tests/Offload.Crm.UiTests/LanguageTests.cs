using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Offload.Crm.UiTests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Offload.Crm.UiTests;

/// <summary>D-29: English/Slovak switch in the app bar (cookie per browser, so it never leaks into other tests).</summary>
[Collection(UiCollection.Name)]
public sealed partial class LanguageTests(PlaywrightFixture fx)
{
    [UiFact]
    public async Task Language_dropdown_switches_to_slovak_keeps_the_page_and_survives_reload()
    {
        await using var context = await fx.NewAdminContextAsync(1440, 900);
        var page = await context.NewPageAsync();
        var errors = UiHelpers.CollectConsoleErrors(page);
        await page.GotoAsync("/customers?status=Active");
        await Expect(page.GetByTestId("language-current")).ToContainTextAsync("EN");

        await page.GetByTestId("language-current").ClickAsync();
        await page.GetByTestId("language-sk").ClickAsync();

        await Expect(page.GetByTestId("language-current")).ToContainTextAsync("SK");
        await Expect(page).ToHaveURLAsync(StatusActiveUrl()); // same page and filters
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Zákazníci" })).ToBeVisibleAsync();
        await Expect(page.GetByTestId("nav-projects")).ToContainTextAsync("Projekty");
        await Expect(page.GetByTestId("customer-new")).ToHaveTextAsync("Nový zákazník");
        Assert.Equal("sk", await page.GetAttributeAsync("html", "lang"));

        await page.ReloadAsync();
        await Expect(page.GetByTestId("customer-new")).ToHaveTextAsync("Nový zákazník");

        // Validation messages from the services are translated too.
        await page.GetByTestId("customer-new").ClickAsync();
        await page.GetByTestId("dialog-save").ClickAsync();
        await Expect(page.GetByTestId("form-error-summary")).ToHaveTextAsync("Opravte zvýraznené polia.");
        await Expect(page.GetByText("Názov spoločnosti je povinný.").First).ToBeVisibleAsync();
        await page.GetByTestId("dialog-cancel").ClickAsync();

        // Formatting stays Slovak/EUR in both languages; switch back.
        await page.GetByTestId("language-current").ClickAsync();
        await page.GetByTestId("language-en").ClickAsync();
        await Expect(page.GetByTestId("customer-new")).ToHaveTextAsync("New customer");
        Assert.Empty(errors);
    }

    [UiFact]
    public async Task Login_page_offers_the_language_and_the_choice_is_kept_after_sign_in()
    {
        await using var context = await fx.NewAnonymousContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync("/account/login");

        await page.GetByTestId("account-language-sk").ClickAsync();
        await Expect(page.GetByTestId("login-submit")).ToHaveTextAsync("Prihlásiť sa");
        await page.GetByTestId("login-submit").ClickAsync();
        await Expect(page.GetByText("Zadajte e-mail.")).ToBeVisibleAsync();

        await UiHelpers.LoginAsync(page, UiTestSettings.AdminEmail, UiTestSettings.AdminPassword, path: "/account/login");
        await Expect(page.GetByTestId("nav-customers")).ToContainTextAsync("Zákazníci");
    }

    [UiFact]
    public async Task Culture_endpoint_only_redirects_within_the_app()
    {
        await using var context = await fx.NewAnonymousContextAsync();
        foreach (var target in new[] { "https://evil.example", "//evil.example", "/\\evil.example", "/	/evil.example" })
        {
            var response = await context.APIRequest.GetAsync(
                $"/culture/set?culture=sk&redirectUri={Uri.EscapeDataString(target)}", new() { MaxRedirects = 0 });
            Assert.Equal(302, response.Status);
            Assert.Equal("/", response.Headers["location"]);
        }
    }

    private static Regex StatusActiveUrl() => StatusActiveRegex();

    [GeneratedRegex(@"/customers\?status=Active$")]
    private static partial Regex StatusActiveRegex();
}
