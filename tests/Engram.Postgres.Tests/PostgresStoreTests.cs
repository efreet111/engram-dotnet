using System;
using System.Text.Json;
using Engram.Store;
using Npgsql;
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
        await ResetAsync(); // Clean DB at startup
    }

    public async Task DisposeAsync()
    {
        Store?.Dispose();
        await _container.DisposeAsync();
    }

    /// <summary>
    /// Clean up all data between tests to ensure isolation.
    /// </summary>
    public async Task ResetAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        // Simple cleanup - delete from leaf tables first to avoid FK issues
        await using var cmd = new NpgsqlCommand(@"
            DELETE FROM cloud_mutations;
            DELETE FROM sync_mutations;
            DELETE FROM sync_enrolled_projects;
            DELETE FROM sync_chunks;
            DELETE FROM observations;
            DELETE FROM user_prompts;
            DELETE FROM sessions;
            DELETE FROM sync_state WHERE target_key != 'cloud';
        ", conn);
        await cmd.ExecuteNonQueryAsync();
    }
}

[Trait("Category", "RequiresDocker")]
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

    private async Task<int> CountSoftDeletedPromptsAsync(string sessionId)
    {
        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM user_prompts WHERE session_id = @session AND deleted_at IS NOT NULL",
            conn);
        cmd.Parameters.AddWithValue("session", sessionId);
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result ?? 0);
    }

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
        await _fixture.ResetAsync();
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

        var prompts = await _fixture.Store.RecentPromptsAsync("test-project", null, 10);

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

    // ─── Delete Session / Prompt ─────────────────────────────────────────────

    [Fact]
    public async Task DeleteSession_EmptySession_Succeeds()
    {
        var sessionId = $"sess-empty-{Guid.NewGuid():N}";
        await _fixture.Store.CreateSessionAsync(sessionId, "test-project", "/tmp");

        await _fixture.Store.DeleteSessionAsync(sessionId);

        var session = await _fixture.Store.GetSessionAsync(sessionId);
        Assert.Null(session);
    }

    [Fact]
    public async Task DeleteSession_NotFound_Throws()
    {
        await Assert.ThrowsAsync<SessionNotFoundException>(
            () => _fixture.Store.DeleteSessionAsync("does-not-exist"));
    }

    [Fact]
    public async Task DeleteSession_DeletesAssociatedPrompts()
    {
        await _fixture.ResetAsync();
        var sessionId = $"sess-with-prompts-{Guid.NewGuid():N}";
        await _fixture.Store.CreateSessionAsync(sessionId, "test-project", "/tmp");
        await _fixture.Store.AddPromptAsync(new AddPromptParams
        {
            SessionId = sessionId,
            Content = "Prompt 1",
            Project = "test-project",
        });
        await _fixture.Store.AddPromptAsync(new AddPromptParams
        {
            SessionId = sessionId,
            Content = "Prompt 2",
            Project = "test-project",
        });

        await _fixture.Store.DeleteSessionAsync(sessionId);

        var session = await _fixture.Store.GetSessionAsync(sessionId);
        Assert.Null(session);

        var promptsAfter = await _fixture.Store.RecentPromptsAsync("test-project", null, 100);
        Assert.Empty(promptsAfter);
        var deletedCount = await CountSoftDeletedPromptsAsync(sessionId);
        Assert.Equal(2, deletedCount);
    }

    [Fact]
    public async Task DeleteSession_BlockedBySoftDeletedObservations()
    {
        // This test verifies the fix: soft-deleted observations should NOT block session delete
        var sessionId = $"sess-soft-del-{Guid.NewGuid():N}";
        await _fixture.Store.CreateSessionAsync(sessionId, "test-project", "/tmp");
        var obsId = await _fixture.Store.AddObservationAsync(new AddObservationParams
        {
            SessionId = sessionId,
            Title = "To delete",
            Content = "Content",
            Type = "manual",
            Project = "test-project",
        });

        await _fixture.Store.DeleteObservationAsync(obsId);

        // After fix: delete should succeed (only active observations block delete)
        await _fixture.Store.DeleteSessionAsync(sessionId);
    }

    [Fact]
    public async Task DeleteSession_ActiveObservations_StillBlocked()
    {
        // Verify active observations still block delete
        var sessionId = $"sess-active-{Guid.NewGuid():N}";
        await _fixture.Store.CreateSessionAsync(sessionId, "test-project", "/tmp");
        await _fixture.Store.AddObservationAsync(new AddObservationParams
        {
            SessionId = sessionId,
            Title = "Active obs",
            Content = "Content",
            Type = "manual",
            Project = "test-project",
        });

        // Active observations should still block delete
        await Assert.ThrowsAsync<SessionDeleteBlockedException>(
            () => _fixture.Store.DeleteSessionAsync(sessionId));
    }

    [Fact]
    public async Task DeletePrompt_Success_SoftDeletes()
    {
        var sessionId = $"sess-prompt-del-{Guid.NewGuid():N}";
        await _fixture.Store.CreateSessionAsync(sessionId, "test-project", "/tmp");
        var promptId = await _fixture.Store.AddPromptAsync(new AddPromptParams
        {
            SessionId = sessionId,
            Content = "To be deleted",
            Project = "test-project",
        });

        var promptsBefore = await _fixture.Store.RecentPromptsAsync("test-project", null, 100);
        Assert.Contains(promptsBefore, p => p.Id == promptId);

        await _fixture.Store.DeletePromptAsync(promptId);

        var promptsAfter = await _fixture.Store.RecentPromptsAsync("test-project", null, 100);
        Assert.DoesNotContain(promptsAfter, p => p.Id == promptId);
    }

    [Fact]
    public async Task DeletePrompt_NotFound_Throws()
    {
        await Assert.ThrowsAsync<PromptNotFoundException>(
            () => _fixture.Store.DeletePromptAsync(999_999));
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
        await _fixture.ResetAsync();
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
        await _fixture.ResetAsync();
        await SeedSession();
        var obsId = await SeedObservation("obs-old", "content", project: "old-proj");

        var result = await _fixture.Store.MergeProjectsAsync(new[] { "old-proj" }, "new-proj");

        Assert.Equal("new-proj", result.Canonical);
        Assert.True(result.ObservationsUpdated >= 1);

        var obs = await _fixture.Store.GetObservationAsync(obsId);
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
        await _fixture.ResetAsync();
        await SeedSession();
        await _fixture.Store.AddObservationAsync(new AddObservationParams
        {
            SessionId = SessionId, Title = "Auth model", Content = "JWT tokens",
            Type = "architecture", Project = "proj", TopicKey = "architecture/auth-model",
        });

        var results = await _fixture.Store.SearchAsync("architecture/auth-model", new SearchOptions { Limit = 10 });

        Assert.NotEmpty(results);
        // Topic-key shortcut should have rank = 10000 (boosted for Postgres DESC sort)
        Assert.Equal(10000.0, results[0].Rank);
    }

    // ─── Server-side mutation apply (ENG-425) ─────────────────────────────────────────

    [Fact]
    public async Task PushMutation_SessionUpsert_AppliesToDataStore()
    {
        // PM-1: Push de session upsert genera entrada consultable
        var sessionKey = $"pm1-session-{Guid.NewGuid():N}";
        var payload = JsonSerializer.Serialize(new
        {
            project = "test-pm1",
            directory = "/test/dir",
            started_at = "2026-06-07T10:00:00Z",
            summary = "Test session"
        });

        var entries = new[]
        {
            new MutationEntry("test-pm1", "session", sessionKey, "upsert", payload)
        };

        await _fixture.Store.InsertMutationBatchAsync(entries);

        // Verify session was applied to data store
        var session = await _fixture.Store.GetSessionAsync(sessionKey);
        Assert.NotNull(session);
        Assert.Equal(sessionKey, session.Id);
        Assert.Equal("test-pm1", session.Project);
        Assert.Equal("/test/dir", session.Directory);
    }

    [Fact]
    public async Task PushMutation_ObservationUpsert_AppliesToDataStore()
    {
        // PM-2: Push de observation upsert genera entrada consultable por sync_id
        await _fixture.ResetAsync();
        var sessionKey = $"pm2-session-{Guid.NewGuid():N}";

        // First push the session via mutation (PM-1)
        var sessionPayload = JsonSerializer.Serialize(new
        {
            project = "test-pm2",
            directory = "/test",
            started_at = "2026-06-07T10:00:00Z"
        });
        await _fixture.Store.InsertMutationBatchAsync(new[]
        {
            new MutationEntry("test-pm2", "session", sessionKey, "upsert", sessionPayload)
        });

        // Now push the observation
        var obsKey = $"pm2-obs-{Guid.NewGuid():N}";
        var obsPayload = JsonSerializer.Serialize(new
        {
            sessionId = sessionKey,  // For FK, use session's sync_id as the reference
            type = "decision",
            title = "Architecture decision",
            content = "We chose hexagonal architecture",
            project = "test-pm2",
            scope = "project",
            occurred_at = "2026-06-07T11:00:00Z"
        });

        var entries = new[]
        {
            new MutationEntry("test-pm2", "observation", obsKey, "upsert", obsPayload)
        };

        await _fixture.Store.InsertMutationBatchAsync(entries);

        // Verify observation exists via direct DB query
        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT title FROM observations WHERE sync_id = @syncId",
            conn);
        cmd.Parameters.AddWithValue("@syncId", obsKey);
        var title = await cmd.ExecuteScalarAsync();
        Assert.NotNull(title);
        Assert.Equal("Architecture decision", title);
    }

    [Fact]
    public async Task PushMutation_ObservationDelete_SoftDeletes()
    {
        // PM-3: Observation delete soft-deletes (no elimina fila)
        await _fixture.ResetAsync();
        var sessionKey = $"pm3-session-{Guid.NewGuid():N}";

        // Push session first
        var sessionPayload = JsonSerializer.Serialize(new
        {
            project = "test-pm3",
            directory = "/test",
            started_at = "2026-06-07T10:00:00Z"
        });
        await _fixture.Store.InsertMutationBatchAsync(new[]
        {
            new MutationEntry("test-pm3", "session", sessionKey, "upsert", sessionPayload)
        });

        // Create observation via direct API
        var obsKey = $"pm3-obs-{Guid.NewGuid():N}";
        var obsId = await _fixture.Store.AddObservationAsync(new AddObservationParams
        {
            SessionId = sessionKey,
            Title = "To delete",
            Content = "Content",
            Type = "manual",
            Project = "test-pm3"
        });

        // Get the sync_id from the observation
        var obs = await _fixture.Store.GetObservationAsync(obsId);
        Assert.NotNull(obs);
        var syncId = obs.SyncId ?? obsKey;

        // Push delete mutation
        var deletePayload = JsonSerializer.Serialize(new { });
        var entries = new[]
        {
            new MutationEntry("test-pm3", "observation", syncId, "delete", deletePayload)
        };

        await _fixture.Store.InsertMutationBatchAsync(entries);

        // Verify soft-delete
        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT deleted_at FROM observations WHERE sync_id = @syncId AND deleted_at IS NOT NULL",
            conn);
        cmd.Parameters.AddWithValue("@syncId", syncId);
        var deletedAt = await cmd.ExecuteScalarAsync();
        Assert.NotNull(deletedAt);
    }

    [Fact]
    public async Task PushMutation_PromptUpsert_AppliesToDataStore()
    {
        // PM-4: Prompt upsert aplica con FK referenciando session existente
        await _fixture.ResetAsync();
        var sessionKey = $"pm4-session-{Guid.NewGuid():N}";

        // Push session first
        var sessionPayload = JsonSerializer.Serialize(new
        {
            project = "test-pm4",
            directory = "/test",
            started_at = "2026-06-07T10:00:00Z"
        });
        await _fixture.Store.InsertMutationBatchAsync(new[]
        {
            new MutationEntry("test-pm4", "session", sessionKey, "upsert", sessionPayload)
        });

        // Now push the prompt
        var promptKey = $"pm4-prompt-{Guid.NewGuid():N}";
        var payload = JsonSerializer.Serialize(new
        {
            sessionId = sessionKey,  // For FK, use session's sync_id as reference
            content = "How do I implement JWT?",
            project = "test-pm4",
            occurred_at = "2026-06-07T12:00:00Z"
        });

        var entries = new[]
        {
            new MutationEntry("test-pm4", "prompt", promptKey, "upsert", payload)
        };

        await _fixture.Store.InsertMutationBatchAsync(entries);

        // Verify prompt was applied
        var prompts = await _fixture.Store.RecentPromptsAsync("test-pm4", null, 10);
        Assert.NotEmpty(prompts);
        Assert.Contains(prompts, p => p.Content.Contains("JWT"));
    }

    [Fact]
    public async Task PushMutation_Batch_Atomic_CommitsTogether()
    {
        // PM-5: Batch atómico - both session and observation commit together
        await _fixture.ResetAsync();
        var sessionKey = $"pm5-session-{Guid.NewGuid():N}";
        var obsKey = $"pm5-obs-{Guid.NewGuid():N}";

        // Batch with valid session + observation
        var sessionPayload = JsonSerializer.Serialize(new
        {
            project = "test-pm5",
            directory = "/test",
            started_at = "2026-06-07T10:00:00Z"
        });
        var obsPayload = JsonSerializer.Serialize(new
        {
            sessionId = sessionKey,
            type = "manual",
            title = "Should exist",
            content = "Content",
            project = "test-pm5"
        });

        var entries = new[]
        {
            new MutationEntry("test-pm5", "session", sessionKey, "upsert", sessionPayload),
            new MutationEntry("test-pm5", "observation", obsKey, "upsert", obsPayload)
        };

        // Should succeed - both apply
        await _fixture.Store.InsertMutationBatchAsync(entries);

        // Verify both were applied
        var session = await _fixture.Store.GetSessionAsync(sessionKey);
        Assert.NotNull(session);

        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT title FROM observations WHERE sync_id = @syncId", conn);
        cmd.Parameters.AddWithValue("@syncId", obsKey);
        var title = await cmd.ExecuteScalarAsync();
        Assert.NotNull(title);
    }

    [Fact]
    public async Task PushMutation_Batch_Atomic_RollbackOnFkViolation()
    {
        // PM-5: Batch atómico con FK violation debe hacer rollback completo
        await _fixture.ResetAsync();
        var obsKey = $"pm5-rollback-obs-{Guid.NewGuid():N}";
        var nonExistentSessionKey = $"non-existent-session-{Guid.NewGuid():N}";

        // Batch with observation referencing NON-EXISTENT-SESSION
        var obsPayload = JsonSerializer.Serialize(new
        {
            sessionId = nonExistentSessionKey,
            type = "manual",
            title = "Should NOT exist",
            content = "Content",
            project = "test-pm5-rollback"
        });

        var entries = new[]
        {
            new MutationEntry("test-pm5-rollback", "observation", obsKey, "upsert", obsPayload)
        };

        // Attempt should fail due to FK violation
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => _fixture.Store.InsertMutationBatchAsync(entries));

        // Verify observation was NOT inserted (atomic rollback worked)
        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await using var checkCmd = new NpgsqlCommand("SELECT title FROM observations WHERE sync_id = @syncId", conn);
        checkCmd.Parameters.AddWithValue("@syncId", obsKey);
        var title = await checkCmd.ExecuteScalarAsync();
        Assert.Null(title);
    }

    [Fact]
    public async Task PushMutation_DuplicateUpsert_IsIdempotent()
    {
        // PM-6: Duplicate upsert es idempotente (no falla, actualiza)
        await _fixture.ResetAsync();
        var sessionKey = $"pm6-session-{Guid.NewGuid():N}";
        var payload1 = JsonSerializer.Serialize(new
        {
            project = "test-pm6",
            directory = "/dir1",
            summary = "Original"
        });
        var payload2 = JsonSerializer.Serialize(new
        {
            project = "test-pm6",
            directory = "/dir2",
            summary = "Updated"
        });

        // First push
        await _fixture.Store.InsertMutationBatchAsync(new[]
        {
            new MutationEntry("test-pm6", "session", sessionKey, "upsert", payload1)
        });

        // Second push (same entity_key, different values)
        await _fixture.Store.InsertMutationBatchAsync(new[]
        {
            new MutationEntry("test-pm6", "session", sessionKey, "upsert", payload2)
        });

        // Verify updated (not error)
        var session = await _fixture.Store.GetSessionAsync(sessionKey);
        Assert.NotNull(session);
        Assert.Equal("/dir2", session.Directory);
        Assert.Equal("Updated", session.Summary);
    }

    [Fact]
    public async Task PushMutation_RecentObservations_IncludesSyncedData()
    {
        // PM-7: Consultar observations desde REST muestra datos sync-eados
        await _fixture.ResetAsync();
        var sessionKey = $"pm7-session-{Guid.NewGuid():N}";
        var payload = JsonSerializer.Serialize(new
        {
            project = "test-pm7",
            directory = "/test",
            started_at = "2026-06-07T10:00:00Z"
        });
        var obsPayload = JsonSerializer.Serialize(new
        {
            sessionId = sessionKey,
            type = "decision",
            title = "Arquitectura decisions",
            content = "We chose hexagonal architecture",
            project = "test-pm7",
            scope = "project",
            occurred_at = "2026-06-07T11:00:00Z"
        });

        // Push session + observation
        await _fixture.Store.InsertMutationBatchAsync(new[]
        {
            new MutationEntry("test-pm7", "session", sessionKey, "upsert", payload),
            new MutationEntry("test-pm7", "observation", $"pm7-obs-{Guid.NewGuid():N}", "upsert", obsPayload)
        });

        // Query via search (simulates GET /observations/recent)
        var results = await _fixture.Store.SearchAsync("Arquitectura", new SearchOptions { Project = "test-pm7", Limit = 10 });
        Assert.NotEmpty(results);
    }
}
