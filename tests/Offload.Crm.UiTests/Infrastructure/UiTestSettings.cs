namespace Offload.Crm.UiTests.Infrastructure;

/// <summary>
/// Settings for the UI tests. They run against an already running app (they don't start it).
/// Env vars: OFFLOAD_UI_BASEURL (default http://localhost:5080), OFFLOAD_UI_HEADED=1 to watch the browser,
/// OFFLOAD_UI_CHANNEL (default msedge), OFFLOAD_UI_ADMIN_EMAIL / OFFLOAD_UI_ADMIN_PASSWORD.
/// </summary>
public static class UiTestSettings
{
    private static readonly Lazy<bool> Reachable = new(CheckReachable);

    public static string BaseUrl { get; } =
        (Environment.GetEnvironmentVariable("OFFLOAD_UI_BASEURL") is { Length: > 0 } url ? url : "http://localhost:5080").TrimEnd('/');

    public static bool Headed => Environment.GetEnvironmentVariable("OFFLOAD_UI_HEADED") == "1";

    public static string Channel =>
        Environment.GetEnvironmentVariable("OFFLOAD_UI_CHANNEL") is { Length: > 0 } c ? c : "msedge";

    public static string AdminEmail =>
        Environment.GetEnvironmentVariable("OFFLOAD_UI_ADMIN_EMAIL") is { Length: > 0 } e ? e : "admin@offload.local";

    public static string AdminPassword =>
        Environment.GetEnvironmentVariable("OFFLOAD_UI_ADMIN_PASSWORD") is { Length: > 0 } p ? p : "Admin123!";

    /// <summary>True when the app answers at <see cref="BaseUrl"/> (checked once per test run).</summary>
    public static bool IsAppReachable => Reachable.Value;

    public static string SkipReason => $"App not reachable at {BaseUrl} (start it, or set OFFLOAD_UI_BASEURL).";

    private static bool CheckReachable()
    {
        try
        {
            using var handler = new HttpClientHandler { AllowAutoRedirect = false };
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(3) };
            using var response = http.GetAsync(BaseUrl + "/account/login").GetAwaiter().GetResult();
            return (int)response.StatusCode < 500;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>A fact that is skipped (not failed) when the app isn't running, so plain <c>dotnet test</c> stays green.</summary>
public sealed class UiFactAttribute : FactAttribute
{
    public UiFactAttribute()
    {
        if (!UiTestSettings.IsAppReachable)
        {
            Skip = UiTestSettings.SkipReason;
        }
    }
}

/// <summary>Theory variant of <see cref="UiFactAttribute"/>.</summary>
public sealed class UiTheoryAttribute : TheoryAttribute
{
    public UiTheoryAttribute()
    {
        if (!UiTestSettings.IsAppReachable)
        {
            Skip = UiTestSettings.SkipReason;
        }
    }
}
