namespace Engram.Store;

/// <summary>
/// Local sync store interface — subset of IStore methods needed by SyncManager.
/// Interface segregation: keeps sync-specific methods out of IStore to avoid
/// polluting all 3 store implementations (SqliteStore, PostgresStore, HttpStore).
/// </summary>
public interface ILocalSyncStore
{
    /// <summary>
    /// Get sync state for a target (e.g., "cloud").
    /// </summary>
    Task<SyncState?> GetSyncStateAsync(string targetKey, CancellationToken ct = default);

    /// <summary>
    /// List pending sync mutations for a target, up to the limit.
    /// </summary>
    Task<List<SyncMutation>> ListPendingSyncMutationsAsync(string targetKey, int limit, CancellationToken ct = default);

    /// <summary>
    /// Count pending local mutations (source='local' AND acked_at IS NULL) for a target.
    /// Used for on-demand sync feedback in MCP tools.
    /// </summary>
    Task<int> CountPendingSyncMutationsAsync(string targetKey, CancellationToken ct = default);

    /// <summary>
    /// Count pending non-enrolled mutations grouped by project.
    /// Used to detect when push should be blocked.
    /// </summary>
    Task<List<PendingProjectCount>> CountPendingNonEnrolledAsync(string targetKey, CancellationToken ct = default);

    /// <summary>
    /// Get total pushed and pulled mutation counts from the database.
    /// Used to provide accurate counts in /sync/status even after process restart.
    /// </summary>
    Task<SyncMutationCounts> GetSyncMutationCountsAsync(string targetKey, CancellationToken ct = default);

    /// <summary>
    /// Acknowledge that mutations with given seqs have been successfully pushed.
    /// </summary>
    Task AckSyncMutationSeqsAsync(string targetKey, IReadOnlyList<long> seqs, CancellationToken ct = default);

    /// <summary>
    /// Update the last_pulled_seq cursor after a successful pull cycle.
    /// </summary>
    Task UpdateSyncStateAsync(string targetKey, long lastPulledSeq, CancellationToken ct = default);

    /// <summary>
    /// Acquire a sync lease to prevent concurrent sync operations.
    /// Returns true if lease was acquired.
    /// </summary>
    Task<bool> AcquireSyncLeaseAsync(string targetKey, string owner, TimeSpan ttl, CancellationToken ct = default);

    /// <summary>
    /// Release a sync lease.
    /// </summary>
    Task ReleaseSyncLeaseAsync(string targetKey, string owner, CancellationToken ct = default);

    /// <summary>
    /// Apply a pulled mutation to the local store.
    /// Handles session/observation/prompt upserts and relation FK deferral.
    /// </summary>
    Task ApplyPulledMutationAsync(string targetKey, SyncMutation mutation, CancellationToken ct = default);

    /// <summary>
    /// Insert a pulled mutation into sync_mutations before applying.
    /// Used by SyncManager to track pulled mutations for recovery.
    /// Returns the local seq assigned by the database.
    /// </summary>
    Task<long> InsertPulledMutationAsync(string targetKey, SyncMutation mutation, CancellationToken ct = default);

    /// <summary>
    /// Re-apply pending pulled mutations (source='pull' AND acked_at IS NULL).
    /// Called on startup or after blocked recovery to handle orphaned mutations.
    /// Returns count of re-applied mutations.
    /// </summary>
    Task<int> ReapplyPendingPulledMutationsAsync(string targetKey, CancellationToken ct = default);

    /// <summary>
    /// Replay deferred relations that failed due to FK misses.
    /// Returns count of successfully replayed and dead rows.
    /// </summary>
    Task<ReplayDeferredResult> ReplayDeferredAsync(CancellationToken ct = default);

    /// <summary>
    /// Record a sync failure with backoff information.
    /// </summary>
    Task MarkSyncFailureAsync(string targetKey, string message, DateTime backoffUntil, CancellationToken ct = default);

    /// <summary>
    /// Mark sync as blocked (e.g., non-enrolled pending mutations detected).
    /// </summary>
    Task MarkSyncBlockedAsync(string targetKey, string reasonCode, string message, CancellationToken ct = default);

    /// <summary>
    /// Mark sync as healthy after successful cycle.
    /// </summary>
    Task MarkSyncHealthyAsync(string targetKey, CancellationToken ct = default);

    // ─── Project enrollment (ENG-514: HU-013 multi-project sync management) ─────

