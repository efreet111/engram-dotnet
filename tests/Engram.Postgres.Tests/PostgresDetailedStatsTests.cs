using Engram.Store;
using Xunit;

namespace Engram.Postgres.Tests;

[Trait("Category", "RequiresDocker")]
public class PostgresDetailedStatsTests : IClassFixture<PostgresStoreFixture>
{
    private readonly PostgresStoreFixture _fixture;
    private const string SessionId = "stats-test-session";

    public PostgresDetailedStatsTests(PostgresStoreFixture fixture)
    {
        _fixture = fixture;
    }

    private Task SeedSession(string id = SessionId, string project = "test-project")
        => _fixture.Store.CreateSessionAsync(id, project, "/tmp");

    private async Task<long> SeedObservation(string type = "insight", string? project = "test-project", string sessionId = SessionId)
        => await _fixture.Store.AddObservationAsync(new AddObservationParams
        {
            SessionId = sessionId,
            Title = "Test observation",
            Content = "This is some content",
            Type = type,
            Project = project,
        });

    [Fact]
    public async Task GetDetailedStatsAsync_EmptyDatabase_ReturnsZeroValues()
    {
        await _fixture.ResetAsync();

        var stats = await _fixture.Store.GetDetailedStatsAsync();

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
        await _fixture.ResetAsync();
        await SeedSession();
        await SeedObservation("decision");
        await SeedObservation("insight");
        await SeedObservation("note");

        var stats = await _fixture.Store.GetDetailedStatsAsync();

        Assert.Equal(3, stats.Overview.Observations);
        Assert.Equal(1, stats.Overview.Sessions);
        Assert.Single(stats.Overview.Projects);
        Assert.Contains("test-project", stats.Overview.Projects);
        Assert.Equal(3, stats.ByType.Count);
    }

    [Fact]
    public async Task GetDetailedStatsAsync_RecentActivity_TracksMostActive()
    {
        await _fixture.ResetAsync();
        await SeedSession();
        await SeedObservation("insight");
        await SeedObservation("note", "project-a");
        await SeedObservation("decision", "project-a");

        var stats = await _fixture.Store.GetDetailedStatsAsync();

        Assert.Equal(3, stats.Recent30Days.Created);
        Assert.Equal("project-a", stats.Recent30Days.MostActiveProject);
        Assert.Equal("decision", stats.Recent30Days.MostActiveType);
    }

    [Fact]
    public async Task GetDetailedStatsAsync_MultipleProjects_ListsAll()
    {
        await _fixture.ResetAsync();
        await SeedSession("s1", "project-a");
        await SeedSession("s2", "project-b");
        await SeedObservation("insight", "project-a", "s1");
        await SeedObservation("note", "project-b", "s2");

        var stats = await _fixture.Store.GetDetailedStatsAsync();

        Assert.Equal(2, stats.Overview.Projects.Count);
        Assert.Contains("project-a", stats.Overview.Projects);
        Assert.Contains("project-b", stats.Overview.Projects);
    }

    [Fact]
    public async Task GetDetailedStatsAsync_SoftDeleted_DoesNotCount()
    {
        await _fixture.ResetAsync();
        await SeedSession();
        var id = await SeedObservation("insight");
        await _fixture.Store.DeleteObservationAsync(id);

        var stats = await _fixture.Store.GetDetailedStatsAsync();

        Assert.Equal(0, stats.Overview.Observations);
        Assert.Empty(stats.ByType);
    }
}
