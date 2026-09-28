using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Offload.Crm.UiTests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Offload.Crm.UiTests;

[Collection(UiCollection.Name)]
public sealed class AuthTests(PlaywrightFixture fx)
{
    private const string DemoUser = "jana.novakova@offload.local";
    private const string DemoPassword = "Demo123!";

    [UiFact]
    public async Task Login_with_valid_credentials_shows_app_shell_and_user_name()
    {
        await using var context = await fx.NewAnonymousContextAsync();
        var page = await context.NewPageAsync();
        var errors = UiHelpers.CollectConsoleErrors(page);

        await UiHelpers.LoginAsync(page, UiTestSettings.AdminEmail, UiTestSettings.AdminPassword);

        await Expect(page).ToHaveURLAsync(UiTestSettings.BaseUrl + "/");
        await Expect(page.GetByTestId("current-user")).ToBeVisibleAsync();
        await Expect(page.GetByTestId("logout")).ToBeVisibleAsync();
        Assert.Empty(errors);
    }

    [UiFact]
    public async Task Login_with_wrong_password_shows_friendly_error()
    {
        await using var context = await fx.NewAnonymousContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync("/account/login");

        // A demo user, so the admin account never gets locked out by the test suite.
        await page.GetByTestId("login-email").FillAsync(DemoUser);
        await page.GetByTestId("login-password").FillAsync("definitely-wrong");
        await page.GetByTestId("login-submit").ClickAsync();

        await Expect(page.GetByTestId("login-error")).ToHaveTextAsync("Invalid e-mail or password.");
        await Expect(page).ToHaveURLAsync(new Regex("/account/login"));

        // A successful sign-in resets the failed-attempt counter, so repeated runs never lock the user out.
        await UiHelpers.LoginAsync(page, DemoUser, DemoPassword);
        await Expect(page.GetByTestId("current-user")).ToBeVisibleAsync();
    }

    [UiFact]
    public async Task Login_with_unknown_user_shows_same_error_as_wrong_password()
    {
        await using var context = await fx.NewAnonymousContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync("/account/login");
        await page.GetByTestId("login-email").FillAsync("nobody-" + Guid.NewGuid().ToString("N")[..6] + "@offload.local");
        await page.GetByTestId("login-password").FillAsync("Whatever1");
        await page.GetByTestId("login-submit").ClickAsync();

        await Expect(page.GetByTestId("login-error")).ToHaveTextAsync("Invalid e-mail or password.");
    }

    [UiFact]
    public async Task Login_with_empty_fields_shows_inline_validation()
    {
        await using var context = await fx.NewAnonymousContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync("/account/login");
        await page.GetByTestId("login-submit").ClickAsync();

        await Expect(page.GetByText("Enter your e-mail.")).ToBeVisibleAsync();
        await Expect(page.GetByText("Enter your password.")).ToBeVisibleAsync();
    }

    [UiFact]
    public async Task Deep_link_while_logged_out_redirects_to_login_and_back_after_sign_in()
    {
        await using var context = await fx.NewAnonymousContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync("/customers?search=a");
        await Expect(page).ToHaveURLAsync(new Regex("/account/login\\?ReturnUrl=%2Fcustomers%3Fsearch%3Da", RegexOptions.IgnoreCase));

        await page.GetByTestId("login-email").FillAsync(UiTestSettings.AdminEmail);
        await page.GetByTestId("login-password").FillAsync(UiTestSettings.AdminPassword);
        await page.GetByTestId("login-submit").ClickAsync();

        await Expect(page).ToHaveURLAsync(UiTestSettings.BaseUrl + "/customers?search=a");
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Customers" })).ToBeVisibleAsync();
    }

    [UiTheory]
    [InlineData("https://evil.example.com")]
    [InlineData("//evil.example.com")]
    [InlineData("/\\evil.example.com")]
    [InlineData("/%09/evil.example.com")]
    public async Task Login_return_url_to_another_host_is_ignored(string returnUrl)
    {
        await using var context = await fx.NewAnonymousContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync("/account/login?ReturnUrl=" + Uri.EscapeDataString(returnUrl));
        await page.GetByTestId("login-email").FillAsync(UiTestSettings.AdminEmail);
        await page.GetByTestId("login-password").FillAsync(UiTestSettings.AdminPassword);
        await page.GetByTestId("login-submit").ClickAsync();
        await page.WaitForURLAsync(url => !url.Contains("/account/login", StringComparison.OrdinalIgnoreCase));

        Assert.StartsWith(UiTestSettings.BaseUrl + "/", page.Url);
    }

    [UiFact]
    public async Task Logout_then_back_button_does_not_show_app_pages()
    {
        await using var context = await fx.NewAnonymousContextAsync();
        var page = await context.NewPageAsync();
        // A demo user: since D-19 a logout rotates the security stamp and would end every admin session (incl. a human tester's).
        await UiHelpers.LoginAsync(page, "petr.dvorak@offload.local", "Demo123!");
        await page.GotoAsync("/customers");
        await Expect(page.GetByTestId("customer-table")).ToBeVisibleAsync();

        await page.GetByTestId("logout").ClickAsync();
        await page.WaitForURLAsync(new Regex("/account/login"));

        await page.GoBackAsync();
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Either still/again on the login page, or no customer data visible.
        await Expect(page.GetByTestId("customer-table")).Not.ToBeVisibleAsync();
        await Expect(page).ToHaveURLAsync(new Regex("/account/login"));

        // The API is also closed after logout.
        var api = await context.APIRequest.GetAsync("/api/customers");
        Assert.Equal(401, api.Status);
    }

    [UiFact]
    public async Task Api_without_auth_cookie_returns_401()
    {
        await using var context = await fx.NewAnonymousContextAsync();
        var response = await context.APIRequest.GetAsync("/api/customers", new() { MaxRedirects = 0 });
        Assert.Equal(401, response.Status);
    }
}
