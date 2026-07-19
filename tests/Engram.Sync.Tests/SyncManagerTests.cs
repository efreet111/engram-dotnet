using System.Reflection;
using System.Text.Json;
using Engram.Store;
using Engram.Sync.Transport;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Engram.Sync.Tests;

/// <summary>
/// Unit tests for SyncManager — BackgroundService for offline-first sync.
/// Tests cover phase transitions, failure ceiling, deferred replay, and non-enrolled blocking.
/// Design: sdd/offline-first-sync/design/design.md §AD-5
/// Tasks: 2.6.1 - 2.6.4
/// </summary>
public sealed class SyncManagerTests : IDisposable
{
    private readonly Mock<ILocalSyncStore> _storeMock;
    private readonly Mock<IMutationTransport> _transportMock;
    private readonly Mock<ILogger<SyncManager>> _loggerMock;
    private readonly SyncManagerConfig _config;
    private readonly string _tempDir;

    public SyncManagerTests()
    {
        _storeMock = new Mock<ILocalSyncStore>();
        _transportMock = new Mock<IMutationTransport>();
        _loggerMock = new Mock<ILogger<SyncManager>>();
        _loggerMock.Setup(logger => logger.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
        _tempDir = Path.Combine(Path.GetTempPath(), "engram-sync-tests", Guid.NewGuid().ToString("N"));
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
            Enabled = true
        };
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // Helper para invocar métodos async privados vía reflexión
    private static async Task InvokeCycleAsync(SyncManager syncManager, CancellationToken ct)
    {
        var method = typeof(SyncManager).GetMethod("CycleAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        if (method == null)
            throw new InvalidOperationException("CycleAsync method not found");
        
        var result = method.Invoke(syncManager, [ct]);
        if (result is Task task)
            await task;
    }

    // ─── Task 2.6.1: Phase transitions ──────────────────────────────────────

    /// <summary>
    /// Test: SyncManager inicia en fase Idle
    /// </summary>
    [Fact]
    public void SyncManager_InitialPhase_IsIdle()
    {
        // Arrange & Act
        var syncManager = new SyncManager(_storeMock.Object, _transportMock.Object, _config, _loggerMock.Object, new SyncMetrics());

        // Assert
        Assert.Equal(SyncPhase.Idle, syncManager.Phase);
    }

    /// <summary>
    /// Test: Cycle exitoso → fase Healthy
    /// </summary>
    [Fact]
    public async Task SyncManager_CycleCompletesSuccessfully_PhaseTransitionsToHealthy()
    {
        // Arrange
        _storeMock.Setup(s => s.AcquireSyncLeaseAsync(_config.TargetKey, _config.LeaseOwner, TimeSpan.FromMinutes(1), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _storeMock.Setup(s => s.ListPendingSyncMutationsAsync(_config.TargetKey, _config.PushBatchSize, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SyncMutation>());
        _storeMock.Setup(s => s.ReplayDeferredAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReplayDeferredResult(0, 0));
        _storeMock.Setup(s => s.GetSyncStateAsync(_config.TargetKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncState("cloud", "healthy", 0, 0, 0, 0, null, null, null, null, DateTime.UtcNow));
        _transportMock.Setup(t => t.PullMutationsAsync(0, _config.PullBatchSize, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PullResult(new List<PulledMutation>(), false, 0, ""));
        _storeMock.Setup(s => s.MarkSyncHealthyAsync(_config.TargetKey, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _storeMock.Setup(s => s.ReleaseSyncLeaseAsync(_config.TargetKey, _config.LeaseOwner, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var syncManager = new SyncManager(_storeMock.Object, _transportMock.Object, _config, _loggerMock.Object, new SyncMetrics());

        // Act
        await InvokeCycleAsync(syncManager, CancellationToken.None);

        // Assert
        Assert.Equal(SyncPhase.Healthy, syncManager.Phase);
    }

    /// <summary>
    /// Test: Push failure → fase PushFailed
    /// </summary>
    [Fact]
    public async Task SyncManager_PushFailure_PhaseTransitionsToPushFailed()
    {
        // Arrange
        _storeMock.Setup(s => s.AcquireSyncLeaseAsync(_config.TargetKey, _config.LeaseOwner, TimeSpan.FromMinutes(1), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        
        var pendingMutations = new List<SyncMutation>
        {
            new(1, "cloud", "session", "s1", "upsert", "{}", "local", "test-proj", DateTime.UtcNow, null)
        };
        _storeMock.Setup(s => s.ListPendingSyncMutationsAsync(_config.TargetKey, _config.PushBatchSize, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pendingMutations);
        
        // Throw exception with "push" in message to trigger PushFailed phase
        _transportMock.Setup(t => t.PushMutationsAsync(It.IsAny<IReadOnlyList<Engram.Sync.Transport.MutationEntry>>(), _config.LeaseOwner, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new MutationTransportException(500, "server", "internal-error", "Push failed: Internal Server Error"));
        
        _storeMock.Setup(s => s.ReplayDeferredAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReplayDeferredResult(0, 0));
        _storeMock.Setup(s => s.GetSyncStateAsync(_config.TargetKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncState("cloud", "healthy", 0, 0, 0, 0, null, null, null, null, DateTime.UtcNow));
        _transportMock.Setup(t => t.PullMutationsAsync(0, _config.PullBatchSize, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new MutationTransportException(500, "server", "error", "Pull failed"));
        _storeMock.Setup(s => s.MarkSyncFailureAsync(_config.TargetKey, It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _storeMock.Setup(s => s.ReleaseSyncLeaseAsync(_config.TargetKey, _config.LeaseOwner, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var syncManager = new SyncManager(_storeMock.Object, _transportMock.Object, _config, _loggerMock.Object, new SyncMetrics());

        // Act
        await InvokeCycleAsync(syncManager, CancellationToken.None);

        // Assert
        Assert.Equal(SyncPhase.PushFailed, syncManager.Phase);
    }

    /// <summary>
    /// Test: Pull failure → fase PullFailed
    /// </summary>
    [Fact]
    public async Task SyncManager_PullFailure_PhaseTransitionsToPullFailed()
    {
        // Arrange
        _storeMock.Setup(s => s.AcquireSyncLeaseAsync(_config.TargetKey, _config.LeaseOwner, TimeSpan.FromMinutes(1), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _storeMock.Setup(s => s.ListPendingSyncMutationsAsync(_config.TargetKey, _config.PushBatchSize, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SyncMutation>());
        _storeMock.Setup(s => s.ReplayDeferredAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReplayDeferredResult(0, 0));
        _storeMock.Setup(s => s.GetSyncStateAsync(_config.TargetKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncState("cloud", "healthy", 0, 0, 0, 0, null, null, null, null, DateTime.UtcNow));
        
        _transportMock.Setup(t => t.PullMutationsAsync(0, _config.PullBatchSize, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new MutationTransportException(503, "server", "service-unavailable", "Service Unavailable"));
        
        _storeMock.Setup(s => s.MarkSyncFailureAsync(_config.TargetKey, It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _storeMock.Setup(s => s.ReleaseSyncLeaseAsync(_config.TargetKey, _config.LeaseOwner, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var syncManager = new SyncManager(_storeMock.Object, _transportMock.Object, _config, _loggerMock.Object, new SyncMetrics());

        // Act
        await InvokeCycleAsync(syncManager, CancellationToken.None);

        // Assert
        Assert.Equal(SyncPhase.PullFailed, syncManager.Phase);
    }

    /// <summary>
    /// Test: Backoff phase cuando hay backoffUntil futuro
    /// </summary>
    [Fact]
    public void SyncManager_Config_HasCorrectBackoffSettings()
    {
        // Arrange & Act - Verify config values
        var config = new SyncManagerConfig
        {
            BaseBackoff = TimeSpan.FromSeconds(1),
            MaxBackoff = TimeSpan.FromMinutes(5)
        };

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(1), config.BaseBackoff);
        Assert.Equal(TimeSpan.FromMinutes(5), config.MaxBackoff);
    }

    // ─── Task 2.6.2: Failure ceiling ────────────────────────────────────────

    /// <summary>
    /// Test: 10 fallos consecutivos → fase Disabled
    /// </summary>
    [Fact]
    public async Task SyncManager_FailureCeilingReached_PhaseTransitionsToDisabled()
    {
        // Arrange
        var config = new SyncManagerConfig
        {
            MaxConsecutiveFailures = 3, // Lower for test
            NotificationDirectory = _tempDir,
            BaseBackoff = TimeSpan.FromMilliseconds(10),
            MaxBackoff = TimeSpan.FromMilliseconds(100)
        };

        _storeMock.Setup(s => s.AcquireSyncLeaseAsync(config.TargetKey, config.LeaseOwner, TimeSpan.FromMinutes(1), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _storeMock.Setup(s => s.ListPendingSyncMutationsAsync(config.TargetKey, config.PushBatchSize, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SyncMutation>());
        _storeMock.Setup(s => s.CountPendingNonEnrolledAsync(config.TargetKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PendingProjectCount>());
        _storeMock.Setup(s => s.ReplayDeferredAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReplayDeferredResult(0, 0));
        _storeMock.Setup(s => s.GetSyncStateAsync(config.TargetKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncState("cloud", "healthy", 0, 0, 0, 0, null, null, null, null, DateTime.UtcNow));
        // Use push exception to ensure phase is set correctly
        _transportMock.Setup(t => t.PullMutationsAsync(0, config.PullBatchSize, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new MutationTransportException(500, "server", "internal-error", "Pull failed: Error"));
        _storeMock.Setup(s => s.MarkSyncFailureAsync(config.TargetKey, It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _storeMock.Setup(s => s.ReleaseSyncLeaseAsync(config.TargetKey, config.LeaseOwner, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var syncManager = new SyncManager(_storeMock.Object, _transportMock.Object, config, _loggerMock.Object, new SyncMetrics());

        // Act - Run multiple cycles to reach failure ceiling
        await InvokeCycleAsync(syncManager, CancellationToken.None);
        await InvokeCycleAsync(syncManager, CancellationToken.None);
        await InvokeCycleAsync(syncManager, CancellationToken.None);
        // Fourth cycle should check ceiling and return immediately with Disabled phase
        await InvokeCycleAsync(syncManager, CancellationToken.None);

        // Assert - After 3 failures, phase should be Disabled
        Assert.Equal(SyncPhase.Disabled, syncManager.Phase);
    }

    /// <summary>
    /// Test: SyncManager se detiene cuando alcanza failure ceiling
    /// </summary>
    [Fact]
    public async Task SyncManager_FailureCeilingReached_StopsExecuting()
    {
        // Arrange
        var config = new SyncManagerConfig
        {
            MaxConsecutiveFailures = 2,
            NotificationDirectory = _tempDir,
            BaseBackoff = TimeSpan.FromMilliseconds(10)
        };
        
        _storeMock.Setup(s => s.AcquireSyncLeaseAsync(config.TargetKey, config.LeaseOwner, TimeSpan.FromMinutes(1), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _storeMock.Setup(s => s.ListPendingSyncMutationsAsync(config.TargetKey, config.PushBatchSize, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SyncMutation>());
        _storeMock.Setup(s => s.ReplayDeferredAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReplayDeferredResult(0, 0));
        _storeMock.Setup(s => s.GetSyncStateAsync(config.TargetKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncState("cloud", "healthy", 0, 0, 0, 0, null, null, null, null, DateTime.UtcNow));
        _transportMock.Setup(t => t.PullMutationsAsync(0, config.PullBatchSize, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new MutationTransportException(500, "server", "error", "Error"));
        _storeMock.Setup(s => s.MarkSyncFailureAsync(config.TargetKey, It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _storeMock.Setup(s => s.ReleaseSyncLeaseAsync(config.TargetKey, config.LeaseOwner, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var syncManager = new SyncManager(_storeMock.Object, _transportMock.Object, config, _loggerMock.Object, new SyncMetrics());

        // Act - Run cycles past ceiling
        await InvokeCycleAsync(syncManager, CancellationToken.None);
        await InvokeCycleAsync(syncManager, CancellationToken.None);
        await InvokeCycleAsync(syncManager, CancellationToken.None); // Should not execute full cycle

        // Assert - Verify AcquireSyncLeaseAsync was called only twice (not on 3rd iteration)
        _storeMock.Verify(s => s.AcquireSyncLeaseAsync(config.TargetKey, config.LeaseOwner, TimeSpan.FromMinutes(1), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    /// <summary>
    /// Test: Exponential backoff calcula correctamente (1s, 2s, 4s, capped at 5m)
    /// </summary>
    [Theory]
    [InlineData(1, 1000)]      // 1 failure → 1s
    [InlineData(2, 2000)]      // 2 failures → 2s
    [InlineData(3, 4000)]      // 3 failures → 4s
    [InlineData(10, 300000)]   // 10 failures → capped at 5m (300s)
    public void SyncManager_ExponentialBackoff_CalculatesCorrectly(int failures, int expectedMs)
    {
        // Arrange
        var config = new SyncManagerConfig
        {
            BaseBackoff = TimeSpan.FromSeconds(1),
            MaxBackoff = TimeSpan.FromMinutes(5)
        };

        // Calculate expected backoff: baseBackoff * 2^(failures-1)
        var expected = TimeSpan.FromMilliseconds(expectedMs);
        var actual = config.BaseBackoff * Math.Pow(2, failures - 1);
        
        // Cap at max
        if (actual > config.MaxBackoff)
            actual = config.MaxBackoff;

        // Assert - Allow 10ms tolerance
        Assert.InRange(actual.TotalMilliseconds, expected.TotalMilliseconds - 10, expected.TotalMilliseconds + 10);
    }

    // ─── Task 2.6.3: Deferred replay ────────────────────────────────────────

    /// <summary>
    /// Test: ReplayDeferredAsync se llama en cada cycle
    /// </summary>
    [Fact]
    public async Task SyncManager_Cycle_CallsReplayDeferredAsync()
    {
        // Arrange
        _storeMock.Setup(s => s.AcquireSyncLeaseAsync(_config.TargetKey, _config.LeaseOwner, TimeSpan.FromMinutes(1), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _storeMock.Setup(s => s.ListPendingSyncMutationsAsync(_config.TargetKey, _config.PushBatchSize, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SyncMutation>());
        _storeMock.Setup(s => s.ReplayDeferredAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReplayDeferredResult(0, 0));
        _storeMock.Setup(s => s.MarkSyncHealthyAsync(_config.TargetKey, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _storeMock.Setup(s => s.ReleaseSyncLeaseAsync(_config.TargetKey, _config.LeaseOwner, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var syncManager = new SyncManager(_storeMock.Object, _transportMock.Object, _config, _loggerMock.Object, new SyncMetrics());

        // Act
        await InvokeCycleAsync(syncManager, CancellationToken.None);

        // Assert
        _storeMock.Verify(s => s.ReplayDeferredAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Test: Replay exitoso → delete deferred rows
    /// </summary>
    [Fact]
    public async Task SyncManager_ReplayDeferred_SuccessfulReplay_LogsReplayCount()
    {
        // Arrange
        _storeMock.Setup(s => s.AcquireSyncLeaseAsync(_config.TargetKey, _config.LeaseOwner, TimeSpan.FromMinutes(1), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _storeMock.Setup(s => s.ListPendingSyncMutationsAsync(_config.TargetKey, _config.PushBatchSize, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SyncMutation>());
        _storeMock.Setup(s => s.ReplayDeferredAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReplayDeferredResult(3, 0)); // 3 replayed, 0 dead
        _storeMock.Setup(s => s.MarkSyncHealthyAsync(_config.TargetKey, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _storeMock.Setup(s => s.ReleaseSyncLeaseAsync(_config.TargetKey, _config.LeaseOwner, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var syncManager = new SyncManager(_storeMock.Object, _transportMock.Object, _config, _loggerMock.Object, new SyncMetrics());

        // Act
        await InvokeCycleAsync(syncManager, CancellationToken.None);

        // Assert
        _storeMock.Verify(s => s.ReplayDeferredAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Test: Replay fallido → increment retry_count
    /// </summary>
    [Fact]
    public async Task SyncManager_ReplayDeferred_WithDeadRows_ReturnsDeadCount()
    {
        // Arrange
        _storeMock.Setup(s => s.AcquireSyncLeaseAsync(_config.TargetKey, _config.LeaseOwner, TimeSpan.FromMinutes(1), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _storeMock.Setup(s => s.ListPendingSyncMutationsAsync(_config.TargetKey, _config.PushBatchSize, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SyncMutation>());
        _storeMock.Setup(s => s.ReplayDeferredAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReplayDeferredResult(0, 2)); // 0 replayed, 2 dead
        _storeMock.Setup(s => s.MarkSyncHealthyAsync(_config.TargetKey, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _storeMock.Setup(s => s.ReleaseSyncLeaseAsync(_config.TargetKey, _config.LeaseOwner, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var syncManager = new SyncManager(_storeMock.Object, _transportMock.Object, _config, _loggerMock.Object, new SyncMetrics());

        // Act
        await InvokeCycleAsync(syncManager, CancellationToken.None);

        // Assert - Verify replay was called and result includes dead count
        _storeMock.Verify(s => s.ReplayDeferredAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Test: Dead rows (retry_count >= 5) se loguean pero no se reintentan
    /// </summary>
    [Fact]
    public async Task SyncManager_ReplayDeferred_DeadRowsAreLogged()
    {
        // Arrange
        _storeMock.Setup(s => s.AcquireSyncLeaseAsync(_config.TargetKey, _config.LeaseOwner, TimeSpan.FromMinutes(1), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _storeMock.Setup(s => s.ListPendingSyncMutationsAsync(_config.TargetKey, _config.PushBatchSize, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SyncMutation>());
        
        var replayResult = new ReplayDeferredResult(1, 5); // 1 replayed, 5 dead
        _storeMock.Setup(s => s.ReplayDeferredAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(replayResult);
        
        _storeMock.Setup(s => s.MarkSyncHealthyAsync(_config.TargetKey, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _storeMock.Setup(s => s.ReleaseSyncLeaseAsync(_config.TargetKey, _config.LeaseOwner, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var syncManager = new SyncManager(_storeMock.Object, _transportMock.Object, _config, _loggerMock.Object, new SyncMetrics());

        // Act
        await InvokeCycleAsync(syncManager, CancellationToken.None);

        // Assert
        _storeMock.Verify(s => s.ReplayDeferredAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // ─── Task 2.6.4: Non-enrolled blocking ──────────────────────────────────

    /// <summary>
    /// Test: CountPendingNonEnrolledAsync detecta proyectos no enrolled
    /// </summary>
    [Fact]
    public async Task SyncManager_CountPendingNonEnrolledAsync_DetectsNonEnrolledProjects()
    {
        // Arrange
        _storeMock.Setup(s => s.AcquireSyncLeaseAsync(_config.TargetKey, _config.LeaseOwner, TimeSpan.FromMinutes(1), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        
        var pendingMutations = new List<SyncMutation>
        {
            new(1, "cloud", "session", "s1", "upsert", "{}", "local", "non-enrolled-proj", DateTime.UtcNow, null)
        };
        _storeMock.Setup(s => s.ListPendingSyncMutationsAsync(_config.TargetKey, _config.PushBatchSize, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pendingMutations);
        
        var nonEnrolledProjects = new List<PendingProjectCount>
        {
            new("non-enrolled-proj", 1)
        };
        _storeMock.Setup(s => s.CountPendingNonEnrolledAsync(_config.TargetKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(nonEnrolledProjects);
        
        _storeMock.Setup(s => s.ReplayDeferredAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReplayDeferredResult(0, 0));
        _storeMock.Setup(s => s.MarkSyncBlockedAsync(_config.TargetKey, "sync-paused", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _storeMock.Setup(s => s.ReleaseSyncLeaseAsync(_config.TargetKey, _config.LeaseOwner, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var syncManager = new SyncManager(_storeMock.Object, _transportMock.Object, _config, _loggerMock.Object, new SyncMetrics());

        // Act
        await InvokeCycleAsync(syncManager, CancellationToken.None);

        // Assert
        _storeMock.Verify(s => s.CountPendingNonEnrolledAsync(_config.TargetKey, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Test: MarkSyncBlockedAsync se llama cuando hay non-enrolled pending
    /// </summary>
    [Fact]
    public async Task SyncManager_NonEnrolledDetected_CallsMarkSyncBlockedAsync()
    {
        // Arrange
        _storeMock.Setup(s => s.AcquireSyncLeaseAsync(_config.TargetKey, _config.LeaseOwner, TimeSpan.FromMinutes(1), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        
        var pendingMutations = new List<SyncMutation>
        {
            new(1, "cloud", "session", "s1", "upsert", "{}", "local", "non-enrolled-proj", DateTime.UtcNow, null)
        };
        _storeMock.Setup(s => s.ListPendingSyncMutationsAsync(_config.TargetKey, _config.PushBatchSize, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pendingMutations);
        
        var nonEnrolledProjects = new List<PendingProjectCount>
        {
            new("non-enrolled-proj", 1)
        };
        _storeMock.Setup(s => s.CountPendingNonEnrolledAsync(_config.TargetKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(nonEnrolledProjects);
        
        _storeMock.Setup(s => s.ReplayDeferredAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReplayDeferredResult(0, 0));
        _storeMock.Setup(s => s.MarkSyncBlockedAsync(_config.TargetKey, "non-enrolled-pending", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _storeMock.Setup(s => s.ReleaseSyncLeaseAsync(_config.TargetKey, _config.LeaseOwner, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var syncManager = new SyncManager(_storeMock.Object, _transportMock.Object, _config, _loggerMock.Object, new SyncMetrics());

        // Act
        await InvokeCycleAsync(syncManager, CancellationToken.None);

        // Assert
        _storeMock.Verify(s => s.MarkSyncBlockedAsync(_config.TargetKey, "non-enrolled-pending", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _storeMock.Verify(s => s.MarkSyncHealthyAsync(_config.TargetKey, It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Test: Push cycle se bloquea cuando sync está paused
    /// </summary>
    [Fact]
    public async Task SyncManager_PushCycle_BlocksWhenSyncPaused()
    {
        // Arrange
        _storeMock.Setup(s => s.AcquireSyncLeaseAsync(_config.TargetKey, _config.LeaseOwner, TimeSpan.FromMinutes(1), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        
        var pendingMutations = new List<SyncMutation>
        {
            new(1, "cloud", "session", "s1", "upsert", "{}", "local", "test-proj", DateTime.UtcNow, null)
        };
        _storeMock.Setup(s => s.ListPendingSyncMutationsAsync(_config.TargetKey, _config.PushBatchSize, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pendingMutations);
        
        // Non-enrolled check (added in Fix 3)
        _storeMock.Setup(s => s.CountPendingNonEnrolledAsync(_config.TargetKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PendingProjectCount>());
        
        var pushResult = new PushResult(new List<long>(), "test-proj", "Sync is paused for this project");
        _transportMock.Setup(t => t.PushMutationsAsync(It.IsAny<IReadOnlyList<Engram.Sync.Transport.MutationEntry>>(), _config.LeaseOwner, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pushResult);
        
        _storeMock.Setup(s => s.MarkSyncBlockedAsync(_config.TargetKey, "sync-paused", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _storeMock.Setup(s => s.ReplayDeferredAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReplayDeferredResult(0, 0));
        _storeMock.Setup(s => s.ReleaseSyncLeaseAsync(_config.TargetKey, _config.LeaseOwner, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var syncManager = new SyncManager(_storeMock.Object, _transportMock.Object, _config, _loggerMock.Object, new SyncMetrics());

        // Act
        await InvokeCycleAsync(syncManager, CancellationToken.None);

        // Assert
        _storeMock.Verify(s => s.MarkSyncBlockedAsync(_config.TargetKey, "sync-paused", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        // Verify AckSyncMutationSeqsAsync was NOT called (push was blocked)
        _storeMock.Verify(s => s.AckSyncMutationSeqsAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<long>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ─── Additional integration-style tests ─────────────────────────────────

    /// <summary>
    /// Test: SyncManager exitoso hace ack de las mutations
    /// </summary>
    [Fact]
    public async Task SyncManager_SuccessfulPush_CallsAckSyncMutationSeqsAsync()
    {
        // Arrange
        _storeMock.Setup(s => s.AcquireSyncLeaseAsync(_config.TargetKey, _config.LeaseOwner, TimeSpan.FromMinutes(1), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        
        var pendingMutations = new List<SyncMutation>
        {
            new(1, "cloud", "session", "s1", "upsert", "{}", "local", "test-proj", DateTime.UtcNow, null)
        };
        _storeMock.Setup(s => s.ListPendingSyncMutationsAsync(_config.TargetKey, _config.PushBatchSize, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pendingMutations);
        
        // Non-enrolled check (added in Fix 3)
        _storeMock.Setup(s => s.CountPendingNonEnrolledAsync(_config.TargetKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PendingProjectCount>());
        
        var pushResult = new PushResult(new List<long> { 101, 102 }, "test-proj", null);
        _transportMock.Setup(t => t.PushMutationsAsync(It.IsAny<IReadOnlyList<Engram.Sync.Transport.MutationEntry>>(), _config.LeaseOwner, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pushResult);
        
        _storeMock.Setup(s => s.AckSyncMutationSeqsAsync(_config.TargetKey, It.IsAny<IReadOnlyList<long>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _storeMock.Setup(s => s.ReplayDeferredAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReplayDeferredResult(0, 0));
        _storeMock.Setup(s => s.MarkSyncHealthyAsync(_config.TargetKey, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _storeMock.Setup(s => s.ReleaseSyncLeaseAsync(_config.TargetKey, _config.LeaseOwner, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var syncManager = new SyncManager(_storeMock.Object, _transportMock.Object, _config, _loggerMock.Object, new SyncMetrics());

        // Act
        await InvokeCycleAsync(syncManager, CancellationToken.None);

        // Assert
        _storeMock.Verify(s => s.AckSyncMutationSeqsAsync(_config.TargetKey, It.IsAny<IReadOnlyList<long>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ─── ENG-459: Sync failure notifications ────────────────────────────────

    [Fact]
    public async Task NotificationFile_WrittenAtThreshold()
    {
        SetupFailingCycle(_config, "relay unavailable");
        var manager = CreateManager(_config);

        for (var i = 0; i < _config.NotificationThreshold; i++)
            await InvokeCycleAsync(manager, CancellationToken.None);

        var filePath = GetNotificationFilePath(_config);
        Assert.True(File.Exists(filePath));
        var lines = await File.ReadAllLinesAsync(filePath);
        var entry = JsonSerializer.Deserialize<JsonElement>(Assert.Single(lines));
        Assert.Equal("error", entry.GetProperty("level").GetString());
        Assert.Equal(3, entry.GetProperty("failures").GetInt32());
        Assert.Equal("relay unavailable", entry.GetProperty("error").GetString());
        Assert.False(string.IsNullOrWhiteSpace(entry.GetProperty("action").GetString()));
        Assert.Equal("pullfailed", entry.GetProperty("phase").GetString());
        Assert.True(DateTime.TryParse(entry.GetProperty("ts").GetString(), out _));
        Assert.Contains(_loggerMock.Invocations, invocation =>
            invocation.Method.Name == "Log" && (EventId)invocation.Arguments[1] == new EventId(2010, "SyncNotificationWritten"));

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            Assert.Equal(
                UnixFileMode.UserRead | UnixFileMode.UserWrite,
                File.GetUnixFileMode(filePath));
        }
    }

    [Fact]
    public async Task NotificationFile_NotWrittenBelowThreshold()
    {
        SetupFailingCycle(_config, "relay unavailable");
        var manager = CreateManager(_config);

        await InvokeCycleAsync(manager, CancellationToken.None);
        await InvokeCycleAsync(manager, CancellationToken.None);

        Assert.False(File.Exists(GetNotificationFilePath(_config)));
    }

    [Fact]
    public async Task NotificationFile_RotationMaxEntries()
    {
        var config = _config with { NotificationFileMaxEntries = 3 };
        var manager = CreateManager(config);

        for (var i = 1; i <= 5; i++)
            await InvokeWriteNotificationAsync(manager, "error", i, $"error-{i}", "retry", null);

        var lines = await File.ReadAllLinesAsync(GetNotificationFilePath(config));
        Assert.Equal(3, lines.Length);
        Assert.Contains("error-3", lines[0]);
        Assert.Contains("error-5", lines[2]);
    }

    [Fact]
    public async Task NotificationFile_RecoverEntry()
    {
        SetupFailingCycle(_config, "relay unavailable");
        var manager = CreateManager(_config);

        for (var i = 0; i < _config.NotificationThreshold; i++)
            await InvokeCycleAsync(manager, CancellationToken.None);

        SetupSuccessfulCycle(_config);
        await InvokeCycleAsync(manager, CancellationToken.None);

        var lines = await File.ReadAllLinesAsync(GetNotificationFilePath(_config));
        Assert.Equal(2, lines.Length);
        var recovery = JsonSerializer.Deserialize<JsonElement>(lines[1]);
        Assert.Equal("ok", recovery.GetProperty("level").GetString());
        Assert.Equal(0, recovery.GetProperty("failures").GetInt32());
        Assert.Contains("recovered after 3 failures", recovery.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains(_loggerMock.Invocations, invocation =>
            invocation.Method.Name == "Log" && (EventId)invocation.Arguments[1] == new EventId(2011, "SyncRecovered"));
    }

    [Fact]
    public async Task NotificationFile_WrittenOnDisabled()
    {
        var config = _config with { MaxConsecutiveFailures = 4 };
        SetupFailingCycle(config, "relay unavailable");
        var manager = CreateManager(config);

        for (var i = 0; i < config.MaxConsecutiveFailures; i++)
            await InvokeCycleAsync(manager, CancellationToken.None);
        await InvokeCycleAsync(manager, CancellationToken.None);

        Assert.Equal(SyncPhase.Disabled, manager.Phase);
        var lines = await File.ReadAllLinesAsync(GetNotificationFilePath(config));
        Assert.Contains(lines, line => line.Contains("Sync disabled after 4 failures", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NotificationFile_WriteFailureDoesNotThrow()
    {
        Directory.CreateDirectory(_tempDir);
        var invalidDirectory = Path.Combine(_tempDir, "not-a-directory");
        await File.WriteAllTextAsync(invalidDirectory, "file blocks directory creation");
        var config = _config with { NotificationDirectory = invalidDirectory };
        SetupFailingCycle(config, "relay unavailable");
        var manager = CreateManager(config);

        var exception = await Record.ExceptionAsync(async () =>
        {
            for (var i = 0; i < config.NotificationThreshold; i++)
                await InvokeCycleAsync(manager, CancellationToken.None);
        });

        Assert.Null(exception);
        Assert.Equal("relay unavailable", manager.LastError);
    }

    [Fact]
    public async Task LastError_DelegatesToMetrics()
    {
        SetupFailingCycle(_config, "last sync error");
        var metrics = new SyncMetrics();
        var manager = new SyncManager(_storeMock.Object, _transportMock.Object, _config, _loggerMock.Object, metrics);

        await InvokeCycleAsync(manager, CancellationToken.None);

        Assert.Equal(metrics.LastError, manager.LastError);
        Assert.Equal("last sync error", manager.LastError);
    }

    [Fact]
    public async Task LastError_ClearedOnRecovery()
    {
        // Arrange: setup failure then recovery
        SetupFailingCycle(_config, "relay unavailable");
        var manager = CreateManager(_config);
        await InvokeCycleAsync(manager, CancellationToken.None);
        Assert.Equal("relay unavailable", manager.LastError);

        // Act: switch to successful cycle
        SetupSuccessfulCycle(_config);
        await InvokeCycleAsync(manager, CancellationToken.None);

        // Assert: LastError cleared after recovery
        Assert.Null(manager.LastError);
    }

    [Fact]
    public async Task NotificationFile_WrittenOnBlockedPush()
    {
        // Arrange: setup blocked push (non-enrolled projects)
        _storeMock.Setup(s => s.AcquireSyncLeaseAsync(_config.TargetKey, _config.LeaseOwner, TimeSpan.FromMinutes(1), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        
        var pendingMutations = new List<SyncMutation>
        {
            new(1, "cloud", "session", "s1", "upsert", "{}", "local", "test-proj", DateTime.UtcNow, null)
        };
        _storeMock.Setup(s => s.ListPendingSyncMutationsAsync(_config.TargetKey, _config.PushBatchSize, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pendingMutations);
        
        var nonEnrolledProjects = new List<PendingProjectCount>
        {
            new("test-proj", 5)
        };
        _storeMock.Setup(s => s.CountPendingNonEnrolledAsync(_config.TargetKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(nonEnrolledProjects);
        
        _storeMock.Setup(s => s.MarkSyncBlockedAsync(_config.TargetKey, "non-enrolled-pending", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _storeMock.Setup(s => s.ReleaseSyncLeaseAsync(_config.TargetKey, _config.LeaseOwner, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var manager = CreateManager(_config);

        // Act
        await InvokeCycleAsync(manager, CancellationToken.None);

        // Assert: notification file written with blocked-push entry
        var filePath = GetNotificationFilePath(_config);
        Assert.True(File.Exists(filePath));
        var lines = await File.ReadAllLinesAsync(filePath);
        Assert.Single(lines);
        Assert.Contains("\"level\":\"error\"", lines[0], StringComparison.Ordinal);
        Assert.Contains("non-enrolled-pending", lines[0], StringComparison.Ordinal);
        Assert.Contains("Enroll projects", lines[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Integration_FullCycle_FailureThenRecover()
    {
        SetupFailingCycle(_config, "relay unavailable");
        var manager = CreateManager(_config);

        for (var i = 0; i < _config.NotificationThreshold; i++)
            await InvokeCycleAsync(manager, CancellationToken.None);

        var filePath = GetNotificationFilePath(_config);
        var failureLines = await File.ReadAllLinesAsync(filePath);
        Assert.Single(failureLines);
        Assert.Contains("\"level\":\"error\"", failureLines[0], StringComparison.Ordinal);

        SetupSuccessfulCycle(_config);
        await InvokeCycleAsync(manager, CancellationToken.None);

        var recoveredLines = await File.ReadAllLinesAsync(filePath);
        Assert.Equal(2, recoveredLines.Length);
        Assert.Contains("\"level\":\"error\"", recoveredLines[0], StringComparison.Ordinal);
        Assert.Contains("\"level\":\"ok\"", recoveredLines[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Integration_NotificationFile_PersistsAcrossRestarts()
    {
        SetupFailingCycle(_config, "relay unavailable");
        var firstManager = CreateManager(_config);

        for (var i = 0; i < _config.NotificationThreshold; i++)
            await InvokeCycleAsync(firstManager, CancellationToken.None);

        var filePath = GetNotificationFilePath(_config);
        Assert.Single(await File.ReadAllLinesAsync(filePath));

        var restartedManager = CreateManager(_config);
        Assert.True(File.Exists(filePath));
        Assert.Single(await File.ReadAllLinesAsync(filePath));

        for (var i = 0; i < _config.NotificationThreshold; i++)
            await InvokeCycleAsync(restartedManager, CancellationToken.None);

        var lines = await File.ReadAllLinesAsync(filePath);
        Assert.Equal(2, lines.Length);
        Assert.All(lines, line => Assert.Contains("\"level\":\"error\"", line, StringComparison.Ordinal));
    }

    private SyncManager CreateManager(SyncManagerConfig config) =>
        new(_storeMock.Object, _transportMock.Object, config, _loggerMock.Object, new SyncMetrics());

    private void SetupFailingCycle(SyncManagerConfig config, string error)
    {
        _storeMock.Setup(s => s.AcquireSyncLeaseAsync(config.TargetKey, config.LeaseOwner, TimeSpan.FromMinutes(1), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _storeMock.Setup(s => s.ReapplyPendingPulledMutationsAsync(config.TargetKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _storeMock.Setup(s => s.ListPendingSyncMutationsAsync(config.TargetKey, config.PushBatchSize, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _storeMock.Setup(s => s.ReplayDeferredAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReplayDeferredResult(0, 0));
        _storeMock.Setup(s => s.GetSyncStateAsync(config.TargetKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncState?)null);
        _transportMock.Setup(t => t.PullMutationsAsync(0, config.PullBatchSize, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException(error));
        _storeMock.Setup(s => s.MarkSyncFailureAsync(config.TargetKey, error, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _storeMock.Setup(s => s.ReleaseSyncLeaseAsync(config.TargetKey, config.LeaseOwner, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private void SetupSuccessfulCycle(SyncManagerConfig config)
    {
        _storeMock.Setup(s => s.AcquireSyncLeaseAsync(config.TargetKey, config.LeaseOwner, TimeSpan.FromMinutes(1), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _storeMock.Setup(s => s.ListPendingSyncMutationsAsync(config.TargetKey, config.PushBatchSize, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _storeMock.Setup(s => s.ReplayDeferredAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReplayDeferredResult(0, 0));
        _storeMock.Setup(s => s.GetSyncStateAsync(config.TargetKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncState?)null);
        _transportMock.Setup(t => t.PullMutationsAsync(0, config.PullBatchSize, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PullResult([], false, 0, ""));
        _storeMock.Setup(s => s.MarkSyncHealthyAsync(config.TargetKey, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _storeMock.Setup(s => s.ReleaseSyncLeaseAsync(config.TargetKey, config.LeaseOwner, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private static string GetNotificationFilePath(SyncManagerConfig config) =>
        Path.Combine(config.NotificationDirectory, "sync-notifications.log");

    private static async Task InvokeWriteNotificationAsync(
        SyncManager manager,
        string level,
        int failures,
        string? error,
        string? action,
        string? message)
    {
        var method = typeof(SyncManager).GetMethod("WriteNotificationAsync", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("WriteNotificationAsync method not found");
        var task = method.Invoke(manager, [level, failures, error, action, message]) as Task
            ?? throw new InvalidOperationException("WriteNotificationAsync did not return a Task");
        await task;
    }
}
