using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Offload.Crm.Domain.Entities;
using Offload.Crm.Infrastructure.Persistence;

namespace Offload.Crm.Infrastructure.Seeding;

/// <summary>Applies migrations and seeds reference data, the admin user and (optionally) demo data. Idempotent.</summary>
public sealed class DbInitializer(
    AppDbContext db,
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole> roleManager,
    IOptions<SeedOptions> seedOptions,
    TimeProvider timeProvider,
    ILogger<DbInitializer> logger)
{
    /// <summary>Seed data for D-02, in order: name, color, IsActive, IsClosed.</summary>
    public static readonly IReadOnlyList<(string Name, string Color, bool IsActive, bool IsClosed)> DefaultStatuses =
    [
        ("Lead", "#6C757D", true, false),
        ("Analysis", "#0DCAF0", true, false),
        ("Proposal", "#6F42C1", true, false),
        ("Development", "#0D6EFD", true, false),
        ("Testing", "#E8912D", true, false),
        ("Delivered", "#0F5132", false, true),
        ("Maintenance", "#20C997", true, false),
        ("Cancelled", "#DC3545", false, true),
    ];

    public async Task InitializeAsync(bool migrate, bool seedDemoData, CancellationToken ct = default)
    {
        if (migrate)
        {
            logger.LogInformation("Applying database migrations");
            await db.Database.MigrateAsync(ct);
        }

        await SeedProjectStatusesAsync(ct);
        await SeedAdminAsync();

        if (seedDemoData && seedOptions.Value.DemoData)
        {
            await new DemoDataSeeder(db, userManager, seedOptions.Value, timeProvider, logger).SeedAsync(ct);
        }
    }

    /// <summary>Seeds the D-02 statuses when the table is empty (renamed/edited statuses are left alone).</summary>
    public async Task SeedProjectStatusesAsync(CancellationToken ct = default)
    {
        if (await db.ProjectStatuses.AnyAsync(ct))
        {
            return;
        }

        var order = 1;
        foreach (var (name, color, isActive, isClosed) in DefaultStatuses)
        {
            db.ProjectStatuses.Add(new ProjectStatus
            {
                Name = name,
                Color = color,
                SortOrder = order++,
                IsActive = isActive,
                IsClosed = isClosed,
                IsDefault = name == "Lead",
            });
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded {Count} project statuses", DefaultStatuses.Count);
    }

    public async Task SeedAdminAsync()
    {
        if (!await roleManager.RoleExistsAsync(Roles.Admin))
        {
            EnsureSucceeded(await roleManager.CreateAsync(new IdentityRole(Roles.Admin)), "create role");
        }

        var options = seedOptions.Value;
        if (await userManager.FindByEmailAsync(options.AdminEmail) is { } existing)
        {
            // D-27: never leave a database without an administrator (e.g. roles removed by hand).
            if (!(await userManager.GetUsersInRoleAsync(Roles.Admin)).Any(u => u.IsActive)
                && !await userManager.IsInRoleAsync(existing, Roles.Admin))
            {
                EnsureSucceeded(await userManager.AddToRoleAsync(existing, Roles.Admin), "assign admin role");
                logger.LogWarning("No active administrator found; {Email} was given the Admin role", options.AdminEmail);
            }

            return;
        }

        var admin = new ApplicationUser
        {
            UserName = options.AdminEmail,
            Email = options.AdminEmail,
            EmailConfirmed = true,
            FullName = options.AdminFullName,
            IsActive = true,
        };
        EnsureSucceeded(await userManager.CreateAsync(admin, options.AdminPassword), "create admin user");
        EnsureSucceeded(await userManager.AddToRoleAsync(admin, Roles.Admin), "assign admin role");
        logger.LogInformation("Seeded admin user {Email}", options.AdminEmail);
    }

    internal static void EnsureSucceeded(IdentityResult result, string action)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Seeding failed to {action}: {string.Join("; ", result.Errors.Select(e => e.Description))}");
        }
    }
}
