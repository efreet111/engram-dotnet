using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Engram.Store;
using Engram.Sync.Transport;
using MutationEntry = Engram.Sync.Transport.MutationEntry;

namespace Engram.Sync;

/// <summary>
/// Background service for automatic mutation-based sync.
/// Implements debounce + poll pattern with push/pull cycles, failure ceiling, and panic recovery.
/// Design: sdd/offline-first-sync/design/design.md §AD-5
/// </summary>
public sealed class SyncManager : BackgroundService, ISyncStatusProvider
{
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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_cfg.Enabled)
        {
            _logger.LogInformation("SyncManager disabled (ENGRAM_SYNC_ENABLED=false)");
            return;
        }

        SyncManagerStarting(_logger, _cfg.TargetKey, _cfg.PollInterval, null);

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
                SetPhase(SyncPhase.Disabled);
                CycleFailed(_logger, _consecutiveFailures, _cfg.MaxConsecutiveFailures, null);
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
            SetPhase(SyncPhase.Disabled);
            CycleFailed(_logger, _consecutiveFailures, _cfg.MaxConsecutiveFailures, null);
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

            SetPhase(SyncPhase.Pushing);
            failedDuringPush = true;
            await PushAsync(ct);
            failedDuringPush = false;

            var replayResult = await _store.ReplayDeferredAsync(ct);
            if (replayResult.ReplayCount > 0)
            {
                DeferredReplay(_logger, replayResult.ReplayCount, replayResult.DeadCount, null);
                _metrics.RecordDeferred(replayResult.ReplayCount, replayResult.DeadCount);
            }

            SetPhase(SyncPhase.Pulling);
            await PullAsync(ct);

            SetPhase(SyncPhase.Healthy);
            _consecutiveFailures = 0;
            _backoffUntil = null;
            await _store.MarkSyncHealthyAsync(_cfg.TargetKey, ct);

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
        }
        finally
        {
            await _store.ReleaseSyncLeaseAsync(_cfg.TargetKey, _cfg.LeaseOwner, ct);
        }
    }

    private async Task PushAsync(CancellationToken ct)
    {
        var pending = await _store.ListPendingSyncMutationsAsync(_cfg.TargetKey, _cfg.PushBatchSize, ct);
        if (pending.Count == 0) { _logger.LogDebug("SyncManager push: no pending mutations"); return; }

        var nonEnrolled = await _store.CountPendingNonEnrolledAsync(_cfg.TargetKey, ct);
        if (nonEnrolled.Count > 0)
        {
            _logger.LogWarning("SyncManager push blocked: {Count} non-enrolled projects detected", nonEnrolled.Count);
            await _store.MarkSyncBlockedAsync(_cfg.TargetKey, "non-enrolled-pending", $"{nonEnrolled.Count} projects not enrolled", ct);
            return;
        }

        var byProject = pending.GroupBy(m => m.Project).ToList();
        PushBatch(_logger, pending.Count, byProject.Count, null);

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
                    return;
                }
                await _store.AckSyncMutationSeqsAsync(_cfg.TargetKey, result.AcceptedSeqs, ct);
                _logger.LogDebug("SyncManager push acked {Count} mutations for project {Project}", result.AcceptedSeqs.Count, group.Key);
                _metrics.IncrementPushed(result.AcceptedSeqs.Count);
            }
            catch (MutationTransportException ex) when (ex.StatusCode == 409)
            {
                _logger.LogWarning("SyncManager push paused (409): {Error}", ex.Message);
                await _store.MarkSyncBlockedAsync(_cfg.TargetKey, "sync-paused", ex.Message, ct);
                return;
            }
        }
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
                var syncMutation = new SyncMutation(mutation.Seq, _cfg.TargetKey, mutation.Entity, mutation.EntityKey, mutation.Op, mutation.Payload, "pull", mutation.Project, DateTime.Parse(mutation.OccurredAt), null);
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
}
