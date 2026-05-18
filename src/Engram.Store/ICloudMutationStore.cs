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
    /// Record an audit log entry for sync events (pause, auth failure, etc.).
    /// </summary>
    Task InsertAuditEntryAsync(AuditEntry entry, CancellationToken ct = default);
}

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
