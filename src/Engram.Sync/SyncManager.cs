using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Engram.Store;
using Engram.Sync.Transport;
using MutationEntry = Engram.Sync.Transport.MutationEntry;

namespace Engram.Sync;

/// <summary>
/// Background service for automatic mutation-based sync.
/// Implements debounce + poll pattern with push/pull cycles, failure ceiling, and panic recovery.
/// Also implements ISyncOnDemandPusher for fire-and-forget pushes from MCP tools.
/// Design: sdd/offline-first-sync/design/design.md §AD-5
/// </summary>
public sealed class SyncManager : BackgroundService, ISyncStatusProvider, ISyncOnDemandPusher
{
    private const string OnDemandLeaseOwnerSuffix = "-on-demand";
    private static readonly Action<ILogger, Exception?> CycleStart =
        LoggerMessage.Define(
            LogLevel.Information,
            new EventId(2000, "SyncCycleStart"),
            "Sync cycle starting");

    private static readonly Action<ILogger, SyncPhase, int, Exception?> CycleComplete =
        LoggerMessage.Define<SyncPhase, int>(
            LogLevel.Information,
            new EventId(2001, "SyncCycleComplete"),
            "Sync cycle completed: phase={Phase}, duration={DurationMs}ms");

    private static readonly Action<ILogger, int, int, Exception?> CycleFailed =
        LoggerMessage.Define<int, int>(
            LogLevel.Error,
            new EventId(2002, "SyncCycleFailed"),
            "Sync cycle failed (failure {Failures}/{Max})");

    private static readonly Action<ILogger, int, int, Exception?> PushBatch =
        LoggerMessage.Define<int, int>(
            LogLevel.Information,
            new EventId(2003, "SyncPushBatch"),
            "Sync pushing {Count} mutations across {Projects} projects");

    private static readonly Action<ILogger, int, long, Exception?> PullBatch =
        LoggerMessage.Define<int, long>(
            LogLevel.Information,
            new EventId(2004, "SyncPullBatch"),
            "Sync pulled {Count} mutations (latest seq {Seq})");

    private static readonly Action<ILogger, int, int, Exception?> DeferredReplay =
        LoggerMessage.Define<int, int>(
            LogLevel.Information,
            new EventId(2005, "SyncDeferredReplay"),
            "Sync replayed {Replayed} deferred relations ({Dead} dead)");

    private static readonly Action<ILogger, Exception?> PanicExit =
        LoggerMessage.Define(
            LogLevel.Critical,
            new EventId(2006, "SyncPanicExit"),
            "SyncManager panic exit");

    private static readonly Action<ILogger, SyncPhase, SyncPhase, Exception?> PhaseTransition =
        LoggerMessage.Define<SyncPhase, SyncPhase>(
            LogLevel.Debug,
            new EventId(2007, "SyncPhaseTransition"),
            "Sync phase transition: {FromPhase} → {ToPhase}");

    private static readonly Action<ILogger, string, TimeSpan, Exception?> SyncManagerStarting =
        LoggerMessage.Define<string, TimeSpan>(
            LogLevel.Information,
            new EventId(2008, "SyncManagerStarting"),
            "SyncManager starting (target={TargetKey}, poll={PollInterval})");

