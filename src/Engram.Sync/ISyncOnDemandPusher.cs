namespace Engram.Sync;

/// <summary>
/// Interface for triggering sync push on-demand (fire-and-forget).
/// Implementations must respect lease and backoff from the background SyncManager.
/// </summary>
public interface ISyncOnDemandPusher
{
    /// <summary>
    /// Trigger an on-demand push of pending mutations to the server.
    /// When <paramref name="project"/> is provided, only mutations for that project are pushed
    /// (validates enrollment first, per HU-014 R5 denylist integration).
    /// When null (default), pushes all enrolled projects (backward-compatible).
    /// Respects lease (skips if background has it) and backoff (skips if active).
    /// Fire-and-forget safe: never throws to caller.
    /// </summary>
    Task TriggerPushAsync(string? project = null, CancellationToken ct = default);

    /// <summary>
    /// Trigger an on-demand push of pending mutations for a specific project only
    /// (HU-014). Delegates to <see cref="TriggerPushAsync"/> with the project parameter.
    /// </summary>
    [System.Obsolete("Use TriggerPushAsync(project) instead.")]
    Task TriggerPushForProjectAsync(string project, CancellationToken ct = default);

    /// <summary>
    /// Count pending local mutations (acked_at IS NULL, source='local').
    /// Used for feedback in MCP tools.
    /// </summary>
    Task<int> CountPendingMutationsAsync(CancellationToken ct = default);

    /// <summary>
    /// Count pending local mutations for a specific project (source='local', acked_at IS NULL).
    /// Used for project-scoped sync feedback in MCP tools (HU-014).
    /// </summary>
    Task<int> CountPendingMutationsByProjectAsync(string project, CancellationToken ct = default);

    /// <summary>
    /// Whether sync is enabled. Used by MCP tools to skip feedback when sync is off.
    /// </summary>
    bool IsEnabled { get; }
}
