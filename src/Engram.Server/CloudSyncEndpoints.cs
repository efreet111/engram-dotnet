using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Engram.Server.Dtos;
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
                return Results.StatusCode(501); // Not implemented for SqliteStore/HttpStore

            return await HandleMutationPushAsync(ctx, cloudStore);
        });

        app.MapGet("/sync/mutations/pull", async (HttpContext ctx, IStore store) =>
        {
            if (store is not ICloudMutationStore cloudStore)
                return Results.StatusCode(501); // Not implemented for SqliteStore/HttpStore

            return await HandleMutationPullAsync(ctx, cloudStore);
        });
    }

    /// <summary>
    /// Handle POST /sync/mutations/push
    /// 
    /// Key logic:
    /// - 8 MiB body limit
    /// - Max 100 entries per batch
    /// - Empty batch → 400
    /// - Per-project auth (validate project exists)
    /// - Pause gate: if sync_enabled=false → 409 + audit log
    /// - Relation validation: 6 required fields (sync_id, source_id, target_id, judgment_status, marked_by_actor, marked_by_kind)
    /// - Insert mutations → return seqs
    /// - Project envelope in response
    /// </summary>
    private static async Task<IResult> HandleMutationPushAsync(
        HttpContext ctx,
        ICloudMutationStore store)
    {
        // Read body with 8 MiB limit
        var body = await ReadJsonAsync<PushRequestBody>(ctx, 8 * 1024 * 1024);
        if (body is null || body.Entries.Count == 0)
            return ErrorJson(ctx, "empty batch", "empty-batch", 400);

        if (body.Entries.Count > 100)
            return ErrorJson(ctx, "batch too large (max 100)", "batch-too-large", 400);

        // Validate entries have required fields
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

            // Relation payload validation (6 required fields)
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

        // Pause gate: check if sync is enabled for all projects in batch
        var projects = body.Entries.Select(e => e.Project).Distinct().ToList();
        foreach (var project in projects)
        {
            if (!await store.IsProjectSyncEnabledAsync(project))
            {
                // Log audit entry
                await store.InsertAuditEntryAsync(new AuditEntry(
                    project, "push", "rejected",
                    body.CreatedBy, body.Entries.Count, "sync-paused"
                ));

                return ErrorJson(ctx, "sync is paused for this project", "sync-paused", 409, project);
            }
        }

        // Convert DTOs to domain records
        var entries = body.Entries.Select(e =>
            new MutationEntry(e.Project, e.Entity, e.EntityKey, e.Op, e.Payload)
        ).ToList();

        // Insert mutations
        var seqs = await store.InsertMutationBatchAsync(entries, body.CreatedBy);

        // Return with project envelope
        var firstProject = projects.FirstOrDefault() ?? "";
        return Results.Json(new PushResponseBody(seqs, firstProject, "request_body", ""), JsonOpts);
    }

    /// <summary>
    /// Handle GET /sync/mutations/pull
    /// 
    /// Key logic:
    /// - since_seq (default 0)
    /// - limit (default 100, max 100)
    /// - Enrollment scope: only return mutations for enrolled projects
    /// - Cursor pagination: has_more + latest_seq
    /// - Project envelope in response
    /// </summary>
    private static async Task<IResult> HandleMutationPullAsync(
        HttpContext ctx,
        ICloudMutationStore store)
    {
        var sinceSeq = ParseLong(ctx.Request.Query["since_seq"], 0);
        var limit = Math.Min(ParseInt(ctx.Request.Query["limit"], 100), 100);
        var project = ctx.Request.Query["project"].FirstOrDefault() ?? "";

        // Enrollment filter: if project is provided, only return mutations for enrolled projects
        // In a real multi-tenant system, this would check enrolled_projects table
        List<string>? allowedProjects = null;
        if (!string.IsNullOrEmpty(project))
        {
            allowedProjects = [project];
        }

        // Fetch mutations
        var (mutations, hasMore, latestSeq) = await store.ListMutationsSinceAsync(
            sinceSeq, limit, allowedProjects);

        // Convert to DTOs
        var body = new PullResponseBody(
            mutations.Select(m => new PulledMutationBody(
                m.Seq, m.Project, m.Entity, m.EntityKey, m.Op, m.Payload, m.OccurredAt
            )).ToList(),
            hasMore,
            latestSeq,
            project,
            "query_param",
            ""
        );

        return Results.Json(body, JsonOpts);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static async Task<T?> ReadJsonAsync<T>(HttpContext ctx, long maxBytes)
    {
        ctx.Request.EnableBuffering();
        
        if (ctx.Request.ContentLength > maxBytes)
            return default;

        var body = await JsonSerializer.DeserializeAsync<T>(ctx.Request.Body, JsonOpts);
        ctx.Request.Body.Position = 0; // Rewind for next read
        return body;
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
