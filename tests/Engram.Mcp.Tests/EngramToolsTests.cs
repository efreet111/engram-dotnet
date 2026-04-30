using Engram.Mcp;
using Engram.Store;
using Xunit;

namespace Engram.Mcp.Tests;

/// <summary>
/// Tests for the EngramTools MCP tool class.
/// Tests are invoked directly (not via JSON-RPC) to verify business logic.
/// </summary>
public class EngramToolsTests : IDisposable
{
    private readonly SqliteStore  _store;
    private readonly EngramTools  _tools;
    private readonly WriteQueue   _writeQueue;
    private readonly string       _tempDir;
    private const string SessionId = "mcp-test-session";

    public EngramToolsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "engram-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _store = new SqliteStore(new StoreConfig { DataDir = _tempDir });
        _writeQueue = new WriteQueue();
        _tools = new EngramTools(_store, new McpConfig { DefaultProject = "default-project" }, _writeQueue);
    }

    public void Dispose()
    {
        _store.Dispose();
        _writeQueue.Dispose();
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    private Task SeedSession()
        => _store.CreateSessionAsync(SessionId, "test-proj", "/tmp");

    private Task<long> SeedObservation(string title, string content, string type = "manual", string project = "test-proj")
        => _store.AddObservationAsync(new AddObservationParams
        {
            SessionId = SessionId,
            Title     = title,
            Content   = content,
            Type      = type,
            Project   = project,
        });

    // ─── mem_search ───────────────────────────────────────────────────────────

    [Fact]
    public async Task MemSearch_ReturnsFoundMessage_WhenMatchExists()
    {
        await SeedSession();
        await SeedObservation("JWT authentication fix", "Fixed the JWT validation logic");

        var result = await _tools.MemSearch("JWT", project: "test-proj");

        Assert.Contains("Found", result);
        Assert.Contains("JWT", result);
    }

    [Fact]
    public async Task MemSearch_ReturnsNotFoundMessage_WhenNoMatch()
    {
        var result = await _tools.MemSearch("xyzzy-nonexistent-42");

        Assert.Contains("No memories found", result);
    }

    [Fact]
    public async Task MemSearch_ClampsLimitTo20()
    {
        await SeedSession();
        for (int i = 0; i < 5; i++)
            await SeedObservation($"Observation {i}", $"Content {i} with some keywords");

        // Request 100 — should clamp to 20 without throwing
        var result = await _tools.MemSearch("observation", limit: 100);
        Assert.NotNull(result);
    }

    // ─── mem_save ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task MemSave_ReturnsSavedMessage_WithTitle()
    {
        var result = await _tools.MemSave(
            title:      "Architecture decision",
            content:    "We chose hexagonal architecture",
            type:       "decision",
            project:    "test-proj",
            session_id: SessionId);

        Assert.Contains("Memory saved", result);
        Assert.Contains("Architecture decision", result);
    }

    [Fact]
    public async Task MemSave_CreatesObservation_RetrievableViaSearch()
    {
        await _tools.MemSave(
            "Searchable memory",
            "Content about hexagonal architecture",
            "architecture",
            null,
            "test-proj");

        var search = await _tools.MemSearch("hexagonal", project: "test-proj");
        Assert.Contains("hexagonal", search.ToLower());
    }

    // ─── mem_update ───────────────────────────────────────────────────────────

    [Fact]
    public async Task MemUpdate_UpdatesExistingObservation()
    {
        await SeedSession();
        var id = await SeedObservation("Old title", "Old content");

        var result = await _tools.MemUpdate(id, title: "New title", content: "New content");

        Assert.Contains("updated", result.ToLower());
    }

    [Fact]
    public async Task MemUpdate_ReturnsNotFound_ForInvalidId()
    {
        var result = await _tools.MemUpdate(999_999, title: "Whatever");
        Assert.Contains("not found", result.ToLower());
    }

    // ─── mem_delete ───────────────────────────────────────────────────────────

    [Fact]
    public async Task MemDelete_RemovesObservation()
    {
        await SeedSession();
        var id = await SeedObservation("Delete me", "Bye");

        var result = await _tools.MemDelete(id);
        Assert.Contains("deleted", result.ToLower());

        var obs = await _store.GetObservationAsync(id);
        Assert.Null(obs);
    }

    [Fact]
    public async Task MemDelete_ReturnsNotFound_ForInvalidId()
    {
        var result = await _tools.MemDelete(999_999);
        Assert.Contains("not found", result.ToLower());
    }

    // ─── mem_get_observation ──────────────────────────────────────────────────

    [Fact]
    public async Task MemGetObservation_ReturnsFullContent()
    {
        await SeedSession();
        var id = await SeedObservation("Full content obs", "This is the FULL content without truncation");

        var result = await _tools.MemGetObservation(id);

        Assert.Contains("Full content obs", result);
        Assert.Contains("FULL content", result);
    }

    [Fact]
    public async Task MemGetObservation_ReturnsNotFound_ForInvalidId()
    {
        var result = await _tools.MemGetObservation(999_999);
        Assert.Contains("not found", result.ToLower());
    }

    // ─── mem_context ──────────────────────────────────────────────────────────

    [Fact]
    public async Task MemContext_ReturnsContextString_WhenDataExists()
    {
        await SeedSession();
        await SeedObservation("Context obs", "Something important happened");

        var result = await _tools.MemContext(project: "test-proj");
        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task MemContext_HandlesEmptyDatabase_Gracefully()
    {
        var result = await _tools.MemContext();
        Assert.NotNull(result);
    }

    // ─── mem_stats ────────────────────────────────────────────────────────────

    [Fact]
    public async Task MemStats_ReturnsFormattedStats()
    {
        var result = await _tools.MemStats();

        Assert.Contains("session", result.ToLower());
        Assert.Contains("observation", result.ToLower());
    }

    // ─── mem_save_prompt ──────────────────────────────────────────────────────

    [Fact]
    public async Task MemSavePrompt_SavesPrompt_ReturnsConfirmation()
    {
        await SeedSession();
        var result = await _tools.MemSavePrompt(
            "How should I structure this service?",
            session_id: SessionId,
            project:    "test-proj");

        Assert.Contains("saved", result.ToLower());
    }

    // ─── mem_suggest_topic_key ────────────────────────────────────────────────

    [Fact]
    public void MemSuggestTopicKey_ReturnsSuggestedKey()
    {
        // MemSuggestTopicKey is synchronous — returns string, not Task<string>
        var result = _tools.MemSuggestTopicKey(
            type:    "architecture",
            title:   "JWT authentication setup",
            content: "Adding JWT auth to the API");

        Assert.NotEmpty(result);
        Assert.Contains("/", result);
    }

    [Fact]
    public void MemSuggestTopicKey_ReturnsError_WhenBothEmpty()
    {
        var result = _tools.MemSuggestTopicKey();
        Assert.Contains("Error", result);
    }

    // ─── mem_session_start ────────────────────────────────────────────────────

    [Fact]
    public async Task MemSessionStart_CreatesSession()
    {
        var result = await _tools.MemSessionStart("mcp-session-x", "proj-x", "/work");
        Assert.Contains("started", result.ToLower());

        var session = await _store.GetSessionAsync("mcp-session-x");
        Assert.NotNull(session);
    }

    // ─── mem_session_end ──────────────────────────────────────────────────────

    [Fact]
    public async Task MemSessionEnd_EndsSession()
    {
        await SeedSession();
        var result = await _tools.MemSessionEnd(SessionId, "Completed work");
        Assert.Contains("completed", result.ToLower());

        var session = await _store.GetSessionAsync(SessionId);
        Assert.NotNull(session?.EndedAt);
    }

    // ─── mem_session_summary ─────────────────────────────────────────────────

    [Fact]
    public async Task MemSessionSummary_SummarizesSession()
    {
        var result = await _tools.MemSessionSummary(
            content: """
            ## Goal
            Testing the MCP tools

            ## Discoveries
            - Tests work fine

            ## Accomplished
            - All tools tested
            """,
            project:    "test-proj",
            session_id: SessionId);

        Assert.Contains("saved", result.ToLower());
    }

    // ─── mem_merge_projects ───────────────────────────────────────────────────

    [Fact]
    public async Task MemMergeProjects_MergesSourcesIntoCanonical()
    {
        await SeedSession();
        await _store.AddObservationAsync(new AddObservationParams
        {
            SessionId = SessionId,
            Title     = "Old project obs",
            Content   = "Something",
            Project   = "old-proj",
        });

        // MemMergeProjects takes CSV string for `from`, target string for `to`
        var result = await _tools.MemMergeProjects("old-proj", "new-proj");

        Assert.Contains("new-proj", result);
    }

    // ─── DefaultProject injection ─────────────────────────────────────────────

    [Fact]
    public async Task MemSearch_UsesDefaultProject_WhenProjectIsNull()
    {
        await _store.CreateSessionAsync("default-s", "default-project", "/tmp");
        await _store.AddObservationAsync(new AddObservationParams
        {
            SessionId = "default-s",
            Title     = "Default project obs",
            Content   = "content",
            Project   = "default-project",
        });

        // Don't pass project — should fall back to cfg.DefaultProject ("default-project")
        var result = await _tools.MemSearch("content");
        Assert.Contains("Found", result);
    }
}

// ─── McpConfig — user/project namespace tests ─────────────────────────────────

public class McpConfigTests
{
    // ─── ResolveNamespacedProject — local mode (no user) ──────────────────────

    [Fact]
    public void ResolveNamespacedProject_ReturnsProject_WhenNoUser()
    {
        var cfg = new McpConfig { DefaultProject = "my-app", User = "" };

        var result = cfg.ResolveNamespacedProject("my-app", Engram.Store.Scopes.Personal);

        Assert.Equal("my-app", result);
    }

    [Fact]
    public void ResolveNamespacedProject_UsesDefault_WhenProjectIsNull()
    {
        var cfg = new McpConfig { DefaultProject = "default-app", User = "" };

        var result = cfg.ResolveNamespacedProject(null, Engram.Store.Scopes.Personal);

        Assert.Equal("default-app", result);
    }

    [Fact]
    public void ResolveNamespacedProject_UsesDefault_WhenProjectIsEmpty()
    {
        var cfg = new McpConfig { DefaultProject = "default-app", User = "" };

        var result = cfg.ResolveNamespacedProject("", Engram.Store.Scopes.Personal);

        Assert.Equal("default-app", result);
    }

    // ─── ResolveNamespacedProject — team mode, personal scope ─────────────────

    [Fact]
    public void ResolveNamespacedProject_PrefixesUser_WhenUserIsSet()
    {
        var cfg = new McpConfig { DefaultProject = "my-app", User = "victor.silgado" };

        var result = cfg.ResolveNamespacedProject("my-app", Engram.Store.Scopes.Personal);

        Assert.Equal("victor.silgado/my-app", result);
    }

    [Fact]
    public void ResolveNamespacedProject_PrefixesUserWithDefault_WhenProjectIsNull()
    {
        var cfg = new McpConfig { DefaultProject = "default-app", User = "victor.silgado" };

        var result = cfg.ResolveNamespacedProject(null, Engram.Store.Scopes.Personal);

        Assert.Equal("victor.silgado/default-app", result);
    }

    [Fact]
    public void ResolveNamespacedProject_PrefixesUserWithDefault_WhenProjectIsEmpty()
    {
        var cfg = new McpConfig { DefaultProject = "default-app", User = "victor.silgado" };

        var result = cfg.ResolveNamespacedProject("", Engram.Store.Scopes.Personal);

        Assert.Equal("victor.silgado/default-app", result);
    }

    [Fact]
    public void ResolveNamespacedProject_DifferentUsers_ProduceDifferentNamespaces()
    {
        var cfgA = new McpConfig { DefaultProject = "shared-app", User = "alice" };
        var cfgB = new McpConfig { DefaultProject = "shared-app", User = "bob" };

        var resultA = cfgA.ResolveNamespacedProject("shared-app", Engram.Store.Scopes.Personal);
        var resultB = cfgB.ResolveNamespacedProject("shared-app", Engram.Store.Scopes.Personal);

        Assert.NotEqual(resultA, resultB);
        Assert.Equal("alice/shared-app", resultA);
        Assert.Equal("bob/shared-app",   resultB);
    }

    // ─── ResolveNamespacedProject — team scope → team/ prefix ─────────────────

    [Fact]
    public void ResolveNamespacedProject_PrefixesTeam_WhenScopeIsTeam()
    {
        var cfg = new McpConfig { DefaultProject = "my-app", User = "victor.silgado" };

        var result = cfg.ResolveNamespacedProject("my-app", Engram.Store.Scopes.Team);

        Assert.Equal("team/my-app", result);
    }

    [Fact]
    public void ResolveNamespacedProject_TeamScope_SameForAllUsers()
    {
        var cfgA = new McpConfig { DefaultProject = "shared-app", User = "alice" };
        var cfgB = new McpConfig { DefaultProject = "shared-app", User = "bob" };

        var resultA = cfgA.ResolveNamespacedProject("shared-app", Engram.Store.Scopes.Team);
        var resultB = cfgB.ResolveNamespacedProject("shared-app", Engram.Store.Scopes.Team);

        Assert.Equal(resultA, resultB);
        Assert.Equal("team/shared-app", resultA);
    }

    // ─── StoreConfig — IsRemote flag ──────────────────────────────────────────

    [Fact]
    public void StoreConfig_IsRemote_FalseByDefault()
    {
        var cfg = new StoreConfig();

        Assert.False(cfg.IsRemote);
    }

    [Fact]
    public void StoreConfig_IsRemote_TrueWhenRemoteUrlSet()
    {
        var cfg = new StoreConfig { RemoteUrl = "http://10.0.0.5:7437" };

        Assert.True(cfg.IsRemote);
    }

    [Fact]
    public void StoreConfig_IsRemote_FalseWhenRemoteUrlIsWhitespace()
    {
        var cfg = new StoreConfig { RemoteUrl = "   " };

        Assert.False(cfg.IsRemote);
    }
}
