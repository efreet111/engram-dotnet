namespace Engram.Sync;

public sealed class SyncMetrics
{
    private long _totalPushed;
    private long _totalPulled;
    private long _totalFailures;
    private int _deferredReplayed;
    private int _deferredDead;
    private long _lastSyncAtTicks = DateTime.MinValue.Ticks;
    private string? _lastError;

    public long TotalPushed => Interlocked.Read(ref _totalPushed);
    public long TotalPulled => Interlocked.Read(ref _totalPulled);
    public long TotalFailures => Interlocked.Read(ref _totalFailures);
    public int DeferredReplayed => Interlocked.CompareExchange(ref _deferredReplayed, 0, 0);
    public int DeferredDead => Interlocked.CompareExchange(ref _deferredDead, 0, 0);
    public DateTime LastSyncAt => new(Interlocked.Read(ref _lastSyncAtTicks), DateTimeKind.Utc);
    public string? LastError => Interlocked.CompareExchange(ref _lastError, null, null);

    public SyncMetricsSnapshot GetSnapshot() => new(
        TotalPushed,
        TotalPulled,
        TotalFailures,
        DeferredReplayed,
        DeferredDead,
        LastSyncAt,
        LastError
    );

    internal void IncrementPushed(long count = 1) => Interlocked.Add(ref _totalPushed, count);
    internal void IncrementPulled(long count = 1) => Interlocked.Add(ref _totalPulled, count);
    internal void IncrementFailures(long count = 1) => Interlocked.Add(ref _totalFailures, count);
    internal void RecordDeferred(int replayed, int dead)
    {
        Interlocked.Exchange(ref _deferredReplayed, replayed);
        Interlocked.Exchange(ref _deferredDead, dead);
    }
    internal void MarkSyncAt(DateTime at) => Interlocked.Exchange(ref _lastSyncAtTicks, at.Ticks);
    internal void RecordError(string? error) => Interlocked.Exchange(ref _lastError, error);
    internal void ClearError() => Interlocked.Exchange(ref _lastError, null);
}

public sealed record SyncMetricsSnapshot(
    long TotalPushed,
    long TotalPulled,
    long TotalFailures,
    int DeferredReplayed,
    int DeferredDead,
    DateTime LastSyncAt,
    string? LastError
);
