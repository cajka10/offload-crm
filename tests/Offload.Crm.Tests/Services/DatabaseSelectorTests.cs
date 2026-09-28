using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Offload.Crm.Infrastructure;
using Offload.Crm.Infrastructure.Persistence;
using Offload.Crm.Tests.Infrastructure;

namespace Offload.Crm.Tests.Services;

/// <summary>D-26: runtime switch between the production and the test database.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class DatabaseSelectorTests(DatabaseFixture db) : IDisposable
{
    private const string Unreachable = "Host=localhost;Port=1;Database=nowhere;Username=postgres;Password=x;Timeout=2";

    private readonly string _stateFile = Path.Combine(Path.GetTempPath(), $"offload-db-{Guid.NewGuid():N}", "active-database.json");

    // Same physical DB, but a distinguishable connection string for the test target.
    private string TestConnection => db.ConnectionString + ";Application Name=offload-test-target";

    public void Dispose()
    {
        var dir = Path.GetDirectoryName(_stateFile)!;
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Without_test_connection_only_production_is_available()
    {
        var selector = Create(test: null);

        Assert.False(selector.CanSwitch);
        Assert.Equal(DatabaseTarget.Production, selector.Current);
        Assert.Equal([DatabaseTarget.Production], selector.ConfiguredTargets);
        Assert.Equal(new Npgsql.NpgsqlConnectionStringBuilder(db.ConnectionString).Database, selector.GetDatabaseName(DatabaseTarget.Production));
        Assert.Throws<InvalidOperationException>(() => selector.GetConnectionString(DatabaseTarget.Test));
    }

    [Fact]
    public async Task Switch_persists_across_instances_and_raises_changed()
    {
        var selector = Create(TestConnection);
        DatabaseTarget? raised = null;
        selector.Changed += target => raised = target;

        await selector.SwitchAsync(DatabaseTarget.Test);

        Assert.Equal(DatabaseTarget.Test, selector.Current);
        Assert.Equal(DatabaseTarget.Test, raised);
        Assert.Equal(DatabaseTarget.Test, Create(TestConnection).Current); // e.g. after an app restart

        await selector.SwitchAsync(DatabaseTarget.Production);
        Assert.Equal(DatabaseTarget.Production, Create(TestConnection).Current);
    }

    [Fact]
    public async Task Switch_to_unreachable_database_fails_and_keeps_current()
    {
        var selector = Create(Unreachable);
        var raised = false;
        selector.Changed += _ => raised = true;

        var ex = await Assert.ThrowsAsync<DatabaseUnavailableException>(() => selector.SwitchAsync(DatabaseTarget.Test));

        Assert.Equal(DatabaseTarget.Test, ex.Target);
        Assert.Contains("\"nowhere\"", ex.Message);
        Assert.DoesNotContain("Password", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(DatabaseTarget.Production, selector.Current);
        Assert.False(raised);
        Assert.False(File.Exists(_stateFile));
    }

    [Fact]
    public async Task Persisted_test_selection_is_ignored_when_test_database_is_no_longer_configured()
    {
        await Create(TestConnection).SwitchAsync(DatabaseTarget.Test);

        Assert.Equal(DatabaseTarget.Production, Create(test: null).Current);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"Target\":99}")]
    [InlineData("")]
    public void Corrupt_selection_file_falls_back_to_production(string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_stateFile)!);
        File.WriteAllText(_stateFile, content);

        Assert.Equal(DatabaseTarget.Production, Create(TestConnection).Current);
    }

    [Fact]
    public async Task Context_factory_follows_the_selected_database()
    {
        await using var services = new ServiceCollection()
            .AddLogging()
            .AddInfrastructure(Configuration(TestConnection))
            .BuildServiceProvider();
        var factory = services.GetRequiredService<IDbContextFactory<AppDbContext>>();
        var selector = services.GetRequiredService<DatabaseSelector>();

        await using (var context = await factory.CreateDbContextAsync())
        {
            Assert.DoesNotContain("offload-test-target", context.Database.GetConnectionString());
        }

        await selector.SwitchAsync(DatabaseTarget.Test);

        await using (var context = await factory.CreateDbContextAsync())
        {
            Assert.Contains("offload-test-target", context.Database.GetConnectionString());
            Assert.True(await context.Database.CanConnectAsync());
        }

        // A scope can pin a database (startup migrates every configured database this way).
        await using var scope = services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<DatabaseScope>().Target = DatabaseTarget.Production;
        Assert.DoesNotContain("offload-test-target", scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.GetConnectionString());
    }

    private DatabaseSelector Create(string? test) => new(Configuration(test), NullLogger<DatabaseSelector>.Instance);

    private IConfiguration Configuration(string? test) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = db.ConnectionString,
            ["ConnectionStrings:Test"] = test,
            ["Database:SelectionFile"] = _stateFile,
        })
        .Build();
}
