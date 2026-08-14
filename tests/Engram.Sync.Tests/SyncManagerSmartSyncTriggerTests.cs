using Engram.Store;
using Engram.Sync.Transport;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using MutationEntry = Engram.Sync.Transport.MutationEntry;

namespace Engram.Sync.Tests;

/// <summary>
/// Phase 4 unit tests for HU-014 Smart Sync Triggers.
/// Validates project-scoped push, per-project mutation counting,
/// denylist (silent-skip) filtering during on-demand push, AutoSyncEnabled guard,
/// and backward-compatible global push (regression).
/// Design: sdd/HU-014/design, sdd/HU-014/spec
/// Tasks: 4.1–4.5, 4.8
/// </summary>
public sealed class SyncManagerSmartSyncTriggerTests : IDisposable
{
    private readonly Mock<ILocalSyncStore> _storeMock;
    private readonly Mock<IMutationTransport> _transportMock;
    private readonly Mock<ILogger<SyncManager>> _loggerMock;
    private readonly SyncManagerConfig _config;
    private readonly string _tempDir;

    public SyncManagerSmartSyncTriggerTests()
    {
        _storeMock = new Mock<ILocalSyncStore>();
        _transportMock = new Mock<IMutationTransport>();
        _loggerMock = new Mock<ILogger<SyncManager>>();
        _loggerMock.Setup(logger => logger.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
        _tempDir = Path.Combine(Path.GetTempPath(), "engram-smart-sync-tests", Guid.NewGuid().ToString("N"));
        _config = new SyncManagerConfig
        {
            TargetKey = "cloud",
            LeaseOwner = "test-lease-owner",
            DebounceDuration = TimeSpan.FromMilliseconds(50),
            PollInterval = TimeSpan.FromSeconds(1),
            PushBatchSize = 100,
            PullBatchSize = 100,
            MaxConsecutiveFailures = 10,
            NotificationThreshold = 3,
            NotificationFileMaxEntries = 10,
            NotificationDirectory = _tempDir,
            BaseBackoff = TimeSpan.FromSeconds(1),
            MaxBackoff = TimeSpan.FromMinutes(5),
            Enabled = true,
            AutoSyncEnabled = true
        };
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // Helper: create mutations for multiple projects
    private static List<SyncMutation> CreateMultiProjectMutations()
    {
        return new List<SyncMutation>
        {
            new(1, "cloud", "observation", "obs-a1", "upsert", "{}", "local", "project-A", DateTime.UtcNow, null),
            new(2, "cloud", "observation", "obs-a2", "upsert", "{}", "local", "project-A", DateTime.UtcNow, null),
            new(3, "cloud", "observation", "obs-b1", "upsert", "{}", "local", "project-B", DateTime.UtcNow, null),
            new(4, "cloud", "prompt", "p-c1", "upsert", "{}", "local", "project-C", DateTime.UtcNow, null),
        };
    }

    // Helper: setup common on-demand push mocks for a successful scoped push
    private void SetupOnDemandPushMocks(string project, List<SyncMutation> pendingMutations)
    {
        // Enrollment check passes (fail-loud behavior)
        _storeMock.Setup(s => s.GetProjectBehaviorAsync(project, It.IsAny<CancellationToken>()))
            .ReturnsAsync("fail-loud");

        // Lease acquisition succeeds
        var onDemandOwner = $"{_config.LeaseOwner}-on-demand";
        _storeMock.Setup(s => s.AcquireSyncLeaseAsync(
                _config.TargetKey, onDemandOwner, TimeSpan.FromSeconds(30), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Return multi-project pending mutations
        _storeMock.Setup(s => s.ListPendingSyncMutationsAsync(
                _config.TargetKey, _config.PushBatchSize, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pendingMutations);

        // No silent-skip projects (all enrolled as fail-loud)
        _storeMock.Setup(s => s.GetEnrolledProjectsLocalAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EnrolledProjectLocal>
            {
                new("project-A", "fail-loud", DateTime.UtcNow.ToString("O")),
                new("project-B", "fail-loud", DateTime.UtcNow.ToString("O")),
                new("project-C", "fail-loud", DateTime.UtcNow.ToString("O")),
            });

        // Transport push succeeds
        _transportMock.Setup(t => t.PushMutationsAsync(
                It.IsAny<IReadOnlyList<MutationEntry>>(), _config.LeaseOwner, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PushResult(new List<long>(), project, null));

        // Ack succeeds
        _storeMock.Setup(s => s.AckSyncMutationSeqsAsync(
                _config.TargetKey, It.IsAny<IReadOnlyList<long>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Lease release
        _storeMock.Setup(s => s.ReleaseSyncLeaseAsync(
                _config.TargetKey, onDemandOwner, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Task 4.1 — Unit test: TriggerPushAsync(project: "A") only pushes project A
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Test: TriggerPushAsync(project: "project-A") only sends project-A mutations
    /// to the transport layer. Mutations from projects B and C are excluded.
    /// Spec R1: Solo proyectos con pendings se pushean.
    /// Spec R4: sync_project=true → push solo proyecto A.
    /// </summary>
    [Fact]
    public async Task TriggerPushAsync_WithProjectParam_OnlyPushesTargetProject()
    {
        // Arrange
        var allMutations = CreateMultiProjectMutations();
        SetupOnDemandPushMocks("project-A", allMutations);

        var syncManager = new SyncManager(
            _storeMock.Object, _transportMock.Object, _config, _loggerMock.Object, new SyncMetrics());

        // Act
        await syncManager.TriggerPushAsync(project: "project-A");

        // Assert: transport was called, but only with project-A entries
        _transportMock.Verify(t => t.PushMutationsAsync(
            It.Is<IReadOnlyList<MutationEntry>>(entries =>
                entries.All(e => e.Project == "project-A") &&
                entries.Count == 2),
            _config.LeaseOwner,
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Test: When TriggerPushAsync(project: "project-A") is called and the
    /// filtered list only contains project-A mutations, transport receives
    /// exactly 2 entries (matching the two project-A mutations in the setup).
    /// </summary>
    [Fact]
    public async Task TriggerPushAsync_WithProjectParam_TransportReceivesFilteredBatch()
    {
        // Arrange
        var allMutations = CreateMultiProjectMutations();
        SetupOnDemandPushMocks("project-A", allMutations);

        IReadOnlyList<MutationEntry>? capturedEntries = null;
        _transportMock.Setup(t => t.PushMutationsAsync(
                It.IsAny<IReadOnlyList<MutationEntry>>(), _config.LeaseOwner, It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<MutationEntry>, string, CancellationToken>(
                (entries, _, _) => capturedEntries = entries)
            .ReturnsAsync(new PushResult(new List<long>(), "project-A", null));

        var syncManager = new SyncManager(
            _storeMock.Object, _transportMock.Object, _config, _loggerMock.Object, new SyncMetrics());

        // Act
        await syncManager.TriggerPushAsync(project: "project-A");

        // Assert
        Assert.NotNull(capturedEntries);
        Assert.Equal(2, capturedEntries.Count);
        Assert.All(capturedEntries, e => Assert.Equal("project-A", e.Project));
        Assert.Contains(capturedEntries, e => e.EntityKey == "obs-a1");
        Assert.Contains(capturedEntries, e => e.EntityKey == "obs-a2");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Task 4.2 — Unit test: TriggerPushAsync(project: "nonexistent") no-op
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Test: TriggerPushAsync(project: "nonexistent") returns without push
    /// when the project has no pending mutations (even if other projects do).
    /// Spec R2: Proyecto inexistente → no-op, no crash.
    /// </summary>
    [Fact]
    public async Task TriggerPushAsync_NonexistentProject_NoPushButNoCrash()
    {
        // Arrange
        var allMutations = CreateMultiProjectMutations(); // project-A, B, C only
        SetupOnDemandPushMocks("nonexistent", allMutations);

        var syncManager = new SyncManager(
            _storeMock.Object, _transportMock.Object, _config, _loggerMock.Object, new SyncMetrics());

        // Act
        await syncManager.TriggerPushAsync(project: "nonexistent");

        // Assert: transport was NOT called (no matching mutations after filter)
        // Note: enrollment check passes because GetProjectBehaviorAsync is mocked
        // for "nonexistent", but after filtering, the pending list is empty
        _transportMock.Verify(t => t.PushMutationsAsync(
            It.IsAny<IReadOnlyList<MutationEntry>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Test: TriggerPushAsync(project: not enrolled) returns without push
    /// when GetProjectBehaviorAsync returns null (not enrolled).
    /// Spec R2: Proyecto no enrolado → error clear, no push.
    /// </summary>
    [Fact]
    public async Task TriggerPushAsync_UnenrolledProject_SkipsWithoutPush()
    {
        // Arrange: GetProjectBehaviorAsync returns null → not enrolled
        _storeMock.Setup(s => s.GetProjectBehaviorAsync("unenrolled-project", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var syncManager = new SyncManager(
            _storeMock.Object, _transportMock.Object, _config, _loggerMock.Object, new SyncMetrics());

        // Act
        await syncManager.TriggerPushAsync(project: "unenrolled-project");

        // Assert: no lease acquired, no push attempted
        _storeMock.Verify(s => s.AcquireSyncLeaseAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _transportMock.Verify(t => t.PushMutationsAsync(
            It.IsAny<IReadOnlyList<MutationEntry>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Task 4.3 — Unit test: CountPendingMutationsByProjectAsync("A") returns correct count
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Test: CountPendingMutationsByProjectAsync("project-A") returns 2
    /// when project-A has 2 pending mutations in the store.
    /// </summary>
    [Fact]
    public async Task CountPendingMutationsByProjectAsync_ReturnsCorrectCount()
    {
        // Arrange
        _storeMock.Setup(s => s.CountPendingMutationsByProjectAsync(
                _config.TargetKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PendingProjectCount>
            {
                new("project-A", 5),
                new("project-B", 3),
                new("project-C", 0),
            });

        var syncManager = new SyncManager(
            _storeMock.Object, _transportMock.Object, _config, _loggerMock.Object, new SyncMetrics());

        // Act
        var count = await syncManager.CountPendingMutationsByProjectAsync("project-A");

        // Assert
        Assert.Equal(5, count);
    }

    /// <summary>
    /// Test: CountPendingMutationsByProjectAsync returns 0 when the project
    /// exists but has zero pending mutations in the grouped results.
    /// </summary>
    [Fact]
    public async Task CountPendingMutationsByProjectAsync_UnknownProject_ReturnsZero()
    {
        // Arrange
        _storeMock.Setup(s => s.CountPendingMutationsByProjectAsync(
                _config.TargetKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PendingProjectCount>
            {
                new("project-A", 2),
            });

        var syncManager = new SyncManager(
            _storeMock.Object, _transportMock.Object, _config, _loggerMock.Object, new SyncMetrics());

        // Act
        var count = await syncManager.CountPendingMutationsByProjectAsync("unknown-project");

        // Assert
        Assert.Equal(0, count);
    }

    /// <summary>
    /// Test: CountPendingMutationsByProjectAsync with empty/null project returns 0.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task CountPendingMutationsByProjectAsync_InvalidProject_ReturnsZero(string? project)
    {
        // Arrange
        var syncManager = new SyncManager(
            _storeMock.Object, _transportMock.Object, _config, _loggerMock.Object, new SyncMetrics());

        // Act
        var count = await syncManager.CountPendingMutationsByProjectAsync(project!);

        // Assert
        Assert.Equal(0, count);
    }

    /// <summary>
    /// Test: CountPendingMutationsByProjectAsync returns 0 on store exception
    /// (fire-and-forget safe, never throws to caller).
    /// </summary>
    [Fact]
    public async Task CountPendingMutationsByProjectAsync_StoreThrows_ReturnsZero()
    {
        // Arrange
        _storeMock.Setup(s => s.CountPendingMutationsByProjectAsync(
                _config.TargetKey, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("store unavailable"));

        var syncManager = new SyncManager(
            _storeMock.Object, _transportMock.Object, _config, _loggerMock.Object, new SyncMetrics());

        // Act
        var count = await syncManager.CountPendingMutationsByProjectAsync("project-A");

        // Assert: should not throw, should return 0
        Assert.Equal(0, count);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Task 4.4 — Unit test: TriggerPushAsync(project: "A") respects denylist
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Test: TriggerPushAsync(project: "silent-project") skips push immediately
    /// when GetProjectBehaviorAsync returns "silent-skip" (denylist).
    /// Spec R5: Denylist por servidor, silent-skip → push omitido.
    /// </summary>
    [Fact]
    public async Task TriggerPushAsync_SilentSkipProject_SkipsWithoutPush()
    {
        // Arrange: GetProjectBehaviorAsync returns silent-skip
        _storeMock.Setup(s => s.GetProjectBehaviorAsync("silent-project", It.IsAny<CancellationToken>()))
            .ReturnsAsync("silent-skip");

        var syncManager = new SyncManager(
            _storeMock.Object, _transportMock.Object, _config, _loggerMock.Object, new SyncMetrics());

        // Act
        await syncManager.TriggerPushAsync(project: "silent-project");

        // Assert: no lease acquired, no push attempted
        _storeMock.Verify(s => s.AcquireSyncLeaseAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _transportMock.Verify(t => t.PushMutationsAsync(
            It.IsAny<IReadOnlyList<MutationEntry>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Test: TriggerPushAsync(project: "fail-loud-project") proceeds with push
    /// when GetProjectBehaviorAsync returns "fail-loud" (allowed).
    /// </summary>
    [Fact]
    public async Task TriggerPushAsync_FailLoudProject_ProceedsWithPush()
    {
        // Arrange
        var mutations = new List<SyncMutation>
        {
            new(10, "cloud", "observation", "obs-f1", "upsert", "{}", "local", "fail-loud-project", DateTime.UtcNow, null),
        };
        SetupOnDemandPushMocks("fail-loud-project", mutations);

        var syncManager = new SyncManager(
            _storeMock.Object, _transportMock.Object, _config, _loggerMock.Object, new SyncMetrics());

        // Act
        await syncManager.TriggerPushAsync(project: "fail-loud-project");

        // Assert: transport was called (push proceeded)
        _transportMock.Verify(t => t.PushMutationsAsync(
            It.IsAny<IReadOnlyList<MutationEntry>>(), _config.LeaseOwner, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Test: TriggerPushAsync(project: "fail-loud") with something that has
    /// some mutations in silent-skip projects → those are filtered out.
    /// Verifies that the silent-skip filter is applied during on-demand project push
    /// (the GetEnrolledProjectsLocalAsync check).
    /// </summary>
    [Fact]
    public async Task TriggerPushAsync_MixedProjectFilter_FiltersSilentSkip()
    {
        // Arrange
        var mutations = new List<SyncMutation>
        {
            new(1, "cloud", "observation", "obs-a1", "upsert", "{}", "local", "project-A", DateTime.UtcNow, null),
            new(2, "cloud", "observation", "obs-s1", "upsert", "{}", "local", "silent-skip-project", DateTime.UtcNow, null),
        };

        // Enrollment check for target project is fail-loud
        _storeMock.Setup(s => s.GetProjectBehaviorAsync("project-A", It.IsAny<CancellationToken>()))
            .ReturnsAsync("fail-loud");

        var onDemandOwner = $"{_config.LeaseOwner}-on-demand";
        _storeMock.Setup(s => s.AcquireSyncLeaseAsync(
                _config.TargetKey, onDemandOwner, TimeSpan.FromSeconds(30), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _storeMock.Setup(s => s.ListPendingSyncMutationsAsync(
                _config.TargetKey, _config.PushBatchSize, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mutations);

        // silent-skip-project is enrolled as silent-skip
        _storeMock.Setup(s => s.GetEnrolledProjectsLocalAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EnrolledProjectLocal>
            {
                new("project-A", "fail-loud", DateTime.UtcNow.ToString("O")),
                new("silent-skip-project", "silent-skip", DateTime.UtcNow.ToString("O")),
            });

        _transportMock.Setup(t => t.PushMutationsAsync(
                It.IsAny<IReadOnlyList<MutationEntry>>(), _config.LeaseOwner, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PushResult(new List<long>(), "project-A", null));

        _storeMock.Setup(s => s.AckSyncMutationSeqsAsync(
                _config.TargetKey, It.IsAny<IReadOnlyList<long>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _storeMock.Setup(s => s.ReleaseSyncLeaseAsync(
                _config.TargetKey, onDemandOwner, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var syncManager = new SyncManager(
            _storeMock.Object, _transportMock.Object, _config, _loggerMock.Object, new SyncMetrics());

        // Act
        await syncManager.TriggerPushAsync(project: "project-A");

        // Assert: transport was called but only with project-A entry (silent-skip filtered)
        _transportMock.Verify(t => t.PushMutationsAsync(
            It.Is<IReadOnlyList<MutationEntry>>(entries =>
                entries.Count == 1 && entries[0].Project == "project-A"),
            _config.LeaseOwner,
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Task 4.5 — Unit test: AutoSyncEnabled=false prevents background poll
    //              but allows on-demand push
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Test: AutoSyncEnabled=false → background poll loop exits early (R6 manual-only).
    /// On-demand push via TriggerPushAsync still works.
    /// Spec R6: Manual-only sin auto-poll, push solo por CLI/MCP explícito.
    /// </summary>
    [Fact]
    public async Task AutoSyncDisabled_OnDemandPush_StillWorks()
    {
        // Arrange: config with AutoSyncEnabled=false
        var manualConfig = _config with { AutoSyncEnabled = false, Enabled = true };

        var mutations = new List<SyncMutation>
        {
            new(1, "cloud", "observation", "obs-m1", "upsert", "{}", "local", "manual-project", DateTime.UtcNow, null),
        };
        SetupOnDemandPushMocks("manual-project", mutations);

        var syncManager = new SyncManager(
            _storeMock.Object, _transportMock.Object, manualConfig, _loggerMock.Object, new SyncMetrics());

        // Act: TriggerPushAsync should still work despite AutoSyncEnabled=false
        await syncManager.TriggerPushAsync(project: "manual-project");

        // Assert: push proceeded (on-demand is not blocked by AutoSyncEnabled)
        _transportMock.Verify(t => t.PushMutationsAsync(
            It.IsAny<IReadOnlyList<MutationEntry>>(), _config.LeaseOwner, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Test: AutoSyncEnabled=false, Enabled=true → ExecuteAsync skips background loop
    /// but logs a clear message and does NOT throw.
    /// </summary>
    [Fact]
    public async Task AutoSyncDisabled_ExecuteAsync_SkipsBackgroundLoop()
    {
        // Arrange: AutoSyncEnabled=false but Enabled=true
        var manualConfig = _config with { AutoSyncEnabled = false, Enabled = true };

        // We need the initial startup push to succeed
        var onDemandOwner = $"{manualConfig.LeaseOwner}-on-demand";
        _storeMock.Setup(s => s.AcquireSyncLeaseAsync(
                manualConfig.TargetKey, onDemandOwner, TimeSpan.FromSeconds(30), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _storeMock.Setup(s => s.ListPendingSyncMutationsAsync(
                manualConfig.TargetKey, manualConfig.PushBatchSize, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SyncMutation>());
        _storeMock.Setup(s => s.ReleaseSyncLeaseAsync(
                manualConfig.TargetKey, onDemandOwner, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _storeMock.Setup(s => s.GetEnrolledProjectsLocalAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EnrolledProjectLocal>());

        var syncManager = new SyncManager(
            _storeMock.Object, _transportMock.Object, manualConfig, _loggerMock.Object, new SyncMetrics());

        // Act: ExecuteAsync is a protected override, so we use a short-lived CancellationTokenSource
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        // Start background execution — it should return quickly because AutoSyncEnabled=false
        var executeTask = syncManager.StartAsync(cts.Token);

        // Wait for it to complete or timeout
        var completedTask = await Task.WhenAny(executeTask, Task.Delay(TimeSpan.FromSeconds(3), CancellationToken.None));

        // Assert: it completed (didn't hang in background loop) and didn't throw
        Assert.Equal(executeTask, completedTask);

        // Verify: the startup on-demand push was called (once), but background loop was skipped
        // Transport was NOT called (no pending mutations after list returned empty)
        _transportMock.Verify(t => t.PushMutationsAsync(
            It.IsAny<IReadOnlyList<MutationEntry>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);

        // Log should contain Auto-sync disabled message — we can verify via logger mock
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Auto-sync disabled")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    /// <summary>
    /// Test: AutoSyncEnabled=true (default) → ExecuteAsync enters background loop.
    /// </summary>
    [Fact]
    public async Task AutoSyncEnabled_ExecuteAsync_EntersBackgroundLoop()
    {
        // Arrange: default config has AutoSyncEnabled=true
        var autoConfig = _config with { AutoSyncEnabled = true, Enabled = true };

        // Startup push mocks
        var onDemandOwner = $"{autoConfig.LeaseOwner}-on-demand";
        _storeMock.Setup(s => s.AcquireSyncLeaseAsync(
                autoConfig.TargetKey, onDemandOwner, TimeSpan.FromSeconds(30), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _storeMock.Setup(s => s.ListPendingSyncMutationsAsync(
                autoConfig.TargetKey, autoConfig.PushBatchSize, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SyncMutation>());
        _storeMock.Setup(s => s.ReleaseSyncLeaseAsync(
                autoConfig.TargetKey, onDemandOwner, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _storeMock.Setup(s => s.GetEnrolledProjectsLocalAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EnrolledProjectLocal>());

        var syncManager = new SyncManager(
            _storeMock.Object, _transportMock.Object, autoConfig, _loggerMock.Object, new SyncMetrics());

        // Act: Start background execution and cancel quickly
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var executeTask = syncManager.StartAsync(cts.Token);
        await executeTask; // expected to be cancelled

        // Assert: log should NOT contain "Auto-sync disabled"
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Auto-sync disabled")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Task 4.8 — Regression: TriggerPushAsync() (no params) pushes all
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Test: TriggerPushAsync() with no project parameter (backward-compatible)
    /// pushes ALL enrolled projects, not just one.
    /// Spec R4: sync_project omitido → push global (comportamiento actual).
    /// </summary>
    [Fact]
    public async Task TriggerPushAsync_NoProjectParam_PushesAllProjects()
    {
        // Arrange: multi-project mutations without project filter
        var allMutations = CreateMultiProjectMutations(); // A, A, B, C

        // Global push: no enrollment validation (project is null)
        var onDemandOwner = $"{_config.LeaseOwner}-on-demand";
        _storeMock.Setup(s => s.AcquireSyncLeaseAsync(
                _config.TargetKey, onDemandOwner, TimeSpan.FromSeconds(30), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _storeMock.Setup(s => s.ListPendingSyncMutationsAsync(
                _config.TargetKey, _config.PushBatchSize, It.IsAny<CancellationToken>()))
            .ReturnsAsync(allMutations);

        // All enrolled as fail-loud
        _storeMock.Setup(s => s.GetEnrolledProjectsLocalAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EnrolledProjectLocal>
            {
                new("project-A", "fail-loud", DateTime.UtcNow.ToString("O")),
                new("project-B", "fail-loud", DateTime.UtcNow.ToString("O")),
                new("project-C", "fail-loud", DateTime.UtcNow.ToString("O")),
            });

        // Accumulate entries across all PushMutationsAsync calls (PushBatchInternalAsync
        // calls transport once per project group, not once for all entries)
        var allPushedEntries = new List<MutationEntry>();
        _transportMock.Setup(t => t.PushMutationsAsync(
                It.IsAny<IReadOnlyList<MutationEntry>>(), _config.LeaseOwner, It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<MutationEntry>, string, CancellationToken>(
                (entries, _, _) => allPushedEntries.AddRange(entries))
            .ReturnsAsync(new PushResult(new List<long>(), ""));

        _storeMock.Setup(s => s.AckSyncMutationSeqsAsync(
                _config.TargetKey, It.IsAny<IReadOnlyList<long>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _storeMock.Setup(s => s.ReleaseSyncLeaseAsync(
                _config.TargetKey, onDemandOwner, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var syncManager = new SyncManager(
            _storeMock.Object, _transportMock.Object, _config, _loggerMock.Object, new SyncMetrics());

        // Act: global push — no project filter
        await syncManager.TriggerPushAsync();

        // Assert: transport pushed all 4 mutations across 3 project groups
        Assert.Equal(4, allPushedEntries.Count);
        // Should include entries from all projects
        Assert.Contains(allPushedEntries, e => e.Project == "project-A");
        Assert.Contains(allPushedEntries, e => e.Project == "project-B");
        Assert.Contains(allPushedEntries, e => e.Project == "project-C");
    }

    /// <summary>
    /// Test: TriggerPushAsync() global push skips silent-skip projects
    /// but still pushes fail-loud ones (backward-compatible + denylist integration).
    /// </summary>
    [Fact]
    public async Task TriggerPushAsync_GlobalPush_FiltersSilentSkipProjects()
    {
        // Arrange: mixed silent-skip and fail-loud projects
        var mutations = new List<SyncMutation>
        {
            new(1, "cloud", "observation", "obs-a1", "upsert", "{}", "local", "project-A", DateTime.UtcNow, null),
            new(2, "cloud", "observation", "obs-s1", "upsert", "{}", "local", "silent-skip-proj", DateTime.UtcNow, null),
        };

        var onDemandOwner = $"{_config.LeaseOwner}-on-demand";
        _storeMock.Setup(s => s.AcquireSyncLeaseAsync(
                _config.TargetKey, onDemandOwner, TimeSpan.FromSeconds(30), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _storeMock.Setup(s => s.ListPendingSyncMutationsAsync(
                _config.TargetKey, _config.PushBatchSize, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mutations);

        _storeMock.Setup(s => s.GetEnrolledProjectsLocalAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EnrolledProjectLocal>
            {
                new("project-A", "fail-loud", DateTime.UtcNow.ToString("O")),
                new("silent-skip-proj", "silent-skip", DateTime.UtcNow.ToString("O")),
            });

        IReadOnlyList<MutationEntry>? capturedEntries = null;
        _transportMock.Setup(t => t.PushMutationsAsync(
                It.IsAny<IReadOnlyList<MutationEntry>>(), _config.LeaseOwner, It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<MutationEntry>, string, CancellationToken>(
                (entries, _, _) => capturedEntries = entries)
            .ReturnsAsync(new PushResult(new List<long>(), "project-A", null));

        _storeMock.Setup(s => s.AckSyncMutationSeqsAsync(
                _config.TargetKey, It.IsAny<IReadOnlyList<long>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _storeMock.Setup(s => s.ReleaseSyncLeaseAsync(
                _config.TargetKey, onDemandOwner, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var syncManager = new SyncManager(
            _storeMock.Object, _transportMock.Object, _config, _loggerMock.Object, new SyncMetrics());

        // Act: global push
        await syncManager.TriggerPushAsync();

        // Assert: only fail-loud project-A pushed, silent-skip-proj excluded
        Assert.NotNull(capturedEntries);
        Assert.Single(capturedEntries);
        Assert.Equal("project-A", capturedEntries[0].Project);
    }

    /// <summary>
    /// Test: TriggerPushForProjectAsync (deprecated) delegates to TriggerPushAsync(project).
    /// Ensures backward compatibility for callers of the old method name.
    /// </summary>
    [Fact]
    public async Task TriggerPushForProjectAsync_DelegatesToTriggerPushAsync()
    {
        // Arrange
        var mutations = new List<SyncMutation>
        {
            new(1, "cloud", "observation", "obs-x1", "upsert", "{}", "local", "project-X", DateTime.UtcNow, null),
        };
        SetupOnDemandPushMocks("project-X", mutations);

        var syncManager = new SyncManager(
            _storeMock.Object, _transportMock.Object, _config, _loggerMock.Object, new SyncMetrics());

        // Act: use deprecated method
        await syncManager.TriggerPushForProjectAsync("project-X");

        // Assert: behavior is identical to TriggerPushAsync(project: "project-X")
        _transportMock.Verify(t => t.PushMutationsAsync(
            It.Is<IReadOnlyList<MutationEntry>>(entries =>
                entries.Count == 1 && entries[0].Project == "project-X"),
            _config.LeaseOwner,
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Additional edge cases
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Test: TriggerPushAsync(project: "A") respects backoff — skips when backoff is active.
    /// </summary>
    [Fact]
    public async Task TriggerPushAsync_DuringBackoff_SkipsPush()
    {
        // Arrange: GetProjectBehaviorAsync passes
        _storeMock.Setup(s => s.GetProjectBehaviorAsync("project-A", It.IsAny<CancellationToken>()))
            .ReturnsAsync("fail-loud");

        var syncManager = new SyncManager(
            _storeMock.Object, _transportMock.Object, _config, _loggerMock.Object, new SyncMetrics());

        // Manually set backoff via reflection (backoffUntil is private)
        var backoffField = typeof(SyncManager).GetField("_backoffUntil",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(backoffField);
        backoffField.SetValue(syncManager, DateTime.UtcNow.AddMinutes(5));

        // Act
        await syncManager.TriggerPushAsync(project: "project-A");

        // Assert: no push attempted
        _transportMock.Verify(t => t.PushMutationsAsync(
            It.IsAny<IReadOnlyList<MutationEntry>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Test: TriggerPushAsync(project: "A") when enrollment check throws
    /// → logs warning and returns without pushing (no throw to caller).
    /// </summary>
    [Fact]
    public async Task TriggerPushAsync_EnrollmentCheckThrows_SkipsPush()
    {
        // Arrange: GetProjectBehaviorAsync throws
        _storeMock.Setup(s => s.GetProjectBehaviorAsync("error-project", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("DB unavailable"));

        var syncManager = new SyncManager(
            _storeMock.Object, _transportMock.Object, _config, _loggerMock.Object, new SyncMetrics());

        // Act: should not throw
        await syncManager.TriggerPushAsync(project: "error-project");

        // Assert: no push attempted
        _transportMock.Verify(t => t.PushMutationsAsync(
            It.IsAny<IReadOnlyList<MutationEntry>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
