using Engram.Store;
using Xunit;

namespace Engram.Store.Tests;

/// <summary>
/// Integration tests for PostgresStore retention capabilities.
/// These require a running PostgreSQL instance configured via ENGRAM_PG_CONNECTION.
/// Skipped by default — run with:
///   ENGRAM_DB_TYPE=postgres ENGRAM_PG_CONNECTION="Host=localhost;..." dotnet test --filter "RetentionPostgres"
/// </summary>
public class RetentionPostgresTests : IDisposable
{
    private readonly PostgresStore? _store;
    private readonly string _testProject = "ret-pg-test";

    public RetentionPostgresTests()
    {
        var cfg = StoreConfig.FromEnvironment();
        if (cfg.IsPostgres && !string.IsNullOrWhiteSpace(cfg.PgConnectionString))
        {
            _store = new PostgresStore(cfg);
        }
        // _store stays null → all tests skip via Skip attribute
    }

    private async Task SeedData()
    {
        if (_store is null) return;
        await _store.CreateSessionAsync("pg-ret-test", _testProject, "/tmp");
        for (int i = 0; i < 3; i++)
            await _store.AddObservationAsync(new AddObservationParams
            {
                SessionId = "pg-ret-test",
                Type = i switch { 0 => "tool_use", 1 => "decision", _ => "bugfix" },
                Title = $"Obs {i}",
                Content = $"Content {i}",
                Project = _testProject
            });
    }

    [Fact(Skip = "Requires PostgreSQL — set ENGRAM_DB_TYPE=postgres and ENGRAM_PG_CONNECTION")]
    public async Task GetRetentionStats_ReturnsStats()
    {
        if (_store is null) return; // defensive
        await SeedData();
        var stats = await _store.GetRetentionStatsAsync();
        Assert.Equal(3, stats.TotalObservations);
    }

    [Fact(Skip = "Requires PostgreSQL — set ENGRAM_DB_TYPE=postgres and ENGRAM_PG_CONNECTION")]
    public async Task GetRetentionStats_Empty_ReturnsZero()
    {
        if (_store is null) return;
        var stats = await _store.GetRetentionStatsAsync();
        Assert.Equal(0, stats.TotalObservations);
    }

    [Fact(Skip = "Requires PostgreSQL — set ENGRAM_DB_TYPE=postgres and ENGRAM_PG_CONNECTION")]
    public async Task PruneOldObservations_DryRun_DoesNotDelete()
    {
        if (_store is null) return;
        await SeedData();
        var result = await _store.PruneOldObservationsAsync(new RetentionPruneParams { DryRun = true });
        Assert.True(result.DryRun);

        var stats = await _store.GetRetentionStatsAsync();
        Assert.Equal(3, stats.TotalObservations);
    }

    [Fact(Skip = "Requires PostgreSQL — set ENGRAM_DB_TYPE=postgres and ENGRAM_PG_CONNECTION")]
    public async Task AddAndGetProjectMigration_Roundtrip()
    {
        if (_store is null) return;
        await _store.CreateSessionAsync("pg-ret-test", _testProject, "/tmp");
        await _store.AddProjectMigrationAsync("old-pg-name", "new-pg-name");

        var migrations = await _store.GetProjectMigrationsAsync();
        Assert.NotEmpty(migrations);
        Assert.Equal("old-pg-name", migrations[0].FromProject);
        Assert.Equal("new-pg-name", migrations[0].ToProject);
    }

    [Fact(Skip = "Requires PostgreSQL — set ENGRAM_DB_TYPE=postgres and ENGRAM_PG_CONNECTION")]
    public async Task PruneOldObservations_PreservesNonExpiringTypes()
    {
        if (_store is null) return;
        await _store.CreateSessionAsync("pg-ret-test", _testProject, "/tmp");
        await _store.AddObservationAsync(new AddObservationParams
        {
            SessionId = "pg-ret-test",
            Type = "decision",
            Title = "Important decision",
            Content = "Never expires",
            Project = _testProject
        });
        await _store.AddObservationAsync(new AddObservationParams
        {
            SessionId = "pg-ret-test",
            Type = "architecture",
            Title = "Architecture record",
            Content = "Also never expires",
            Project = _testProject
        });

        var result = await _store.PruneOldObservationsAsync(new RetentionPruneParams());
        Assert.Equal(0, result.Pruned);

        var stats = await _store.GetRetentionStatsAsync();
        Assert.Equal(2, stats.TotalObservations);
    }

    public void Dispose()
    {
        _store?.Dispose();
    }
}