    /// <summary>
    /// Enroll a project for local sync push with optional sync behavior.
    /// Behavior: 'fail-loud' (default, blocks sync) or 'silent-skip' (skip, continue sync).
    /// </summary>
    Task EnrollProjectLocalAsync(string project, string? behavior = "fail-loud", CancellationToken ct = default);

    /// <summary>
    /// Unenroll a project from local sync push.
    /// </summary>
    Task UnenrollProjectLocalAsync(string project, CancellationToken ct = default);

    /// <summary>
    /// Get all enrolled projects with their behavior and metadata.
    /// Returns list of (project, behavior, enrolled_at) tuples.
    /// </summary>
    Task<List<EnrolledProjectLocal>> GetEnrolledProjectsLocalAsync(CancellationToken ct = default);

    /// <summary>
    /// Get the sync behavior for a specific enrolled project.
    /// Returns 'fail-loud', 'silent-skip', or null if the project is not enrolled.
    /// </summary>
    Task<string?> GetProjectBehaviorAsync(string project, CancellationToken ct = default);

    /// <summary>
    /// List distinct projects that have pending sync mutations (not yet acked).
    /// Used for interactive enrollment — shows projects that need attention.
    /// </summary>
    Task<List<string>> ListDistinctProjectsWithPendingMutationsAsync(string targetKey, CancellationToken ct = default);

    /// <summary>
    /// Count pending local mutations grouped by project (source='local', acked_at IS NULL).
    /// Returns all projects with pending counts, regardless of enrollment status.
    /// Used by HU-014 for per-project smart sync triggers.
    /// </summary>
    Task<List<PendingProjectCount>> CountPendingMutationsByProjectAsync(string targetKey, CancellationToken ct = default);

    // ─── RFC-006: Multi-server pull deduplication ────────────────────────────

    /// <summary>
    /// Get the last pulled sequence number for a specific server.
    /// Used for per-server cursor tracking in multi-server pull (RFC-006).
    /// </summary>
    /// <param name="targetKey">Target key (e.g., "cloud")</param>
    /// <param name="serverId">Server identifier (e.g., "server-A")</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Last pulled seq, or null if no cursor exists for this server</returns>
    Task<long?> GetLastPulledSeqForServerAsync(string targetKey, string serverId, CancellationToken ct = default);

    /// <summary>
    /// Set the last pulled sequence number for a specific server.
    /// Persists the cursor so the next pull starts after this seq (RFC-006).
    /// </summary>
    /// <param name="targetKey">Target key (e.g., "cloud")</param>
    /// <param name="serverId">Server identifier</param>
    /// <param name="lastPulledSeq">New cursor value</param>
    /// <param name="ct">Cancellation token</param>
    Task SetLastPulledSeqForServerAsync(string targetKey, string serverId, long lastPulledSeq, CancellationToken ct = default);

    /// <summary>
    /// Get all known server identifiers for multi-server pull.
    /// Returns distinct server_id values from the pull cursor store (RFC-006).
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    /// <returns>List of server identifiers</returns>
    Task<List<string>> GetKnownServerIdsAsync(CancellationToken ct = default);
}

/// <summary>
/// Sync state for a target (e.g., "cloud").
/// Matches sync_state table schema.
/// </summary>
public sealed record SyncState(
    string TargetKey,
    string Lifecycle,
    long LastEnqueuedSeq,
    long LastAckedSeq,
    long LastPulledSeq,
    int ConsecutiveFailures,
    string? BackoffUntil,
    string? LeaseOwner,
    string? LeaseUntil,
    string? LastError,
    DateTime UpdatedAt,
    string ServerId = "cloud");

/// <summary>
/// Pending sync mutation from local store.
/// Matches sync_mutations table schema.
/// </summary>
public sealed record SyncMutation(
    long Seq,
    string TargetKey,
    string Entity,
    string EntityKey,
    string Op,
    string Payload,
    string Source,
    string Project,
    DateTime OccurredAt,
    string? AckedAt);

/// <summary>
/// Count of pending mutations for a project.
/// </summary>
public sealed record PendingProjectCount(
    string Project,
    long Count);

/// <summary>
/// Counts of sync mutations by source.
/// </summary>
public sealed record SyncMutationCounts(
    long TotalPushed,
    long TotalPulled);

/// <summary>
/// Result of deferred replay operation.
/// </summary>
public sealed record ReplayDeferredResult(
    int ReplayCount,
    int DeadCount);

/// <summary>
/// Enrolled project with behavior metadata — used by local sync store (ENG-514).
/// </summary>
public sealed record EnrolledProjectLocal(
    string Project,
    string Behavior,
    string EnrolledAt);
