using System.Globalization;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Offload.Crm.Application.Common;
using Offload.Crm.Infrastructure.Persistence;

namespace Offload.Crm.Web.Settings;

/// <summary>Read-only application info for Settings → General (T-25). Never includes secrets or connection strings.</summary>
public sealed record AppInfo(string Version, string Environment, string Currency, string Culture, string TimeZone, string DatabaseServer, string Database);

public sealed class AppInfoService(
    IDbContextFactory<AppDbContext> dbFactory, DatabaseSelector databases, IHostEnvironment environment)
{
    public async Task<AppInfo> GetAsync(CancellationToken ct = default)
    {
        var version = typeof(AppInfoService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
        version = version.Split('+')[0]; // drop the source-revision suffix

        string server;
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            await db.Database.OpenConnectionAsync(ct);
            server = "PostgreSQL " + db.Database.GetDbConnection().ServerVersion;
        }
        catch (Exception)
        {
            server = "not reachable";
        }

        return new AppInfo(
            version,
            environment.EnvironmentName,
            Offload.Crm.Web.Ui.AppCulture.CurrencyCode,
            CultureInfo.DefaultThreadCurrentCulture?.Name ?? CultureInfo.CurrentCulture.Name,
            AppTime.Zone.Id,
            server,
            $"{databases.GetDatabaseName(databases.Current)} ({databases.Current})");
    }
}
