using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Offload.Crm.Application.Abstractions;
using Offload.Crm.Domain.Entities;
using Offload.Crm.Infrastructure.Persistence;
using Offload.Crm.Infrastructure.Seeding;
using Respawn;
using Respawn.Graph;

namespace Offload.Crm.Tests.Infrastructure;

/// <summary>
/// Recreates the Postgres test database (<c>offload_crm_test</c>) with migrations once per test run and resets
/// all data between tests with Respawn. Connection string: env <c>OFFLOAD_TEST_CONNECTION</c>, otherwise the Web
/// project's user-secret <c>ConnectionStrings:Default</c> with the database <c>offload_crm_test</c> (no passwords in the repo).
/// </summary>
public sealed class DatabaseFixture : IAsyncLifetime, IAppDbContextFactory
{
    private Respawner? _respawner;

    public string ConnectionString { get; } =
        Environment.GetEnvironmentVariable("OFFLOAD_TEST_CONNECTION") is { Length: > 0 } fromEnv
            ? fromEnv
            : FromUserSecrets();

    public TestClock Clock { get; } = new();

    private static string FromUserSecrets()
    {
        var configured = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddUserSecrets(typeof(Program).Assembly, optional: true)
            .Build()
            .GetConnectionString("Default");
        if (string.IsNullOrEmpty(configured))
        {
            throw new InvalidOperationException(
                "No test database connection. Set OFFLOAD_TEST_CONNECTION or the Web project's user secret ConnectionStrings:Default (see README).");
        }

        return new NpgsqlConnectionStringBuilder(configured) { Database = "offload_crm_test" }.ConnectionString;
    }

    public async Task InitializeAsync()
    {
        await using (var db = CreateContext())
        {
            await db.Database.EnsureDeletedAsync();
            await db.Database.MigrateAsync();
        }

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        _respawner = await Respawner.CreateAsync(connection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = ["public"],
            TablesToIgnore = [new Table("__EFMigrationsHistory")],
        });
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>Deletes all data and re-seeds the default project statuses.</summary>
    public async Task ResetAsync()
    {
        await using (var connection = new NpgsqlConnection(ConnectionString))
        {
            await connection.OpenAsync();
            await _respawner!.ResetAsync(connection);
        }

        await using var db = CreateContext();
        var order = 1;
        foreach (var (name, color, isActive, isClosed) in DbInitializer.DefaultStatuses)
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

        await db.SaveChangesAsync();
    }

    public AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnectionString)
            .AddInterceptors(new AuditableInterceptor(Clock))
            .Options;
        return new AppDbContext(options);
    }

    public Task<IAppDbContext> CreateAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IAppDbContext>(CreateContext());
}

[CollectionDefinition(Name)]
public sealed class DatabaseCollection : ICollectionFixture<DatabaseFixture>
{
    public const string Name = "Database";
}

/// <summary>Controllable clock for timestamp assertions.</summary>
public sealed class TestClock : TimeProvider
{
    public DateTimeOffset? Now { get; set; }

    public override DateTimeOffset GetUtcNow() => Now ?? base.GetUtcNow();
}

/// <summary>Current user for service tests.</summary>
public sealed class TestCurrentUser : Offload.Crm.Application.Abstractions.ICurrentUser
{
    public string? UserId { get; set; }

    public Task<string?> GetUserIdAsync(CancellationToken cancellationToken = default) => Task.FromResult(UserId);
}
