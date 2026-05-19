# Phase 4: Observability — Specification

**Status**: Draft  
**Priority**: Medium  
**Estimated Effort**: 3.5h  
**Depends On**: Phase 3

---

## REQ-OBS-01: Status Endpoint

`GET /sync/status` MUST return sync state: cursor, health, counts, enrolled/paused projects.

### Scenario: Healthy sync
- GIVEN sync is enabled and running
- WHEN client calls `GET /sync/status`
- THEN response includes `sync_enabled: true`, `phase: "healthy"`
- AND cursor, health, counts, enrolled/paused arrays

### Scenario: Sync disabled
- GIVEN `ENGRAM_SYNC_ENABLED=false`
- WHEN client calls `GET /sync/status`
- THEN response includes `sync_enabled: false`, minimal data, no phase

### Scenario: SyncManager inactive
- GIVEN SyncManager has not started
- WHEN client calls `GET /sync/status`
- THEN response includes `health.status: "unknown"` without failure counts
- AND cursor reports last known seqs from `sync_state`

### Response contract
```json
{
  "sync_enabled": true,
  "phase": "healthy",
  "target": "cloud",
  "cursor": {
    "last_pushed_seq": 142,
    "last_pulled_seq": 89,
    "last_enqueued_seq": 145
  },
  "health": {
    "status": "healthy",
    "consecutive_failures": 0,
    "backoff_until": null,
    "last_error": null,
    "last_sync_at": "2026-05-19T10:30:00Z"
  },
  "counts": {
    "pending_push": 3,
    "total_pushed": 142,
    "total_pulled": 89,
    "deferred_pending": 0
  },
  "enrolled_projects": ["team/mi-proyecto"],
  "paused_projects": []
}
```

**Impl**: Server queries `ICloudMutationStore` for enrolled/paused + latest seqs. Client queries `ILocalSyncStore.GetSyncStateAsync()`. `ISyncStatusProvider` exposes SyncManager runtime state.

---

## REQ-OBS-02: Structured Logging

SyncManager MUST emit structured log events using `LoggerMessage` source-gen.

| Event | Level | EventId | Data |
|-------|-------|---------|------|
| CycleStart | Debug | 2000 | phase, cycle_id |
| CycleComplete | Information | 2001 | phase, pushed, pulled, duration_ms |
| CycleFailed | Error | 2002 | phase, failures, max_failures, error |
| PushBatch | Debug | 2003 | project, count |
| PullBatch | Debug | 2004 | count, since_seq, latest_seq |
| DeferredReplay | Information | 2005 | replayed, dead |
| PanicExit | Critical | 2006 | error |
| PhaseTransition | Debug | 2007 | from, to |

### Scenario: Cycle completes
- GIVEN SyncManager finishes a push+pull cycle
- WHEN no errors occur
- THEN log Information event with pushed/pulled counts and duration_ms

### Scenario: Failure ceiling exceeded
- GIVEN `consecutive_failures >= max_consecutive_failures`
- WHEN next operation fails
- THEN log Error event with phase, failure count, max, and error message

### SyncMetrics in-memory counters
```csharp
public sealed class SyncMetrics
{
    private long _totalPushed;
    private long _totalPulled;
    private int _totalFailures;
    private int _consecutiveFailures;
    private DateTime _lastSyncAt;
    private string? _lastError;
    private SyncPhase _phase;
}
```
Registered as singleton, updated by SyncManager per cycle, readable by status endpoint.

---

## REQ-OBS-03: CLI `engram sync status`

CLI MUST expose `sync status` consuming `GET /sync/status`. Flag `--json` outputs raw JSON.

### Scenario: Formatted output
- GIVEN server is running
- WHEN user runs `engram sync status`
- THEN display phase, cursor, health, counts, enrolled/paused as formatted table

### Scenario: JSON output
- GIVEN server is running
- WHEN user runs `engram sync status --json`
- THEN output raw JSON response

### Scenario: Server offline
- GIVEN no server running (connection refused)
- WHEN user runs `engram sync status`
- THEN display error: "No se pudo conectar al servidor — ¿está engram server corriendo?"
- AND exit with non-zero code

---

## REQ-OBS-04: Setup Documentation

`docs/SYNC-SETUP.md` MUST document setup.

### Required sections
1. **Prerequisites**: PostgreSQL on TrueNAS, .NET 8 runtime
2. **Environment variables**: table of all `ENGRAM_SYNC_*` vars with defaults
3. **Step-by-step**: PostgreSQL → env vars → start server → enroll projects → verify with `sync status`
4. **Architecture**: `local SQLite ↔ SyncManager ↔ Transport ↔ Cloud Server ↔ PostgreSQL`
5. **Troubleshooting**: sync won't start, 409 pause, failure ceiling, deferred mutations

---

## Files Changed

| File | Change |
|------|--------|
| `src/Engram.Sync/SyncMetrics.cs` | New |
| `src/Engram.Sync/ISyncStatusProvider.cs` | New |
| `src/Engram.Sync/SyncManager.cs` | Add SyncMetrics + LoggerMessage |
| `src/Engram.Server/CloudSyncEndpoints.cs` | Add `GET /sync/status` |
| `src/Engram.Server/EngramServer.cs` | Replace stub |
| `src/Engram.Cli/Program.cs` | `sync status` + `--json` |
| `docs/SYNC-SETUP.md` | New |
