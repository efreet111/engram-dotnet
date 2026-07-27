namespace Engram.Sync;

/// <summary>
/// Interface for triggering sync push on-demand (fire-and-forget).
/// Implementations must respect lease and backoff from the background SyncManager.
/// </summary>
public interface ISyncOnDemandPusher
{
    /// <summary>
    /// Trigger an on-demand push of pending mutations to the server.
    /// Respects lease (skips if background has it) and backoff (skips if active).
    /// Fire-and-forget safe: never throws to caller.
    /// </summary>
    Task TriggerPushAsync(CancellationToken ct = default);

    /// <summary>
    /// Count pending local mutations (acked_at IS NULL, source='local').
    /// Used for feedback in MCP tools.
    /// </summary>
    Task<int> CountPendingMutationsAsync(CancellationToken ct = default);

    /// <summary>
    /// Whether sync is enabled. Used by MCP tools to skip feedback when sync is off.
    /// </summary>
    bool IsEnabled { get; }
}