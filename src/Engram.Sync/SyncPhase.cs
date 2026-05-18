namespace Engram.Sync;

/// <summary>
/// SyncManager lifecycle phases for observability and state tracking.
/// </summary>
public enum SyncPhase
{
    /// <summary>Idle: waiting for debounce or poll trigger.</summary>
    Idle,

    /// <summary>Pushing: sending mutations to cloud server.</summary>
    Pushing,

    /// <summary>Pulling: fetching mutations from cloud server.</summary>
    Pulling,

    /// <summary>PushFailed: push cycle failed, will retry.</summary>
    PushFailed,

    /// <summary>PullFailed: pull cycle failed, will retry.</summary>
    PullFailed,

    /// <summary>Backoff: exponential backoff before retry.</summary>
    Backoff,

    /// <summary>Healthy: last cycle completed successfully.</summary>
    Healthy,

    /// <summary>Disabled: failure ceiling reached, sync stopped.</summary>
    Disabled
}
