using Engram.Store;
using Testcontainers.PostgreSql;
using Xunit;

namespace Engram.Postgres.Tests;

/// <summary>
/// Parity tests for PostgresStore vs SqliteStore.
/// Each test class spins up a Testcontainers PostgreSQL instance.
/// </summary>
public sealed class PostgresStoreFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("engram")
        .WithUsername("engram")
        .WithPassword("engram")
        .Build();

    public PostgresStore Store { get; private set; } = null!;
    public string ConnectionString { get; private set; } = "";

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        ConnectionString = _container.GetConnectionString();
        var cfg = new StoreConfig
        {
            DbType = StoreDbType.Postgres,
            PgConnectionString = ConnectionString,
            DataDir = "/tmp", // not used by PostgresStore but required by StoreConfig
        };
        Store = new PostgresStore(cfg);
    }

    public async Task DisposeAsync()
    {
        Store.Dispose();
        await _container.DisposeAsync();
    }
}

public class PostgresStoreTests : IClassFixture<PostgresStoreFixture>
{
    private readonly PostgresStoreFixture _fixture;
    private const string SessionId = "test-session-1";

    public PostgresStoreTests(PostgresStoreFixture fixture)
    {
        _fixture = fixture;
    }

    // ─── helpers ──────────────────────────────────────────────────────────────

    private Task SeedSession(string id = SessionId)
        => _fixture.Store.CreateSessionAsync(id, "test-project", "/tmp");

    private Task<long> SeedObservation(
        string title = "Test observation",
        string content = "This is some content",
        string type = "manual",
        string? project = "test-project",
        string? topicKey = null)
        => _fixture.Store.AddObservationAsync(new AddObservationParams
        {
            SessionId = SessionId,
            Title = title,
            Content = content,
            Type = type,
            Project = project,
            TopicKey = topicKey,
        });

    // ─── Sessions ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateSession_StoresAndRetrievesSession()
    {
        await SeedSession();

        var session = await _fixture.Store.GetSessionAsync(SessionId);

        Assert.NotNull(session);
        Assert.Equal(SessionId, session.Id);
        Assert.Equal("test-project", session.Project);
        Assert.Equal("/tmp", session.Directory);
        Assert.NotEmpty(session.StartedAt);
    }

    [Fact]
    public async Task EndSession_SetsSummaryAndEndedAt()
    {
        await SeedSession();
        await _fixture.Store.EndSessionAsync(SessionId, "All done");

        var session = await _fixture.Store.GetSessionAsync(SessionId);

        Assert.NotNull(session);
        Assert.Equal("All done", session.Summary);
        Assert.NotNull(session.EndedAt);
    }

    [Fact]
    public async Task GetSession_ReturnsNull_WhenNotFound()
    {
        var session = await _fixture.Store.GetSessionAsync("nonexistent");
        Assert.Null(session);
    }

    [Fact]
    public async Task RecentSessions_FiltersAndReturnsCorrectCount()
    {
        await _fixture.Store.CreateSessionAsync("s1", "proj-a", "/a");
        await _fixture.Store.CreateSessionAsync("s2", "proj-a", "/b");
        await _fixture.Store.CreateSessionAsync("s3", "proj-b", "/c");

        var all = await _fixture.Store.RecentSessionsAsync(null, 10);
        var proj = await _fixture.Store.RecentSessionsAsync("proj-a", 10);

        Assert.Equal(3, all.Count);
        Assert.Equal(2, proj.Count);
        Assert.All(proj, s => Assert.Equal("proj-a", s.Project));
    }

    // ─── Observations ─────────────────────────────────────────────────────────

    [Fact]
    public async Task AddObservation_CreatesAndRetrieves()
    {
        await SeedSession();
        var id = await SeedObservation("My decision", "We chose hexagonal architecture", "decision");

        var obs = await _fixture.Store.GetObservationAsync(id);

        Assert.NotNull(obs);
        Assert.Equal("My decision", obs.Title);
        Assert.Equal("We chose hexagonal architecture", obs.Content);
        Assert.Equal("decision", obs.Type);
        Assert.Equal("test-project", obs.Project);
    }

