using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Engram.Server.Dtos;
using Engram.Sync;
using Engram.Store;

namespace Engram.Server;

/// <summary>
/// Cloud sync endpoints for mutation-based sync protocol.
/// Handles POST /sync/mutations/push and GET /sync/mutations/pull.
/// Separated from EngramServer.cs for testability and maintainability (AD-3).
/// </summary>
public static class CloudSyncEndpoints
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    /// <summary>
    /// Register cloud sync routes on the ASP.NET Core app.
    /// </summary>
    public static void MapCloudSyncRoutes(this IEndpointRouteBuilder app)
    {
        app.MapPost("/sync/mutations/push", async (HttpContext ctx, IStore store) =>
        {
            if (store is not ICloudMutationStore cloudStore)
                return Results.StatusCode(501);

            return await HandleMutationPushAsync(ctx, cloudStore);
        });

        app.MapGet("/sync/mutations/pull", async (HttpContext ctx, IStore store) =>
        {
            if (store is not ICloudMutationStore cloudStore)
                return Results.StatusCode(501);

            return await HandleMutationPullAsync(ctx, cloudStore);
        });

        // Phase 3.2: Pause/Resume endpoints
        app.MapPost("/sync/pause", async (HttpContext ctx, IStore store) =>
        {
            if (store is not ICloudMutationStore cloudStore)
                return Results.StatusCode(501);

            return await HandlePauseAsync(ctx, cloudStore);
        });

        app.MapDelete("/sync/pause", async (HttpContext ctx, IStore store) =>
        {
            if (store is not ICloudMutationStore cloudStore)
                return Results.StatusCode(501);

            return await HandleResumeAsync(ctx, cloudStore);
        });

        // Phase 3.1: Enrollment endpoints
        app.MapPost("/sync/enroll", async (HttpContext ctx, IStore store) =>
        {
            Console.Error.WriteLine(">>> DEBUG: POST /sync/enroll reached");
            System.Diagnostics.Debug.WriteLine(">>> DEBUG: POST /sync/enroll reached");

            if (store is not ICloudMutationStore cloudStore)
                return Results.StatusCode(501);

            Console.Error.WriteLine(">>> DEBUG: store is ICloudMutationStore");
            try
            {
                var body = await ReadJsonAsync<EnrollmentRequest>(ctx, 1024 * 1024);
                Console.Error.WriteLine($">>> DEBUG: body parsed: {body?.Project}");

                if (body is null || string.IsNullOrEmpty(body.Project))
                    return ErrorJson(ctx, "project is required", "invalid-request", 400);

                var user = ctx.Request.Headers[EngramServer.UserHeader].FirstOrDefault() ?? "anonymous";
                Console.Error.WriteLine($">>> DEBUG: user={user}, project={body.Project}");

                var result = await cloudStore.EnrollProjectAsync(body.Project, user, ctx.RequestAborted);
                Console.Error.WriteLine($">>> DEBUG: enroll result: {result?.Status}");

                if (result.Status == "already_enrolled")
                {
                    return Results.Json(
                        new { error = "project already enrolled", project = body.Project },
                        JsonOpts,
                        statusCode: 409);
                }

                return Results.Json(new
                {
                    project = result.Project,
                    enrolled_at = result.EnrolledAt ?? DateTime.UtcNow.ToString("O"),
                    enrolled_by = result.EnrolledBy ?? user
                }, JsonOpts);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($">>> DEBUG ERROR: {ex.GetType().Name}: {ex.Message}");
                Console.Error.WriteLine(ex.ToString());
                return Results.Json(new { error = ex.Message, type = ex.GetType().Name }, JsonOpts, statusCode: 500);
            }
        });

        app.MapDelete("/sync/enroll", async (HttpContext ctx, IStore store) =>
        {
            if (store is not ICloudMutationStore cloudStore)
                return Results.StatusCode(501);

            return await HandleUnenrollProjectAsync(ctx, cloudStore);
        });

        app.MapGet("/sync/enroll", async (HttpContext ctx, IStore store) =>
        {
            if (store is not ICloudMutationStore cloudStore)
                return Results.StatusCode(501);

            return await HandleListEnrolledProjectsAsync(ctx, cloudStore);
        });

        // Phase 4: Observability — GET /sync/status
        app.MapGet("/sync/status", async (HttpContext ctx, IStore store) =>
        {
            return await HandleSyncStatusAsync(ctx, store);
        });
    }

    /// <summary>
    /// Handle POST /sync/mutations/push
    /// </summary>
    private static async Task<IResult> HandleMutationPushAsync(
        HttpContext ctx,
        ICloudMutationStore store)
    {
        var body = await ReadJsonAsync<PushRequestBody>(ctx, 8 * 1024 * 1024);
        if (body is null || body.Entries.Count == 0)
            return ErrorJson(ctx, "empty batch", "empty-batch", 400);

        if (body.Entries.Count > 100)
            return ErrorJson(ctx, "batch too large (max 100)", "batch-too-large", 400);

        for (int i = 0; i < body.Entries.Count; i++)
        {
            var entry = body.Entries[i];
            if (string.IsNullOrEmpty(entry.Project))
                return ErrorJson(ctx, $"entry {i}: project is required", "invalid-entry", 400);
            if (string.IsNullOrEmpty(entry.Entity))
                return ErrorJson(ctx, $"entry {i}: entity is required", "invalid-entry", 400);
            if (string.IsNullOrEmpty(entry.EntityKey))
                return ErrorJson(ctx, $"entry {i}: entity_key is required", "invalid-entry", 400);
            if (string.IsNullOrEmpty(entry.Op))
                return ErrorJson(ctx, $"entry {i}: op is required", "invalid-entry", 400);
            if (string.IsNullOrEmpty(entry.Payload))
                return ErrorJson(ctx, $"entry {i}: payload is required", "invalid-entry", 400);

            if (entry.Entity == "relation")
            {
                try
                {
                    var payload = JsonSerializer.Deserialize<JsonElement>(entry.Payload);
                    var requiredFields = new[] { "sync_id", "source_id", "target_id", "judgment_status", "marked_by_actor", "marked_by_kind" };
                    foreach (var field in requiredFields)
                    {
                        if (!payload.TryGetProperty(field, out _) || payload.GetProperty(field).ValueKind == JsonValueKind.Null || payload.GetProperty(field).ValueKind == JsonValueKind.Undefined)
                            return ErrorJson(ctx, $"entry {i}: relation payload missing required field '{field}'", "invalid-relation", 400);
                    }
                }
                catch (JsonException)
                {
                    return ErrorJson(ctx, $"entry {i}: relation payload is not valid JSON", "invalid-relation", 400);
                }
            }
        }

        var projects = body.Entries.Select(e => e.Project).Distinct().ToList();
        foreach (var project in projects)
        {
            if (!await store.IsProjectSyncEnabledAsync(project))
            {
                await store.InsertAuditEntryAsync(new AuditEntry(
                    project, "push", "rejected",
                    body.CreatedBy, body.Entries.Count, "sync-paused"
                ));

                return ErrorJson(ctx, "sync is paused for this project", "sync-paused", 409, project);
            }
        }

        var entries = body.Entries.Select(e =>
            new MutationEntry(e.Project, e.Entity, e.EntityKey, e.Op, e.Payload)
        ).ToList();

        var seqs = await store.InsertMutationBatchAsync(entries, body.CreatedBy);

        var firstProject = projects.FirstOrDefault() ?? "";
        return Results.Json(new PushResponseBody(seqs, firstProject, "request_body", ""), JsonOpts);
    }

    /// <summary>
    /// Handle GET /sync/mutations/pull
    /// </summary>
    private static async Task<IResult> HandleMutationPullAsync(
        HttpContext ctx,
        ICloudMutationStore store)
    {
        var sinceSeq = ParseLong(ctx.Request.Query["since_seq"], 0);
        var limit = Math.Min(ParseInt(ctx.Request.Query["limit"], 100), 100);
        var project = ctx.Request.Query["project"].FirstOrDefault() ?? "";

        List<string>? allowedProjects = null;
        if (!string.IsNullOrEmpty(project))
        {
            // Get user from context (for team mode) or use empty string for local mode
            var user = ctx.User.Identity?.Name ?? ctx.Request.Headers[EngramServer.UserHeader].FirstOrDefault() ?? "";
            var enrolledProjects = await store.GetEnrolledProjectsAsync(user, ctx.RequestAborted);
            
            // Check if project is enrolled
            var isEnrolled = enrolledProjects.Any(ep => ep.Project == project);
            if (!isEnrolled)
            {
                return Results.Ok(new PullResponseBody(
                    Mutations: [],
                    HasMore: false,
                    LatestSeq: 0,
                    Project: project,
                    ProjectSource: "query_param",
                    ProjectPath: "not_enrolled"
                ));
            }
            
            allowedProjects = [project];
        }

        var (mutations, hasMore, latestSeq) = await store.ListMutationsSinceAsync(
            sinceSeq, limit, allowedProjects, ctx.RequestAborted);

        var body = new PullResponseBody(
            Mutations: mutations.Select(m => new PulledMutationBody(
                m.Seq, m.Project, m.Entity, m.EntityKey, m.Op, m.Payload, m.OccurredAt
            )).ToList(),
            HasMore: hasMore,
            LatestSeq: latestSeq,
            Project: project,
            ProjectSource: "query_param",
            ProjectPath: ""
        );

        return Results.Json(body, JsonOpts);
    }

    /// <summary>
    /// Handle POST /sync/pause
    /// </summary>
    private static async Task<IResult> HandlePauseAsync(
        HttpContext ctx,
        ICloudMutationStore store)
    {
        var body = await ReadJsonAsync<PauseRequestBody>(ctx, 8 * 1024 * 1024);
        if (body is null || string.IsNullOrEmpty(body.Project))
            return ErrorJson(ctx, "project is required", "invalid-request", 400);

        if (string.IsNullOrEmpty(body.Reason))
            return ErrorJson(ctx, "reason is required", "invalid-request", 400);

        var pausedBy = ctx.Request.Headers["X-Engram-User"].FirstOrDefault() ?? "admin";

        var result = await store.PauseProjectAsync(body.Project, body.Reason, pausedBy);

        var responseBody = new PauseResponseBody(
            Project: result.Project,
            Paused: result.Paused,
            PausedAt: result.PausedAt,
            PausedBy: result.PausedBy,
            Reason: result.Reason
        );

        return Results.Json(responseBody, JsonOpts);
    }

    /// <summary>
    /// Handle DELETE /sync/pause
    /// </summary>
    private static async Task<IResult> HandleResumeAsync(
        HttpContext ctx,
        ICloudMutationStore store)
    {
        var project = ctx.Request.Query["project"].FirstOrDefault();
        if (string.IsNullOrEmpty(project))
            return ErrorJson(ctx, "project query parameter is required", "invalid-request", 400);

        var resumedBy = ctx.Request.Headers["X-Engram-User"].FirstOrDefault() ?? "admin";

        var result = await store.ResumeProjectAsync(project, resumedBy);

        var responseBody = new PauseResponseBody(
            Project: result.Project,
            Paused: result.Paused,
            ResumedAt: result.ResumedAt,
            ResumedBy: result.ResumedBy
        );

        return Results.Json(responseBody, JsonOpts);
    }

    /// <summary>
    /// Handle POST /sync/enroll
    /// </summary>
    private static async Task<IResult> HandleEnrollProjectAsync(
        HttpContext ctx,
        ICloudMutationStore store)
    {
        var body = await ReadJsonAsync<EnrollmentRequest>(ctx, 1024 * 1024);
        if (body is null || string.IsNullOrEmpty(body.Project))
            return ErrorJson(ctx, "project is required", "invalid-request", 400);

        var user = ctx.Request.Headers[EngramServer.UserHeader].FirstOrDefault() ?? "anonymous";
        var result = await store.EnrollProjectAsync(body.Project, user, ctx.RequestAborted);

        if (result.Status == "already_enrolled")
        {
            return Results.Json(
                new { error = "project already enrolled", project = body.Project },
                JsonOpts,
                statusCode: 409);
        }

        // Log audit entry
        await store.InsertAuditEntryAsync(new AuditEntry(
            Project: body.Project,
            Action: "enroll",
            Outcome: "success",
            Contributor: user,
            EntryCount: 0
        ), ctx.RequestAborted);

        return Results.Json(new
        {
            project = result.Project,
            enrolled_at = result.EnrolledAt ?? DateTime.UtcNow.ToString("O"),
            enrolled_by = result.EnrolledBy ?? user
        }, JsonOpts);
    }

    /// <summary>
    /// Handle DELETE /sync/enroll
    /// </summary>
    private static async Task<IResult> HandleUnenrollProjectAsync(
        HttpContext ctx,
        ICloudMutationStore store)
    {
        var project = ctx.Request.Query["project"].FirstOrDefault();
        if (string.IsNullOrEmpty(project))
            return ErrorJson(ctx, "project query param is required", "invalid-request", 400);

        var user = ctx.Request.Headers[EngramServer.UserHeader].FirstOrDefault() ?? "anonymous";
        var result = await store.UnenrollProjectAsync(project, user, ctx.RequestAborted);

        if (result.Status == "not_found")
        {
            return Results.Json(
                new { error = "project not enrolled", project },
                JsonOpts,
                statusCode: 404);
        }

        // Log audit entry
        await store.InsertAuditEntryAsync(new AuditEntry(
            Project: project,
            Action: "unenroll",
            Outcome: "success",
            Contributor: user,
            EntryCount: 0
        ), ctx.RequestAborted);

        return Results.Json(new
        {
            project = result.Project,
            unenrolled_at = result.UnenrolledAt ?? DateTime.UtcNow.ToString("O"),
            status = result.Status
        }, JsonOpts);
    }

    /// <summary>
    /// Handle GET /sync/enroll
    /// </summary>
    private static async Task<IResult> HandleListEnrolledProjectsAsync(
        HttpContext ctx,
        ICloudMutationStore store)
    {
        var user = ctx.Request.Headers[EngramServer.UserHeader].FirstOrDefault() ?? "anonymous";
        var projects = await store.GetEnrolledProjectsAsync(user, ctx.RequestAborted);

        return Results.Json(new
        {
            projects = projects.Select(p => new { project = p.Project, enrolled_at = p.EnrolledAt, enrolled_by = p.EnrolledBy }).ToList(),
            count = projects.Count
        }, JsonOpts);
    }

    /// <summary>
    /// Handle GET /sync/status — consolidated sync observability endpoint.
    /// Resolves ISyncStatusProvider optionally (null-safe when SyncManager not registered).
    /// </summary>
    private static async Task<IResult> HandleSyncStatusAsync(HttpContext ctx, IStore store)
    {
        var provider = ctx.RequestServices.GetService<ISyncStatusProvider>();

        SyncState? state = null;
        List<SyncMutation> pending = [];
        if (store is ILocalSyncStore localStore)
        {
            state = await localStore.GetSyncStateAsync("cloud", ctx.RequestAborted);
            pending = await localStore.ListPendingSyncMutationsAsync("cloud", 0, ctx.RequestAborted);
        }

        List<EnrolledProject> enrolled = [];
        if (store is ICloudMutationStore cloudStore)
        {
            var user = ctx.Request.Headers[EngramServer.UserHeader].FirstOrDefault() ?? "";
            enrolled = await cloudStore.GetEnrolledProjectsAsync(user, ctx.RequestAborted);
        }

        var metrics = provider?.Metrics;
        var phase = provider?.Phase ?? SyncPhase.Idle;
        // PostgreSQL cloud server: no local SyncManager — push/pull API is always available.
        var isCloudRelay = store is ICloudMutationStore && provider is null;

        var response = new SyncStatusResponse(
            SyncEnabled: provider?.IsEnabled ?? isCloudRelay,
            Phase: isCloudRelay ? "cloud" : phase.ToString().ToLowerInvariant(),
            Target: "cloud",
            Cursor: new StatusCursorBody(
                LastPushedSeq: state?.LastAckedSeq ?? metrics?.TotalPushed ?? 0,
                LastPulledSeq: state?.LastPulledSeq ?? metrics?.TotalPulled ?? 0,
                LastEnqueuedSeq: state?.LastEnqueuedSeq ?? 0
            ),
            Health: new StatusHealthBody(
                Status: isCloudRelay
                    ? "healthy"
                    : phase switch
                {
                    SyncPhase.Disabled => "disabled",
                    SyncPhase.Backoff or SyncPhase.PushFailed or SyncPhase.PullFailed => "degraded",
                    _ => "healthy"
                },
                ConsecutiveFailures: state?.ConsecutiveFailures ?? provider?.ConsecutiveFailures ?? 0,
                BackoffUntil: provider?.BackoffUntil?.ToString("O"),
                LastError: state?.LastError ?? metrics?.LastError,
                LastSyncAt: metrics?.LastSyncAt is DateTime lastSync && lastSync > DateTime.MinValue
                    ? lastSync.ToString("O")
                    : null
            ),
            Counts: new StatusCountsBody(
                PendingPush: pending.Count,
                TotalPushed: metrics?.TotalPushed ?? 0,
                TotalPulled: metrics?.TotalPulled ?? 0,
                DeferredPending: metrics?.DeferredReplayed ?? 0
            ),
            EnrolledProjects: enrolled.Select(e => e.Project).ToList(),
            PausedProjects: []
        );

        return Results.Json(response, JsonOpts);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static async Task<T?> ReadJsonAsync<T>(HttpContext ctx, long maxBytes)
    {
        try
        {
            ctx.Request.EnableBuffering();
            
            if (ctx.Request.ContentLength > maxBytes)
                return default;

            var body = await JsonSerializer.DeserializeAsync<T>(ctx.Request.Body, JsonOpts);
            ctx.Request.Body.Position = 0;
            return body;
        }
        catch (Exception ex)
        {
            var logger = ctx.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("ReadJson");
            logger.LogError(ex, "ReadJsonAsync failed");
            return default;
        }
    }

    private static IResult ErrorJson(HttpContext ctx, string error, string errorCode, int status, string? project = null)
    {
        var body = new ErrorResponseBody("policy", errorCode, error, project ?? "", "request", "");
        return Results.Json(body, JsonOpts, statusCode: status);
    }

    private static long ParseLong(string? value, long defaultValue) =>
        long.TryParse(value, out var v) ? v : defaultValue;

    private static int ParseInt(string? value, int defaultValue) =>
        int.TryParse(value, out var v) ? v : defaultValue;
}
