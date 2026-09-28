namespace Offload.Crm.Infrastructure.Seeding;

/// <summary>Configuration section <c>Seed</c> (D-09).</summary>
public sealed class SeedOptions
{
    public const string SectionName = "Seed";

    public string AdminEmail { get; set; } = "admin@offload.local";

    public string AdminPassword { get; set; } = "Admin123!";

    public string AdminFullName { get; set; } = "Administrator";

    /// <summary>Demo data is seeded only in Development and only when this is true.</summary>
    public bool DemoData { get; set; } = true;

    /// <summary>Password of the demo users created with the demo data.</summary>
    public string DemoUserPassword { get; set; } = "Demo123!";
}

/// <summary>Configuration section <c>Database</c> (D-01).</summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>Apply pending migrations on startup. Always on in Development.</summary>
    public bool MigrateOnStartup { get; set; }
}

public static class Roles
{
    public const string Admin = "Admin";
}