    [Fact]
    public async Task AddObservation_DeduplicatesOnTopicKey()
    {
        await SeedSession();
        var id1 = await _fixture.Store.AddObservationAsync(new AddObservationParams
        {
            SessionId = SessionId, Title = "First", Content = "Content 1", Type = "decision",
            Project = "proj", TopicKey = "architecture/model",
        });
        var id2 = await _fixture.Store.AddObservationAsync(new AddObservationParams
        {
            SessionId = SessionId, Title = "Updated", Content = "Content 2", Type = "decision",
            Project = "proj", TopicKey = "architecture/model",
        });

        Assert.Equal(id1, id2);
        var obs = await _fixture.Store.GetObservationAsync(id1);
        Assert.NotNull(obs);
        Assert.Equal("Updated", obs.Title);
        Assert.Equal(2, obs.RevisionCount);
    }

    [Fact]
    public async Task AddObservation_DeduplicatesOnHashWithinWindow()
    {
        await SeedSession();
        var id1 = await SeedObservation("Same title", "Same content", "manual", "proj");
        var id2 = await SeedObservation("Same title", "Same content", "manual", "proj");

        Assert.Equal(id1, id2);
        var obs = await _fixture.Store.GetObservationAsync(id1);
        Assert.NotNull(obs);
        Assert.Equal(2, obs.DuplicateCount);
    }

    [Fact]
    public async Task UpdateObservation_ChangesTitle()
    {
        await SeedSession();
        var id = await SeedObservation("Old title", "Content");

        var ok = await _fixture.Store.UpdateObservationAsync(id, new UpdateObservationParams { Title = "New title" });

        Assert.True(ok);
        var obs = await _fixture.Store.GetObservationAsync(id);
        Assert.NotNull(obs);
        Assert.Equal("New title", obs.Title);
    }

    [Fact]
    public async Task DeleteObservation_SoftDeletes()
    {
        await SeedSession();
        var id = await SeedObservation("To delete", "Content");

        var ok = await _fixture.Store.DeleteObservationAsync(id);
        Assert.True(ok);

        var obs = await _fixture.Store.GetObservationAsync(id);
        Assert.Null(obs);
    }