    private static readonly Action<ILogger, string, Exception?> SyncNotificationWritten =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(2010, "SyncNotificationWritten"),
            "Sync notification written: {FilePath}");

    private static readonly Action<ILogger, int, Exception?> SyncRecovered =
        LoggerMessage.Define<int>(
            LogLevel.Information,
            new EventId(2011, "SyncRecovered"),
            "Sync recovered after {Failures} consecutive failures");

    /// <summary>
    /// Timeout per server during multi-server pull (RFC-006 §2.2).
    /// If a server doesn't respond within this window, it's skipped and the cycle continues.
    /// </summary>
    private static readonly TimeSpan PerServerPullTimeout = TimeSpan.FromSeconds(5);

    private readonly ILocalSyncStore _store;
    private readonly IMutationTransport _transport;
    private readonly SyncManagerConfig _cfg;
    private readonly ILogger<SyncManager> _logger;
    private readonly SyncMetrics _metrics;

    private SyncPhase _phase = SyncPhase.Idle;
    private int _consecutiveFailures;
    private DateTime? _backoffUntil;

    public SyncManager(ILocalSyncStore store, IMutationTransport transport, SyncManagerConfig cfg, ILogger<SyncManager> logger, SyncMetrics metrics)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
    }

    public SyncPhase Phase => _phase;
    public bool IsEnabled => _cfg.Enabled;
    public int ConsecutiveFailures => _consecutiveFailures;
    public DateTime? BackoffUntil => _backoffUntil;
    public SyncMetrics Metrics => _metrics;
    public string? LastError => _metrics.LastError;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_cfg.Enabled)
        {
            _logger.LogInformation("SyncManager disabled (ENGRAM_SYNC_ENABLED=false)");
            return;
        }

        SyncManagerStarting(_logger, _cfg.TargetKey, _cfg.PollInterval, null);

        // ENG-476 FR-002: Immediate push of pending mutations on startup
        // before entering the background loop, so any mutations left pending
        // from a previous session get flushed ASAP.
        try
        {
            await TriggerPushAsync(ct: stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Startup on-demand push failed (continuing to background loop)");
        }

        // HU-014 R6: Auto-sync guard — skip background poll loop when AutoSyncEnabled=false.
        // On-demand pushes (CLI / MCP trigger) still work via TriggerPushAsync.
        if (!_cfg.AutoSyncEnabled)
        {
            _logger.LogInformation("SyncManager: Auto-sync disabled (ENGRAM_SYNC_AUTO_SYNC=false). Background poll skipped. On-demand pushes still available.");
            return;
        }

        try
        {
            await RunLoopAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("SyncManager stopped (cancellation requested)");
        }
        catch (Exception ex)
        {
            PanicExit(_logger, ex);
            throw;
        }
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        var pollDelay = _cfg.PollInterval;
        DateTime nextPoll = DateTime.UtcNow;

        while (!ct.IsCancellationRequested)
        {
            if (_consecutiveFailures >= _cfg.MaxConsecutiveFailures)
            {
                await DisableSyncAsync();
                return;
            }

            if (_backoffUntil.HasValue && DateTime.UtcNow < _backoffUntil.Value)
            {
                SetPhase(SyncPhase.Backoff);
                await Task.Delay(_cfg.DebounceDuration, ct);
                continue;
            }

            var pollRemaining = nextPoll - DateTime.UtcNow;
            if (pollRemaining > TimeSpan.Zero)
            {
                try { await Task.Delay(pollRemaining, ct); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            }

            await CycleAsync(ct);
            nextPoll = DateTime.UtcNow + pollDelay;
        }
    }

    private async Task CycleAsync(CancellationToken ct)
    {
        if (_consecutiveFailures >= _cfg.MaxConsecutiveFailures)
        {
            await DisableSyncAsync();
            return;
        }

        var failedDuringPush = false;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        CycleStart(_logger, null);

        try
        {
            var leaseAcquired = await _store.AcquireSyncLeaseAsync(_cfg.TargetKey, _cfg.LeaseOwner, TimeSpan.FromMinutes(1), ct);
            if (!leaseAcquired)
            {
                _logger.LogDebug("SyncManager cycle skipped: lease not acquired");
                return;
            }

            // Re-apply any orphaned pulled mutations from a previous interrupted sync.
            // Must run BEFORE push because push may be blocked by non-enrolled projects,
            // and the cycle will return early without reaching this step otherwise.
            var reapplyCount = await _store.ReapplyPendingPulledMutationsAsync(_cfg.TargetKey, ct);
            if (reapplyCount > 0)
                _logger.LogInformation("SyncManager recovered {Count} orphaned pulled mutations", reapplyCount);

            SetPhase(SyncPhase.Pushing);
            failedDuringPush = true;
            var pushCompleted = await PushAsync(ct);
            failedDuringPush = false;
            if (!pushCompleted)
                return;

            var replayResult = await _store.ReplayDeferredAsync(ct);
            if (replayResult.ReplayCount > 0)
            {
                DeferredReplay(_logger, replayResult.ReplayCount, replayResult.DeadCount, null);
                _metrics.RecordDeferred(replayResult.ReplayCount, replayResult.DeadCount);
            }

            SetPhase(SyncPhase.Pulling);
            await PullAllServersAsync(ct);

            var previousFailures = _consecutiveFailures;
            SetPhase(SyncPhase.Healthy);
            _consecutiveFailures = 0;
            _backoffUntil = null;
            await _store.MarkSyncHealthyAsync(_cfg.TargetKey, ct);
            _metrics.ClearError();
            if (previousFailures > 0)
            {
                SyncRecovered(_logger, previousFailures, null);
                await WriteNotificationAsync(
                    "ok",
                    0,
                    null,
                    null,
                    $"Sync recovered after {previousFailures} failures");
            }

            _metrics.MarkSyncAt(DateTime.UtcNow);
            CycleComplete(_logger, _phase, (int)sw.Elapsed.TotalMilliseconds, null);
        }
        catch (Exception ex)
        {
            SetPhase(failedDuringPush ? SyncPhase.PushFailed : SyncPhase.PullFailed);
            _consecutiveFailures++;
            _backoffUntil = DateTime.UtcNow + CalculateBackoff();
            await _store.MarkSyncFailureAsync(_cfg.TargetKey, ex.Message, _backoffUntil.Value, ct);

            _metrics.IncrementFailures();
            _metrics.RecordError(ex.Message);
            CycleFailed(_logger, _consecutiveFailures, _cfg.MaxConsecutiveFailures, ex);

            if (_consecutiveFailures == _cfg.NotificationThreshold)
            {
                await WriteNotificationAsync(
                    "error",
                    _consecutiveFailures,
                    ex.Message,
                    "Check server connectivity and run 'engram sync status' for details.",
                    null);
            }
        }
        finally
        {
            await _store.ReleaseSyncLeaseAsync(_cfg.TargetKey, _cfg.LeaseOwner, ct);
        }
    }

    private async Task<bool> PushAsync(CancellationToken ct)
    {
        var pending = await _store.ListPendingSyncMutationsAsync(_cfg.TargetKey, _cfg.PushBatchSize, ct);
        if (pending.Count == 0) { _logger.LogDebug("SyncManager push: no pending mutations"); return true; }

        // ENG-514 (HU-013): Check non-enrolled and fail-loud projects that block sync
        var nonEnrolled = await _store.CountPendingNonEnrolledAsync(_cfg.TargetKey, ct);
        if (nonEnrolled.Count > 0)
        {
            var projectNames = string.Join(", ", nonEnrolled.Select(p => p.Project));
            _logger.LogWarning("SyncManager push blocked: {Count} non-enrolled/fail-loud projects detected: {Projects}",
                nonEnrolled.Count, projectNames);
            await _store.MarkSyncBlockedAsync(_cfg.TargetKey, "non-enrolled-pending",
                $"{nonEnrolled.Count} projects blocking sync: {projectNames}", ct);
            await WriteNotificationAsync(
                "error",
                _consecutiveFailures,
                $"non-enrolled-pending: {nonEnrolled.Count} projects not enrolled (fail-loud): {projectNames}",
                "Enroll projects: POST /sync/enroll",
                null);
            return false;
        }

        // ENG-514 (HU-013): Filter out projects with behavior='silent-skip' — these don't push
        var enrolledProjects = await _store.GetEnrolledProjectsLocalAsync(ct);
        var silentSkipProjects = enrolledProjects
            .Where(ep => ep.Behavior == "silent-skip")
            .Select(ep => ep.Project)
            .ToHashSet();

        if (silentSkipProjects.Count > 0)
        {
            var skippedMutations = pending.Where(m => silentSkipProjects.Contains(m.Project)).ToList();
            if (skippedMutations.Count > 0)
            {
                _logger.LogInformation(
                    "SyncManager push: skipping {Count} mutations from {ProjectCount} silent-skip projects: {Projects}",
                    skippedMutations.Count,
                    skippedMutations.Select(m => m.Project).Distinct().Count(),
                    string.Join(", ", skippedMutations.Select(m => m.Project).Distinct()));
            }
        }

        // Only push mutations from non-silent-skip projects
        var toPush = pending.Where(m => !silentSkipProjects.Contains(m.Project)).ToList();

        if (toPush.Count == 0)
        {
            _logger.LogDebug("SyncManager push: all pending mutations belong to silent-skip projects");
            // Ack silent-skip mutations so they don't accumulate infinitely
            var silentSeqs = pending
                .Where(m => silentSkipProjects.Contains(m.Project))
                .Select(m => m.Seq)
                .ToList();
            if (silentSeqs.Count > 0)
            {
                await _store.AckSyncMutationSeqsAsync(_cfg.TargetKey, silentSeqs, ct);
                _logger.LogInformation("SyncManager push: acknowledged {Count} silent-skip mutations", silentSeqs.Count);
            }
            return true;
        }

        var byProject = toPush.GroupBy(m => m.Project).ToList();
        PushBatch(_logger, toPush.Count, byProject.Count, null);

        return await PushBatchInternalAsync(toPush, ct);
    }

    /// <summary>
    /// Push a batch of pending mutations to the server. Used by both the
    /// background cycle and the on-demand trigger from MCP tools.
    /// </summary>
    private async Task<bool> PushBatchInternalAsync(IList<SyncMutation> pending, CancellationToken ct)
    {
        var byProject = pending.GroupBy(m => m.Project).ToList();

        foreach (var group in byProject)
        {
            var entries = group.Select(m => new MutationEntry(m.Project, m.Entity, m.EntityKey, m.Op, m.Payload)).ToList();
            try
            {
                var result = await _transport.PushMutationsAsync(entries, _cfg.LeaseOwner, ct);
                if (!string.IsNullOrEmpty(result.PauseError))
                {
                    _logger.LogWarning("SyncManager push paused for project {Project}: {Error}", group.Key, result.PauseError);
                    await _store.MarkSyncBlockedAsync(_cfg.TargetKey, "sync-paused", result.PauseError, ct);
                    await WriteNotificationAsync(
                        "error",
                        _consecutiveFailures,
                        $"sync-paused: {result.PauseError}",
                        $"Resume sync: DELETE /sync/pause?project={group.Key}",
                        null);
                    return false;
                }
                await _store.AckSyncMutationSeqsAsync(_cfg.TargetKey, result.AcceptedSeqs, ct);
                _logger.LogDebug("SyncManager push acked {Count} mutations for project {Project}", result.AcceptedSeqs.Count, group.Key);
                _metrics.IncrementPushed(result.AcceptedSeqs.Count);
            }
            catch (MutationTransportException ex) when (ex.StatusCode == 409)
            {
                _logger.LogWarning("SyncManager push paused (409): {Error}", ex.Message);
                await _store.MarkSyncBlockedAsync(_cfg.TargetKey, "sync-paused", ex.Message, ct);
                await WriteNotificationAsync(
                    "error",
                    _consecutiveFailures,
                    $"sync-paused: {ex.Message}",
                    $"Resume sync: DELETE /sync/pause?project={group.Key}",
                    null);
                return false;
            }
        }

        return true;
    }

    private async Task PullAsync(CancellationToken ct)
    {
        var state = await _store.GetSyncStateAsync(_cfg.TargetKey, ct);
        var sinceSeq = state?.LastPulledSeq ?? 0;
        var totalPulled = 0;

        while (!ct.IsCancellationRequested)
        {
            var result = await _transport.PullMutationsAsync(sinceSeq, _cfg.PullBatchSize, ct);
            if (result.Mutations.Count == 0) { _logger.LogDebug("SyncManager pull: no new mutations since seq {Seq}", sinceSeq); break; }

            foreach (var mutation in result.Mutations)
            {
                // Insert into sync_mutations first to get local seq, then apply
                var tempMutation = new SyncMutation(0, _cfg.TargetKey, mutation.Entity, mutation.EntityKey, mutation.Op, mutation.Payload, "pull", mutation.Project, DateTime.Parse(mutation.OccurredAt), null);
                var localSeq = await _store.InsertPulledMutationAsync(_cfg.TargetKey, tempMutation, ct);
                var syncMutation = new SyncMutation(localSeq, _cfg.TargetKey, mutation.Entity, mutation.EntityKey, mutation.Op, mutation.Payload, "pull", mutation.Project, DateTime.Parse(mutation.OccurredAt), null);
                await _store.ApplyPulledMutationAsync(_cfg.TargetKey, syncMutation, ct);
                sinceSeq = mutation.Seq;
                totalPulled++;
            }

            PullBatch(_logger, result.Mutations.Count, sinceSeq, null);
            if (!result.HasMore) break;
        }

        if (totalPulled > 0)
        {
            _logger.LogInformation("SyncManager pulled {Total} mutations total", totalPulled);
            _metrics.IncrementPulled(totalPulled);
            await _store.UpdateSyncStateAsync(_cfg.TargetKey, sinceSeq, ct);
        }
    }

    /// <summary>
    /// Multi-server pull with deduplication (RFC-006).
    /// Pulls mutations from all known servers sequentially, applying last-write-wins
    /// deduplication on conflicts. Uses per-server cursors from sync_pull_cursors.
    /// Each server gets a 5s timeout; if it doesn't respond, it's skipped with a warning.
    /// </summary>
    private async Task PullAllServersAsync(CancellationToken ct)
    {
        // Get the list of known servers (may be null from mocks or empty stores)
        List<string>? servers;
        try
        {
            servers = await _store.GetKnownServerIdsAsync(ct);
        }
        catch (Exception ex)
        {
            // Store doesn't support multi-server cursors — fall back to single-server pull
            _logger.LogDebug(ex, "GetKnownServerIdsAsync not supported, falling back to single-server pull");
            await PullAsync(ct);
            return;
        }

        if (servers is null || servers.Count == 0)
        {
            // No servers registered — fall back to single-server pull
            _logger.LogDebug("SyncManager pull: no servers in cursor store, using single-server pull");
            await PullAsync(ct);
            return;
        }

        // Single-server fast path: use the existing PullAsync logic
        if (servers.Count == 1)
        {
            await PullAsync(ct);
            return;
        }

        _logger.LogInformation("SyncManager multi-server pull: {ServerCount} servers", servers.Count);

        var allMutations = new List<(string ServerId, PulledMutation Mutation)>();
        var lastSeqPerServer = new Dictionary<string, long>();
        var totalPulled = 0;

        foreach (var serverId in servers)
        {
            // Get per-server cursor (RFC-006 §2.1)
            var cursor = await _store.GetLastPulledSeqForServerAsync(_cfg.TargetKey, serverId, ct) ?? 0;

            try
            {
                // Create a 5s timeout token for this server (RFC-006 §2.2)
                using var serverCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                serverCts.CancelAfter(PerServerPullTimeout);

                var result = await _transport.PullMutationsAsync(
                    cursor, _cfg.PullBatchSize, serverCts.Token, serverId);

                if (result.Mutations.Count > 0)
                {
                    foreach (var mutation in result.Mutations)
                    {
                        allMutations.Add((serverId, mutation));
                    }
                    totalPulled += result.Mutations.Count;
                    lastSeqPerServer[serverId] = result.LatestSeq;
                    _logger.LogDebug(
                        "Pulled {Count} mutations from server {Server} (since seq {Cursor})",
                        result.Mutations.Count, serverId, cursor);
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Server timeout — skip and continue with next server (RFC-006 §3 Q4)
                _logger.LogWarning(
                    "Timeout pulling from server {Server} after {TimeoutSeconds}s, skipping",
                    serverId, PerServerPullTimeout.TotalSeconds);
                continue;
            }
            catch (HttpRequestException ex)
            {
                // Network error from this server — skip and continue (RFC-006 §2.2)
                _logger.LogWarning(ex, "Network error pulling from server {Server}, skipping", serverId);
                continue;
            }
        }

        if (allMutations.Count == 0)
        {
            _logger.LogDebug("SyncManager multi-server pull: no mutations from any server");
            return;
        }

        // Apply with dedup (RFC-006 §2.3)
        await ApplyWithDedupAsync(allMutations, ct);

        // Update per-server cursors (RFC-006 §2.2 step 7)
        foreach (var (serverId, seq) in lastSeqPerServer)
        {
            await _store.SetLastPulledSeqForServerAsync(_cfg.TargetKey, serverId, seq, ct);
        }

        if (totalPulled > 0)
        {
            _logger.LogInformation(
                "SyncManager multi-server pull: {Total} total mutations from {ServerCount} servers",
                totalPulled, servers.Count);
            _metrics.IncrementPulled(totalPulled);

            // Update the global cursor for backward compat
            var maxSeq = lastSeqPerServer.Values.Max();
            await _store.UpdateSyncStateAsync(_cfg.TargetKey, maxSeq, ct);
        }
    }

    /// <summary>
    /// Apply pulled mutations with last-write-wins deduplication (RFC-006 §2.3).
    /// Groups mutations by <c>sync_id</c> (canonical observation identifier).
    /// When the same sync_id appears from multiple servers, the winner is determined by:
    /// 1. Most recent <c>occurred_at</c> (last-write-wins, ADR-009)
    /// 2. Highest <c>server_id</c> as tiebreaker (RFC-006 §3 Q1)
    /// Mutations without a sync_id are treated as unique (no grouping).
    /// </summary>
    private async Task ApplyWithDedupAsync(
        List<(string ServerId, PulledMutation Mutation)> allMutations,
        CancellationToken ct)
    {
        if (allMutations.Count == 0) return;

        // Group by sync_id for dedup (RFC-006 §2.3, AD8)
        var groups = allMutations
            .GroupBy(m => m.Mutation.SyncId ?? $"__unique__{Guid.NewGuid()}");

        foreach (var group in groups)
        {
            PulledMutation winner;
            string winningServerId;

            if (group.Count() == 1)
            {
                var single = group.First();
                winner = single.Mutation;
                winningServerId = single.ServerId;
            }
            else
            {
                // Multiple versions from different servers → last-write-wins (ADR-009)
                // Tiebreaker: highest server_id (RFC-006 §3 Q1)
                var winnerTuple = group
                    .OrderByDescending(m => DateTime.TryParse(m.Mutation.OccurredAt, out var dt) ? dt : DateTime.MinValue)
                    .ThenByDescending(m => m.ServerId)
                    .First();

                winner = winnerTuple.Mutation;
                winningServerId = winnerTuple.ServerId;

                _logger.LogDebug(
                    "Dedup conflict for {SyncId}: {Count} versions, winning from {Server} at {OccurredAt}",
                    winner.SyncId ?? "(no sync_id)",
                    group.Count(),
                    winningServerId,
                    winner.OccurredAt);
            }

            // Apply to local store via existing mutation pipeline
            var tempMutation = new SyncMutation(
                0, _cfg.TargetKey, winner.Entity, winner.EntityKey,
                winner.Op, winner.Payload, "pull", winner.Project,
                DateTime.TryParse(winner.OccurredAt, out var occurredAt) ? occurredAt : DateTime.UtcNow,
                null);

            var localSeq = await _store.InsertPulledMutationAsync(_cfg.TargetKey, tempMutation, ct);
            var syncMutation = new SyncMutation(
                localSeq, _cfg.TargetKey, winner.Entity, winner.EntityKey,
                winner.Op, winner.Payload, "pull", winner.Project,
                tempMutation.OccurredAt, null);

            await _store.ApplyPulledMutationAsync(_cfg.TargetKey, syncMutation, ct);
        }
    }

    private async Task DisableSyncAsync()
    {
        if (_phase == SyncPhase.Disabled)
            return;

        SetPhase(SyncPhase.Disabled);
        await WriteNotificationAsync(
            "error",
            _consecutiveFailures,
            $"Sync disabled after {_consecutiveFailures} failures",
            "Restart engram server or check ENGRAM_SERVER_URL configuration.",
            null);
        CycleFailed(_logger, _consecutiveFailures, _cfg.MaxConsecutiveFailures, null);
    }

    private async Task WriteNotificationAsync(
        string level,
        int failures,
        string? error,
        string? action,
        string? message)
    {
        try
        {
            var entry = new Dictionary<string, object?>
            {
                ["ts"] = DateTime.UtcNow.ToString("O"),
                ["level"] = level,
                ["failures"] = failures,
                ["error"] = error,
                ["action"] = action,
                ["phase"] = _phase.ToString().ToLowerInvariant(),
                ["message"] = message
            };
            var json = JsonSerializer.Serialize(entry, new JsonSerializerOptions
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });

            Directory.CreateDirectory(_cfg.NotificationDirectory);
            var filePath = Path.Combine(_cfg.NotificationDirectory, "sync-notifications.log");
            var lines = File.Exists(filePath)
                ? (await File.ReadAllLinesAsync(filePath))
                    .Where(line => !string.IsNullOrWhiteSpace(line))
                    .ToList()
                : [];

            lines.Add(json);
            var maxEntries = Math.Max(1, _cfg.NotificationFileMaxEntries);
            if (lines.Count > maxEntries)
                lines = lines.Skip(lines.Count - maxEntries).ToList();

            await File.WriteAllTextAsync(filePath, string.Join('\n', lines) + "\n");

            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
                File.SetUnixFileMode(filePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);

            SyncNotificationWritten(_logger, filePath, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write sync notification file");
        }
    }

    private void SetPhase(SyncPhase newPhase)
    {
        var oldPhase = _phase;
        if (oldPhase == newPhase) return;
        _phase = newPhase;
        PhaseTransition(_logger, oldPhase, newPhase, null);
    }

    private TimeSpan CalculateBackoff()
    {
        var backoff = _cfg.BaseBackoff * Math.Pow(2, _consecutiveFailures - 1);
        return backoff > _cfg.MaxBackoff ? _cfg.MaxBackoff : backoff;
    }

    // ─── ISyncOnDemandPusher (ENG-476) ─────────────────────────────────────────

    /// <summary>
    /// Fire-and-forget push triggered by MCP tools after a write.
    /// When <paramref name="project"/> is provided, validates enrollment and pushes only that project
    /// (HU-014 R4, R5 denylist integration).
    /// When null (default), pushes all enrolled projects (backward-compatible).
    /// Respects lease (skips if background holds it) and backoff (skips if active).
    /// Never throws to caller.
    /// </summary>
    public async Task TriggerPushAsync(string? project = null, CancellationToken ct = default)
    {
        if (!_cfg.Enabled) return;

        // HU-014 R5: Validate project enrollment before acquiring lease (fail fast)
        if (!string.IsNullOrWhiteSpace(project))
        {
            try
            {
                var behavior = await _store.GetProjectBehaviorAsync(project, ct);
                if (behavior is null)
                {
                    _logger.LogWarning("On-demand push for project {Project} skipped: project not enrolled", project);
                    return;
                }
                if (behavior == "silent-skip")
                {
                    _logger.LogDebug("On-demand push for project {Project} skipped: silent-skip behavior", project);
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "On-demand push for project {Project}: failed to check enrollment", project);
                return;
            }
        }

        // FR-005: Respect backoff
        if (_backoffUntil.HasValue && DateTime.UtcNow < _backoffUntil.Value)
        {
            _logger.LogDebug("On-demand push skipped: in backoff until {BackoffUntil}", _backoffUntil.Value);
            return;
        }

        // FR-004: Respect lease — use a different owner so we don't race the background loop.
        var onDemandOwner = $"{_cfg.LeaseOwner}{OnDemandLeaseOwnerSuffix}";
        var leaseAcquired = await _store.AcquireSyncLeaseAsync(
            _cfg.TargetKey, onDemandOwner, TimeSpan.FromSeconds(30), ct);

        if (!leaseAcquired)
        {
            _logger.LogDebug("On-demand push skipped: lease held by background");
            return;
        }

        try
        {
            var pending = await _store.ListPendingSyncMutationsAsync(
                _cfg.TargetKey, _cfg.PushBatchSize, ct);

            // HU-014: Filter to target project when scoped push is requested
            if (!string.IsNullOrWhiteSpace(project))
            {
                pending = pending.Where(m => string.Equals(m.Project, project, StringComparison.Ordinal)).ToList();
            }

            if (pending.Count == 0)
            {
                _logger.LogDebug("On-demand push: no pending mutations");
                return;
            }

            // ENG-514 (HU-013): Filter out silent-skip projects on on-demand push
            var enrolledProjects = await _store.GetEnrolledProjectsLocalAsync(ct);
            var silentSkipProjects = enrolledProjects
                .Where(ep => ep.Behavior == "silent-skip")
                .Select(ep => ep.Project)
                .ToHashSet();

            var toPush = pending;
            if (silentSkipProjects.Count > 0)
            {
                var skipped = pending.Where(m => silentSkipProjects.Contains(m.Project)).ToList();
                if (skipped.Count > 0)
                {
                    _logger.LogInformation(
                        "On-demand push: skipping {Count} mutations from silent-skip projects: {Projects}",
                        skipped.Count,
                        string.Join(", ", skipped.Select(m => m.Project).Distinct()));
                }
                toPush = pending.Where(m => !silentSkipProjects.Contains(m.Project)).ToList();
            }

            if (toPush.Count == 0)
            {
                _logger.LogDebug("On-demand push: all pending mutations in silent-skip projects");
                return;
            }

            if (!string.IsNullOrWhiteSpace(project))
            {
                _logger.LogInformation(
                    "On-demand push for project {Project} starting: {Count} pending mutations",
                    project, toPush.Count);
            }
            else
            {
                _logger.LogInformation("On-demand push starting: {Count} pending mutations", toPush.Count);
            }

            await PushBatchInternalAsync(toPush, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "On-demand push failed");
        }
        finally
        {
            await _store.ReleaseSyncLeaseAsync(_cfg.TargetKey, onDemandOwner, ct);
        }
    }

    public async Task<int> CountPendingMutationsAsync(CancellationToken ct = default)
    {
        try
        {
            return await _store.CountPendingSyncMutationsAsync(_cfg.TargetKey, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "CountPendingMutationsAsync failed");
            return 0;
        }
    }

    /// <summary>
    /// Fire-and-forget push for a specific project only.
    /// Delegates to <see cref="TriggerPushAsync"/> with the project parameter.
    /// </summary>
    [Obsolete("Use TriggerPushAsync(project) instead.")]
    public Task TriggerPushForProjectAsync(string project, CancellationToken ct = default)
    {
        return TriggerPushAsync(project, ct);
    }

    /// <summary>
    /// Count pending local mutations for a specific project (HU-014).
    /// Delegates to store-level grouped query for efficiency.
    /// </summary>
    public async Task<int> CountPendingMutationsByProjectAsync(string project, CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(project)) return 0;
            var all = await _store.CountPendingMutationsByProjectAsync(_cfg.TargetKey, ct);
            var entry = all.FirstOrDefault(p => p.Project == project);
            return entry is null ? 0 : (int)entry.Count;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "CountPendingMutationsByProjectAsync failed for project {Project}", project);
            return 0;
        }
    }
}
