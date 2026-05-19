using System.Text.Json;
using Engram.Store;
using Engram.Sync;
using Engram.Sync.Transport;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Engram.Server;

/// <summary>
/// Builds and configures the Engram HTTP API.
/// Route structure and behaviour are identical to the Go original.
/// </summary>
public static class EngramServer
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition      = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented               = false,
    };

    // ─── WebApplication factory ─────────────────────────────────────────────

    /// <summary>
    /// Creates and configures a minimal WebApplication.
    /// Call <c>app.Run()</c> after this to start listening.
    /// </summary>
    public static WebApplication Build(IStore store, StoreConfig cfg)
    {
        var builder = WebApplication.CreateBuilder();

        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();

        builder.Services.AddSingleton(store);
        builder.Services.AddSingleton(cfg);

        // Offline-first sync (Phase 2.4) — SyncManager background service
        var syncConfig = SyncManagerConfig.FromEnvironment();
        if (syncConfig.Enabled)
        {
            builder.Services.AddHttpClient("sync");
            builder.Services.AddSingleton(syncConfig);
            builder.Services.AddSingleton<IMutationTransport>(sp =>
            {
                var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient("sync");
                return new MutationTransport(httpClient, $"http://localhost:{cfg.Port}", cfg.User);
            });
            builder.Services.AddSingleton<SyncMetrics>();
            builder.Services.AddSingleton<ISyncStatusProvider>(sp => sp.GetRequiredService<SyncManager>());
            builder.Services.AddHostedService<SyncManager>();
        }

        // CORS (optional — driven by ENGRAM_CORS_ORIGINS)
        if (!string.IsNullOrEmpty(cfg.CorsOrigins))
        {
            builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
            {
                var origins = cfg.CorsOrigins.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod();
            }));
        }

        var app = builder.Build();

        if (!string.IsNullOrEmpty(cfg.CorsOrigins))
            app.UseCors();

        MapRoutes(app, store);

        return app;
    }

    // ─── Routes ─────────────────────────────────────────────────────────────

    internal static void MapRoutes(IEndpointRouteBuilder app, IStore store)
    {
        app.MapGet("/health",                       (Func<IStore, IResult>)(HandleHealth));
        app.MapPost("/sessions",                    (Func<HttpContext, Task<IResult>>)((ctx) => HandleCreateSession(ctx, store)));
        app.MapPost("/sessions/{id}/end",           (Func<HttpContext, Task<IResult>>)((ctx) => HandleEndSession(ctx, store)));
        app.MapGet("/sessions/recent",              (Func<HttpContext, Task<IResult>>)((ctx) => HandleRecentSessions(ctx, store)));
        app.MapGet("/sessions/{id}",                (Func<HttpContext, Task<IResult>>)((ctx) => HandleGetSession(ctx, store)));
        app.MapDelete("/sessions/{id}",             (Func<HttpContext, Task<IResult>>)((ctx) => HandleDeleteSession(ctx, store)));
        app.MapPost("/observations",                (Func<HttpContext, Task<IResult>>)((ctx) => HandleAddObservation(ctx, store)));
        app.MapPost("/observations/passive",        (Func<HttpContext, Task<IResult>>)((ctx) => HandlePassiveCapture(ctx, store)));
        app.MapGet("/observations/recent",          (Func<HttpContext, Task<IResult>>)((ctx) => HandleRecentObservations(ctx, store)));
        app.MapGet("/observations/{id:long}",       (Func<HttpContext, Task<IResult>>)((ctx) => HandleGetObservation(ctx, store)));
        app.MapPatch("/observations/{id:long}",     (Func<HttpContext, Task<IResult>>)((ctx) => HandleUpdateObservation(ctx, store)));
        app.MapDelete("/observations/{id:long}",    (Func<HttpContext, Task<IResult>>)((ctx) => HandleDeleteObservation(ctx, store)));
        app.MapGet("/search",                       (Func<HttpContext, Task<IResult>>)((ctx) => HandleSearch(ctx, store)));
        app.MapGet("/timeline",                     (Func<HttpContext, Task<IResult>>)((ctx) => HandleTimeline(ctx, store)));
        app.MapPost("/prompts",                     (Func<HttpContext, Task<IResult>>)((ctx) => HandleAddPrompt(ctx, store)));
        app.MapGet("/prompts/recent",               (Func<HttpContext, Task<IResult>>)((ctx) => HandleRecentPrompts(ctx, store)));
        app.MapGet("/prompts/search",               (Func<HttpContext, Task<IResult>>)((ctx) => HandleSearchPrompts(ctx, store)));
        app.MapDelete("/prompts/{id}",              (Func<HttpContext, Task<IResult>>)((ctx) => HandleDeletePrompt(ctx, store)));
        app.MapGet("/context",                      (Func<HttpContext, Task<IResult>>)((ctx) => HandleContext(ctx, store)));
        app.MapGet("/export",                       (Func<HttpContext, Task<IResult>>)((ctx) => HandleExport(ctx, store)));
        app.MapPost("/import",                      (Func<HttpContext, Task<IResult>>)((ctx) => HandleImport(ctx, store)));
        app.MapGet("/stats",                        (Func<HttpContext, Task<IResult>>)((ctx) => HandleStats(ctx, store)));
        app.MapPost("/projects/migrate",            (Func<HttpContext, Task<IResult>>)((ctx) => HandleMigrateProject(ctx, store)));
        app.MapGet("/projects/list",                 (Func<HttpContext, Task<IResult>>)((ctx) => HandleProjectList(ctx, store)));
        app.MapGet("/projects/stats",                (Func<HttpContext, Task<IResult>>)((ctx) => HandleProjectStats(ctx, store)));
        app.MapPost("/projects/prune",               (Func<HttpContext, Task<IResult>>)((ctx) => HandleProjectPrune(ctx, store)));
        app.MapPost("/md/promote/{id:long}",        (Func<HttpContext, Task<IResult>>)((ctx) => HandlePromoteToMd(ctx, store)));
        app.MapPost("/md/sync",                     (Func<HttpContext, Task<IResult>>)((ctx) => HandleSyncMd(ctx, store)));
        app.MapPost("/md/index",                    (Func<HttpContext, Task<IResult>>)((ctx) => HandleGenerateIndex(ctx, store)));
        app.MapGet("/retention/stats",              (Func<HttpContext, Task<IResult>>)((ctx) => HandleRetentionStats(ctx, store)));
        app.MapPost("/retention/prune",             (Func<HttpContext, Task<IResult>>)((ctx) => HandleRetentionPrune(ctx, store)));
        app.MapGet("/projects/migrations",          (Func<HttpContext, Task<IResult>>)((ctx) => HandleProjectMigrations(ctx, store)));

        // Cloud sync endpoints (offline-first-sync Phase 1.3)
        app.MapCloudSyncRoutes();
    }
    
    internal const string UserHeader = "X-Engram-User";

    // ─── Handlers ───────────────────────────────────────────────────────────

    private static IResult HandleHealth(IStore store) =>
        Json(new { status = "ok", service = "engram", version = "1.1.0", backend = store.BackendName });

    private static async Task<IResult> HandleCreateSession(HttpContext ctx, IStore store)
    {
        var body = await ReadJson<CreateSessionRequest>(ctx);
        if (body is null || string.IsNullOrEmpty(body.Id) || string.IsNullOrEmpty(body.Project))
            return Error("id and project are required");

        await store.CreateSessionAsync(body.Id, body.Project, body.Directory ?? "");
        return Results.Created("", new { id = body.Id, status = "created" });
    }

    private static async Task<IResult> HandleEndSession(HttpContext ctx, IStore store)
    {
        var id   = ctx.Request.RouteValues["id"]?.ToString() ?? "";
        var body = await ReadJson<EndSessionRequest>(ctx) ?? new EndSessionRequest();
        await store.EndSessionAsync(id, body.Summary ?? "");
        return Json(new { id, status = "completed" });
    }

    private static async Task<IResult> HandleRecentSessions(HttpContext ctx, IStore store)
    {
        var project = ctx.Request.Query["project"].FirstOrDefault();
        var limit   = QueryInt(ctx, "limit", 5);
        var result  = await store.RecentSessionsAsync(project, limit);
        return Json(result);
    }

    private static async Task<IResult> HandleGetSession(HttpContext ctx, IStore store)
    {
        var id      = ctx.Request.RouteValues["id"]?.ToString() ?? "";
        var session = await store.GetSessionAsync(id);
        if (session is null) return Results.NotFound(new { error = $"session {id} not found" });
        return Json(session);
    }

    private static async Task<IResult> HandleDeleteSession(HttpContext ctx, IStore store)
    {
        var id = ctx.Request.RouteValues["id"]?.ToString() ?? "";

        try
        {
            await store.DeleteSessionAsync(id);
        }
        catch (SessionNotFoundException)
        {
            return Error($"session not found: {id}", 404);
        }
        catch (SessionDeleteBlockedException ex)
        {
            return Error($"session has {ex.ObservationCount} active observations, cannot delete", 409);
        }

        return Json(new { id, status = "deleted" });
    }

    private static async Task<IResult> HandleAddObservation(HttpContext ctx, IStore store)
    {
        var body = await ReadJson<AddObservationParams>(ctx);
        if (body is null || string.IsNullOrEmpty(body.SessionId) || string.IsNullOrEmpty(body.Title) || string.IsNullOrEmpty(body.Content))
            return Error("session_id, title, and content are required");

        var userId = GetUserId(ctx);
        var p      = body with { Scope = NormalizeScope(body.Scope, userId) };

        var id = await store.AddObservationAsync(p);
        return Results.Created("", new { id, status = "saved" });
    }

    private static async Task<IResult> HandlePassiveCapture(HttpContext ctx, IStore store)
    {
        var body = await ReadJson<PassiveCaptureRequest>(ctx);
        if (body is null || string.IsNullOrEmpty(body.SessionId))
            return Error("session_id is required");
        if (string.IsNullOrEmpty(body.Content))
            return Json(new { extracted = 0, saved = 0, duplicates = 0 });

        var learnings = PassiveCapture.ExtractLearnings(body.Content);
        int saved = 0;

        foreach (var learning in learnings)
        {
            var title = learning.Length > 60 ? learning[..60] + "..." : learning;
            var result = await store.AddObservationAsync(new AddObservationParams
            {
                SessionId = body.SessionId,
                Type      = "passive",
                Title     = title,
                Content   = learning,
                Project   = body.Project,
                Scope     = "project",
                ToolName  = body.Source,
            });
            // If duplicate the store returns the existing ID and increments duplicate_count
            if (result > 0) saved++;
        }

        return Json(new { extracted = learnings.Count, saved, duplicates = learnings.Count - saved });
    }

    private static async Task<IResult> HandleRecentObservations(HttpContext ctx, IStore store)
    {
        var project = ctx.Request.Query["project"].FirstOrDefault();
        var scope   = ctx.Request.Query["scope"].FirstOrDefault();
        var limit   = QueryInt(ctx, "limit", 20);

        var userId  = GetUserId(ctx);
        var result  = await store.RecentObservationsAsync(project, NormalizeScope(scope, userId), limit);
        return Json(result);
    }

    private static async Task<IResult> HandleGetObservation(HttpContext ctx, IStore store)
    {
        if (!long.TryParse(ctx.Request.RouteValues["id"]?.ToString(), out var id))
            return Error("invalid observation id", 400);

        var obs = await store.GetObservationAsync(id);
        if (obs is null) return Results.NotFound(new { error = "observation not found" });
        return Json(obs);
    }

    private static async Task<IResult> HandleUpdateObservation(HttpContext ctx, IStore store)
    {
        if (!long.TryParse(ctx.Request.RouteValues["id"]?.ToString(), out var id))
            return Error("invalid observation id", 400);

        var body = await ReadJson<UpdateObservationParams>(ctx);
        if (body is null || (body.Type is null && body.Title is null && body.Content is null &&
                             body.Project is null && body.Scope is null && body.TopicKey is null))
            return Error("at least one field is required");

        var ok = await store.UpdateObservationAsync(id, body);
        if (!ok) return Results.NotFound(new { error = "observation not found" });

        var updated = await store.GetObservationAsync(id);
        return Json(updated);
    }

    private static async Task<IResult> HandleDeleteObservation(HttpContext ctx, IStore store)
    {
        if (!long.TryParse(ctx.Request.RouteValues["id"]?.ToString(), out var id))
            return Error("invalid observation id", 400);

        var ok = await store.DeleteObservationAsync(id);
        if (!ok) return Results.NotFound(new { error = "observation not found" });

        return Json(new { id, status = "deleted", hard_delete = false });
    }

    private static async Task<IResult> HandleSearch(HttpContext ctx, IStore store)
    {
        var query = ctx.Request.Query["q"].FirstOrDefault() ?? "";
        if (string.IsNullOrEmpty(query)) return Error("q parameter is required");

        var userId = GetUserId(ctx);
        var results = await store.SearchAsync(query, new SearchOptions
        {
            Type    = ctx.Request.Query["type"].FirstOrDefault(),
            Project = ctx.Request.Query["project"].FirstOrDefault(),
            Scope   = NormalizeScope(ctx.Request.Query["scope"].FirstOrDefault(), userId),
            Limit   = QueryInt(ctx, "limit", 10),
        });

        // Enrich results with project redirect info
        if (results.Count > 0)
        {
            var migrations = await store.GetProjectMigrationsAsync();
            var redirects  = migrations.ToDictionary(m => m.FromProject, m => m.ToProject);
            var found      = new List<object>();

            foreach (var r in results)
            {
                if (r.Observation?.Project is not null && redirects.TryGetValue(r.Observation.Project, out var to))
                    found.Add(new { from = r.Observation.Project, to });
            }

            if (found.Count > 0)
                return Json(new { results, redirects = found });
        }

        return Json(results);
    }

    private static async Task<IResult> HandleTimeline(HttpContext ctx, IStore store)
    {
        var idStr = ctx.Request.Query["observation_id"].FirstOrDefault() ?? "";
        if (string.IsNullOrEmpty(idStr)) return Error("observation_id parameter is required");
        if (!long.TryParse(idStr, out var id)) return Error("invalid observation_id");

        var before = QueryInt(ctx, "before", 5);
        var after  = QueryInt(ctx, "after",  5);

        var result = await store.TimelineAsync(id, before, after);
        if (result is null) return Results.NotFound(new { error = "observation not found" });
        return Json(result);
    }

    private static async Task<IResult> HandleAddPrompt(HttpContext ctx, IStore store)
    {
        var body = await ReadJson<AddPromptParams>(ctx);
        if (body is null || string.IsNullOrEmpty(body.SessionId) || string.IsNullOrEmpty(body.Content))
            return Error("session_id and content are required");

        var id = await store.AddPromptAsync(body);
        return Results.Created("", new { id, status = "saved" });
    }

    private static async Task<IResult> HandleRecentPrompts(HttpContext ctx, IStore store)
    {
        var project = ctx.Request.Query["project"].FirstOrDefault();
        var limit   = QueryInt(ctx, "limit", 20);
        var result  = await store.RecentPromptsAsync(project, limit);
        return Json(result);
    }

    private static async Task<IResult> HandleSearchPrompts(HttpContext ctx, IStore store)
    {
        var query = ctx.Request.Query["q"].FirstOrDefault() ?? "";
        if (string.IsNullOrEmpty(query)) return Error("q parameter is required");

        var project = ctx.Request.Query["project"].FirstOrDefault();
        var limit   = QueryInt(ctx, "limit", 10);
        var result  = await store.SearchPromptsAsync(query, project, limit);
        return Json(result);
    }

    private static async Task<IResult> HandleDeletePrompt(HttpContext ctx, IStore store)
    {
        var idValue = ctx.Request.RouteValues["id"]?.ToString() ?? "";
        if (!long.TryParse(idValue, out var id))
            return Error("invalid prompt id", 400);

        try
        {
            await store.DeletePromptAsync(id);
        }
        catch (PromptNotFoundException)
        {
            return Error($"prompt not found: {id}", 404);
        }

        return Json(new { id, status = "deleted" });
    }

    private static async Task<IResult> HandleContext(HttpContext ctx, IStore store)
    {
        var project = ctx.Request.Query["project"].FirstOrDefault();
        var scope   = ctx.Request.Query["scope"].FirstOrDefault();
        var userId  = GetUserId(ctx);
        
        var context = await store.FormatContextAsync(project, NormalizeScope(scope, userId));
        return Json(new { context });
    }

    private static async Task<IResult> HandleExport(HttpContext ctx, IStore store)
    {
        var data = await store.ExportAsync();
        ctx.Response.Headers["Content-Disposition"] = "attachment; filename=engram-export.json";
        return Json(data);
    }

    private static async Task<IResult> HandleImport(HttpContext ctx, IStore store)
    {
        using var ms = new System.IO.MemoryStream();
        await ctx.Request.Body.CopyToAsync(ms);
        if (ms.Length > 50 * 1024 * 1024) return Error("request body too large", 413);

        ExportData? data;
        try { data = JsonSerializer.Deserialize<ExportData>(ms.ToArray(), JsonOpts); }
        catch { return Error("invalid json"); }

        if (data is null) return Error("invalid json");

        var result = await store.ImportAsync(data);
        return Json(result);
    }

    private static async Task<IResult> HandleStats(HttpContext ctx, IStore store)
    {
        var stats = await store.StatsAsync();
        stats.Backend = store.BackendName;
        return Json(stats);
    }

    private static async Task<IResult> HandleMigrateProject(HttpContext ctx, IStore store)
    {
        var body = await ReadJson<MigrateProjectRequest>(ctx);
        if (body is null || string.IsNullOrEmpty(body.OldProject) || string.IsNullOrEmpty(body.NewProject))
            return Error("old_project and new_project are required");

        if (body.OldProject == body.NewProject)
            return Json(new { status = "skipped", reason = "names are identical" });

        var result = await store.MergeProjectsAsync([body.OldProject], body.NewProject);

        if (result.ObservationsUpdated == 0 && result.SessionsUpdated == 0 && result.PromptsUpdated == 0)
            return Json(new { status = "skipped", reason = "no records found" });

        await store.AddProjectMigrationAsync(body.OldProject, body.NewProject);

        return Json(new
        {
            status       = "migrated",
            old_project  = body.OldProject,
            new_project  = body.NewProject,
            observations = result.ObservationsUpdated,
            sessions     = result.SessionsUpdated,
            prompts      = result.PromptsUpdated,
        });
    }

    private static async Task<IResult> HandleProjectList(HttpContext ctx, IStore store)
    {
        var names = await store.ListProjectNamesAsync();
        return Json(names);
    }

    private static async Task<IResult> HandleProjectStats(HttpContext ctx, IStore store)
    {
        var stats = await store.ListProjectsWithStatsAsync();
        return Json(stats);
    }

    private static async Task<IResult> HandleProjectPrune(HttpContext ctx, IStore store)
    {
        var body = await ReadJson<PruneProjectRequest>(ctx);
        if (body is null || string.IsNullOrEmpty(body.Project))
            return Error("missing or invalid 'project' field");

        var result = await store.PruneProjectAsync(body.Project);
        return Json(result);
    }

    private static async Task<IResult> HandleRetentionStats(HttpContext ctx, IStore store)
    {
        var stats = await store.GetRetentionStatsAsync();
        return Json(stats);
    }

    private static async Task<IResult> HandleRetentionPrune(HttpContext ctx, IStore store)
    {
        var body  = await ReadJson<RetentionPruneBody>(ctx) ?? new RetentionPruneBody();
        var dryRun = ctx.Request.Query["dry_run"].FirstOrDefault() == "true";
        var result = await store.PruneOldObservationsAsync(new RetentionPruneParams
        {
            Type   = body.Type,
            DryRun = dryRun,
        });
        return Json(result);
    }

    private static async Task<IResult> HandleProjectMigrations(HttpContext ctx, IStore store)
    {
        var migrations = await store.GetProjectMigrationsAsync();
        return Json(migrations);
    }

    // ─── MD Promotion Handlers ──────────────────────────────────────────────

    private static async Task<IResult> HandlePromoteToMd(HttpContext ctx, IStore store)
    {
        var id = ctx.Request.RouteValues["id"]?.ToString() ?? "";
        if (!long.TryParse(id, out var obsId))
            return Error("invalid observation id");

        var body = await ReadJson<PromoteRequest>(ctx) ?? new PromoteRequest();
        var mdDir = body.MdDir ?? "docs/decisions";
        var result = await store.PromoteToMdAsync(obsId, mdDir);
        return Json(new { id = result, md_dir = mdDir });
    }

    private static async Task<IResult> HandleSyncMd(HttpContext ctx, IStore store)
    {
        var body = await ReadJson<SyncMdRequest>(ctx) ?? new SyncMdRequest();
        var mdDir = body.MdDir ?? "docs/decisions";
        var count = await store.SyncMdToRepoAsync(mdDir, body.DryRun);
        return Json(new { count, md_dir = mdDir, dry_run = body.DryRun });
    }

    private static async Task<IResult> HandleGenerateIndex(HttpContext ctx, IStore store)
    {
        var body = await ReadJson<GenerateIndexRequest>(ctx) ?? new GenerateIndexRequest();
        var mdDir = body.MdDir ?? "docs/decisions";
        var content = await store.GenerateIndexAsync(mdDir);
        return Results.Content(content, "text/markdown");
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private static IResult Json(object? data) =>
        Results.Json(data, JsonOpts);

    private static IResult Error(string message, int status = 400) =>
        Results.Json(new { error = message }, JsonOpts, statusCode: status);

    private static async Task<T?> ReadJson<T>(HttpContext ctx)
    {
        try
        {
            return await JsonSerializer.DeserializeAsync<T>(ctx.Request.Body, JsonOpts);
        }
        catch
        {
            return default;
        }
    }

    private static int QueryInt(HttpContext ctx, string key, int defaultVal)
    {
        var v = ctx.Request.Query[key].FirstOrDefault();
        return int.TryParse(v, out var n) ? n : defaultVal;
    }

    private static string GetUserId(HttpContext ctx)
    {
        var id = ctx.Request.Headers[UserHeader].FirstOrDefault();
        return string.IsNullOrWhiteSpace(id) ? "global" : id.Trim().ToLowerInvariant();
    }

    private static string? NormalizeScope(string? scope, string userId)
    {
        if (string.IsNullOrWhiteSpace(scope)) return null;
        var s = scope.Trim().ToLowerInvariant();
        
        // "project" is legacy alias for personal
        if (s == "personal" || s == "project")
            return $"personal:{userId}";
            
        return s;
    }

    // ─── Request bodies ─────────────────────────────────────────────────────

    private sealed record CreateSessionRequest(string Id, string Project, string? Directory);
    private sealed record EndSessionRequest(string? Summary = null);
    private sealed record PassiveCaptureRequest(string SessionId, string? Content, string? Project, string? Source);
    private sealed record MigrateProjectRequest(string OldProject, string NewProject);
    private sealed record PruneProjectRequest(string Project);

    // MD Promotion request bodies
    private sealed record PromoteRequest(string? MdDir = null);
    private sealed record SyncMdRequest(string? MdDir = null, bool DryRun = false);
    private sealed record GenerateIndexRequest(string? MdDir = null);
    private sealed record RetentionPruneBody(string? Type = null);
}
