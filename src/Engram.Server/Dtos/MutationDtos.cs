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

// ═══════════════════════════════════════════════════════════════════════════
// Pause/Resume Request/Response
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Pause request body — received from POST /sync/pause
/// </summary>
public sealed record PauseRequestBody(
    [property: JsonPropertyName("project")] string Project,
    [property: JsonPropertyName("reason")] string Reason);

/// <summary>
/// Pause/Resume response — returned from POST /sync/pause and DELETE /sync/pause
/// </summary>
public sealed record PauseResponseBody(
    [property: JsonPropertyName("project")] string Project,
    [property: JsonPropertyName("paused")] bool Paused,
    [property: JsonPropertyName("paused_at")] string? PausedAt = null,
    [property: JsonPropertyName("resumed_at")] string? ResumedAt = null,
    [property: JsonPropertyName("paused_by")] string? PausedBy = null,
    [property: JsonPropertyName("resumed_by")] string? ResumedBy = null,
    [property: JsonPropertyName("reason")] string? Reason = null);

// ═══════════════════════════════════════════════════════════════════════════
// Enrollment DTOs (Phase 3.1)
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Enrollment request body — POST /sync/enroll
/// </summary>
public sealed record EnrollmentRequest(
    [property: JsonPropertyName("project")] string Project);

/// <summary>
/// Enrollment response — returned from POST /sync/enroll
/// </summary>
public sealed record EnrollmentResponse(
    [property: JsonPropertyName("project")] string Project,
    [property: JsonPropertyName("enrolled_at")] string EnrolledAt,
    [property: JsonPropertyName("enrolled_by")] string EnrolledBy);

/// <summary>
/// Conflict response for POST /sync/enroll (already enrolled)
/// </summary>
public sealed record EnrollmentConflictResponse(
    [property: JsonPropertyName("error")] string Error,
    [property: JsonPropertyName("project")] string Project);

/// <summary>
/// Unenrollment response — returned from DELETE /sync/enroll
/// </summary>
public sealed record UnenrollmentResponse(
    [property: JsonPropertyName("project")] string Project,
    [property: JsonPropertyName("unenrolled_at")] string UnenrolledAt,
    [property: JsonPropertyName("status")] string Status);

/// <summary>
/// Not found response for DELETE /sync/enroll
/// </summary>
public sealed record UnenrollmentNotFoundResponse(
    [property: JsonPropertyName("error")] string Error,
    [property: JsonPropertyName("project")] string Project);

/// <summary>
/// Enrollment list response — returned from GET /sync/enroll
/// </summary>
public sealed record EnrollmentListResponse(
    [property: JsonPropertyName("projects")] IReadOnlyList<EnrolledProjectItem> Projects,
    [property: JsonPropertyName("count")] int Count);

/// <summary>
/// Single item in enrollment list
/// </summary>
public sealed record EnrolledProjectItem(
    [property: JsonPropertyName("project")] string Project,
    [property: JsonPropertyName("enrolled_at")] string EnrolledAt);
