using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Web;

namespace Engram.Store;

/// <summary>
/// IStore implementation that proxies every call to a remote Engram HTTP server.
/// Used in team/centralized mode when ENGRAM_URL is set.
///
/// Headers sent on every request:
///   X-Engram-User: {user}          — developer identity (from ENGRAM_USER)
///   Authorization: Bearer {token}  — only when ENGRAM_JWT_SECRET is set (future)
///
/// Error contract:
///   - Network / timeout errors  → throw EngramRemoteException with a human-readable message
///   - 404 responses             → return null / false (same as SqliteStore "not found")
///   - Other non-2xx responses   → throw EngramRemoteException with the server error body
/// </summary>
public sealed class HttpStore : IStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy   = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient  _http;
    private readonly StoreConfig _cfg;

    public int MaxObservationLength => _cfg.MaxObservationLength;

    public HttpStore(StoreConfig cfg)
    {
        _cfg = cfg;

        var handler = new HttpClientHandler();
        _http = new HttpClient(handler)
        {
            BaseAddress = new Uri(cfg.RemoteUrl!.TrimEnd('/') + "/"),
            Timeout     = TimeSpan.FromSeconds(30),
        };

        // Identity header — sent on every request via DefaultRequestHeaders
        if (!string.IsNullOrEmpty(cfg.User))
            _http.DefaultRequestHeaders.Add("X-Engram-User", cfg.User);

        // JWT auth — future: when server validates tokens
        // if (!string.IsNullOrEmpty(cfg.JwtSecret))
        //     _http.DefaultRequestHeaders.Authorization = new("Bearer", BuildToken(cfg));
    }

    // ─── Sessions ─────────────────────────────────────────────────────────────

    public async Task CreateSessionAsync(string id, string project, string directory)
    {
        var resp = await Post("sessions", new { id, project, directory });
        await EnsureSuccess(resp, "CreateSession");
    }

    public async Task EndSessionAsync(string id, string summary)
    {
        var resp = await Post($"sessions/{Uri.EscapeDataString(id)}/end", new { summary });
        await EnsureSuccess(resp, "EndSession");
    }

    public async Task<Session?> GetSessionAsync(string id)
    {
        var resp = await Get($"sessions/{Uri.EscapeDataString(id)}");
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        await EnsureSuccess(resp, "GetSession");
        return await Deserialize<Session>(resp);
    }

    public async Task<IList<SessionSummary>> RecentSessionsAsync(string? project, int limit)
    {
        var qs   = BuildQuery(("project", project), ("limit", limit.ToString()));
        var resp = await Get($"sessions/recent{qs}");
        await EnsureSuccess(resp, "RecentSessions");
        return await Deserialize<List<SessionSummary>>(resp) ?? [];
    }

    // ─── Observations ─────────────────────────────────────────────────────────

    public async Task<long> AddObservationAsync(AddObservationParams p)
    {
        var resp = await Post("observations", p);
        await EnsureSuccess(resp, "AddObservation");
        var result = await Deserialize<IdResponse>(resp);
        return result?.Id ?? 0;
    }

    public async Task<Observation?> GetObservationAsync(long id)
    {
        var resp = await Get($"observations/{id}");
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        await EnsureSuccess(resp, "GetObservation");
        return await Deserialize<Observation>(resp);
    }

    public async Task<IList<Observation>> RecentObservationsAsync(string? project, string? scope, int limit)
    {
        var qs   = BuildQuery(("project", project), ("scope", scope), ("limit", limit.ToString()));
        var resp = await Get($"observations/recent{qs}");
        await EnsureSuccess(resp, "RecentObservations");
        return await Deserialize<List<Observation>>(resp) ?? [];
    }

    public async Task<bool> UpdateObservationAsync(long id, UpdateObservationParams p)
    {
        var resp = await Patch($"observations/{id}", p);
        if (resp.StatusCode == HttpStatusCode.NotFound) return false;
        await EnsureSuccess(resp, "UpdateObservation");
        return true;
    }

    public async Task<bool> DeleteObservationAsync(long id)
    {
        var resp = await Delete($"observations/{id}");
        if (resp.StatusCode == HttpStatusCode.NotFound) return false;
        await EnsureSuccess(resp, "DeleteObservation");
        return true;
    }

    // ─── Search ───────────────────────────────────────────────────────────────

    public async Task<IList<SearchResult>> SearchAsync(string query, SearchOptions opts)
    {
        var qs = BuildQuery(
            ("q",       query),
            ("type",    opts.Type),
            ("project", opts.Project),
            ("scope",   opts.Scope),
            ("limit",   opts.Limit.ToString()));

        var resp = await Get($"search{qs}");
        await EnsureSuccess(resp, "Search");
        return await Deserialize<List<SearchResult>>(resp) ?? [];
    }

    public async Task<TimelineResult?> TimelineAsync(long observationId, int before, int after)
    {
        var qs   = BuildQuery(
            ("observation_id", observationId.ToString()),
            ("before",         before.ToString()),
            ("after",          after.ToString()));

        var resp = await Get($"timeline{qs}");
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        await EnsureSuccess(resp, "Timeline");
        return await Deserialize<TimelineResult>(resp);
    }

    // ─── Prompts ──────────────────────────────────────────────────────────────

    public async Task<long> AddPromptAsync(AddPromptParams p)
    {
        var resp = await Post("prompts", p);
        await EnsureSuccess(resp, "AddPrompt");
        var result = await Deserialize<IdResponse>(resp);
        return result?.Id ?? 0;
    }

    public async Task<IList<Prompt>> RecentPromptsAsync(string? project, int limit)
    {
        var qs   = BuildQuery(("project", project), ("limit", limit.ToString()));
        var resp = await Get($"prompts/recent{qs}");
        await EnsureSuccess(resp, "RecentPrompts");
        return await Deserialize<List<Prompt>>(resp) ?? [];
    }

    public async Task<IList<Prompt>> SearchPromptsAsync(string query, string? project, int limit)
    {
        var qs   = BuildQuery(("q", query), ("project", project), ("limit", limit.ToString()));
        var resp = await Get($"prompts/search{qs}");
        await EnsureSuccess(resp, "SearchPrompts");
        return await Deserialize<List<Prompt>>(resp) ?? [];
    }

    // ─── Context & Stats ──────────────────────────────────────────────────────

    public async Task<string> FormatContextAsync(string? project, string? scope)
    {
        var qs   = BuildQuery(("project", project), ("scope", scope));
        var resp = await Get($"context{qs}");
        await EnsureSuccess(resp, "FormatContext");
        var result = await Deserialize<ContextResponse>(resp);
        return result?.Context ?? "";
    }

    public async Task<Stats> StatsAsync()
    {
        var resp = await Get("stats");
        await EnsureSuccess(resp, "Stats");
        return await Deserialize<Stats>(resp) ?? new Stats();
    }

    // ─── Export / Import ──────────────────────────────────────────────────────

    public async Task<ExportData> ExportAsync()
    {
        var resp = await Get("export");
        await EnsureSuccess(resp, "Export");
        return await Deserialize<ExportData>(resp) ?? new ExportData();
    }

    public async Task<ImportResult> ImportAsync(ExportData data)
    {
        var resp = await Post("import", data);
        await EnsureSuccess(resp, "Import");
        return await Deserialize<ImportResult>(resp) ?? new ImportResult();
    }

    // ─── Projects ─────────────────────────────────────────────────────────────

    public async Task<MergeResult> MergeProjectsAsync(IList<string> sources, string canonical)
    {
        // The server exposes POST /projects/migrate (single source → canonical).
        // For multi-source we call it once per source.
        long obs = 0, sess = 0, prompts = 0;
        var merged = new List<string>();

        foreach (var src in sources)
        {
            var resp = await Post("projects/migrate", new { old_project = src, new_project = canonical });
            // 200 = migrated or skipped — both are fine
            if (resp.IsSuccessStatusCode)
                merged.Add(src);
        }

        return new MergeResult
        {
            Canonical           = canonical,
            SourcesMerged       = merged,
            ObservationsUpdated = obs,
            SessionsUpdated     = sess,
            PromptsUpdated      = prompts,
        };
    }

    // ─── Sync chunks (not supported in proxy mode) ────────────────────────────

    public Task<ISet<string>> GetSyncedChunksAsync()
        => Task.FromResult<ISet<string>>(new HashSet<string>());

    public Task RecordSyncedChunkAsync(string chunkId)
        => Task.CompletedTask;

    // ─── IDisposable ──────────────────────────────────────────────────────────

    public void Dispose() => _http.Dispose();

    // ─── HTTP helpers ─────────────────────────────────────────────────────────

    private async Task<HttpResponseMessage> Get(string path)
    {
        try { return await _http.GetAsync(path); }
        catch (Exception ex) { throw RemoteException("GET", path, ex); }
    }

    private async Task<HttpResponseMessage> Post(string path, object body)
    {
        try { return await _http.PostAsJsonAsync(path, body, JsonOpts); }
        catch (Exception ex) { throw RemoteException("POST", path, ex); }
    }

    private async Task<HttpResponseMessage> Patch(string path, object body)
    {
        try
        {
            var content = JsonContent.Create(body, options: JsonOpts);
            var request = new HttpRequestMessage(HttpMethod.Patch, path) { Content = content };
            return await _http.SendAsync(request);
        }
        catch (Exception ex) { throw RemoteException("PATCH", path, ex); }
    }

    private async Task<HttpResponseMessage> Delete(string path)
    {
        try { return await _http.DeleteAsync(path); }
        catch (Exception ex) { throw RemoteException("DELETE", path, ex); }
    }

    private static async Task EnsureSuccess(HttpResponseMessage resp, string operation)
    {
        if (resp.IsSuccessStatusCode) return;

        string detail;
        try
        {
            var body = await resp.Content.ReadAsStringAsync();
            // Try to extract "error" field from JSON response
            using var doc  = JsonDocument.Parse(body);
            detail = doc.RootElement.TryGetProperty("error", out var err)
                ? err.GetString() ?? body
                : body;
        }
        catch
        {
            detail = $"HTTP {(int)resp.StatusCode}";
        }

        throw new EngramRemoteException($"[engram] {operation} failed: {detail} (HTTP {(int)resp.StatusCode})");
    }

    private static async Task<T?> Deserialize<T>(HttpResponseMessage resp)
    {
        try
        {
            return await resp.Content.ReadFromJsonAsync<T>(JsonOpts);
        }
        catch (Exception ex)
        {
            throw new EngramRemoteException($"[engram] Failed to deserialize response: {ex.Message}");
        }
    }

    /// <summary>
    /// Builds a query string from key/value pairs, skipping null/empty values.
    /// </summary>
    private static string BuildQuery(params (string Key, string? Value)[] pairs)
    {
        var qs = HttpUtility.ParseQueryString("");
        foreach (var (key, val) in pairs)
            if (!string.IsNullOrEmpty(val))
                qs[key] = val;

        var built = qs.ToString();
        return string.IsNullOrEmpty(built) ? "" : "?" + built;
    }

    private static EngramRemoteException RemoteException(string method, string path, Exception inner)
    {
        var msg = inner is TaskCanceledException or TimeoutException
            ? $"[engram] Remote server timed out ({method} {path}). Is ENGRAM_URL reachable?"
            : $"[engram] Network error ({method} {path}): {inner.Message}";
        return new EngramRemoteException(msg, inner);
    }

    // ─── Private response models ──────────────────────────────────────────────

    private sealed class IdResponse
    {
        [JsonPropertyName("id")] public long Id { get; set; }
    }

    private sealed class ContextResponse
    {
        [JsonPropertyName("context")] public string? Context { get; set; }
    }
}

/// <summary>
/// Thrown when the remote Engram server returns an error or is unreachable.
/// The message is safe to surface to the LLM as a tool error.
/// </summary>
public sealed class EngramRemoteException(string message, Exception? inner = null)
    : Exception(message, inner);
