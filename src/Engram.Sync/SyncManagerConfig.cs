namespace Engram.Sync;

/// <summary>
/// Configuration for SyncManager background loop.
/// Populated from environment variables or DI.
/// </summary>
public sealed record SyncManagerConfig
{
    /// <summary>Target key for sync state (default: "cloud").</summary>
    public string TargetKey { get; init; } = "cloud";

    /// <summary>Lease owner identifier (default: MachineName + process ID).</summary>
    public string LeaseOwner { get; init; } = $"{Environment.MachineName}-{Environment.ProcessId}";

    /// <summary>Debounce duration before triggering cycle after dirty signal (default: 500ms).</summary>
    public TimeSpan DebounceDuration { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Poll interval when no dirty signal (default: 30s).</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Max mutations per push batch (default: 100).</summary>
    public int PushBatchSize { get; init; } = 100;

    /// <summary>Max mutations per pull batch (default: 100).</summary>
    public int PullBatchSize { get; init; } = 100;

    /// <summary>Max consecutive failures before disabling sync (default: 10).</summary>
    public int MaxConsecutiveFailures { get; init; } = 10;

    /// <summary>Base backoff duration for exponential backoff (default: 1s).</summary>
    public TimeSpan BaseBackoff { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Max backoff duration cap (default: 5m).</summary>
    public TimeSpan MaxBackoff { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Feature flag to disable sync at startup (default: true).</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Create config from environment variables.
    /// </summary>
    public static SyncManagerConfig FromEnvironment() => new()
    {
        TargetKey = Environment.GetEnvironmentVariable("ENGRAM_SYNC_TARGET") ?? "cloud",
        LeaseOwner = Environment.GetEnvironmentVariable("ENGRAM_SYNC_LEASE_OWNER") 
                     ?? $"{Environment.MachineName}-{Environment.ProcessId}",
        DebounceDuration = ParseTimeSpan(Environment.GetEnvironmentVariable("ENGRAM_SYNC_DEBOUNCE_MS"), 500),
        PollInterval = ParseTimeSpan(Environment.GetEnvironmentVariable("ENGRAM_SYNC_POLL_SECONDS"), 30),
        PushBatchSize = ParseInt(Environment.GetEnvironmentVariable("ENGRAM_SYNC_PUSH_BATCH"), 100),
        PullBatchSize = ParseInt(Environment.GetEnvironmentVariable("ENGRAM_SYNC_PULL_BATCH"), 100),
        MaxConsecutiveFailures = ParseInt(Environment.GetEnvironmentVariable("ENGRAM_SYNC_MAX_FAILURES"), 10),
        BaseBackoff = ParseTimeSpan(Environment.GetEnvironmentVariable("ENGRAM_SYNC_BACKOFF_BASE_MS"), 1000),
        MaxBackoff = ParseTimeSpan(Environment.GetEnvironmentVariable("ENGRAM_SYNC_BACKOFF_MAX_MS"), 300000),
        Enabled = Environment.GetEnvironmentVariable("ENGRAM_SYNC_ENABLED") != "false",
    };

    private static TimeSpan ParseTimeSpan(string? value, int defaultMs) =>
        int.TryParse(value, out var ms) ? TimeSpan.FromMilliseconds(ms) : TimeSpan.FromMilliseconds(defaultMs);

    private static int ParseInt(string? value, int defaultValue) =>
        int.TryParse(value, out var v) ? v : defaultValue;
}
