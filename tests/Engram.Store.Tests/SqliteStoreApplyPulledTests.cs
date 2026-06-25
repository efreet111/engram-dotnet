using Engram.Store;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Engram.Store.Tests;

/// <summary>
/// Unit tests for SqliteStore.ApplyPulledMutationAsync and its private Apply* methods.
/// Covers session, observation, and prompt upsert/delete operations.
/// </summary>
public class SqliteStoreApplyPulledTests : IDisposable
{
    private readonly SqliteStore _store;
    private readonly string _tempDir;
    private const string SessionId = "test-session-apply";
    private const string Project = "test-project";

    public SqliteStoreApplyPulledTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "engram-apply-tests", Guid.NewGuid().ToString("N"));
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

    private async Task SeedSession(string id = SessionId)
        => await _store.CreateSessionAsync(id, Project, "/tmp");

    private async Task<long> SeedObservation(string syncId, string title = "Test observation")
    {
        await SeedSession();
        var id = await _store.AddObservationAsync(new AddObservationParams
        {
            SessionId = SessionId,
            Title = title,
            Content = "Content here",
            Type = "manual",
            Project = Project,
        });

        // Set sync_id directly in DB (AddObservationAsync doesn't set it)
        if (syncId is { })
        {
            var dbPath = Path.Combine(_tempDir, "engram.db");
            using var conn = new SqliteConnection($"Data Source={dbPath}");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE observations SET sync_id = @syncId WHERE id = @id";
            cmd.Parameters.AddWithValue("@syncId", syncId);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        return id;
    }

    private async Task<long> SeedPrompt(string content = "Test prompt")
    {
        await SeedSession();
        return await _store.AddPromptAsync(new AddPromptParams
        {
            SessionId = SessionId,
            Content = content,
            Project = Project,
        });
    }

    private Task ApplyMutation(string entity, string entityKey, string op, string payload)
    {
        var mutation = new SyncMutation(
            1,
            entityKey,
            entity,
            entityKey,
            op,
            payload,
            "test",
            Project,
            DateTime.UtcNow,
            null);
        return _store.ApplyPulledMutationAsync(entityKey, mutation);
    }

    private int CountDeferred()
    {
        var dbPath = Path.Combine(_tempDir, "engram.db");
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sync_apply_deferred";
        return System.Convert.ToInt32(cmd.ExecuteScalar());
    }

    private long? GetObservationIdBySyncId(string syncId)
    {
        var dbPath = Path.Combine(_tempDir, "engram.db");
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id FROM observations WHERE sync_id = @syncId AND deleted_at IS NULL";
        cmd.Parameters.AddWithValue("@syncId", syncId);
        var result = cmd.ExecuteScalar();
        return result == DBNull.Value ? null : (long?)Convert.ToInt64(result);
    }

    private string? GetPromptSyncIdBySessionId(string sessionId)
    {
        var dbPath = Path.Combine(_tempDir, "engram.db");
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT sync_id FROM user_prompts WHERE session_id = @sessionId AND deleted_at IS NULL ORDER BY id DESC LIMIT 1";
        cmd.Parameters.AddWithValue("@sessionId", sessionId);
        var result = cmd.ExecuteScalar();
        return result == DBNull.Value ? null : (string?)result;
    }

    // ─── FR-001: ApplySessionUpsert ───────────────────────────────────────────────

    [Fact]
    public async Task ApplySessionUpsert_NewSession_StoresSession()
    {
        // Given: a valid session upsert mutation
        var payload = """{"id": "new-session", "project": "proj", "directory": "/home", "summary": "Test"}""";

        // When: ApplyPulledMutationAsync is called
        await ApplyMutation("session", "new-session", "upsert", payload);

        // Then: session is stored and retrievable
        var session = await _store.GetSessionAsync("new-session");
        Assert.NotNull(session);
        Assert.Equal("new-session", session.Id);
        Assert.Equal("proj", session.Project);
        Assert.Equal("/home", session.Directory);
        Assert.Equal("Test", session.Summary);
    }

    [Fact]
    public async Task ApplySessionUpsert_ExistingSession_UpdatesSummary()
    {
        // Given: session already exists
        await SeedSession("existing-session");

        // When: upsert mutation with updated summary is applied
        var payload = """{"id": "existing-session", "project": "proj", "directory": "/home", "summary": "Updated"}""";
        await ApplyMutation("session", "existing-session", "upsert", payload);

        // Then: summary is updated
        var session = await _store.GetSessionAsync("existing-session");
        Assert.NotNull(session);
        Assert.Equal("Updated", session.Summary);
    }

    [Fact]
    public async Task ApplySessionUpsert_MalformedPayload_DoesNotThrow()
    {
        // Given: a malformed JSON payload
        var payload = """{"id": "bad-session", "project": """; // invalid JSON

        // When: ApplyPulledMutationAsync is called
        var ex = await Record.ExceptionAsync(() => ApplyMutation("session", "bad-session", "upsert", payload));

        // Then: no exception is thrown and session is not created
        Assert.Null(ex);
        var session = await _store.GetSessionAsync("bad-session");
        Assert.Null(session);
    }

    // ─── FR-002: ApplyObservationUpsert ─────────────────────────────────────────

    [Fact]
    public async Task ApplyObservationUpsert_ExistingObservation_UpdatesTitle()
    {
        // Given: observation exists
        var obsId = await SeedObservation("obs-existing", "Original title");

        // When: upsert mutation with new title is applied
        var payload = """{"session_id": "test-session-apply", "sync_id": "obs-existing", "title": "New title", "content": "Content", "type": "manual", "project": "test-project"}""";
        await ApplyMutation("observation", "obs-existing", "upsert", payload);

        // Then: title is updated
        var obs = await _store.GetObservationAsync(obsId);
        Assert.NotNull(obs);
        Assert.Equal("New title", obs.Title);
    }

    [Fact]
    public async Task ApplyObservationUpsert_NewObservation_StoresObservation()
    {
        // Given: session exists (FK required)
        await SeedSession();

        // When: upsert mutation for new observation is applied
        var payload = """{"session_id": "test-session-apply", "sync_id": "obs-new", "title": "New obs", "content": "Content", "type": "manual", "project": "test-project"}""";
        await ApplyMutation("observation", "obs-new", "upsert", payload);

        // Then: observation is stored
        var obsId = GetObservationIdBySyncId("obs-new");
        Assert.NotNull(obsId);
        var obs = await _store.GetObservationAsync(obsId.Value);
        Assert.NotNull(obs);
        Assert.Equal("New obs", obs.Title);
    }

    [Fact]
    public async Task ApplyObservationUpsert_MissingSession_DefersMutation()
    {
        // Given: no session with id "missing-session"
        // When: upsert mutation with missing FK is applied
        var payload = """{"session_id": "missing-session", "sync_id": "obs-deferred", "title": "Title", "content": "Content", "type": "manual"}""";
        await ApplyMutation("observation", "obs-deferred", "upsert", payload);

        // Then: mutation is deferred
        var deferred = CountDeferred();
        Assert.Equal(1, deferred);
    }

    [Fact]
    public async Task ApplyObservationUpsert_MalformedPayload_DoesNotThrow()
    {
        // Given: malformed JSON payload
        var payload = """{"session_id": "test-session-apply", "sync_id": "obs-bad", "title": """;

        // When: ApplyPulledMutationAsync is called
        var ex = await Record.ExceptionAsync(() => ApplyMutation("observation", "obs-bad", "upsert", payload));

        // Then: no exception thrown
        Assert.Null(ex);
    }

    // ─── FR-003: ApplyObservationDelete ─────────────────────────────────────

    [Fact]
    public async Task ApplyObservationDelete_SoftDeletesObservation()
    {
        // Given: observation exists
        var obsId = await SeedObservation("obs-to-delete");

        // When: delete mutation is applied
        var payload = """{"sync_id": "obs-to-delete"}""";
        await ApplyMutation("observation", "obs-to-delete", "delete", payload);

        // Then: observation is soft-deleted (not in active results)
        var obs = await _store.GetObservationAsync(obsId);
        Assert.Null(obs);
    }

    [Fact]
    public async Task ApplyObservationDelete_IdempotentForNonexistent_DoesNotThrow()
    {
        // Given: no observation with sync_id
        // When: delete for nonexistent is applied
        var payload = """{"sync_id": "nonexistent-obs"}""";
        var ex = await Record.ExceptionAsync(() => ApplyMutation("observation", "nonexistent-obs", "delete", payload));

        // Then: no error
        Assert.Null(ex);
    }

    // ─── FR-004: ApplyPromptUpsert ────────────────────────────────────────────

    [Fact]
    public async Task ApplyPromptUpsert_NewPrompt_StoresPrompt()
    {
        // Given: session exists
        await SeedSession();

        // When: upsert mutation for new prompt is applied
        var payload = """{"session_id": "test-session-apply", "sync_id": "prompt-new", "content": "Prompt content", "project": "test-project"}""";
        await ApplyMutation("prompt", "prompt-new", "upsert", payload);

        // Then: prompt is stored
        var prompts = await _store.RecentPromptsAsync(Project, null, 10);
        var prompt = prompts.FirstOrDefault(p => p.SyncId == "prompt-new");
        Assert.NotNull(prompt);
        Assert.Equal("Prompt content", prompt.Content);
    }

    [Fact]
    public async Task ApplyPromptUpsert_ExistingPrompt_UpdatesContent()
    {
        // Given: prompt exists
        await SeedPrompt();

        // When: upsert with updated content is applied
        var payload = """{"session_id": "test-session-apply", "sync_id": "prompt-existing", "content": "Updated content", "project": "test-project"}""";
        await ApplyMutation("prompt", "prompt-existing", "upsert", payload);

        // Then: content is updated
        var prompts = await _store.RecentPromptsAsync(Project, null, 10);
        var prompt = prompts.FirstOrDefault(p => p.SyncId == "prompt-existing");
        Assert.NotNull(prompt);
        Assert.Equal("Updated content", prompt.Content);
    }

    [Fact]
    public async Task ApplyPromptUpsert_MissingSession_DefersMutation()
    {
        // Given: no session exists
        // When: prompt upsert with missing FK is applied
        var payload = """{"session_id": "missing-session", "sync_id": "prompt-deferred", "content": "Content"}""";
        await ApplyMutation("prompt", "prompt-deferred", "upsert", payload);

        // Then: mutation is deferred
        var deferred = CountDeferred();
        Assert.Equal(1, deferred);
    }

    // ─── FR-005: ApplyPromptDelete ─────────────────────────────────────────────

    [Fact]
    public async Task ApplyPromptDelete_SoftDeletesPrompt()
    {
        // Given: prompt exists
        await SeedPrompt();

        // When: delete mutation is applied
        var promptSyncId = GetPromptSyncIdBySessionId(SessionId);
        Assert.NotNull(promptSyncId);
        var payload = "{\"sync_id\": \"" + promptSyncId + "\"}";
        await ApplyMutation("prompt", promptSyncId, "delete", payload);

        // Then: prompt is soft-deleted
        var prompts = await _store.RecentPromptsAsync(Project, null, 10);
        var prompt = prompts.FirstOrDefault(p => p.SyncId == promptSyncId);
        Assert.Null(prompt);
    }

    [Fact]
    public async Task ApplyPromptDelete_Idempotent_DoesNotThrow()
    {
        // Given: no prompt exists
        // When: delete for nonexistent is applied
        var payload = """{"sync_id": "nonexistent-prompt"}""";
        var ex = await Record.ExceptionAsync(() => ApplyMutation("prompt", "nonexistent-prompt", "delete", payload));

        // Then: no error
        Assert.Null(ex);
    }

    // ─── FR-007: FK insert verification (SnakeCaseLower fix) ────────────────

    [Fact]
    public async Task ApplyObservationUpsert_WithExistingSession_NoDeferral()
    {
        // Given: session exists
        await SeedSession("session-fk-test");

        // When: observation upsert with valid FK is applied
        var payload = """{"session_id": "session-fk-test", "sync_id": "obs-fk-verify", "title": "Title", "content": "Content", "type": "manual", "project": "test-project"}""";
        await ApplyMutation("observation", "obs-fk-verify", "upsert", payload);

        // Then: observation is stored directly (not deferred)
        var deferred = CountDeferred();
        Assert.Equal(0, deferred);
        var obsId = GetObservationIdBySyncId("obs-fk-verify");
        Assert.NotNull(obsId);
    }

    [Fact]
    public async Task ApplyPromptUpsert_WithExistingSession_NoDeferral()
    {
        // Given: session exists
        await SeedSession("session-fk-test-prompt");

        // When: prompt upsert with valid FK is applied
        var payload = """{"session_id": "session-fk-test-prompt", "sync_id": "prompt-fk-verify", "content": "Content", "project": "test-project"}""";
        await ApplyMutation("prompt", "prompt-fk-verify", "upsert", payload);

        // Then: prompt is stored directly (not deferred)
        var deferred = CountDeferred();
        Assert.Equal(0, deferred);
        var prompts = await _store.RecentPromptsAsync(Project, null, 10);
        var prompt = prompts.FirstOrDefault(p => p.SyncId == "prompt-fk-verify");
        Assert.NotNull(prompt);
    }
}