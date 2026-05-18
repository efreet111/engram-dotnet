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
public sealed class SyncManager : BackgroundService
{
    private readonly ILocalSyncStore _store;
    private readonly IMutationTransport _transport;
    private readonly SyncManagerConfig _cfg;
    private readonly ILogger<SyncManager> _logger;

    private SyncPhase _phase = SyncPhase.Idle;
    private int _consecutiveFailures;
    private DateTime? _backoffUntil;

    public SyncManager(ILocalSyncStore store, IMutationTransport transport, SyncManagerConfig cfg, ILogger<SyncManager> logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public SyncPhase Phase => _phase;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_cfg.Enabled)
        {
            _logger.LogInformation("SyncManager disabled (ENGRAM_SYNC_ENABLED=false)");
            return;
        }

        _logger.LogInformation("SyncManager starting (target={TargetKey}, poll={PollInterval})", _cfg.TargetKey, _cfg.PollInterval);

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
            _logger.LogCritical(ex, "SyncManager panic exit (stack trace logged)");
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
                _phase = SyncPhase.Disabled;
                _logger.LogError("SyncManager disabled: failure ceiling reached ({Failures}/{Max})", _consecutiveFailures, _cfg.MaxConsecutiveFailures);
                return;
            }

            if (_backoffUntil.HasValue && DateTime.UtcNow < _backoffUntil.Value)
            {
                _phase = SyncPhase.Backoff;
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
        // Check failure ceiling at start of cycle (AD-5 requirement)
        if (_consecutiveFailures >= _cfg.MaxConsecutiveFailures)
        {
            _phase = SyncPhase.Disabled;
            _logger.LogError("SyncManager cycle aborted: failure ceiling reached ({Failures}/{Max})", _consecutiveFailures, _cfg.MaxConsecutiveFailures);
            return;
        }

        var failedDuringPush = false;

        try
        {
            var leaseAcquired = await _store.AcquireSyncLeaseAsync(_cfg.TargetKey, _cfg.LeaseOwner, TimeSpan.FromMinutes(1), ct);
            if (!leaseAcquired)
            {
                _logger.LogDebug("SyncManager cycle skipped: lease not acquired");
                return;
            }

            _phase = SyncPhase.Pushing;
            failedDuringPush = true; // Still in push phase
            await PushAsync(ct);
            failedDuringPush = false; // Push completed successfully

            var replayResult = await _store.ReplayDeferredAsync(ct);
            if (replayResult.ReplayCount > 0)
                _logger.LogInformation("SyncManager replayed {Replayed} deferred relations ({Dead} dead)", replayResult.ReplayCount, replayResult.DeadCount);

            _phase = SyncPhase.Pulling;
            await PullAsync(ct);

            _phase = SyncPhase.Healthy;
            _consecutiveFailures = 0;
            _backoffUntil = null;
            await _store.MarkSyncHealthyAsync(_cfg.TargetKey, ct);
            _logger.LogDebug("SyncManager cycle completed successfully");
        }
        catch (Exception ex)
        {
            _phase = failedDuringPush ? SyncPhase.PushFailed : SyncPhase.PullFailed;
            _consecutiveFailures++;
            _backoffUntil = DateTime.UtcNow + CalculateBackoff();
            await _store.MarkSyncFailureAsync(_cfg.TargetKey, ex.Message, _backoffUntil.Value, ct);
            _logger.LogError(ex, "SyncManager cycle failed (failure {Failures}/{Max})", _consecutiveFailures, _cfg.MaxConsecutiveFailures);
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

        // Check for non-enrolled projects before pushing (AD-5 step 4)
        var nonEnrolled = await _store.CountPendingNonEnrolledAsync(_cfg.TargetKey, ct);
        if (nonEnrolled.Count > 0)
        {
            _logger.LogWarning("SyncManager push blocked: {Count} non-enrolled projects detected", nonEnrolled.Count);
            await _store.MarkSyncBlockedAsync(_cfg.TargetKey, "non-enrolled-pending", $"{nonEnrolled.Count} projects not enrolled", ct);
            return;
        }

        var byProject = pending.GroupBy(m => m.Project).ToList();
        _logger.LogInformation("SyncManager pushing {Count} mutations across {Projects} projects", pending.Count, byProject.Count);

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

            _logger.LogDebug("SyncManager pulled {Count} mutations (latest seq {Seq})", result.Mutations.Count, sinceSeq);
            if (!result.HasMore) break;
        }

        if (totalPulled > 0) _logger.LogInformation("SyncManager pulled {Total} mutations total", totalPulled);
    }

    private TimeSpan CalculateBackoff()
    {
        var backoff = _cfg.BaseBackoff * Math.Pow(2, _consecutiveFailures - 1);
        return backoff > _cfg.MaxBackoff ? _cfg.MaxBackoff : backoff;
    }
}
