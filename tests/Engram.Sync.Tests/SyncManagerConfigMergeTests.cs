using Engram.Store;
using Engram.Sync;
using Xunit;

namespace Engram.Sync.Tests;

/// <summary>
/// Integration tests for <see cref="SyncManagerConfig.FromEnvironment"/> merge precedence.
/// Design: sdd/deploy-profile-system/design/design.md
/// Tasks: HU-012
///
/// Precedence rule: explicit env var > profile default > hardcoded default.
/// </summary>
[Collection("ENGRAM_PROFILE")]
public class SyncManagerConfigMergeTests : IDisposable
{
    private readonly string? _originalProfile;
    private readonly string? _originalSyncEnabled;
    private readonly string? _originalSyncTarget;
    private readonly string? _originalSyncPoll;

    public SyncManagerConfigMergeTests()
    {
        _originalProfile    = Environment.GetEnvironmentVariable("ENGRAM_PROFILE");
        _originalSyncEnabled = Environment.GetEnvironmentVariable("ENGRAM_SYNC_ENABLED");
        _originalSyncTarget  = Environment.GetEnvironmentVariable("ENGRAM_SYNC_TARGET");
        _originalSyncPoll    = Environment.GetEnvironmentVariable("ENGRAM_SYNC_POLL_SECONDS");
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("ENGRAM_PROFILE", _originalProfile);
        Environment.SetEnvironmentVariable("ENGRAM_SYNC_ENABLED", _originalSyncEnabled);
        Environment.SetEnvironmentVariable("ENGRAM_SYNC_TARGET", _originalSyncTarget);
        Environment.SetEnvironmentVariable("ENGRAM_SYNC_POLL_SECONDS", _originalSyncPoll);
    }

    // ─── Profile enables sync ───────────────────────────────────────────────

    [Fact]
    public void FromEnvironment_OfflineFirstProfile_EnablesSync()
    {
        Environment.SetEnvironmentVariable("ENGRAM_PROFILE", "offline-first");
        Environment.SetEnvironmentVariable("ENGRAM_SYNC_ENABLED", null);

        var cfg = SyncManagerConfig.FromEnvironment();

        Assert.True(cfg.Enabled);
    }

    [Fact]
    public void FromEnvironment_OfflineFirstProfile_SetsTargetToCloud()
    {
        Environment.SetEnvironmentVariable("ENGRAM_PROFILE", "offline-first");
        Environment.SetEnvironmentVariable("ENGRAM_SYNC_TARGET", null);

        var cfg = SyncManagerConfig.FromEnvironment();

        Assert.Equal("cloud", cfg.TargetKey);
    }

    [Fact]
    public void FromEnvironment_NoProfile_SyncDisabled()
    {
        Environment.SetEnvironmentVariable("ENGRAM_PROFILE", null);
        Environment.SetEnvironmentVariable("ENGRAM_SYNC_ENABLED", null);

        var cfg = SyncManagerConfig.FromEnvironment();

        Assert.False(cfg.Enabled);
    }

    [Fact]
    public void FromEnvironment_LocalProfile_SyncDisabled()
    {
        Environment.SetEnvironmentVariable("ENGRAM_PROFILE", "local");
        Environment.SetEnvironmentVariable("ENGRAM_SYNC_ENABLED", null);

        var cfg = SyncManagerConfig.FromEnvironment();

        Assert.False(cfg.Enabled);
    }

    // ─── Explicit env var > profile default ────────────────────────────────

    [Fact]
    public void FromEnvironment_OfflineFirstProfile_ExplicitDisable_Overrides()
    {
        // OfflineFirst profile enables sync by default, but explicit false overrides
        Environment.SetEnvironmentVariable("ENGRAM_PROFILE", "offline-first");
        Environment.SetEnvironmentVariable("ENGRAM_SYNC_ENABLED", "false");

        var cfg = SyncManagerConfig.FromEnvironment();

        Assert.False(cfg.Enabled);
    }

    [Fact]
    public void FromEnvironment_OfflineFirstProfile_ExplicitDisableZero_Overrides()
    {
        Environment.SetEnvironmentVariable("ENGRAM_PROFILE", "offline-first");
        Environment.SetEnvironmentVariable("ENGRAM_SYNC_ENABLED", "0");

        var cfg = SyncManagerConfig.FromEnvironment();

        Assert.False(cfg.Enabled);
    }

    [Fact]
    public void FromEnvironment_OfflineFirstProfile_ExplicitTarget_Overrides()
    {
        Environment.SetEnvironmentVariable("ENGRAM_PROFILE", "offline-first");
        Environment.SetEnvironmentVariable("ENGRAM_SYNC_TARGET", "custom-server");

        var cfg = SyncManagerConfig.FromEnvironment();

        Assert.Equal("custom-server", cfg.TargetKey);
    }

    [Fact]
    public void FromEnvironment_NoProfile_ExplicitEnable_EnablesSync()
    {
        // No profile means sync is off by default, but explicit env enables it
        Environment.SetEnvironmentVariable("ENGRAM_PROFILE", null);
        Environment.SetEnvironmentVariable("ENGRAM_SYNC_ENABLED", "true");

        var cfg = SyncManagerConfig.FromEnvironment();

        Assert.True(cfg.Enabled);
    }

    [Fact]
    public void FromEnvironment_LocalProfile_ExplicitEnable_EnablesSync()
    {
        // Local profile disables sync, but explicit env overrides
        Environment.SetEnvironmentVariable("ENGRAM_PROFILE", "local");
        Environment.SetEnvironmentVariable("ENGRAM_SYNC_ENABLED", "true");

        var cfg = SyncManagerConfig.FromEnvironment();

        Assert.True(cfg.Enabled);
    }

    // ─── Full precedence chain ──────────────────────────────────────────────

    [Fact]
    public void FromEnvironment_OfflineFirstProfile_PollSecondsFromProfile()
    {
        Environment.SetEnvironmentVariable("ENGRAM_PROFILE", "offline-first");
        Environment.SetEnvironmentVariable("ENGRAM_SYNC_POLL_SECONDS", null);

        var cfg = SyncManagerConfig.FromEnvironment();

        Assert.Equal(TimeSpan.FromSeconds(30), cfg.PollInterval);
    }

    [Fact]
    public void FromEnvironment_OfflineFirstProfile_ExplicitPollSeconds_Overrides()
    {
        Environment.SetEnvironmentVariable("ENGRAM_PROFILE", "offline-first");
        Environment.SetEnvironmentVariable("ENGRAM_SYNC_POLL_SECONDS", "60000");

        var cfg = SyncManagerConfig.FromEnvironment();

        Assert.Equal(TimeSpan.FromSeconds(60000), cfg.PollInterval);
    }
}
