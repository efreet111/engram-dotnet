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

    /// <summary>Consecutive failures before writing a notification (default: 3).</summary>
    public int NotificationThreshold { get; init; } = 3;

    /// <summary>Maximum entries retained in the notification file (default: 10).</summary>
    public int NotificationFileMaxEntries { get; init; } = 10;

    /// <summary>Directory containing the notification file (default: ~/.engram).</summary>
    public string NotificationDirectory { get; init; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".engram");

    /// <summary>Base backoff duration for exponential backoff (default: 1s).</summary>
    public TimeSpan BaseBackoff { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Max backoff duration cap (default: 5m).</summary>
    public TimeSpan MaxBackoff { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Feature flag to disable sync at startup (default: true).</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Creates a <see cref="SyncManagerConfig"/> from environment variables using the profile-based
    /// merge pattern: <c>explicit env var > profile default > hardcoded default</c>.
    /// Profile-aware properties: <see cref="TargetKey"/>, <see cref="PollInterval"/>, <see cref="Enabled"/>.
    /// Other properties read directly from environment variables.
    /// </summary>
    public static SyncManagerConfig FromEnvironment()
    {
        var profile = Engram.Store.DeployProfileExtensions.FromEnvironment();
        var defaults = Engram.Store.ProfileDefaults.For(profile);

        string? Resolve(string key, string? hc = null) =>
            Environment.GetEnvironmentVariable(key)
            ?? (defaults.TryGetValue(key, out var d) ? d : null)
            ?? hc;

        bool ResolveBool(string key, bool hc = false)
        {
            var raw = Environment.GetEnvironmentVariable(key);
            if (raw is not null)
                return raw.Trim().ToLowerInvariant() is not ("false" or "0");
            if (defaults.TryGetValue(key, out var d))
                return d == "true";
            return hc;
        }

        return new SyncManagerConfig()
        {
            // Profile-aware properties
            TargetKey = Resolve("ENGRAM_SYNC_TARGET", "cloud")!,
            PollInterval = ParseTimeSpanSeconds(Resolve("ENGRAM_SYNC_POLL_SECONDS"), 30),
            Enabled = ResolveBool("ENGRAM_SYNC_ENABLED", false),

            // Non-profile properties (direct env vars only)
            LeaseOwner = Environment.GetEnvironmentVariable("ENGRAM_SYNC_LEASE_OWNER") 
                         ?? $"{Environment.MachineName}-{Environment.ProcessId}",
            DebounceDuration = ParseTimeSpan(Environment.GetEnvironmentVariable("ENGRAM_SYNC_DEBOUNCE_MS"), 500),
            PushBatchSize = ParseInt(Environment.GetEnvironmentVariable("ENGRAM_SYNC_PUSH_BATCH"), 100),
            PullBatchSize = ParseInt(Environment.GetEnvironmentVariable("ENGRAM_SYNC_PULL_BATCH"), 100),
            MaxConsecutiveFailures = ParseInt(Environment.GetEnvironmentVariable("ENGRAM_SYNC_MAX_FAILURES"), 10),
            NotificationThreshold = ParseInt(Environment.GetEnvironmentVariable("ENGRAM_SYNC_NOTIFICATION_THRESHOLD"), 3),
            NotificationFileMaxEntries = ParseInt(Environment.GetEnvironmentVariable("ENGRAM_SYNC_NOTIFICATION_MAX"), 10),
            NotificationDirectory = Environment.GetEnvironmentVariable("ENGRAM_SYNC_NOTIFICATION_DIR")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".engram"),
            BaseBackoff = ParseTimeSpan(Environment.GetEnvironmentVariable("ENGRAM_SYNC_BACKOFF_BASE_MS"), 1000),
            MaxBackoff = ParseTimeSpan(Environment.GetEnvironmentVariable("ENGRAM_SYNC_BACKOFF_MAX_MS"), 300000),
        };
    }

    private static TimeSpan ParseTimeSpan(string? value, int defaultMs) =>
        int.TryParse(value, out var ms) ? TimeSpan.FromMilliseconds(ms) : TimeSpan.FromMilliseconds(defaultMs);

    private static TimeSpan ParseTimeSpanSeconds(string? value, int defaultSeconds) =>
        int.TryParse(value, out var s) ? TimeSpan.FromSeconds(s) : TimeSpan.FromSeconds(defaultSeconds);

    private static int ParseInt(string? value, int defaultValue) =>
        int.TryParse(value, out var v) ? v : defaultValue;
}
