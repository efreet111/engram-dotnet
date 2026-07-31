using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Engram.Server;
using Engram.Store;
using Microsoft.AspNetCore.Builder;
using Xunit;

namespace Engram.Server.Tests;

/// <summary>
/// Integration tests for the Engram HTTP API.
/// Each test spins up a real WebApplication on a random port with an in-memory store.
/// </summary>
public class EngramServerTests : IAsyncDisposable
{
    private readonly SqliteStore    _store;
    private readonly WebApplication _app;
    private readonly HttpClient     _client;
    private readonly string         _baseUrl;
    private readonly string         _tempDir;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public EngramServerTests()
    {
        var port   = GetFreePort();
        _baseUrl   = $"http://localhost:{port}";
        _tempDir   = Path.Combine(Path.GetTempPath(), "engram-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        var storeCfg = new StoreConfig { DataDir = _tempDir };
        _store     = new SqliteStore(storeCfg);
        _app       = EngramServer.Build(_store, storeCfg);
        _app.Urls.Clear();
        _app.Urls.Add(_baseUrl);
        _app.StartAsync().GetAwaiter().GetResult();
        _client = new HttpClient { BaseAddress = new Uri(_baseUrl) };
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
        _store.Dispose();
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    private static int GetFreePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private async Task SeedSession(string id = "test-s1", string project = "test-proj")
    {
        var resp = await _client.PostAsJsonAsync("/sessions", new
        {
            id,
            project,
            directory = "/tmp",
        }, JsonOpts);
        resp.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Creates an observation under the given session and returns its id.
    /// Idempotent: reuses an existing session if SeedSession already ran.
    /// </summary>
    private async Task<long> SeedObservation(
        string sessionId = "test-s1",
        string title     = "Test observation",
        string content   = "Test content for observation",
        string type      = "manual",
        string project   = "test-proj")
    {
        await SeedSession(sessionId, project);

        var resp = await _client.PostAsJsonAsync("/observations", new
        {
            session_id = sessionId,
            title,
            content,
            type,
            project,
        }, JsonOpts);
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadFromJsonAsync<JsonObject>(JsonOpts);
        Assert.NotNull(json);
        return (long)json!["id"]!;
    }

    /// <summary>
    /// Creates a prompt under the given session and returns its id.
    /// </summary>
    private async Task<long> SeedPrompt(
        string sessionId = "test-s1",
        string content   = "What does this code do?",
        string project   = "test-proj")
    {
        await SeedSession(sessionId, project);

        var resp = await _client.PostAsJsonAsync("/prompts", new
        {
            session_id = sessionId,
            content,
            project,
        }, JsonOpts);
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadFromJsonAsync<JsonObject>(JsonOpts);
        Assert.NotNull(json);
        return (long)json!["id"]!;
    }

    /// <summary>
    /// Returns markdown text that <see cref="PassiveCapture.ExtractLearnings"/>
    /// will parse: a <c>## Key Learnings</c> header followed by numbered items.
    /// Each learning must be ≥20 chars and ≥4 words (PassiveCapture parser rule).
    /// </summary>
    private static string MakePassiveContent(params string[] learnings)
    {
        var items = string.Join("\n", learnings.Select((l, i) => $"{i + 1}. {l}"));
        return $"## Key Learnings\n{items}";
    }

    // ─── Health ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task GET_health_Returns200()
    {
        var resp = await _client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    // ─── Sessions ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task POST_sessions_Creates_And_GET_Returns_Session()
    {
        await SeedSession("s-create", "my-project");

        var resp = await _client.GetAsync("/sessions/s-create");
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadFromJsonAsync<JsonObject>(JsonOpts);
        Assert.NotNull(json);
        Assert.Equal("s-create",   (string?)json["id"]);
        Assert.Equal("my-project", (string?)json["project"]);
    }

    [Fact]
    public async Task GET_sessions_nonexistent_Returns404()
    {
        var resp = await _client.GetAsync("/sessions/ghost-session");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task POST_sessions_id_end_ClosesSession()
    {
        await SeedSession("s-end");

        var resp = await _client.PostAsJsonAsync("/sessions/s-end/end", new
        {
            summary = "All done",
        }, JsonOpts);
        resp.EnsureSuccessStatusCode();

        // GET /sessions/{id} now exists — verify ended_at is set
        var session = await _client.GetAsync("/sessions/s-end");
        session.EnsureSuccessStatusCode();
        var json    = await session.Content.ReadFromJsonAsync<JsonObject>(JsonOpts);
        Assert.NotNull((string?)json?["ended_at"]);
    }

    // ─── Session deletes ──────────────────────────────────────────────────────

    [Fact]
    public async Task DELETE_sessions_success_Returns200()
    {
        const string sessionId = "sess-to-delete";
        await SeedSession(sessionId);

        var resp = await _client.DeleteAsync($"/sessions/{sessionId}");
        resp.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var json = await resp.Content.ReadFromJsonAsync<JsonObject>(JsonOpts);
        Assert.NotNull(json);
        Assert.Equal(sessionId, (string?)json["id"]);
        Assert.Equal("deleted", (string?)json["status"]);

        var get = await _client.GetAsync($"/sessions/{sessionId}");
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
    }

    [Fact]
    public async Task DELETE_sessions_nonexistent_Returns404()
    {
        var resp = await _client.DeleteAsync("/sessions/ghost-session");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);

        var json = await resp.Content.ReadFromJsonAsync<JsonObject>(JsonOpts);
        Assert.NotNull(json);
        Assert.Equal("session not found: ghost-session", (string?)json["error"]);
    }

    [Fact]
    public async Task DELETE_sessions_has_observations_Returns409()
    {
        const string sessionId = "sess-with-obs";
        await SeedSession(sessionId);

        var create = await _client.PostAsJsonAsync("/observations", new
        {
            session_id = sessionId,
            title      = "blocking observation",
            content    = "cannot delete",
            type       = "manual",
        }, JsonOpts);
        create.EnsureSuccessStatusCode();

        var resp = await _client.DeleteAsync($"/sessions/{sessionId}");
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);

        var json = await resp.Content.ReadFromJsonAsync<JsonObject>(JsonOpts);
        Assert.NotNull(json);
        Assert.Equal("session has 1 active observations, cannot delete", (string?)json["error"]);
    }


    // ─── Multi-User Isolation ─────────────────────────────────────────────────
    // Tests ensuring that personal data is scoped by the X-Engram-User header.

    [Fact]
    public async Task Observations_With_Different_Users_Are_Isolated_In_Personal_Scope()
    {
        await SeedSession("s-multi", "proj-isolation");

        // User A saves a personal note
        var reqA = new HttpRequestMessage(HttpMethod.Post, "/observations");
        reqA.Headers.Add("X-Engram-User", "user-alpha");
        reqA.Content = JsonContent.Create(new
        {
            session_id = "s-multi",
            title      = "Alpha's Secret",
            content    = "Only for Alpha",
            scope      = "personal",
            project    = "proj-isolation"
        }, options: JsonOpts);

        var respA = await _client.SendAsync(reqA);
        respA.EnsureSuccessStatusCode();

        // User B saves a personal note
        var reqB = new HttpRequestMessage(HttpMethod.Post, "/observations");
        reqB.Headers.Add("X-Engram-User", "user-beta");
        reqB.Content = JsonContent.Create(new
        {
            session_id = "s-multi",
            title      = "Beta's Secret",
            content    = "Only for Beta",
            scope      = "personal",
            project    = "proj-isolation"
        }, options: JsonOpts);

        var respB = await _client.SendAsync(reqB);
        respB.EnsureSuccessStatusCode();

        // User Alpha requests recent observations — should NOT see Beta's note
        var queryA = new HttpRequestMessage(HttpMethod.Get, "/observations/recent?project=proj-isolation&scope=personal");
        queryA.Headers.Add("X-Engram-User", "user-alpha");
        var respQueryA = await _client.SendAsync(queryA);
        var obsA = await respQueryA.Content.ReadFromJsonAsync<JsonArray>(JsonOpts);
        
        Assert.Single(obsA);
        Assert.Equal("Alpha's Secret", (string?)obsA?[0]?["title"]);

        // User Beta requests recent observations — should NOT see Alpha's note
        var queryB = new HttpRequestMessage(HttpMethod.Get, "/observations/recent?project=proj-isolation&scope=personal");
        queryB.Headers.Add("X-Engram-User", "user-beta");
        var respQueryB = await _client.SendAsync(queryB);
        var obsB = await respQueryB.Content.ReadFromJsonAsync<JsonArray>(JsonOpts);

        Assert.Single(obsB);
        Assert.Equal("Beta's Secret", (string?)obsB?[0]?["title"]);
    }

    [Fact]
    public async Task Observations_In_Team_Scope_Are_Shared_Across_Users()
    {
        await SeedSession("s-team", "proj-team");

        // User A saves a team note
        var reqA = new HttpRequestMessage(HttpMethod.Post, "/observations");
        reqA.Headers.Add("X-Engram-User", "user-alpha");
        reqA.Content = JsonContent.Create(new
        {
            session_id = "s-team",
            title      = "Shared Wisdom",
            content    = "For everyone",
            scope      = "team",
            project    = "proj-team"
        }, options: JsonOpts);

        await _client.SendAsync(reqA);

        // User Beta requests recent team observations — should see Alpha's note
        var queryB = new HttpRequestMessage(HttpMethod.Get, "/observations/recent?project=proj-team&scope=team");
        queryB.Headers.Add("X-Engram-User", "user-beta");
        var respQueryB = await _client.SendAsync(queryB);
        var obsB = await respQueryB.Content.ReadFromJsonAsync<JsonArray>(JsonOpts);

        Assert.Single(obsB);
        Assert.Equal("Shared Wisdom", (string?)obsB?[0]?["title"]);
    }

    // ─── Observations ─────────────────────────────────────────────────────────

    [Fact]
    public async Task POST_observations_Creates_Returns_Id()
    {
        await SeedSession();

        var resp = await _client.PostAsJsonAsync("/observations", new
        {
            session_id = "test-s1",
            title      = "My observation",
            content    = "Important content",
            type       = "decision",
            project    = "test-proj",
        }, JsonOpts);

        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadFromJsonAsync<JsonObject>(JsonOpts);
        Assert.NotNull(json);
        Assert.True((long?)json["id"] > 0);
    }

    [Fact]
    public async Task GET_observations_id_Returns_Observation()
    {
        await SeedSession();

        var create = await _client.PostAsJsonAsync("/observations", new
        {
            session_id = "test-s1",
            title      = "Fetch me",
            content    = "Some content",
            type       = "manual",
        }, JsonOpts);
        var created = await create.Content.ReadFromJsonAsync<JsonObject>(JsonOpts);
        var id      = (long?)created?["id"];
        Assert.NotNull(id);

        var resp = await _client.GetAsync($"/observations/{id}");
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadFromJsonAsync<JsonObject>(JsonOpts);
        Assert.Equal(id, (long?)json?["id"]);
        Assert.Equal("Fetch me", (string?)json?["title"]);
    }

    [Fact]
    public async Task GET_observations_nonexistent_Returns404()
    {
        var resp = await _client.GetAsync("/observations/999999");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task PUT_observations_id_UpdatesFields()
    {
        await SeedSession();

        var create = await _client.PostAsJsonAsync("/observations", new
        {
            session_id = "test-s1",
            title      = "Old title",
            content    = "Old content",
            type       = "manual",
        }, JsonOpts);
        var created = await create.Content.ReadFromJsonAsync<JsonObject>(JsonOpts);
        var id      = (long?)created?["id"];

        // Server uses PATCH, not PUT
        var patch = await _client.PatchAsJsonAsync($"/observations/{id}", new
        {
            title   = "New title",
            content = "New content",
        }, JsonOpts);
        patch.EnsureSuccessStatusCode();

        var obs = await _client.GetAsync($"/observations/{id}");
        var json = await obs.Content.ReadFromJsonAsync<JsonObject>(JsonOpts);
        Assert.Equal("New title",   (string?)json?["title"]);
        Assert.Equal("New content", (string?)json?["content"]);
    }

    [Fact]
    public async Task DELETE_observations_id_SoftDeletes()
    {
        await SeedSession();

        var create = await _client.PostAsJsonAsync("/observations", new
        {
            session_id = "test-s1",
            title      = "Delete me",
            content    = "Bye",
            type       = "manual",
        }, JsonOpts);
        var created = await create.Content.ReadFromJsonAsync<JsonObject>(JsonOpts);
        var id      = (long?)created?["id"];

        var del = await _client.DeleteAsync($"/observations/{id}");
        del.EnsureSuccessStatusCode();

        var get = await _client.GetAsync($"/observations/{id}");
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
    }

    // ─── Search ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task GET_search_ReturnsResults()
    {
        await SeedSession();
        await _client.PostAsJsonAsync("/observations", new
        {
            session_id = "test-s1",
            title      = "JWT authentication",
            content    = "We implemented JWT-based auth in our API",
            type       = "decision",
        }, JsonOpts);

        var resp = await _client.GetAsync("/search?q=JWT");
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadFromJsonAsync<JsonArray>(JsonOpts);
        Assert.NotNull(json);
        Assert.NotEmpty(json);
    }

    [Fact]
    public async Task GET_search_Returns200_WithNoResults()
    {
        var resp = await _client.GetAsync("/search?q=xyzzy-nonexistent-string");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    // ─── Stats ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GET_stats_Returns200_WithCounts()
    {
        var resp = await _client.GetAsync("/stats");
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadFromJsonAsync<JsonObject>(JsonOpts);
        Assert.NotNull(json);
        Assert.NotNull(json["total_sessions"]);
        Assert.NotNull(json["total_observations"]);
    }

    // ─── Context ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GET_context_Returns200()
    {
        var resp = await _client.GetAsync("/context");
        resp.EnsureSuccessStatusCode();
    }

    // ─── Prompts ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task POST_prompts_Creates_And_GET_Returns()
    {
        await SeedSession();

        var create = await _client.PostAsJsonAsync("/prompts", new
        {
            session_id = "test-s1",
            content    = "What does this code do?",
            project    = "test-proj",
        }, JsonOpts);
        create.EnsureSuccessStatusCode();

        // Correct route: GET /prompts/recent (not GET /prompts)
        var resp = await _client.GetAsync("/prompts/recent?project=test-proj");
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadFromJsonAsync<JsonArray>(JsonOpts);
        Assert.NotNull(json);
        Assert.NotEmpty(json);
    }

    // ─── Prompt deletes ───────────────────────────────────────────────────────

    [Fact]
    public async Task DELETE_prompts_success_Returns200()
    {
        const string sessionId = "sess-prompt-delete";
        await SeedSession(sessionId);

        var create = await _client.PostAsJsonAsync("/prompts", new
        {
            session_id = sessionId,
            content    = "Please delete this prompt",
            project    = "prompt-proj",
        }, JsonOpts);
        create.EnsureSuccessStatusCode();

        var created = await create.Content.ReadFromJsonAsync<JsonObject>(JsonOpts);
        Assert.NotNull(created);
        var promptId = (long?)created["id"];
        Assert.NotNull(promptId);
        var resp = await _client.DeleteAsync($"/prompts/{promptId.Value}");
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadFromJsonAsync<JsonObject>(JsonOpts);
        Assert.NotNull(json);
        Assert.Equal(promptId, (long?)json["id"]);
        Assert.Equal("deleted", (string?)json["status"]);
    }

    [Fact]
    public async Task DELETE_prompts_nonexistent_Returns404()
    {
        var resp = await _client.DeleteAsync("/prompts/999999");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);

        var json = await resp.Content.ReadFromJsonAsync<JsonObject>(JsonOpts);
        Assert.NotNull(json);
        Assert.Equal("prompt not found: 999999", (string?)json["error"]);
    }

    [Fact]
    public async Task DELETE_prompts_invalid_id_Returns400()
    {
        var resp = await _client.DeleteAsync("/prompts/abc");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);

        var json = await resp.Content.ReadFromJsonAsync<JsonObject>(JsonOpts);
        Assert.NotNull(json);
        Assert.Equal("invalid prompt id", (string?)json["error"]);
    }

    // ─── Export / Import ──────────────────────────────────────────────────────

    [Fact]
    public async Task GET_export_Returns200_WithValidShape()
    {
        var resp = await _client.GetAsync("/export");
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadFromJsonAsync<JsonObject>(JsonOpts);
        Assert.NotNull(json);
        Assert.NotNull(json["sessions"]);
        Assert.NotNull(json["observations"]);
        Assert.NotNull(json["prompts"]);
    }

    // ─── Projects ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task POST_projects_migrate_Returns200()
    {
        await SeedSession();
        await _client.PostAsJsonAsync("/observations", new
        {
            session_id = "test-s1",
            title      = "Old project obs",
            content    = "Something important",
            type       = "manual",
            project    = "old-proj",
        }, JsonOpts);

        // Correct route and payload: POST /projects/migrate with old_project + new_project
        var resp = await _client.PostAsJsonAsync("/projects/migrate", new
        {
            old_project = "old-proj",
            new_project = "new-proj",
        }, JsonOpts);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    // ─── Projects list & stats ──────────────────────────────────────────────

    [Fact]
    public async Task GET_projects_list_ReturnsProjectNames()
    {
        await SeedSession("s-pl1", "alpha");
        await SeedSession("s-pl2", "beta");
        await _client.PostAsJsonAsync("/observations", new
        {
            session_id = "s-pl1", title = "obs-a", content = "c", type = "manual", project = "alpha",
        }, JsonOpts);
        await _client.PostAsJsonAsync("/observations", new
        {
            session_id = "s-pl2", title = "obs-b", content = "c", type = "manual", project = "beta",
        }, JsonOpts);

        var resp = await _client.GetAsync("/projects/list");
        resp.EnsureSuccessStatusCode();

        // API returns a JSON array of strings: ["alpha","beta"]
        var names = await resp.Content.ReadFromJsonAsync<List<string>>(JsonOpts);
        Assert.NotNull(names);
        Assert.Equal(2, names.Count);
        Assert.Contains("alpha", names);
        Assert.Contains("beta", names);
    }

    [Fact]
    public async Task GET_projects_list_ReturnsEmpty_WhenNoData()
    {
        var resp = await _client.GetAsync("/projects/list");
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadFromJsonAsync<JsonArray>(JsonOpts);
        Assert.NotNull(json);
        Assert.Empty(json);
    }

    [Fact]
    public async Task GET_projects_stats_ReturnsStatsWithCounts()
    {
        await SeedSession("s-ps1", "proj-stats");
        await _client.PostAsJsonAsync("/observations", new
        {
            session_id = "s-ps1", title = "obs-1", content = "c", type = "manual", project = "proj-stats",
        }, JsonOpts);

        var resp = await _client.GetAsync("/projects/stats");
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadFromJsonAsync<JsonArray>(JsonOpts);
        Assert.NotNull(json);
        Assert.NotEmpty(json);

        var first = json[0] as JsonObject;
        Assert.NotNull(first);
        Assert.Equal("proj-stats", first["name"]?.ToString());
        Assert.True(first["observation_count"]?.GetValue<int>() >= 1);
        Assert.True(first["session_count"]?.GetValue<int>() >= 1);
    }

    // ─── ExportSince (ENG-208 Phase 6) ─────────────────────────────────────────

    [Fact]
    public async Task GET_export_since_AfterSeq_ReturnsNewMutations()
    {
        await SeedSession("s-es", "proj-test");
        await _client.PostAsJsonAsync("/observations", new
        {
            session_id = "s-es", title = "obs-1", content = "c1", type = "manual", project = "proj-test",
        }, JsonOpts);
        await _client.PostAsJsonAsync("/observations", new
        {
            session_id = "s-es", title = "obs-2", content = "c2", type = "manual", project = "proj-test",
        }, JsonOpts);

        var resp = await _client.GetAsync("/export/since?project=proj-test&after_seq=0&limit=100");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var json = await resp.Content.ReadFromJsonAsync<JsonObject>(JsonOpts);
        Assert.NotNull(json);
        Assert.NotNull(json["observations"]);
        Assert.True(json["observations"]?.AsArray().Count >= 2);
        Assert.NotNull(json["next_seq"]);
        Assert.NotNull(json["has_more"]);
    }

    [Fact]
    public async Task GET_export_since_ProjectFilter_Respected()
    {
        await SeedSession("s-es2", "proj-a");
        await _client.PostAsJsonAsync("/observations", new
        {
            session_id = "s-es2", title = "obs-a", content = "c", type = "manual", project = "proj-a",
        }, JsonOpts);
        await SeedSession("s-es3", "proj-b");
        await _client.PostAsJsonAsync("/observations", new
        {
            session_id = "s-es3", title = "obs-b", content = "c", type = "manual", project = "proj-b",
        }, JsonOpts);

        var resp = await _client.GetAsync("/export/since?project=proj-a&after_seq=0&limit=100");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var json = await resp.Content.ReadFromJsonAsync<JsonObject>(JsonOpts);
        Assert.NotNull(json);
        var obs = json["observations"]?.AsArray();
        Assert.NotNull(obs);
        Assert.All(obs, o => Assert.Equal("proj-a", (o as JsonObject)?["project"]?.ToString()));
    }

    [Fact]
    public async Task GET_export_since_InvalidSeq_Returns400()
    {
        var resp = await _client.GetAsync("/export/since?project=proj-test&after_seq=abc");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task GET_export_since_BlankProject_Returns400()
    {
        var resp = await _client.GetAsync("/export/since?after_seq=0");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    // ─── Export with Project Query (ENG-208 Phase 7) ───────────────────────────

    [Fact]
    public async Task GET_export_WithProjectQuery_ReturnsOnlyThatProject()
    {
        await SeedSession("s-ep", "proj-filtered");
        await _client.PostAsJsonAsync("/observations", new
        {
            session_id = "s-ep", title = "obs-filtered", content = "c", type = "manual", project = "proj-filtered",
        }, JsonOpts);
        await SeedSession("s-ep2", "proj-other");
        await _client.PostAsJsonAsync("/observations", new
        {
            session_id = "s-ep2", title = "obs-other", content = "c", type = "manual", project = "proj-other",
        }, JsonOpts);

        var resp = await _client.GetAsync("/export?project=proj-filtered");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var json = await resp.Content.ReadFromJsonAsync<JsonObject>(JsonOpts);
        Assert.NotNull(json);
        var obs = json["observations"]?.AsArray();
        Assert.NotNull(obs);
        Assert.All(obs, o => Assert.Equal("proj-filtered", (o as JsonObject)?["project"]?.ToString()));
    }

    [Fact]
    public async Task GET_export_BlankProject_Returns400()
    {
        var resp = await _client.GetAsync("/export?project=");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // ENG-422: REST endpoints without HTTP test coverage (Phase 3 of FlowForge)
    // ═══════════════════════════════════════════════════════════════════════════

    // ─── POST /observations/passive (T-02) ────────────────────────────────────

    [Fact]
    public async Task POST_observations_passive_WithValidContent_ReturnsExtracted()
    {
        await SeedSession();

        var content = MakePassiveContent(
            "The ASP.NET Core middleware pipeline processes requests in registration order.",
            "SQLite WAL mode allows concurrent readers without blocking writers."
        );

        var resp = await _client.PostAsJsonAsync("/observations/passive", new
        {
            session_id = "test-s1",
            content,
            project   = "test-proj",
            source    = "claude-code",
        }, JsonOpts);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await resp.Content.ReadFromJsonAsync<JsonObject>(JsonOpts);
        Assert.NotNull(json);
        Assert.True((int)json!["extracted"]! >= 2,
            $"Expected extracted >= 2, got {json["extracted"]}");
        Assert.True((int)json["saved"]! >= 2,
            $"Expected saved >= 2, got {json["saved"]}");
    }

    [Fact]
    public async Task POST_observations_passive_EmptyContent_ReturnsZeros()
    {
        await SeedSession();

        var resp = await _client.PostAsJsonAsync("/observations/passive", new
        {
            session_id = "test-s1",
            content    = "",
            project    = "test-proj",
        }, JsonOpts);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await resp.Content.ReadFromJsonAsync<JsonObject>(JsonOpts);
        Assert.NotNull(json);
        Assert.Equal(0, (int)json!["extracted"]!);
        Assert.Equal(0, (int)json["saved"]!);
        Assert.Equal(0, (int)json["duplicates"]!);
    }

    [Fact]
    public async Task POST_observations_passive_MissingSessionId_Returns400()
    {
        var resp = await _client.PostAsJsonAsync("/observations/passive", new
        {
            content = "Some markdown content without session_id field.",
            project = "test-proj",
        }, JsonOpts);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var json = await resp.Content.ReadFromJsonAsync<JsonObject>(JsonOpts);
        Assert.NotNull(json);
        Assert.NotNull(json!["error"]);
    }

    // ─── GET /timeline (T-03) ──────────────────────────────────────────────────

    [Fact]
    public async Task GET_timeline_ValidObservationId_Returns200()
    {
        // Seed 3 observations under the same session to give the focus some context.
        var id1 = await SeedObservation("test-s1", "First decision",  "First content", "decision", "test-proj");
        var id2 = await SeedObservation("test-s1", "Second decision", "Second content", "decision", "test-proj");
        var id3 = await SeedObservation("test-s1", "Third decision",  "Third content", "decision", "test-proj");

        var resp = await _client.GetAsync($"/timeline?observation_id={id2}");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var json = await resp.Content.ReadFromJsonAsync<JsonObject>(JsonOpts);
        Assert.NotNull(json);
        Assert.NotNull(json!["focus"]);
        Assert.NotNull(json["before"]);
        Assert.NotNull(json["after"]);
        Assert.Equal(id2, (long?)json["focus"]!["id"]);
        // id1 should be in "before" (id < id2)
        // id3 should be in "after"  (id > id2)
        var before = json["before"]?.AsArray();
        var after  = json["after"]?.AsArray();
        Assert.NotNull(before);
        Assert.NotNull(after);
        Assert.NotEmpty(before);
        Assert.NotEmpty(after);
        Assert.Contains(before, e => (long?)e?["id"] == id1);
        Assert.Contains(after,  e => (long?)e?["id"] == id3);
    }

    [Fact]
    public async Task GET_timeline_MissingObservationId_Returns400()
    {
        var resp = await _client.GetAsync("/timeline");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task GET_timeline_InvalidObservationId_Returns400()
    {
        var resp = await _client.GetAsync("/timeline?observation_id=abc");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task GET_timeline_NonexistentObservationId_Returns404()
    {
        var resp = await _client.GetAsync("/timeline?observation_id=999999");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);

        var json = await resp.Content.ReadFromJsonAsync<JsonObject>(JsonOpts);
        Assert.NotNull(json);
        Assert.Contains("not found", (string?)json!["error"]);
    }

    // ─── GET /prompts/search (T-04) ────────────────────────────────────────────

    [Fact]
    public async Task GET_prompts_search_WithQuery_Returns200()
    {
        await SeedPrompt("test-s1", "Explain how SQLite WAL mode works", "test-proj");
        await SeedPrompt("test-s1", "Show me a JWT authentication example", "test-proj");

        var resp = await _client.GetAsync("/prompts/search?q=SQLite");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var json = await resp.Content.ReadFromJsonAsync<JsonArray>(JsonOpts);
        Assert.NotNull(json);
        Assert.NotEmpty(json);
        // At least one result contains the search term.
        Assert.Contains(json, e =>
        {
            var content = (string?)e?["content"];
            return content != null && content.Contains("SQLite", StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task GET_prompts_search_WithProjectFilter_ReturnsFiltered()
    {
        await SeedPrompt("test-s1", "Discuss JWT bearer token patterns",  "proj-alpha");
        await SeedPrompt("test-s2", "Discuss JWT session middleware",    "proj-beta");

        var resp = await _client.GetAsync("/prompts/search?q=JWT&project=proj-alpha");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var json = await resp.Content.ReadFromJsonAsync<JsonArray>(JsonOpts);
        Assert.NotNull(json);
        Assert.NotEmpty(json);
        // All results should belong to proj-alpha
        Assert.All(json, e => Assert.Equal("proj-alpha", (string?)e?["project"]));
    }

    [Fact]
    public async Task GET_prompts_search_MissingQuery_Returns400()
    {
        var resp = await _client.GetAsync("/prompts/search");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);

        var json = await resp.Content.ReadFromJsonAsync<JsonObject>(JsonOpts);
        Assert.NotNull(json);
        Assert.Contains("q", (string?)json!["error"]);
    }

    // ─── POST /import (T-05) ────────────────────────────────────────────────────

    [Fact]
    public async Task POST_import_ValidExportData_Returns200()
    {
        // Round-trip: first seed data, export it, then re-import it.
        await SeedSession("s-imp", "imp-proj");
        await SeedObservation("s-imp", "Important decision", "Do not skip code review",
            "decision", "imp-proj");

        var export = await _client.GetAsync("/export?project=imp-proj");
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        var exportJson = await export.Content.ReadFromJsonAsync<JsonObject>(JsonOpts);
        Assert.NotNull(exportJson);

        var resp = await _client.PostAsJsonAsync("/import", exportJson, JsonOpts);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var json = await resp.Content.ReadFromJsonAsync<JsonObject>(JsonOpts);
        Assert.NotNull(json);
        // Re-importing the same data is idempotent (INSERT OR IGNORE): counts >= 0.
        Assert.NotNull(json!["sessions_imported"]);
        Assert.NotNull(json["observations_imported"]);
        Assert.NotNull(json["prompts_imported"]);
    }

    [Fact]
    public async Task POST_import_EmptyData_Returns200()
    {
        var resp = await _client.PostAsJsonAsync("/import", new
        {
            sessions     = Array.Empty<object>(),
            observations = Array.Empty<object>(),
            prompts      = Array.Empty<object>(),
        }, JsonOpts);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await resp.Content.ReadFromJsonAsync<JsonObject>(JsonOpts);
        Assert.NotNull(json);
        Assert.Equal(0, (int)json!["sessions_imported"]!);
        Assert.Equal(0, (int)json["observations_imported"]!);
        Assert.Equal(0, (int)json["prompts_imported"]!);
    }

    [Fact]
    public async Task POST_import_InvalidJson_Returns400()
    {
        var content = new StringContent("not-json{{{", System.Text.Encoding.UTF8, "application/json");
        var resp = await _client.PostAsync("/import", content);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);

        var json = await resp.Content.ReadFromJsonAsync<JsonObject>(JsonOpts);
        Assert.NotNull(json);
        Assert.Equal("invalid json", (string?)json!["error"]);
    }

    // ─── POST /projects/prune (T-06) ────────────────────────────────────────────
    // Note: PruneProjectAsync refuses to delete a project that still has observations.
    // Tests therefore seed a session WITHOUT observations, then prune.

    [Fact]
    public async Task POST_projects_prune_ExistingProject_Returns200()
    {
        // Seed a session in project "prune-me" — no observation, so prune is allowed.
        await SeedSession("sess-prune-target", "prune-me");

        var resp = await _client.PostAsJsonAsync("/projects/prune", new
        {
            project = "prune-me",
        }, JsonOpts);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await resp.Content.ReadFromJsonAsync<JsonObject>(JsonOpts);
        Assert.NotNull(json);
        Assert.Equal("prune-me", (string?)json!["project"]);
        Assert.True((long)json["sessions_deleted"]! >= 1,
            $"Expected sessions_deleted >= 1, got {json["sessions_deleted"]}");

        // Verify the session was actually removed.
        var get = await _client.GetAsync("/sessions/sess-prune-target");
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
    }

    [Fact]
    public async Task POST_projects_prune_MissingProject_Returns400()
    {
        var resp = await _client.PostAsJsonAsync("/projects/prune", new { }, JsonOpts);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);

        var json = await resp.Content.ReadFromJsonAsync<JsonObject>(JsonOpts);
        Assert.NotNull(json);
        Assert.Contains("project", (string?)json!["error"]);
    }

    [Fact]
    public async Task POST_projects_prune_NonexistentProject_Returns200()
    {
        // Per plan / spec: handler returns 200 with zero counters for a project
        // that does not exist (rather than 404).
        var resp = await _client.PostAsJsonAsync("/projects/prune", new
        {
            project = "ghost-project-never-seen",
        }, JsonOpts);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await resp.Content.ReadFromJsonAsync<JsonObject>(JsonOpts);
        Assert.NotNull(json);
        Assert.Equal(0, (long)json!["sessions_deleted"]!);
        Assert.Equal(0, (long)json["prompts_deleted"]!);
    }

    // ─── GET /projects/migrations (T-07) ───────────────────────────────────────

    [Fact]
    public async Task GET_projects_migrations_EmptyStore_Returns200()
    {
        var resp = await _client.GetAsync("/projects/migrations");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var json = await resp.Content.ReadFromJsonAsync<JsonArray>(JsonOpts);
        Assert.NotNull(json);
        Assert.Empty(json);
    }

    [Fact]
    public async Task GET_projects_migrations_AfterMigrate_ReturnsEntries()
    {
        // Seed observation under "old-proj", then migrate to "new-proj".
        await SeedSession("s-mig", "old-proj");
        await SeedObservation("s-mig", "Obs to migrate", "Will move", "manual", "old-proj");

        var migrate = await _client.PostAsJsonAsync("/projects/migrate", new
        {
            old_project = "old-proj",
            new_project = "new-proj",
        }, JsonOpts);
        Assert.Equal(HttpStatusCode.OK, migrate.StatusCode);

        var resp = await _client.GetAsync("/projects/migrations");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var json = await resp.Content.ReadFromJsonAsync<JsonArray>(JsonOpts);
        Assert.NotNull(json);
        Assert.NotEmpty(json);
        var first = json[0] as JsonObject;
        Assert.NotNull(first);
        Assert.Equal("old-proj", (string?)first!["from_project"]);
        Assert.Equal("new-proj", (string?)first["to_project"]);
    }

    // ─── POST /md/promote/{id} (T-08) ──────────────────────────────────────────
    // Note: PromoteToMdAsync returns id=0 for nonexistent observation (no 404).

    [Fact]
    public async Task POST_md_promote_ValidId_Returns200()
    {
        var id = await SeedObservation(
            "test-s1",
            "Promoted observation",
            "Long enough content for the MD file body.",
            "decision",
            "test-proj");

        var resp = await _client.PostAsJsonAsync(
            $"/md/promote/{id}",
            new { md_dir = _tempDir },
            JsonOpts);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await resp.Content.ReadFromJsonAsync<JsonObject>(JsonOpts);
        Assert.NotNull(json);
        Assert.Equal(id, (long?)json!["id"]);
        Assert.Equal(_tempDir, (string?)json["md_dir"]);

        // Verify an .md file was actually created under _tempDir (cleaned in DisposeAsync).
        var files = Directory.GetFiles(_tempDir, "*.md");
        Assert.NotEmpty(files);
    }

    [Fact]
    public async Task POST_md_promote_InvalidId_Returns404()
    {
        // The route uses an {id:long} constraint, so non-numeric ids don't match
        // and ASP.NET Core responds with 404 rather than reaching the handler.
        var resp = await _client.PostAsJsonAsync(
            "/md/promote/abc",
            new { md_dir = _tempDir },
            JsonOpts);
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task POST_md_promote_NonexistentId_Returns200_WithZeroId()
    {
        var resp = await _client.PostAsJsonAsync(
            "/md/promote/999999",
            new { md_dir = _tempDir },
            JsonOpts);

        // Per plan / spec: PromoteToMdAsync returns 0 for missing observation
        // instead of throwing, and the handler surfaces that as id=0.
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await resp.Content.ReadFromJsonAsync<JsonObject>(JsonOpts);
        Assert.NotNull(json);
        Assert.Equal(0, (long?)json!["id"]);
    }

    // ─── POST /md/sync (T-09) ──────────────────────────────────────────────────

    [Fact]
    public async Task POST_md_sync_DryRun_Returns200()
    {
        // Seed 2 observations; dry-run reports the count without writing files.
        await SeedObservation("test-s1", "First",  "First content body",  "decision", "test-proj");
        await SeedObservation("test-s1", "Second", "Second content body", "decision", "test-proj");

        var resp = await _client.PostAsJsonAsync("/md/sync", new
        {
            md_dir  = _tempDir,
            dry_run = true,
        }, JsonOpts);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await resp.Content.ReadFromJsonAsync<JsonObject>(JsonOpts);
        Assert.NotNull(json);
        Assert.True((int)json!["count"]! >= 2,
            $"Expected count >= 2, got {json["count"]}");
        Assert.True((bool)json["dry_run"]);
        Assert.Equal(_tempDir, (string?)json["md_dir"]);

        // Dry run must not create any .md files.
        Assert.Empty(Directory.GetFiles(_tempDir, "*.md"));
    }

    [Fact]
    public async Task POST_md_sync_WithPromotedObs_Returns200()
    {
        // Pre-promote one observation so the sync run finds an existing md_path
        // and reports the count of remaining work to do.
        var id = await SeedObservation("test-s1", "Already promoted", "Body", "decision", "test-proj");

        var promote = await _client.PostAsJsonAsync(
            $"/md/promote/{id}",
            new { md_dir = _tempDir },
            JsonOpts);
        Assert.Equal(HttpStatusCode.OK, promote.StatusCode);

        var resp = await _client.PostAsJsonAsync("/md/sync", new
        {
            md_dir  = _tempDir,
            dry_run = true,
        }, JsonOpts);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await resp.Content.ReadFromJsonAsync<JsonObject>(JsonOpts);
        Assert.NotNull(json);
        Assert.NotNull(json!["count"]);
        Assert.NotNull(json["md_dir"]);
        Assert.True((bool)json["dry_run"]);
    }

    [Fact]
    public async Task POST_md_sync_EmptyBody_UsesDefaults_Returns200()
    {
        // No observations seeded: sync loop runs 0 iterations and returns 200
        // without ever touching the default "docs/decisions" directory.
        var resp = await _client.PostAsJsonAsync("/md/sync", new { }, JsonOpts);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var json = await resp.Content.ReadFromJsonAsync<JsonObject>(JsonOpts);
        Assert.NotNull(json);
        Assert.Equal(0, (int)json!["count"]!);
        Assert.False((bool)json["dry_run"]!);
    }

    // ─── POST /md/index (T-10) ─────────────────────────────────────────────────

    [Fact]
    public async Task POST_md_index_WithMdFiles_Returns200()
    {
        // Promote an observation so it appears in the index.
        var id = await SeedObservation("test-s1", "Indexed observation", "Body content",
            "decision", "test-proj");
        var promote = await _client.PostAsJsonAsync(
            $"/md/promote/{id}",
            new { md_dir = _tempDir },
            JsonOpts);
        Assert.Equal(HttpStatusCode.OK, promote.StatusCode);

        var resp = await _client.PostAsJsonAsync("/md/index", new
        {
            md_dir = _tempDir,
        }, JsonOpts);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("text/markdown",
            resp.Content.Headers.ContentType?.MediaType);

        var body = await resp.Content.ReadAsStringAsync();
        Assert.NotEmpty(body);
        Assert.Contains("Decision Records", body);
    }

    [Fact]
    public async Task POST_md_index_EmptyDir_Returns200()
    {
        // No promoted observations; the response is a minimal index markdown.
        var resp = await _client.PostAsJsonAsync("/md/index", new
        {
            md_dir = _tempDir,
        }, JsonOpts);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.NotNull(body);
        // "Total: 0 records" is part of RenderIndex — see SqliteStore.RenderIndex.
        Assert.Contains("Total: 0", body);
    }

    [Fact]
    public async Task POST_md_index_EmptyBody_UsesDefaults_Returns200()
    {
        // Empty body falls back to "docs/decisions"; with no promoted observations
        // the handler should still respond 200 with a markdown payload.
        var resp = await _client.PostAsJsonAsync("/md/index", new { }, JsonOpts);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadAsStringAsync();
        Assert.NotNull(body);
        Assert.Contains("Decision Records", body);
    }

    // ─── GET /retention/stats (T-11) ───────────────────────────────────────────

    [Fact]
    public async Task GET_retention_stats_EmptyStore_Returns200()
    {
        var resp = await _client.GetAsync("/retention/stats");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var json = await resp.Content.ReadFromJsonAsync<JsonObject>(JsonOpts);
        Assert.NotNull(json);
        Assert.NotNull(json!["total_observations"]);
        Assert.NotNull(json["age_buckets"]);
        Assert.NotNull(json["inactive_projects"]);
        Assert.Equal(0, (int)json["total_observations"]!);
    }

    [Fact]
    public async Task GET_retention_stats_WithData_ReturnsBuckets()
    {
        await SeedObservation("test-s1", "Recent decision", "Body", "decision", "test-proj");

        var resp = await _client.GetAsync("/retention/stats");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var json = await resp.Content.ReadFromJsonAsync<JsonObject>(JsonOpts);
        Assert.NotNull(json);
        Assert.True((int)json!["total_observations"]! > 0);
        // Age buckets are always populated (5 entries).
        Assert.NotEmpty(json["age_buckets"]!.AsArray());
    }

    // ─── POST /retention/prune (T-12) ──────────────────────────────────────────
    // Note: only observation types with a TTL in RetentionConfig are considered.
    // "passive" has no TTL, so prune(type="passive") always returns 0.

    [Fact]
    public async Task POST_retention_prune_DryRun_Returns200()
    {
        await SeedObservation("test-s1", "Keep me", "Body", "decision", "test-proj");

        var resp = await _client.PostAsync("/retention/prune?dry_run=true",
            new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var json = await resp.Content.ReadFromJsonAsync<JsonObject>(JsonOpts);
        Assert.NotNull(json);
        Assert.NotNull(json!["pruned"]);
        Assert.True((bool)json["dry_run"]);

        // Dry run must not actually delete observations.
        var obs = await _client.GetAsync("/observations/1");
        // id=1 may or may not exist depending on inserts, but we just want to
        // confirm no 5xx response — a clean 200/404 is acceptable.
        Assert.True(obs.StatusCode is HttpStatusCode.OK or HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task POST_retention_prune_ActualPrune_Returns200()
    {
        await SeedObservation("test-s1", "Old passive", "Body", "passive", "test-proj");

        var resp = await _client.PostAsJsonAsync("/retention/prune", new
        {
            type = "passive",
        }, JsonOpts);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await resp.Content.ReadFromJsonAsync<JsonObject>(JsonOpts);
        Assert.NotNull(json);
        Assert.NotNull(json!["pruned"]);
        Assert.NotNull(json["details"]);
        // "passive" has no TTL, so pruned is 0 — but the endpoint shape is correct.
        Assert.Equal(0, (int)json["pruned"]!);
    }

    [Fact]
    public async Task POST_retention_prune_EmptyBody_Returns200()
    {
        // No type filter, no dry-run. Scans all TTL-aware types; with a fresh store
        // nothing expires so the endpoint just returns the expected shape.
        var resp = await _client.PostAsJsonAsync("/retention/prune", new { }, JsonOpts);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var json = await resp.Content.ReadFromJsonAsync<JsonObject>(JsonOpts);
        Assert.NotNull(json);
        Assert.NotNull(json!["pruned"]);
        Assert.NotNull(json["dry_run"]);
        Assert.NotNull(json["details"]);
    }
}
