using System.Text.Json.Serialization;

namespace Engram.Sync.Transport;

// ═══════════════════════════════════════════════════════════════════════════
// Push Request/Response
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Push request body — sent to POST /sync/mutations/push
/// Matches Go engram API contract exactly.
/// </summary>
public sealed record PushRequest(
    [property: JsonPropertyName("entries")] IReadOnlyList<MutationEntry> Entries,
    [property: JsonPropertyName("created_by")] string? CreatedBy = null);

/// <summary>
/// Mutation entry as sent in push request.
/// Project is required (unlike local sync_mutations which defaults to empty).
/// </summary>
public sealed record MutationEntry(
    [property: JsonPropertyName("project")] string Project,
    [property: JsonPropertyName("entity")] string Entity,
    [property: JsonPropertyName("entity_key")] string EntityKey,
    [property: JsonPropertyName("op")] string Op,
    [property: JsonPropertyName("payload")] string Payload);

/// <summary>
/// Push response — returned from POST /sync/mutations/push
/// Returns accepted_seqs in the same order as input entries.
/// </summary>
public sealed record PushResponse(
    [property: JsonPropertyName("accepted_seqs")] IReadOnlyList<long> AcceptedSeqs,
    [property: JsonPropertyName("project")] string Project,
    [property: JsonPropertyName("project_source")] string ProjectSource,
    [property: JsonPropertyName("project_path")] string ProjectPath);

// ═══════════════════════════════════════════════════════════════════════════
// Pull Request/Response
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Pull response — returned from GET /sync/mutations/pull
/// Includes has_more flag for cursor-based pagination.
/// </summary>
public sealed record PullResponse(
    [property: JsonPropertyName("mutations")] IReadOnlyList<PulledMutation> Mutations,
    [property: JsonPropertyName("has_more")] bool HasMore,
    [property: JsonPropertyName("latest_seq")] long LatestSeq,
    [property: JsonPropertyName("project")] string Project,
    [property: JsonPropertyName("project_source")] string ProjectSource,
    [property: JsonPropertyName("project_path")] string ProjectPath);

/// <summary>
/// Single mutation returned from pull.
/// </summary>
public sealed record PulledMutation(
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
/// Error response for 409 Conflict (sync paused) and other errors.
/// </summary>
public sealed record ErrorResponse(
    [property: JsonPropertyName("error_class")] string ErrorClass,
    [property: JsonPropertyName("error_code")] string ErrorCode,
    [property: JsonPropertyName("error")] string Error,
    [property: JsonPropertyName("project")] string Project,
    [property: JsonPropertyName("project_source")] string ProjectSource,
    [property: JsonPropertyName("project_path")] string ProjectPath);
