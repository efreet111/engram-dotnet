namespace Engram.Sync;

public interface ISyncStatusProvider
{
    SyncPhase Phase { get; }
    bool IsEnabled { get; }
    int ConsecutiveFailures { get; }
    DateTime? BackoffUntil { get; }
    SyncMetrics Metrics { get; }
}
