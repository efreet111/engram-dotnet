namespace Engram.Store;

/// <summary>
/// Server-side cloud mutation storage interface.
/// Implemented by PostgresStore for storing mutations received from clients.
/// </summary>
public interface ICloudMutationStore
{
    /// <summary>
    /// Insert a batch of mutations into the cloud journal.
    /// Returns the assigned sequence numbers in the same order as input entries.
    /// </summary>
    Task<List<long>> InsertMutationBatchAsync(
        IReadOnlyList<MutationEntry> entries,
        string? createdBy = null,
        CancellationToken ct = default);

    /// <summary>
    /// List mutations since a given sequence number, scoped to allowed projects.
    /// Returns has_more=true if there are additional mutations beyond the limit.
    /// </summary>
    Task<(List<StoredMutation> Mutations, bool HasMore, long LatestSeq)> ListMutationsSinceAsync(
        long sinceSeq,
        int limit,
        List<string>? allowedProjects,
        CancellationToken ct = default);

    /// <summary>
    /// Check if sync is enabled for a given project.
    /// Returns false if the project is paused (cloud_project_controls.sync_enabled = false).
    /// </summary>
    Task<bool> IsProjectSyncEnabledAsync(string project, CancellationToken ct = default);

    /// <summary>
    /// Get list of enrolled projects for the given user.
    /// Used by Pull endpoint to filter mutations (fail-closed security).
    /// </summary>
    Task<List<EnrolledProject>> GetEnrolledProjectsAsync(string user, CancellationToken ct = default);

    /// <summary>
    /// Enroll a user in a project for sync access.
    /// Returns EnrollmentResult with status "already_enrolled" if exists.
    /// </summary>
    Task<EnrollmentResult> EnrollProjectAsync(string project, string user, CancellationToken ct = default);

    /// <summary>
    /// Unenroll a user from a project.
    /// Returns EnrollmentResult with status "not_found" if not enrolled.
    /// </summary>
    Task<EnrollmentResult> UnenrollProjectAsync(string project, string user, CancellationToken ct = default);

    /// <summary>
    /// Pause sync for a project by setting sync_enabled = false.
    /// Returns the pause result with timestamp and metadata.
    /// </summary>
    Task<PauseResult> PauseProjectAsync(string project, string reason, string pausedBy, CancellationToken ct = default);

    /// <summary>
    /// Resume sync for a project by setting sync_enabled = true and clearing pause_reason.
    /// Returns the resume result with timestamp and metadata.
    /// </summary>
    Task<PauseResult> ResumeProjectAsync(string project, string resumedBy, CancellationToken ct = default);

    /// <summary>
    /// Record an audit log entry for sync events (pause, auth failure, etc.).
    /// </summary>
    Task InsertAuditEntryAsync(AuditEntry entry, CancellationToken ct = default);
}

/// <summary>
/// Result of enrollment operation.
/// </summary>
public sealed record EnrollmentResult(
    string Project,
    string? EnrolledAt = null,
    string? EnrolledBy = null,
    string? UnenrolledAt = null,
    string? Status = null);

/// <summary>
/// Enrolled project with metadata.
/// </summary>
public sealed record EnrolledProject(
    string Project,
    string EnrolledAt,
    string EnrolledBy);

/// <summary>
/// Mutation entry as received from clients (push request).
/// </summary>
public sealed record MutationEntry(
    string Project,
    string Entity,
    string EntityKey,
    string Op,
    string Payload);

/// <summary>
/// Stored mutation with server-assigned sequence and metadata.
/// </summary>
public sealed record StoredMutation(
    long Seq,
    string Project,
    string Entity,
    string EntityKey,
    string Op,
    string Payload,
    string OccurredAt);

/// <summary>
/// Audit log entry for sync events.
/// </summary>
public sealed record AuditEntry(
    string Project,
    string Action,
    string Outcome,
    string? Contributor = null,
    int EntryCount = 0,
    string? ReasonCode = null);

/// <summary>
/// Result of pause/resume operations.
/// </summary>
public sealed record PauseResult(
    string Project,
    bool Paused,
    string? PausedAt = null,
    string? ResumedAt = null,
    string? PausedBy = null,
    string? ResumedBy = null,
    string? Reason = null);
