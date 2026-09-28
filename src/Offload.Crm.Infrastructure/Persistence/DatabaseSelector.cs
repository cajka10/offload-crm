using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Offload.Crm.Infrastructure.Persistence;

public enum DatabaseTarget
{
    Production,
    Test,
}

/// <summary>
/// D-26: the app can run against the production database (<c>ConnectionStrings:Default</c>) or a test database
/// (<c>ConnectionStrings:Test</c>). The choice is app-wide (all users), survives restarts (a small JSON file) and is
/// only available when a test connection string is configured.
/// </summary>
public sealed class DatabaseSelector
{
    public const string TestConnectionStringName = "Test";

    private static readonly JsonSerializerOptions JsonOptions = new() { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    private readonly Dictionary<DatabaseTarget, string> _connectionStrings = [];
    private readonly string _stateFile;
    private readonly ILogger<DatabaseSelector> _logger;
    private readonly SemaphoreSlim _switchLock = new(1, 1);
    private volatile int _current;

    public DatabaseSelector(IConfiguration configuration, ILogger<DatabaseSelector> logger)
    {
        _logger = logger;
        _connectionStrings[DatabaseTarget.Production] = configuration.GetConnectionString(DependencyInjection.ConnectionStringName) is { Length: > 0 } production
            ? production
            : throw new InvalidOperationException(
                $"Connection string '{DependencyInjection.ConnectionStringName}' is missing. Set it with 'dotnet user-secrets set \"ConnectionStrings:{DependencyInjection.ConnectionStringName}\" \"...\" --project src/Offload.Crm.Web' or the ConnectionStrings__{DependencyInjection.ConnectionStringName} environment variable (see README).");
        if (configuration.GetConnectionString(TestConnectionStringName) is { Length: > 0 } test)
        {
            _connectionStrings[DatabaseTarget.Test] = test;
        }

        var file = configuration["Database:SelectionFile"] is { Length: > 0 } configured ? configured : Path.Combine("App_Data", "active-database.json");
        _stateFile = Path.IsPathRooted(file) ? file : Path.Combine(configuration["contentRoot"] ?? AppContext.BaseDirectory, file);
        _current = (int)LoadPersistedTarget();
    }

    /// <summary>Raised after a switch; open UI sessions reload so nothing keeps using the old database.</summary>
    public event Action<DatabaseTarget>? Changed;

    public DatabaseTarget Current => (DatabaseTarget)_current;

    public bool CanSwitch => _connectionStrings.ContainsKey(DatabaseTarget.Test);

    public IReadOnlyCollection<DatabaseTarget> ConfiguredTargets => _connectionStrings.Keys;

    public string GetConnectionString(DatabaseTarget target) =>
        _connectionStrings.TryGetValue(target, out var value)
            ? value
            : throw new InvalidOperationException($"The {target} database is not configured (ConnectionStrings:{TestConnectionStringName}).");

    /// <summary>The database name only — never expose the full connection string (it contains the password).</summary>
    public string GetDatabaseName(DatabaseTarget target) =>
        new NpgsqlConnectionStringBuilder(GetConnectionString(target)).Database ?? "(default)";

    /// <summary>Checks that the target database is reachable, then switches the whole app to it.</summary>
    public async Task SwitchAsync(DatabaseTarget target, CancellationToken ct = default)
    {
        var connectionString = GetConnectionString(target);
        await _switchLock.WaitAsync(ct);
        try
        {
            if (target == Current)
            {
                return;
            }

            try
            {
                await using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync(ct);
            }
            catch (Exception ex) when (ex is NpgsqlException or TimeoutException or InvalidOperationException)
            {
                throw new DatabaseUnavailableException(target, GetDatabaseName(target), ex);
            }

            await PersistAsync(target, ct);
            _current = (int)target;
            _logger.LogWarning("Active database switched to {Target} ({Database})", target, GetDatabaseName(target));
        }
        finally
        {
            _switchLock.Release();
        }

        Changed?.Invoke(target);
    }

    private DatabaseTarget LoadPersistedTarget()
    {
        if (!CanSwitch || !File.Exists(_stateFile))
        {
            return DatabaseTarget.Production;
        }

        try
        {
            var state = JsonSerializer.Deserialize<SelectionState>(File.ReadAllText(_stateFile), JsonOptions);
            if (state is not null && Enum.IsDefined(state.Target))
            {
                return state.Target;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not read the database selection file {File}; using Production", _stateFile);
        }

        return DatabaseTarget.Production;
    }

    private async Task PersistAsync(DatabaseTarget target, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_stateFile)!);
        var temp = _stateFile + ".tmp";
        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(new SelectionState(target, DateTimeOffset.UtcNow), JsonOptions), ct);
        File.Move(temp, _stateFile, overwrite: true);
    }

    private sealed record SelectionState(DatabaseTarget Target, DateTimeOffset ChangedAt);
}

public sealed class DatabaseUnavailableException(DatabaseTarget target, string database, Exception inner)
    : Exception($"The {target} database \"{database}\" is not reachable.", inner)
{
    public DatabaseTarget Target { get; } = target;
}

/// <summary>Lets a DI scope pin a specific database (startup migration of each configured database).</summary>
public sealed class DatabaseScope
{
    public DatabaseTarget? Target { get; set; }
}

/// <summary>Creates contexts for the currently selected database; one cached options instance per target.</summary>
internal sealed class SwitchableDbContextFactory(DatabaseSelector selector, AuditableInterceptor interceptor)
    : IDbContextFactory<AppDbContext>
{
    private readonly ConcurrentDictionary<DatabaseTarget, DbContextOptions<AppDbContext>> _options = new();

    public AppDbContext CreateDbContext() => CreateDbContext(selector.Current);

    public AppDbContext CreateDbContext(DatabaseTarget target) => new(_options.GetOrAdd(target, Build));

    private DbContextOptions<AppDbContext> Build(DatabaseTarget target) =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(selector.GetConnectionString(target), npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .AddInterceptors(interceptor)
            .Options;
}
