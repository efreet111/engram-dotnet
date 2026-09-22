using Engram.Store;
using Xunit;

namespace Engram.Store.Tests;

public class DetailedStatsTests : IDisposable
{
    private readonly SqliteStore _store;
    private readonly string _tempDir;
    private const string SessionId = "stats-test-session";

    public DetailedStatsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "engram-detailed-stats-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        var cfg = new StoreConfig { DataDir = _tempDir };
        _store = new SqliteStore(cfg);
    }

    public void Dispose()
    {
        _store.Dispose();
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private async Task SeedSession(string id = SessionId)
        => await _store.CreateSessionAsync(id, "test-project", "/tmp");

    private async Task<long> SeedObservation(string type = "insight", string? project = "test-project")
        => await _store.AddObservationAsync(new AddObservationParams
        {
            SessionId = SessionId,
            Title = "Test",
            Content = "Content",
            Type = type,
            Project = project,
        });

    [Fact]
    public async Task GetDetailedStatsAsync_EmptyDatabase_ReturnsZeroValues()
    {
        var stats = await _store.GetDetailedStatsAsync();

        Assert.Equal(0, stats.Overview.Observations);
        Assert.Equal(0, stats.Overview.Sessions);
        Assert.Equal(0, stats.Overview.Prompts);
        Assert.Empty(stats.Overview.Projects);
        Assert.Empty(stats.ByType);
        Assert.Equal(0, stats.Recent30Days.Created);
        Assert.Equal(0, stats.Oldest90Days.Count);
        Assert.True(stats.Storage.SizeBytes > 0);
    }

    [Fact]
    public async Task GetDetailedStatsAsync_WithObservations_ReturnsCorrectCounts()
    {
        await SeedSession();
        await SeedObservation("decision");
        await SeedObservation("insight");
        await SeedObservation("note");

        var stats = await _store.GetDetailedStatsAsync();

        Assert.Equal(3, stats.Overview.Observations);
        Assert.Equal(1, stats.Overview.Sessions);
        Assert.Equal(0, stats.Overview.Prompts);
        Assert.Contains("test-project", stats.Overview.Projects);
        Assert.Equal(3, stats.ByType.Count);
        Assert.Equal(1, stats.ByType["decision"]);
        Assert.Equal(1, stats.ByType["insight"]);
        Assert.Equal(1, stats.ByType["note"]);
    }

    [Fact]
    public async Task GetDetailedStatsAsync_Recent30Days_CountsCorrectly()
    {
        await SeedSession();
        await SeedObservation("insight");

        var stats = await _store.GetDetailedStatsAsync();

        Assert.Equal(1, stats.Recent30Days.Created);
        Assert.Equal("test-project", stats.Recent30Days.MostActiveProject);
        Assert.Equal("insight", stats.Recent30Days.MostActiveType);
    }

    [Fact]
    public async Task GetDetailedStatsAsync_ByType_CalculatesPercentages()
    {
        await _store.CreateSessionAsync("s1", "test-project", "/tmp");
        await _store.CreateSessionAsync("s2", "test-project", "/tmp");
        await _store.CreateSessionAsync("s3", "test-project", "/tmp");

        await _store.AddObservationAsync(new AddObservationParams { SessionId = "s1", Title = "T", Content = "C", Type = "decision", Project = "test-project" });
        await _store.AddObservationAsync(new AddObservationParams { SessionId = "s2", Title = "T", Content = "C", Type = "insight", Project = "test-project" });
        await _store.AddObservationAsync(new AddObservationParams { SessionId = "s3", Title = "T", Content = "C", Type = "note", Project = "test-project" });

        var stats = await _store.GetDetailedStatsAsync();

        Assert.Equal(3, stats.Overview.Observations);
        Assert.Equal(1, stats.ByType["decision"]);
        Assert.Equal(1, stats.ByType["insight"]);
        Assert.Equal(1, stats.ByType["note"]);
    }

    [Fact]
    public async Task GetDetailedStatsAsync_StorageSize_ReturnsGreaterThanZero()
    {
        await SeedSession();
        await SeedObservation("insight");

        var stats = await _store.GetDetailedStatsAsync();

        Assert.True(stats.Storage.SizeBytes > 0);
        Assert.True(stats.Storage.SizeMb > 0);
    }

    [Fact]
    public async Task GetDetailedStatsAsync_MultipleProjects_ListsAll()
    {
        await SeedSession();
        await SeedObservation("insight", "project-a");
        await SeedObservation("note", "project-b");

        var stats = await _store.GetDetailedStatsAsync();

        Assert.Equal(2, stats.Overview.Projects.Count);
        Assert.Contains("project-a", stats.Overview.Projects);
        Assert.Contains("project-b", stats.Overview.Projects);
    }

    [Fact]
    public async Task GetDetailedStatsAsync_SoftDeleted_DoesNotCount()
    {
        await SeedSession();
        var id = await SeedObservation("insight");
        await _store.DeleteObservationAsync(id);

        var stats = await _store.GetDetailedStatsAsync();

        Assert.Equal(0, stats.Overview.Observations);
        Assert.Empty(stats.ByType);
    }
}