    // ─── Search ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Search_ReturnsMatchingObservations()
    {
        await SeedSession();
        await SeedObservation("JWT authentication", "We implemented JWT-based auth");

        var results = await _fixture.Store.SearchAsync("JWT", new SearchOptions { Limit = 10 });

        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.Observation.Title.Contains("JWT"));
    }

    [Fact]
    public async Task Search_ReturnsEmpty_WhenNoMatch()
    {
        var results = await _fixture.Store.SearchAsync("xyzzy-nonexistent-string", new SearchOptions { Limit = 10 });
        Assert.Empty(results);
    }

    [Fact]
    public async Task Search_FiltersByProject()
    {
        await SeedSession();
        await SeedObservation("obs-a", "content", project: "proj-a");
        await SeedObservation("obs-b", "content", project: "proj-b");

        var results = await _fixture.Store.SearchAsync("obs", new SearchOptions { Project = "proj-a", Limit = 10 });

        Assert.NotEmpty(results);
        Assert.All(results, r => Assert.Equal("proj-a", r.Observation.Project));
    }

    // ─── Prompts ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddPrompt_CreatesAndRetrieves()
    {
        await SeedSession();
        await _fixture.Store.AddPromptAsync(new AddPromptParams
        {
            SessionId = SessionId,
            Content = "How do I implement JWT?",
            Project = "test-project",
        });

        var prompts = await _fixture.Store.RecentPromptsAsync("test-project", 10);

        Assert.NotEmpty(prompts);
        Assert.Contains(prompts, p => p.Content.Contains("JWT"));
    }

    [Fact]
    public async Task SearchPrompts_ReturnsMatch()
    {
        await SeedSession();
        await _fixture.Store.AddPromptAsync(new AddPromptParams
        {
            SessionId = SessionId,
            Content = "How to configure Redis caching?",
            Project = "test-project",
        });

        var results = await _fixture.Store.SearchPromptsAsync("Redis", "test-project", 10);

        Assert.NotEmpty(results);
    }

    // ─── Stats ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Stats_ReturnsCorrectCounts()
    {
        await SeedSession();
        await SeedObservation("obs-1", "content");

        var stats = await _fixture.Store.StatsAsync();

        Assert.True(stats.TotalSessions >= 1);
        Assert.True(stats.TotalObservations >= 1);
    }

    // ─── Export / Import ──────────────────────────────────────────────────────

    [Fact]
    public async Task Export_ReturnsExportData()
    {
        await SeedSession();
        await SeedObservation("Export test", "Content");

        var data = await _fixture.Store.ExportAsync();

        Assert.NotNull(data);
        Assert.NotEmpty(data.Observations);
    }

    // ─── Projects ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListProjectNames_ReturnsDistinctProjects()
    {
        await SeedSession();
        await SeedObservation("obs-1", "content", project: "alpha");
        await SeedObservation("obs-2", "content", project: "beta");

        var names = await _fixture.Store.ListProjectNamesAsync();

        Assert.Equal(2, names.Count);
        Assert.Contains("alpha", names);
        Assert.Contains("beta", names);
    }

    [Fact]
    public async Task ListProjectsWithStats_ReturnsCorrectCounts()
    {
        await _fixture.Store.CreateSessionAsync("s-ps-1", "proj-a", "/a");
        await _fixture.Store.AddObservationAsync(new AddObservationParams
        {
            SessionId = "s-ps-1", Title = "obs-1", Content = "c", Type = "manual", Project = "proj-a",
        });

        var stats = await _fixture.Store.ListProjectsWithStatsAsync();

        var projA = stats.FirstOrDefault(s => s.Name == "proj-a");
        Assert.NotNull(projA);
        Assert.Equal(1, projA.ObservationCount);
        Assert.Equal(1, projA.SessionCount);
    }

    [Fact]
    public async Task MergeProjects_ReassignsObservations()
    {
        await SeedSession();
        await SeedObservation("obs-old", "content", project: "old-proj");

        var result = await _fixture.Store.MergeProjectsAsync(new[] { "old-proj" }, "new-proj");

        Assert.Equal("new-proj", result.Canonical);
        Assert.True(result.ObservationsUpdated >= 1);

        var obs = await _fixture.Store.GetObservationAsync(1);
        Assert.NotNull(obs);
        Assert.Equal("new-proj", obs.Project);
    }

    [Fact]
    public async Task PruneProject_DeletesSessionsAndPrompts()
    {
        await _fixture.Store.CreateSessionAsync("s-prune", "prune-proj", "/tmp");
        await _fixture.Store.AddPromptAsync(new AddPromptParams
            { SessionId = "s-prune", Content = "a prompt", Project = "prune-proj" });

        var result = await _fixture.Store.PruneProjectAsync("prune-proj");

        Assert.Equal("prune-proj", result.Project);
        Assert.True(result.SessionsDeleted >= 1);
        Assert.True(result.PromptsDeleted >= 1);
    }

    [Fact]
    public async Task PruneProject_Throws_WhenObservationsExist()
    {
        await SeedSession();
        await SeedObservation("important", "content", project: "obs-proj");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _fixture.Store.PruneProjectAsync("obs-proj"));
    }

    // ─── Timeline ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Timeline_ReturnsResult_ForExistingObservation()
    {
        await SeedSession();
        var id = await SeedObservation("Timeline focus", "Content for timeline");

        var result = await _fixture.Store.TimelineAsync(id, before: 3, after: 3);

        Assert.NotNull(result);
        Assert.Equal(id, result.Focus.Id);
    }

    [Fact]
    public async Task Timeline_ReturnsNull_WhenObservationNotFound()
    {
        var result = await _fixture.Store.TimelineAsync(999_999_999L, 3, 3);
        Assert.Null(result);
    }

    // ─── FTS: tsvector ────────────────────────────────────────────────────────

    [Fact]
    public async Task Search_UsesTsvector_ForFullTextSearch()
    {
        await SeedSession();
        await SeedObservation("Architecture decision", "We chose hexagonal architecture for the system");

        var results = await _fixture.Store.SearchAsync("hexagonal", new SearchOptions { Limit = 10 });

        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.Observation.Title.Contains("Architecture"));
    }

    [Fact]
    public async Task Search_TopicKeyShortcut_RanksFirst()
    {
        await SeedSession();
        await _fixture.Store.AddObservationAsync(new AddObservationParams
        {
            SessionId = SessionId, Title = "Auth model", Content = "JWT tokens",
            Type = "architecture", Project = "proj", TopicKey = "architecture/auth-model",
        });

        var results = await _fixture.Store.SearchAsync("architecture/auth-model", new SearchOptions { Limit = 10 });

        Assert.NotEmpty(results);
        // Topic-key shortcut should have rank = -1000
        Assert.Equal(-1000.0, results[0].Rank);
    }
}
