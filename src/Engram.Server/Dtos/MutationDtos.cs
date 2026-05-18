using System.Text.Json.Serialization;

namespace Engram.Server.Dtos;

// ═══════════════════════════════════════════════════════════════════════════
// Push Request/Response
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Push request body — received from POST /sync/mutations/push
/// </summary>
public sealed record PushRequestBody(
    [property: JsonPropertyName("entries")] IReadOnlyList<MutationEntryBody> Entries,
    [property: JsonPropertyName("created_by")] string? CreatedBy = null);

/// <summary>
/// Single mutation entry in push request
/// </summary>
public sealed record MutationEntryBody(
    [property: JsonPropertyName("project")] string Project,
    [property: JsonPropertyName("entity")] string Entity,
    [property: JsonPropertyName("entity_key")] string EntityKey,
    [property: JsonPropertyName("op")] string Op,
    [property: JsonPropertyName("payload")] string Payload);

/// <summary>
/// Push response — returned from POST /sync/mutations/push
/// </summary>
public sealed record PushResponseBody(
    [property: JsonPropertyName("accepted_seqs")] IReadOnlyList<long> AcceptedSeqs,
    [property: JsonPropertyName("project")] string Project,
    [property: JsonPropertyName("project_source")] string ProjectSource,
    [property: JsonPropertyName("project_path")] string ProjectPath);

// ═══════════════════════════════════════════════════════════════════════════
// Pull Response
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Pull response — returned from GET /sync/mutations/pull
/// </summary>
public sealed record PullResponseBody(
    [property: JsonPropertyName("mutations")] IReadOnlyList<PulledMutationBody> Mutations,
    [property: JsonPropertyName("has_more")] bool HasMore,
    [property: JsonPropertyName("latest_seq")] long LatestSeq,
    [property: JsonPropertyName("project")] string Project,
    [property: JsonPropertyName("project_source")] string ProjectSource,
    [property: JsonPropertyName("project_path")] string ProjectPath);

/// <summary>
/// Single mutation in pull response
/// </summary>
public sealed record PulledMutationBody(
    [property: JsonPropertyName("seq")] long Seq,
    [property: JsonPropertyName("project")] string Project,
    [property: JsonPropertyName("entity")] string Entity,
    [property: JsonPropertyName("entity_key")] string EntityKey,
    [property: JsonPropertyName("op")] string Op,
    [property: JsonPropertyName("payload")] string Payload,
    [property: JsonPropertyName("occurred_at")] string OccurredAt);

// ═══════════════════════════════════════════════════════════════════════════
// Error Response
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Error response for 409 Conflict (sync paused)
/// </summary>
public sealed record ErrorResponseBody(
    [property: JsonPropertyName("error_class")] string ErrorClass,
    [property: JsonPropertyName("error_code")] string ErrorCode,
    [property: JsonPropertyName("error")] string Error,
    [property: JsonPropertyName("project")] string Project,
    [property: JsonPropertyName("project_source")] string ProjectSource,
    [property: JsonPropertyName("project_path")] string ProjectPath);
