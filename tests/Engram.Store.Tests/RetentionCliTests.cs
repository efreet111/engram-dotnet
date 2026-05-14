using Engram.Store;
using Xunit;

namespace Engram.Store.Tests;

/// <summary>
/// End-to-end style tests that exercise retention via store methods
/// the same way CLI commands and server endpoints would.
/// </summary>
public class RetentionCliTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SqliteStore _store;
    private const string SessionId = "ret-cli-test";

    public RetentionCliTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "engram-ret-cli", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _store = new SqliteStore(new StoreConfig { DataDir = _tempDir });
    }

    private async Task SeedSession()
        => await _store.CreateSessionAsync(SessionId, "test", "/tmp");

    /// <summary>
    /// Opens a direct SQLite connection to backdate an observation's created_at,
    /// simulating observations old enough to be pruned by TTL.
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

    [Fact]
    public async Task RetentionPrune_WithType_FiltersCorrectly()
    {
        await SeedSession();

        // Add a tool_use and backdate it so it's eligible for pruning
        var toolUseId = await _store.AddObservationAsync(new AddObservationParams
        {
            SessionId = SessionId,
            Type = "tool_use",
            Title = "Old tool use",
            Content = "Should be pruned",
            Project = "test"
        });
        BackdateObservation(toolUseId, daysAgo: 60);

        // Add a decision (never expires) — stays regardless of age
        await _store.AddObservationAsync(new AddObservationParams
        {
            SessionId = SessionId,
            Type = "decision",
            Title = "Decision",
            Content = "Should NOT be pruned",
            Project = "test"
        });

        // Prune only tool_use — should find and prune 1
        var result = await _store.PruneOldObservationsAsync(new RetentionPruneParams { Type = "tool_use" });
        Assert.Equal(1, result.Pruned);
        Assert.Contains("tool_use", result.Details);
        Assert.Equal(1, result.Details["tool_use"]);

        // Only the decision remains
        var stats = await _store.GetRetentionStatsAsync();
        Assert.Equal(1, stats.TotalObservations);
    }

    [Fact]
    public async Task RetentionPrune_DryRun_ShowsCorrectCountWithoutDelete()
    {
        await SeedSession();

        // Create two prunable observations (tool_use) and backdate them
        for (int i = 0; i < 2; i++)
        {
            var id = await _store.AddObservationAsync(new AddObservationParams
            {
                SessionId = SessionId,
                Type = "tool_use",
                Title = $"Old tool use {i}",
                Content = $"Prunable content {i}",
                Project = "test"
            });
            BackdateObservation(id, daysAgo: 60);
        }

        // Dry run — reports 2 would be pruned, but 0 actually pruned
        var result = await _store.PruneOldObservationsAsync(new RetentionPruneParams { DryRun = true });
        Assert.Equal(2, result.Pruned);
        Assert.True(result.DryRun);

        // All observations still exist
        var stats = await _store.GetRetentionStatsAsync();
        Assert.Equal(2, stats.TotalObservations);
    }

    [Fact]
    public async Task RetentionPrune_CalledTwice_Idempotent()
    {
        await SeedSession();

        var obsId = await _store.AddObservationAsync(new AddObservationParams
        {
            SessionId = SessionId,
            Type = "tool_use",
            Title = "Will be pruned",
            Content = "Content",
            Project = "test"
        });
        BackdateObservation(obsId, daysAgo: 60);

        // First prune — removes 1
        var first = await _store.PruneOldObservationsAsync(new RetentionPruneParams());
        Assert.Equal(1, first.Pruned);

        // Second prune — removes 0 (soft-deleted observations already marked)
        var second = await _store.PruneOldObservationsAsync(new RetentionPruneParams());
        Assert.Equal(0, second.Pruned);
    }

    [Fact]
    public async Task RetentionStats_AgeBuckets_IncludeBackdatedData()
    {
        await SeedSession();

        var obsId = await _store.AddObservationAsync(new AddObservationParams
        {
            SessionId = SessionId,
            Type = "tool_use",
            Title = "Old data",
            Content = "Content",
            Project = "test"
        });
        BackdateObservation(obsId, daysAgo: 100); // 100 days ago → falls in "90-180 days" bucket

        var stats = await _store.GetRetentionStatsAsync();

        Assert.Equal(1, stats.TotalObservations);

        // Find the bucket for 90-180 days
        var bucket = stats.AgeBuckets.FirstOrDefault(b => b.Label == "90-180 days");
        Assert.NotNull(bucket);
        Assert.Equal(1, bucket.Count);
    }

    public void Dispose()
    {
        _store.Dispose();
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort cleanup */ }
    }
}
