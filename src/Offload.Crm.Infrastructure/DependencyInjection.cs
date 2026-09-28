using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Offload.Crm.Application.Abstractions;
using Offload.Crm.Infrastructure.Persistence;
using Offload.Crm.Infrastructure.Seeding;

namespace Offload.Crm.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "Default";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<AuditableInterceptor>();

        // D-26: production or test database, switchable at runtime. The factory gives services a short-lived
        // context per operation (Blazor Server friendly) on the currently selected database; the scoped
        // AppDbContext (Identity stores, initializer) uses the scope's pinned database or the selected one.
        services.AddSingleton(sp => new DatabaseSelector(configuration, sp.GetRequiredService<ILogger<DatabaseSelector>>()));
        services.AddSingleton<SwitchableDbContextFactory>();
        services.AddSingleton<IDbContextFactory<AppDbContext>>(sp => sp.GetRequiredService<SwitchableDbContextFactory>());
        services.AddScoped<DatabaseScope>();
        services.AddScoped(sp => sp.GetRequiredService<SwitchableDbContextFactory>().CreateDbContext(
            sp.GetRequiredService<DatabaseScope>().Target ?? sp.GetRequiredService<DatabaseSelector>().Current));
        services.AddSingleton<IAppDbContextFactory, AppDbContextFactory>();

        services.Configure<SeedOptions>(configuration.GetSection(SeedOptions.SectionName));
        services.Configure<DatabaseOptions>(configuration.GetSection(DatabaseOptions.SectionName));
        services.AddScoped<DbInitializer>();

        return services;
    }
}
