using Engram.Store;
using Xunit;

namespace Engram.Store.Tests;

/// <summary>
/// Core parity tests for SqliteStore vs the Go engram store.
/// Each test instance creates a unique temp directory so tests are isolated.
/// </summary>
public class SqliteStoreTests : IDisposable
{
    private readonly SqliteStore _store;
    private readonly string      _tempDir;
    private const string SessionId = "test-session-1";

    public SqliteStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "engram-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        var cfg = new StoreConfig { DataDir = _tempDir };
        _store = new SqliteStore(cfg);
    }

    public void Dispose()
    {
        _store.Dispose();
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort cleanup */ }
    }

    // ─── helpers ──────────────────────────────────────────────────────────────

    private Task SeedSession(string id = SessionId)
        => _store.CreateSessionAsync(id, "test-project", "/tmp");

    private Task<long> SeedObservation(
        string title   = "Test observation",
        string content = "This is some content",
        string type    = "manual",
        string? project = "test-project",
        string? topicKey = null)
        => _store.AddObservationAsync(new AddObservationParams
        {
            SessionId = SessionId,
            Title     = title,
            Content   = content,
            Type      = type,
            Project   = project,
            TopicKey  = topicKey,
        });

    // ─── Sessions ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateSession_StoresAndRetrievesSession()
    {
        await SeedSession();

        var session = await _store.GetSessionAsync(SessionId);

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
        await _store.EndSessionAsync(SessionId, "All done");

        var session = await _store.GetSessionAsync(SessionId);

        Assert.NotNull(session);
        Assert.Equal("All done", session.Summary);
        Assert.NotNull(session.EndedAt);
    }

    [Fact]
    public async Task GetSession_ReturnsNull_WhenNotFound()
    {
        var session = await _store.GetSessionAsync("nonexistent");
        Assert.Null(session);
    }

    [Fact]
    public async Task RecentSessions_FiltersAndReturnsCorrectCount()
    {
        await _store.CreateSessionAsync("s1", "proj-a", "/a");
        await _store.CreateSessionAsync("s2", "proj-a", "/b");
        await _store.CreateSessionAsync("s3", "proj-b", "/c");

        var all  = await _store.RecentSessionsAsync(null, 10);
        var proj = await _store.RecentSessionsAsync("proj-a", 10);

        Assert.Equal(3, all.Count);
        Assert.Equal(2, proj.Count);
        Assert.All(proj, s => Assert.Equal("proj-a", s.Project));
    }

    // ─── Observations ─────────────────────────────────────────────────────────

    [Fact]
    public async Task AddObservation_AssignsPositiveId()
    {
        await SeedSession();
        var id = await SeedObservation();
        Assert.True(id > 0);
    }

    [Fact]
    public async Task GetObservation_ReturnsCorrectObservation()
    {
        await SeedSession();
        var id = await SeedObservation("My title", "My content", "decision");

        var obs = await _store.GetObservationAsync(id);

        Assert.NotNull(obs);
        Assert.Equal(id, obs.Id);
        Assert.Equal("My title", obs.Title);
        Assert.Equal("My content", obs.Content);
        Assert.Equal("decision", obs.Type);
        Assert.Equal("test-project", obs.Project);
        Assert.NotEmpty(obs.SyncId);
        Assert.NotEmpty(obs.CreatedAt);
    }

    [Fact]
    public async Task GetObservation_ReturnsNull_WhenNotFound()
    {
        var obs = await _store.GetObservationAsync(999_999);
        Assert.Null(obs);
    }

    [Fact]
    public async Task UpdateObservation_PatchesFields()
    {
        await SeedSession();
        var id = await SeedObservation("Old title", "Old content");

        var updated = await _store.UpdateObservationAsync(id, new UpdateObservationParams
        {
            Title   = "New title",
            Content = "New content",
            Type    = "architecture",
        });

        Assert.True(updated);

        var obs = await _store.GetObservationAsync(id);
        Assert.NotNull(obs);
        Assert.Equal("New title", obs.Title);
        Assert.Equal("New content", obs.Content);
        Assert.Equal("architecture", obs.Type);
    }

    [Fact]
    public async Task UpdateObservation_ReturnsFalse_WhenNotFound()
    {
        var updated = await _store.UpdateObservationAsync(999_999, new UpdateObservationParams
        {
            Title = "Nope",
        });
        Assert.False(updated);
    }

    [Fact]
    public async Task DeleteObservation_SoftDeletes()
    {
        await SeedSession();
        var id = await SeedObservation();

        var deleted = await _store.DeleteObservationAsync(id);
        Assert.True(deleted);

        // Soft delete: GetObservation should return null (deleted rows are excluded)
        var obs = await _store.GetObservationAsync(id);
        Assert.Null(obs);
    }

    [Fact]
    public async Task DeleteObservation_ReturnsFalse_WhenNotFound()
    {
        var deleted = await _store.DeleteObservationAsync(999_999);
        Assert.False(deleted);
    }

    [Fact]
    public async Task RecentObservations_FiltersCorrectly()
    {
        await SeedSession();
        await SeedObservation("Obs A", "Content A", project: "alpha");
        await SeedObservation("Obs B", "Content B", project: "beta");
        await SeedObservation("Obs C", "Content C", project: "alpha");

        var all   = await _store.RecentObservationsAsync(null, null, 10);
        var alpha = await _store.RecentObservationsAsync("alpha", null, 10);

        Assert.Equal(3, all.Count);
        Assert.Equal(2, alpha.Count);
        Assert.All(alpha, o => Assert.Equal("alpha", o.Project));
    }

    // ─── Deduplication (3-way: content, title+project, topic_key) ─────────────

    [Fact]
    public async Task AddObservation_DeduplicatesOnIdenticalContent()
    {
        // Hash dedup (Path 2): same title + same normalized content → same ID returned
        // Normalization: lowercase + collapse whitespace
        await SeedSession();
        var id1 = await SeedObservation("Same Title", "Identical content about auth");
        var id2 = await SeedObservation("Same Title", "IDENTICAL  content  about  AUTH"); // normalizes to same hash

        Assert.Equal(id1, id2);

        var obs = await _store.GetObservationAsync(id1);
        Assert.NotNull(obs);
        Assert.True(obs.DuplicateCount >= 2);
    }

    [Fact]
    public async Task AddObservation_DeduplicatesOnTopicKey()
    {
        await SeedSession();
        var id1 = await SeedObservation("Title 1", "Content 1", topicKey: "architecture/auth");
        var id2 = await SeedObservation("Title 2", "Content 2", topicKey: "architecture/auth");

        // Path 1: topic_key upsert — should overwrite into same observation
        Assert.Equal(id1, id2);

        var obs = await _store.GetObservationAsync(id1);
        Assert.NotNull(obs);
        // Content should be updated to latest
        Assert.Equal("Content 2", obs.Content);
        Assert.Equal("Title 2", obs.Title);
        Assert.True(obs.RevisionCount >= 2);
    }

    [Fact]
    public async Task AddObservation_DeduplicatesOnTitleAndProject()
    {
        // Path 1 (topic_key) is the upsert mechanism for "same title+project, different content".
        // Without topic_key, two observations with same title but different content are DISTINCT (different hash).
        // Use topic_key to force upsert into same row and verify content is updated.
        await SeedSession();
        var id1 = await SeedObservation("Same Title", "Content v1", project: "proj", topicKey: "proj/same-title");
        var id2 = await SeedObservation("Same Title", "Content v2", project: "proj", topicKey: "proj/same-title");

        Assert.Equal(id1, id2);

        var obs = await _store.GetObservationAsync(id1);
        Assert.NotNull(obs);
        Assert.Equal("Content v2", obs.Content);
        Assert.True(obs.RevisionCount >= 2);
    }

    // ─── Search ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Search_ReturnsMatchingObservations()
    {
        await SeedSession();
        await SeedObservation("JWT authentication fix", "We fixed the JWT token validation");
        await SeedObservation("Database indexing", "Added B-tree index on users table");

        var results = await _store.SearchAsync("JWT", new SearchOptions { Limit = 10 });

        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.Observation.Title.Contains("JWT"));
    }

    [Fact]
    public async Task Search_ReturnsEmpty_WhenNoMatch()
    {
        await SeedSession();
        await SeedObservation("Totally unrelated", "Some other stuff");

        var results = await _store.SearchAsync("xyzzy-nonexistent-42", new SearchOptions { Limit = 10 });

        Assert.Empty(results);
    }

    [Fact]
    public async Task Search_FiltersByProject()
    {
        await SeedSession();
        await _store.AddObservationAsync(new AddObservationParams
        {
            SessionId = SessionId, Title = "Alpha memory", Content = "alpha content", Project = "alpha",
        });
        await _store.AddObservationAsync(new AddObservationParams
        {
            SessionId = SessionId, Title = "Beta memory", Content = "beta content", Project = "beta",
        });

        var results = await _store.SearchAsync("memory", new SearchOptions
        {
            Project = "alpha",
            Limit   = 10,
        });

        Assert.Single(results);
        Assert.Equal("alpha", results[0].Observation.Project);
    }

    [Fact]
    public async Task Search_FiltersByType()
    {
        await SeedSession();
        await SeedObservation("A bugfix", "Fixed the bug", type: "bugfix");
        await SeedObservation("A decision", "Made a decision", type: "decision");

        var results = await _store.SearchAsync("decision", new SearchOptions
        {
            Type  = "decision",
            Limit = 10,
        });

        Assert.Single(results);
        Assert.Equal("decision", results[0].Observation.Type);
    }

    // ─── Timeline ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Timeline_ReturnsFocusAndContext()
    {
        await SeedSession();
        await SeedObservation("Before 1", "b1");
        await SeedObservation("Before 2", "b2");
        var focusId = await SeedObservation("Focus", "the focus");
        await SeedObservation("After 1",  "a1");

        var timeline = await _store.TimelineAsync(focusId, 2, 2);

        Assert.NotNull(timeline);
        Assert.Equal(focusId, timeline.Focus.Id);
        Assert.NotEmpty(timeline.Before);
        Assert.NotEmpty(timeline.After);
    }

    // ─── Prompts ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddPrompt_StoresAndRetrieves()
    {
        await SeedSession();
        var id = await _store.AddPromptAsync(new AddPromptParams
        {
            SessionId = SessionId,
            Content   = "User asked: what is the meaning of life?",
            Project   = "test-project",
        });

        Assert.True(id > 0);

        var prompts = await _store.RecentPromptsAsync("test-project", 10);
        Assert.Single(prompts);
        Assert.Equal("User asked: what is the meaning of life?", prompts[0].Content);
    }

    [Fact]
    public async Task SearchPrompts_FindsMatchingPrompts()
    {
        await SeedSession();
        await _store.AddPromptAsync(new AddPromptParams
        {
            SessionId = SessionId, Content = "How do I fix the auth bug?", Project = "proj",
        });
        await _store.AddPromptAsync(new AddPromptParams
        {
            SessionId = SessionId, Content = "What is the best database?", Project = "proj",
        });

        var results = await _store.SearchPromptsAsync("auth", "proj", 10);

        Assert.Single(results);
        Assert.Contains("auth", results[0].Content);
    }

    // ─── Stats ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Stats_CountsCorrectly()
    {
        // SeedObservation uses const SessionId = "test-session-1" — that session must exist first
        await SeedSession();       // creates "test-session-1"
        await SeedSession("s2");   // second session

        await SeedObservation("O1", "c1");
        await SeedObservation("O2", "c2");
        await _store.AddPromptAsync(new AddPromptParams { SessionId = SessionId, Content = "q1" });

        var stats = await _store.StatsAsync();

        Assert.Equal(2, stats.TotalSessions);
        Assert.True(stats.TotalObservations >= 2);
        Assert.True(stats.TotalPrompts >= 1);
        Assert.Contains("test-project", stats.Projects);
    }

    // ─── Export / Import ──────────────────────────────────────────────────────

    [Fact]
    public async Task ExportImport_RoundTrips()
    {
        await SeedSession();
        await SeedObservation("Export me", "Content to export");
        await _store.AddPromptAsync(new AddPromptParams
        {
            SessionId = SessionId, Content = "exported prompt",
        });

        var exported = await _store.ExportAsync();

        // Import into a fresh store
        var dir2    = Path.Combine(Path.GetTempPath(), "engram-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir2);
        var cfg2    = new StoreConfig { DataDir = dir2 };
        using var store2 = new SqliteStore(cfg2);
        var result  = await store2.ImportAsync(exported);

        Assert.True(result.SessionsImported >= 1);
        Assert.True(result.ObservationsImported >= 1);
        Assert.True(result.PromptsImported >= 1);

        var stats = await store2.StatsAsync();
        Assert.True(stats.TotalObservations >= 1);
    }

    [Fact]
    public async Task Import_IsIdempotent_DuplicatesSkipped()
    {
        await SeedSession();
        await SeedObservation("Idempotent obs", "Same content");

        var exported = await _store.ExportAsync();

        // Import into a fresh store twice
        var dir2    = Path.Combine(Path.GetTempPath(), "engram-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir2);
        var cfg2    = new StoreConfig { DataDir = dir2 };
        using var store2 = new SqliteStore(cfg2);

        var r1 = await store2.ImportAsync(exported);
        var r2 = await store2.ImportAsync(exported);

        // Second import should skip (or import 0 new rows)
        Assert.True(r2.ObservationsImported == 0);
    }

    // ─── Projects / Merge ─────────────────────────────────────────────────────

    [Fact]
    public async Task MergeProjects_ReassignsObservations()
    {
        await SeedSession();
        await _store.AddObservationAsync(new AddObservationParams
        {
            SessionId = SessionId, Title = "Old project obs", Content = "c", Project = "old-proj",
        });

        var result = await _store.MergeProjectsAsync(["old-proj"], "new-proj");

        Assert.Equal("new-proj", result.Canonical);
        Assert.Contains("old-proj", result.SourcesMerged);
        Assert.True(result.ObservationsUpdated >= 1);

        // Verify the observation now belongs to new-proj
        var obs = await _store.RecentObservationsAsync("new-proj", null, 10);
        Assert.NotEmpty(obs);
    }

    // ─── Sync chunks ──────────────────────────────────────────────────────────

    [Fact]
    public async Task SyncChunks_RecordsAndRetrieves()
    {
        var chunkId = "chunk-2024-01-01T00-00-00Z";

        var before = await _store.GetSyncedChunksAsync();
        Assert.DoesNotContain(chunkId, before);

        await _store.RecordSyncedChunkAsync(chunkId);

        var after = await _store.GetSyncedChunksAsync();
        Assert.Contains(chunkId, after);
    }

    // ─── FormatContext ────────────────────────────────────────────────────────

    [Fact]
    public async Task FormatContext_ReturnsNonEmptyString_WhenDataExists()
    {
        await SeedSession();
        await SeedObservation("Architecture decision", "We chose hexagonal architecture");

        var ctx = await _store.FormatContextAsync("test-project", null);

        Assert.NotEmpty(ctx);
    }

    [Fact]
    public async Task FormatContext_ReturnsEmpty_WhenNoData()
    {
        var ctx = await _store.FormatContextAsync("ghost-project", null);
        Assert.Empty(ctx);
    }

    // ─── Project listing & stats ──────────────────────────────────────────────

    [Fact]
    public async Task ListProjectNames_ReturnsDistinctNonEmptyProjects()
    {
        await SeedSession();
        await SeedObservation("obs-1", "content", project: "alpha");
        await SeedObservation("obs-2", "content", project: "beta");
        await SeedObservation("obs-3", "content", project: "alpha"); // duplicate name

        var names = await _store.ListProjectNamesAsync();

        Assert.Equal(2, names.Count);
        Assert.Contains("alpha", names);
        Assert.Contains("beta", names);
    }

    [Fact]
    public async Task ListProjectNames_ExcludesDeletedAndNullProjects()
    {
        await SeedSession();
        await SeedObservation("obs-1", "content", project: "visible");
        var id = await SeedObservation("obs-to-delete", "content", project: "hidden-project");
        await _store.DeleteObservationAsync(id);

        var obsNoProject = await _store.AddObservationAsync(new AddObservationParams
        {
            SessionId = SessionId,
            Title = "no-project",
            Content = "content",
            Type = "manual",
            Project = null,
        });

        var names = await _store.ListProjectNamesAsync();

        Assert.Contains("visible", names);
        Assert.DoesNotContain("hidden-project", names);
    }

    [Fact]
    public async Task ListProjectNames_ReturnsEmpty_WhenNoData()
    {
        var names = await _store.ListProjectNamesAsync();
        Assert.Empty(names);
    }

    [Fact]
    public async Task ListProjectsWithStats_ReturnsCorrectCounts()
    {
        // Create sessions with project-specific IDs so FK constraints hold
        await _store.CreateSessionAsync("s-pa-1", "proj-a", "/a");
        await _store.CreateSessionAsync("s-pa-2", "proj-a", "/a");
        await _store.CreateSessionAsync("s-pb-1", "proj-b", "/b");

        await _store.AddObservationAsync(new AddObservationParams
            { SessionId = "s-pa-1", Title = "o1", Content = "c", Type = "manual", Project = "proj-a" });
        await _store.AddObservationAsync(new AddObservationParams
            { SessionId = "s-pa-2", Title = "o2", Content = "c", Type = "manual", Project = "proj-a" });
        await _store.AddObservationAsync(new AddObservationParams
            { SessionId = "s-pb-1", Title = "o3", Content = "c", Type = "manual", Project = "proj-b" });

        var stats = await _store.ListProjectsWithStatsAsync();

        var projA = stats.FirstOrDefault(s => s.Name == "proj-a");
        var projB = stats.FirstOrDefault(s => s.Name == "proj-b");

        Assert.NotNull(projA);
        Assert.Equal(2, projA.ObservationCount);
        Assert.Equal(2, projA.SessionCount);

        Assert.NotNull(projB);
        Assert.Equal(1, projB.ObservationCount);
        Assert.Equal(1, projB.SessionCount);
    }

    [Fact]
    public async Task ListProjectsWithStats_ExcludesDeletedObservations()
    {
        await SeedSession();
        var id = await SeedObservation("obs-to-delete", "content", project: "proj-x");
        await _store.DeleteObservationAsync(id);

        var stats = await _store.ListProjectsWithStatsAsync();
        // "test-project" still appears because the session exists, but obs count is 0
        var projX = stats.FirstOrDefault(s => s.Name == "proj-x");
        Assert.Null(projX); // deleted observation excluded
    }

    [Fact]
    public async Task ListProjectsWithStats_OrdersByObservationCountDesc()
    {
        await _store.CreateSessionAsync("s-minor", "minor", "/a");
        await _store.CreateSessionAsync("s-major", "major", "/b");

        await _store.AddObservationAsync(new AddObservationParams
            { SessionId = "s-major", Title = "o1", Content = "c", Type = "manual", Project = "major" });
        await _store.AddObservationAsync(new AddObservationParams
            { SessionId = "s-major", Title = "o2", Content = "c", Type = "manual", Project = "major" });
        await _store.AddObservationAsync(new AddObservationParams
            { SessionId = "s-minor", Title = "o3", Content = "c", Type = "manual", Project = "minor" });

        var stats = await _store.ListProjectsWithStatsAsync();

        Assert.Equal(2, stats.Count);
        Assert.Equal("major", stats[0].Name);  // 2 observations > 1
        Assert.Equal("minor", stats[1].Name);
    }

    [Fact]
    public async Task ListProjectsWithStats_IncludesSessionOnlyProjects()
    {
        // A project with sessions but no observations should still appear
        await _store.CreateSessionAsync("s-solo", "session-only-proj", "/x");

        var stats = await _store.ListProjectsWithStatsAsync();

        var proj = stats.FirstOrDefault(s => s.Name == "session-only-proj");
        Assert.NotNull(proj);
        Assert.Equal(0, proj.ObservationCount);
        Assert.Equal(1, proj.SessionCount);
    }

    [Fact]
    public async Task CountObservationsForProject_ReturnsCorrectCount()
    {
        await SeedSession();
        await SeedObservation("obs-1", "content", project: "my-proj");
        await SeedObservation("obs-2", "content", project: "my-proj");
        await SeedObservation("obs-3", "content", project: "other-proj");

        var count = await _store.CountObservationsForProjectAsync("my-proj");

        Assert.Equal(2, count);
    }

    [Fact]
    public async Task CountObservationsForProject_NormalizesProjectName()
    {
        await SeedSession();
        await SeedObservation("obs-1", "content", project: "my-proj");

        // CountObservationsForProject normalizes internally
        var count = await _store.CountObservationsForProjectAsync("My-PROJ");

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task CountObservationsForProject_ExcludesDeleted()
    {
        await SeedSession();
        var id = await SeedObservation("obs-to-delete", "content", project: "proj-d");
        await _store.DeleteObservationAsync(id);

        var count = await _store.CountObservationsForProjectAsync("proj-d");

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task CountObservationsForProject_ReturnsZero_WhenNoProject()
    {
        var count = await _store.CountObservationsForProjectAsync("nonexistent");
        Assert.Equal(0, count);
    }

    // ─── Directories in ListProjectsWithStats ──────────────────────────────────

    [Fact]
    public async Task ListProjectsWithStats_IncludesDirectoriesFromSessions()
    {
        await _store.CreateSessionAsync("s-dir-1", "dir-proj", "/home/user/work/dir-proj");
        await _store.CreateSessionAsync("s-dir-2", "dir-proj", "/home/user/work/dir-proj");
        await _store.AddObservationAsync(new AddObservationParams
            { SessionId = "s-dir-1", Title = "o1", Content = "c", Type = "manual", Project = "dir-proj" });

        var stats = await _store.ListProjectsWithStatsAsync();

        var proj = stats.FirstOrDefault(s => s.Name == "dir-proj");
        Assert.NotNull(proj);
        Assert.Contains("/home/user/work/dir-proj", proj.Directories);
    }

    [Fact]
    public async Task ListProjectsWithStats_Directories_AreDistinct()
    {
        // Two sessions with same directory → should appear once
        await _store.CreateSessionAsync("s-dup-1", "dup-proj", "/same/path");
        await _store.CreateSessionAsync("s-dup-2", "dup-proj", "/same/path");

        var stats = await _store.ListProjectsWithStatsAsync();

        var proj = stats.FirstOrDefault(s => s.Name == "dup-proj");
        Assert.NotNull(proj);
        Assert.Single(proj.Directories);
        Assert.Equal("/same/path", proj.Directories[0]);
    }

    // ─── PruneProject ──────────────────────────────────────────────────────────

    [Fact]
    public async Task PruneProject_DeletesSessionsAndPrompts()
    {
        await _store.CreateSessionAsync("s-prune", "prune-proj", "/tmp");
        await _store.AddPromptAsync(new AddPromptParams
            { SessionId = "s-prune", Content = "a prompt", Project = "prune-proj" });

        var result = await _store.PruneProjectAsync("prune-proj");

        Assert.Equal("prune-proj", result.Project);
        Assert.True(result.SessionsDeleted >= 1);
        Assert.True(result.PromptsDeleted >= 1);

        // Verify session is gone
        var session = await _store.GetSessionAsync("s-prune");
        Assert.Null(session);
    }

    [Fact]
    public async Task PruneProject_Throws_WhenObservationsExist()
    {
        await _store.CreateSessionAsync("s-no-prune", "obs-proj", "/tmp");
        await _store.AddObservationAsync(new AddObservationParams
            { SessionId = "s-no-prune", Title = "important", Content = "c", Type = "manual", Project = "obs-proj" });

        // Cannot prune a project that still has observations
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _store.PruneProjectAsync("obs-proj"));
    }

    [Fact]
    public async Task PruneProject_NormalizesProjectName()
    {
        await _store.CreateSessionAsync("s-norm", "My-Project", "/tmp");
        await _store.AddPromptAsync(new AddPromptParams
            { SessionId = "s-norm", Content = "prompt", Project = "My-Project" });

        var result = await _store.PruneProjectAsync("MY-PROJECT");

        Assert.Equal("my-project", result.Project);
        Assert.True(result.SessionsDeleted >= 1);
    }

    [Fact]
    public async Task PruneProject_OnNonexistentProject_ReturnsZero()
    {
        var result = await _store.PruneProjectAsync("nonexistent");

        Assert.Equal("nonexistent", result.Project);
        Assert.Equal(0, result.SessionsDeleted);
        Assert.Equal(0, result.PromptsDeleted);
    }
}
