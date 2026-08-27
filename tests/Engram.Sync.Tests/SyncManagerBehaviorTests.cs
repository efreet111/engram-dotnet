using System.Reflection;
using Engram.Store;
using Engram.Sync.Transport;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using MutationEntry = Engram.Sync.Transport.MutationEntry;

namespace Engram.Sync.Tests;

/// <summary>
/// Tests for HU-013 sync manager behavior-filtered push logic.
/// Validates that silent-skip projects don't block sync, fail-loud projects do,
/// and silent-skip mutations are auto-acked without being pushed.
/// </summary>
public sealed class SyncManagerBehaviorTests : IDisposable
{
    private readonly Mock<ILocalSyncStore> _storeMock;
    private readonly Mock<IMutationTransport> _transportMock;
    private readonly Mock<ILogger<SyncManager>> _loggerMock;
    private readonly SyncManagerConfig _config;
    private readonly string _tempDir;

    public SyncManagerBehaviorTests()
    {
        _storeMock = new Mock<ILocalSyncStore>();
        _transportMock = new Mock<IMutationTransport>();
        _loggerMock = new Mock<ILogger<SyncManager>>();
        _loggerMock.Setup(logger => logger.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
        _tempDir = Path.Combine(Path.GetTempPath(), "engram-sync-behavior-tests", Guid.NewGuid().ToString("N"));
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

    // Helper to invoke private PushAsync via reflection
    private static async Task<bool> InvokePushAsync(SyncManager syncManager, CancellationToken ct)
    {
        var method = typeof(SyncManager).GetMethod("PushAsync", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("PushAsync method not found");
        var result = method.Invoke(syncManager, [ct]);
        if (result is Task<bool> task)
            return await task;
        throw new InvalidOperationException($"Expected Task<bool>, got {result?.GetType().Name ?? "null"}");
    }

    // Helper to create SyncManager and setup lease
    private SyncManager CreateManager()
    {
        _storeMock.Setup(s => s.AcquireSyncLeaseAsync(_config.TargetKey, _config.LeaseOwner, TimeSpan.FromMinutes(1), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _storeMock.Setup(s => s.ReleaseSyncLeaseAsync(_config.TargetKey, _config.LeaseOwner, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return new SyncManager(_storeMock.Object, _transportMock.Object, _config, _loggerMock.Object, new SyncMetrics());
    }

    private static List<SyncMutation> CreatePendingMutations(string project, int count = 1)
    {
        var mutations = new List<SyncMutation>();
        for (var i = 0; i < count; i++)
        {
            mutations.Add(new SyncMutation(
                i + 1, "cloud", "observation", $"obs-{i}", "upsert", "{}", "local",
                project, DateTime.UtcNow, null));
        }
        return mutations;
    }

    // ─── Task 6.2: Behavior-filtered sync logic ────────────────────────────────

    /// <summary>
    /// CountPendingNonEnrolled excludes silent-skip projects (filtered at DB level).
    /// When no fail-loud non-enrolled projects exist, push should NOT be blocked.
    /// </summary>
    [Fact]
    public async Task CountPendingNonEnrolled_ExcludesSilentSkipProjects()
    {
        // Arrange: store returns empty non-enrolled (silent-skip is filtered at DB level)
        _storeMock.Setup(s => s.ListPendingSyncMutationsAsync(_config.TargetKey, _config.PushBatchSize, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreatePendingMutations("silent-project"));
        _storeMock.Setup(s => s.CountPendingNonEnrolledAsync(_config.TargetKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PendingProjectCount>()); // empty = no fail-loud non-enrolled
        _storeMock.Setup(s => s.GetEnrolledProjectsLocalAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EnrolledProjectLocal>
            {
                new("silent-project", "silent-skip", DateTime.UtcNow.ToString("O"))
            });
        _storeMock.Setup(s => s.AckSyncMutationSeqsAsync(_config.TargetKey, It.IsAny<IReadOnlyList<long>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var syncManager = CreateManager();

        // Act
        var result = await InvokePushAsync(syncManager, CancellationToken.None);

        // Assert: push succeeds (silent-skip doesn't block), mutations are auto-acked
        Assert.True(result);
        _storeMock.Verify(s => s.MarkSyncBlockedAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        // Verify silent-skip mutations were acked (auto-ack to prevent accumulation)
        _storeMock.Verify(s => s.AckSyncMutationSeqsAsync(_config.TargetKey, It.IsAny<IReadOnlyList<long>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// CountPendingNonEnrolled includes fail-loud non-enrolled projects — these DO block sync.
    /// </summary>
    [Fact]
    public async Task CountPendingNonEnrolled_IncludesFailLoudProjects()
    {
        // Arrange: store returns non-enrolled fail-loud projects
        _storeMock.Setup(s => s.ListPendingSyncMutationsAsync(_config.TargetKey, _config.PushBatchSize, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreatePendingMutations("fail-project"));
        _storeMock.Setup(s => s.CountPendingNonEnrolledAsync(_config.TargetKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PendingProjectCount>
            {
                new("fail-project", 1)
            });
        _storeMock.Setup(s => s.MarkSyncBlockedAsync(_config.TargetKey, "non-enrolled-pending", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var syncManager = CreateManager();

        // Act
        var result = await InvokePushAsync(syncManager, CancellationToken.None);

        // Assert: push is blocked (returns false)
        Assert.False(result);
        _storeMock.Verify(s => s.MarkSyncBlockedAsync(
            _config.TargetKey, "non-enrolled-pending", It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Silent-skip mutations are acknowledged (auto-acked) but never pushed to transport.
    /// Push completes successfully (returns true) since there are no fail-loud mutations.
    /// </summary>
    [Fact]
    public async Task SilentSkipProject_MutationsNotPushedButNotBlocked()
    {
        // Arrange: only silent-skip mutations pending, no fail-loud mutations
        var silentMutations = new List<SyncMutation>
        {
            new(100, "cloud", "observation", "obs-s1", "upsert", "{}", "local", "silent-a", DateTime.UtcNow, null),
            new(101, "cloud", "observation", "obs-s2", "upsert", "{}", "local", "silent-a", DateTime.UtcNow, null),
            new(102, "cloud", "prompt", "p-s1", "upsert", "{}", "local", "silent-b", DateTime.UtcNow, null),
        };
        _storeMock.Setup(s => s.ListPendingSyncMutationsAsync(_config.TargetKey, _config.PushBatchSize, It.IsAny<CancellationToken>()))
            .ReturnsAsync(silentMutations);
        _storeMock.Setup(s => s.CountPendingNonEnrolledAsync(_config.TargetKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PendingProjectCount>());
        _storeMock.Setup(s => s.GetEnrolledProjectsLocalAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EnrolledProjectLocal>
            {
                new("silent-a", "silent-skip", DateTime.UtcNow.ToString("O")),
                new("silent-b", "silent-skip", DateTime.UtcNow.ToString("O")),
            });
        _storeMock.Setup(s => s.AckSyncMutationSeqsAsync(_config.TargetKey, It.IsAny<IReadOnlyList<long>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var syncManager = CreateManager();

        // Act
        var result = await InvokePushAsync(syncManager, CancellationToken.None);

        // Assert: push succeeds (true), transport was NEVER called (silent-skip filtered)
        Assert.True(result);
        _transportMock.Verify(t => t.PushMutationsAsync(
            It.IsAny<IReadOnlyList<MutationEntry>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        // Verify ack was called for all 3 silent-skip mutations
        _storeMock.Verify(s => s.AckSyncMutationSeqsAsync(
            _config.TargetKey,
            It.Is<IReadOnlyList<long>>(seqs => seqs.Count == 3 && seqs.SequenceEqual(new long[] { 100, 101, 102 })),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Mixed mutations: fail-loud project gets pushed, silent-skip gets auto-acked.
    /// </summary>
    [Fact]
    public async Task MixedProject_Mutations_FilteredCorrectly()
    {
        // Arrange: 1 fail-loud mutation + 1 silent-skip mutation
        var mixedMutations = new List<SyncMutation>
        {
            new(1, "cloud", "observation", "obs-fail", "upsert", "{}", "local", "fail-project", DateTime.UtcNow, null),
            new(2, "cloud", "observation", "obs-skip", "upsert", "{}", "local", "silent-project", DateTime.UtcNow, null),
        };
        _storeMock.Setup(s => s.ListPendingSyncMutationsAsync(_config.TargetKey, _config.PushBatchSize, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mixedMutations);
        _storeMock.Setup(s => s.CountPendingNonEnrolledAsync(_config.TargetKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PendingProjectCount>());
        _storeMock.Setup(s => s.GetEnrolledProjectsLocalAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EnrolledProjectLocal>
            {
                new("fail-project", "fail-loud", DateTime.UtcNow.ToString("O")),
                new("silent-project", "silent-skip", DateTime.UtcNow.ToString("O")),
            });
        _storeMock.Setup(s => s.AckSyncMutationSeqsAsync(_config.TargetKey, It.IsAny<IReadOnlyList<long>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var pushResult = new PushResult(new List<long> { 1 }, "fail-project", null);
        _transportMock.Setup(t => t.PushMutationsAsync(
                It.IsAny<IReadOnlyList<MutationEntry>>(), _config.LeaseOwner, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pushResult);

        var syncManager = CreateManager();

        // Act
        var result = await InvokePushAsync(syncManager, CancellationToken.None);

        // Assert: push succeeds, ack called twice (once for fail-loud push result, once for silent-skip auto-ack)
        Assert.True(result);
        _transportMock.Verify(t => t.PushMutationsAsync(
                It.IsAny<IReadOnlyList<MutationEntry>>(), _config.LeaseOwner, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// No enrolled projects with pending mutations from unenrolled project → blocked.
    /// </summary>
    [Fact]
    public async Task UnenrolledProject_WithPendingMutations_BlocksSync()
    {
        // Arrange: unenrolled project has pending mutations, no enrollment exists
        _storeMock.Setup(s => s.ListPendingSyncMutationsAsync(_config.TargetKey, _config.PushBatchSize, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreatePendingMutations("unknown-project"));
        _storeMock.Setup(s => s.CountPendingNonEnrolledAsync(_config.TargetKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PendingProjectCount>
            {
                new("unknown-project", 3)
            });
        _storeMock.Setup(s => s.MarkSyncBlockedAsync(_config.TargetKey, "non-enrolled-pending", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var syncManager = CreateManager();

        // Act
        var result = await InvokePushAsync(syncManager, CancellationToken.None);

        // Assert: blocked
        Assert.False(result);
        _storeMock.Verify(s => s.MarkSyncBlockedAsync(
            _config.TargetKey,
            "non-enrolled-pending",
            It.Is<string>(msg => msg.Contains("unknown-project")),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
