using Engram.Store;
using Xunit;

namespace Engram.Store.Tests;

public class RetentionStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SqliteStore _store;

    public RetentionStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "engram-ret", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _store = new SqliteStore(new StoreConfig { DataDir = _tempDir });
    }

    private async Task SeedData()
    {
        await _store.CreateSessionAsync("ret-test", "test", "/tmp");
        // Three observations with different types
        for (int i = 0; i < 3; i++)
            await _store.AddObservationAsync(new AddObservationParams
            {
                SessionId = "ret-test",
                Type = i switch { 0 => "tool_use", 1 => "decision", _ => "bugfix" },
                Title = $"Obs {i}",
                Content = $"Content {i}",
                Project = "test"
            });
    }

    [Fact]
    public async Task GetRetentionStats_ReturnsStats()
    {
        await SeedData();
        var stats = await _store.GetRetentionStatsAsync();
        Assert.Equal(3, stats.TotalObservations);
    }

    [Fact]
    public async Task GetRetentionStats_Empty_ReturnsZero()
    {
        var stats = await _store.GetRetentionStatsAsync();
        Assert.Equal(0, stats.TotalObservations);
    }

    [Fact]
    public async Task PruneOldObservations_DryRun_DoesNotDelete()
    {
        await SeedData();
        var result = await _store.PruneOldObservationsAsync(new RetentionPruneParams { DryRun = true });
        Assert.True(result.DryRun);

        var stats = await _store.GetRetentionStatsAsync();
        Assert.Equal(3, stats.TotalObservations); // unchanged
    }

    [Fact]
    public async Task AddAndGetProjectMigration_Roundtrip()
    {
        await _store.CreateSessionAsync("ret-test", "test", "/tmp");
        await _store.AddProjectMigrationAsync("old-name", "new-name");

        var migrations = await _store.GetProjectMigrationsAsync();
        Assert.NotEmpty(migrations);
        Assert.Equal("old-name", migrations[0].FromProject);
        Assert.Equal("new-name", migrations[0].ToProject);
    }

    [Fact]
    public async Task PruneOldObservations_PreservesNonExpiringTypes()
    {
        // decision and architecture should NEVER be pruned
        await _store.CreateSessionAsync("ret-test", "test", "/tmp");
        await _store.AddObservationAsync(new AddObservationParams
        {
            SessionId = "ret-test",
            Type = "decision",
            Title = "Important decision",
            Content = "Should never expire",
            Project = "test"
        });
        await _store.AddObservationAsync(new AddObservationParams
        {
            SessionId = "ret-test",
            Type = "architecture",
            Title = "Architecture record",
            Content = "Also should never expire",
            Project = "test"
        });

        // Prune all types — decision and architecture should be skipped
        var result = await _store.PruneOldObservationsAsync(new RetentionPruneParams());
        Assert.Equal(0, result.Pruned);

        var stats = await _store.GetRetentionStatsAsync();
        Assert.Equal(2, stats.TotalObservations);
    }

    [Fact]
    public async Task PruneOldObservations_RealRun_BackdatedObservations()
    {
        await _store.CreateSessionAsync("ret-test", "test", "/tmp");

        // Add a fresh observation (won't be pruned)
        await _store.AddObservationAsync(new AddObservationParams
        {
            SessionId = "ret-test",
            Type = "tool_use",
            Title = "Fresh observation",
            Content = "Too young to prune",
            Project = "test"
        });

        // Add an observation and backdate it via raw SQL so it appears old
        var obsId = await _store.AddObservationAsync(new AddObservationParams
        {
            SessionId = "ret-test",
            Type = "tool_use",
            Title = "Old observation",
            Content = "Old enough to prune",
            Project = "test"
        });

        BackdateObservation(obsId, daysAgo: 60);

        // Prune — should catch only the backdated tool_use
        var result = await _store.PruneOldObservationsAsync(new RetentionPruneParams { Type = "tool_use" });
        Assert.Equal(1, result.Pruned);

        var stats = await _store.GetRetentionStatsAsync();
        Assert.Equal(1, stats.TotalObservations); // only the fresh one remains
    }

    /// <summary>
    /// Opens a direct connection to the test DB and sets created_at in the past.
    /// Follows the same pattern as CountSoftDeletedPrompts in SqliteStoreTests.
    /// </summary>
    private void BackdateObservation(long observationId, int daysAgo)
    {
        var dbPath = Path.Combine(_tempDir, "engram.db");
        using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE observations SET created_at = datetime('now', @offset) WHERE id = @id";
        cmd.Parameters.AddWithValue("@offset", $"-{daysAgo} days");
        cmd.Parameters.AddWithValue("@id", observationId);
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        _store.Dispose();
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort cleanup */ }
    }
}
