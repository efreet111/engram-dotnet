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
}
