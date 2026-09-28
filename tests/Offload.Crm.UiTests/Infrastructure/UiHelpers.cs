using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Offload.Crm.UiTests.Infrastructure;

public static class UiHelpers
{
    /// <summary>Signs in on the static SSR login page and waits until the interactive app shell is rendered.</summary>
    public static async Task LoginAsync(IPage page, string email, string password, string? path = null)
    {
        await page.GotoAsync(path ?? "/account/login");
        await page.GetByTestId("login-email").FillAsync(email);
        await page.GetByTestId("login-password").FillAsync(password);
        await page.GetByTestId("login-submit").ClickAsync();
        await page.WaitForURLAsync(url => !url.Contains("/account/login", StringComparison.OrdinalIgnoreCase));
        await WaitForAppAsync(page);
    }

    /// <summary>Waits until the Interactive Server app shell has rendered (the nav menu is present).</summary>
    public static Task WaitForAppAsync(IPage page) =>
        page.GetByTestId("nav-customers").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 15000 });

    /// <summary>Unique text so tests can run repeatedly against the same (dev) DB.</summary>
    public static string Unique(string prefix) => $"{prefix} {DateTime.UtcNow:HHmmss}-{Guid.NewGuid().ToString("N")[..6]}";

    /// <summary>Collects console errors and uncaught page errors.</summary>
    public static List<string> CollectConsoleErrors(IPage page)
    {
        var errors = new List<string>();
        page.Console += (_, msg) =>
        {
            if (msg.Type == "error")
            {
                errors.Add(msg.Text);
            }
        };
        page.PageError += (_, err) => errors.Add("pageerror: " + err);
        return errors;
    }

    /// <summary>The top-most open MudBlazor dialog.</summary>
    public static ILocator Dialog(IPage page) => page.Locator(".mud-dialog").Last;

    /// <summary>The input element inside a MudBlazor field marked with data-testid.</summary>
    public static ILocator Field(IPage page, string testId) =>
        page.Locator($"[data-testid='{testId}'] input, [data-testid='{testId}'] textarea, input[data-testid='{testId}'], textarea[data-testid='{testId}']").First;

    /// <summary>Clicks a button (e.g. "Delete" / "Cancel") in the open confirmation message box.</summary>
    public static async Task ConfirmAsync(IPage page, string buttonText)
    {
        var box = page.Locator(".mud-dialog").Filter(new LocatorFilterOptions { Has = page.Locator(".mud-dialog-actions") }).Last;
        await box.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = buttonText, Exact = true }).ClickAsync();
    }

    /// <summary>Opens a MudSelect (single or multi) identified by the data-testid on its input.</summary>
    public static Task OpenSelectAsync(IPage page, string testId, float? timeout = null) =>
        page.Locator($".mud-select:has([data-testid='{testId}'])").First.ClickAsync(new LocatorClickOptions { Timeout = timeout });

    /// <summary>Selects an option of an open MudSelect popover by its visible text.</summary>
    public static async Task PickPopoverItemAsync(IPage page, string optionText)
    {
        await page.Locator(".mud-popover-open .mud-list-item").Filter(new LocatorFilterOptions
        {
            HasTextRegex = new Regex("^\\s*" + Regex.Escape(optionText) + "\\s*$"),
        }).First.ClickAsync();
    }
}
