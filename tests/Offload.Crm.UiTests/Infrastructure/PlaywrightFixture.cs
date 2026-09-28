using Microsoft.Playwright;

namespace Offload.Crm.UiTests.Infrastructure;

/// <summary>One browser per test run; each admin context gets a fresh sign-in (see <see cref="GetAdminStateAsync"/>).</summary>
public sealed class PlaywrightFixture : IAsyncLifetime
{
    private readonly SemaphoreSlim _authLock = new(1, 1);
    private string? _adminStatePath;

    public IPlaywright Playwright { get; private set; } = null!;

    public IBrowser Browser { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        if (!UiTestSettings.IsAppReachable)
        {
            return; // every test is skipped anyway
        }

        Playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        Browser = await Playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Channel = UiTestSettings.Channel,
            Headless = !UiTestSettings.Headed,
        });
    }

    public async Task DisposeAsync()
    {
        if (Browser is not null)
        {
            await Browser.DisposeAsync();
        }

        Playwright?.Dispose();
        if (_adminStatePath is not null && File.Exists(_adminStatePath))
        {
            File.Delete(_adminStatePath);
        }
    }

    /// <summary>New browser context (1280x800 by default), already signed in as the admin.</summary>
    public async Task<IBrowserContext> NewAdminContextAsync(int width = 1280, int height = 800)
    {
        var state = await GetAdminStateAsync();
        return await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = UiTestSettings.BaseUrl,
            ViewportSize = new ViewportSize { Width = width, Height = height },
            StorageStatePath = state,
        });
    }

    /// <summary>New anonymous browser context.</summary>
    public Task<IBrowserContext> NewAnonymousContextAsync(int width = 1280, int height = 800) =>
        Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = UiTestSettings.BaseUrl,
            ViewportSize = new ViewportSize { Width = width, Height = height },
        });

    private async Task<string> GetAdminStateAsync()
    {
        await _authLock.WaitAsync();
        try
        {
            // Always sign in fresh: since D-19 a logout (e.g. AuthTests) changes the admin's security stamp, which
            // invalidates previously issued admin cookies (checked every minute). Tests run sequentially.
            if (_adminStatePath is not null && File.Exists(_adminStatePath))
            {
                File.Delete(_adminStatePath);
            }

            await using var context = await NewAnonymousContextAsync();
            var page = await context.NewPageAsync();
            await UiHelpers.LoginAsync(page, UiTestSettings.AdminEmail, UiTestSettings.AdminPassword);
            var path = Path.Combine(Path.GetTempPath(), $"offload-ui-admin-{Guid.NewGuid():N}.json");
            await context.StorageStateAsync(new BrowserContextStorageStateOptions { Path = path });
            _adminStatePath = path;
            return path;
        }
        finally
        {
            _authLock.Release();
        }
    }
}

[CollectionDefinition(Name)]
public sealed class UiCollection : ICollectionFixture<PlaywrightFixture>
{
    public const string Name = "UI";
}
