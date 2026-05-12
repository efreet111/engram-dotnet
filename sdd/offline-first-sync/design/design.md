# Design: Offline-First Sync

**Change**: `offline-first-sync`
**Version**: 1.0.0
**Phase**: Design
**Status**: Draft

---

## 1. Architecture Overview

```
┌─────────────────────────────────────────────────────┐
│  Engram.Sync                                        │
│  ┌──────────────────┐  ┌─────────────────────────┐  │
│  │ SyncManager       │  │ IMutationTransport      │  │
│  │ (IHostedService)  │──│ PushMutationsAsync()    │  │
│  │ debounce+push+pull│  │ PullMutationsAsync()    │──┼──► Cloud Server
│  └────────┬─────────┘  │ Handle409Async()         │  │     POST /sync/mutations/push
│           │            └─────────────────────────┘  │     GET  /sync/mutations/pull
│           │ owns                                       │
│           ▼                                           │
│  ┌──────────────────┐                                 │
│  │ SyncManagerConfig│                                 │
│  └──────────────────┘                                 │
└──────────────────────┬────────────────────────────────┘
                       │ calls
                       ▼
┌─────────────────────────────────────────────────────┐
│  Engram.Store / SqliteStore                         │
│  ┌──────────────────────────────────────────────┐   │
│  │ ListPendingSyncMutations()                   │   │
│  │ AckSyncMutationSeqs()                        │   │
│  │ AcquireSyncLease()                           │   │
│  │ ApplyPulledMutation() → sync_apply_deferred  │   │
│  │ ReplayDeferred()                             │   │
│  │ CountPendingNonEnrolled() + MarkSync*()      │   │
│  └──────────────────────────────────────────────┘   │
└──────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────┐
│  Engram.Server                           Cloud only │
│  POST /sync/mutations/push                          │
│  GET  /sync/mutations/pull                          │
│  ┌──────────────────────────────────────────────┐   │
│  │ ICloudMutationStore (PG, new tables)         │   │
│  │ cloud_mutations | cloud_sync_audit_log       │   │
│  │ cloud_project_controls                       │   │
│  └──────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────┘
```

---

## 2. Architecture Decisions

### AD-1: IMutationTransport location

| | Choice | Alternatives | Rationale |
|--|--------|-------------|-----------|
| **A** | `src/Engram.Sync/Transport/IMutationTransport.cs` | Put it in Engram.Store | Engram.Sync already has EngramSync — the transport is a sync-layer concern, not a storage concern |

**File**: `src/Engram.Sync/Transport/IMutationTransport.cs` (new)
**File**: `src/Engram.Sync/Transport/MutationTransport.cs` (new, HTTP impl)
**File**: `src/Engram.Sync/Transport/MutationTransportException.cs` (new)

```csharp
// IMutationTransport.cs
public interface IMutationTransport
{
    Task<PushResult> PushMutationsAsync(
        IReadOnlyList<MutationEntry> entries,
        CancellationToken ct = default);

    Task<PullResult> PullMutationsAsync(
        long sinceSeq,
        int limit = 100,
        CancellationToken ct = default);
}

public sealed record MutationEntry(
    string Project,
    string Entity,
    string EntityKey,
    string Op,
    string Payload);

public sealed record PushResult(
    IReadOnlyList<long> AcceptedSeqs,
    string Project,
    string? PauseError = null); // non-null → 409

public sealed record PullResult(
    IReadOnlyList<PulledMutation> Mutations,
    bool HasMore,
    long LatestSeq,
    string Project);

public sealed record PulledMutation(
    long Seq, string Entity, string EntityKey,
    string Op, string Payload, string OccurredAt);
```

---

### AD-2: MutationTransport HTTP impl

| | Choice | Alternatives | Rationale |
|--|--------|-------------|-----------|
| **B** | `HttpClient` via `IHttpClientFactory` in constructor | Raw `new HttpClient()` | DI-friendly, connection pooling, testable via `HttpMessageHandler` mock |

**Push**: POST `{baseUrl}/sync/mutations/push` with body `{"entries": [...]}`, header `Authorization: Bearer {token}`. 200 → parse `accepted_seqs`. 409 → detect `error_code == "sync-paused"`, return `PushResult` with `PauseError` set. Other non-200 → throw `MutationTransportException(StatusCode, ErrorCode, Body)`.

**Pull**: GET `{baseUrl}/sync/mutations/pull?since_seq={N}&limit={M}`. Parse `mutations[]`, `has_more`, `latest_seq`. Non-200 → throw as above.

**Retry**: Only on `HttpRequestException` (transient), not on 4xx. Exponential backoff: 1s, 2s, 4s, max 3 retries. 409 is NOT retried — paused state must be handled by SyncManager.

---

### AD-3: Server endpoints

| | Choice | Alternatives | Rationale |
|--|--------|-------------|-----------|
| **C** | New `Engram.Server/CloudSyncEndpoints.cs` static class | Cram into EngramServer.cs | EngramServer.cs is 466 lines. Separate file = testable, swappable, matches Go cloudserver/mutations.go pattern |

**File**: `src/Engram.Server/CloudSyncEndpoints.cs` (new)
**File**: `src/Engram.Server/Dtos/MutationDtos.cs` (new)

Routes registered via `app.MapCloudSyncEndpoints(store)` extension on `IEndpointRouteBuilder`:

| Route | Method | Handler | Key Logic |
|-------|--------|---------|-----------|
| `/sync/mutations/push` | POST | `HandleMutationPushAsync` | 8 MiB body limit, max 100 entries, empty batch → 400, per-project auth, pause gate (409 + audit), relation validation (6 fields), `InsertMutationBatchAsync` → seqs, project envelope in response |
| `/sync/mutations/pull` | GET | `HandleMutationPullAsync` | `since_seq` (default 0), `limit` (default 100, max 100), enrollment scope → `allowedProjects`, `ListMutationsSinceAsync`, `has_more` + `latest_seq` cursor, project envelope |

Each handler receives `ICloudMutationStore` via `HttpContext.RequestServices` (the PostgresStore registered as singleton already).

---

### AD-4: ICloudMutationStore (server-side)

| | Choice | Alternatives | Rationale |
|--|--------|-------------|-----------|
| **D** | New interface in `Engram.Store`, implemented by `PostgresStore` | Separate CloudStore class | PostgresStore already has Npgsql open. Adding cloud tables via additive migrations avoids a second connection pool. Type‑assertion‑safe: handler checks `store is ICloudMutationStore` |

**File**: `src/Engram.Store/ICloudMutationStore.cs` (new)
**File**: `src/Engram.Store/PostgresStore.cs` (modified — add migrations + interface impl)

```csharp
public interface ICloudMutationStore
{
    Task<List<long>> InsertMutationBatchAsync(
        IReadOnlyList<MutationEntry> entries,
        string? createdBy = null);
    Task<(List<StoredMutation> Mutations, bool HasMore, long LatestSeq)>
        ListMutationsSinceAsync(long sinceSeq, int limit, List<string>? allowedProjects);
    Task<bool> IsProjectSyncEnabledAsync(string project);
    Task InsertAuditEntryAsync(AuditEntry entry);
}
```

New PG tables (additive migrations — `AddCloudMutationMigrations()` called from `PostgresStore.Migrate()` if a `cloud_migrations_applied` flag is not set):

| Table | Columns | Indexes |
|-------|---------|---------|
| `cloud_mutations` | `seq BIGSERIAL PK`, `project TEXT NOT NULL`, `entity TEXT NOT NULL`, `entity_key TEXT NOT NULL`, `op TEXT NOT NULL`, `payload JSONB NOT NULL`, `created_by TEXT DEFAULT ''`, `occurred_at TIMESTAMPTZ DEFAULT NOW()` | `idx_cm_project_seq ON cloud_mutations(project, seq)` |
| `cloud_sync_audit_log` | `id BIGSERIAL PK`, `project TEXT NOT NULL`, `action TEXT NOT NULL`, `outcome TEXT NOT NULL`, `contributor TEXT DEFAULT ''`, `entry_count INT DEFAULT 0`, `reason_code TEXT DEFAULT ''`, `created_at TIMESTAMPTZ DEFAULT NOW()` | `idx_csal_project ON cloud_sync_audit_log(project, created_at)` |
| `cloud_project_controls` | `project TEXT PK`, `sync_enabled BOOL DEFAULT true`, `pause_reason TEXT DEFAULT ''`, `updated_at TIMESTAMPTZ DEFAULT NOW()` | PK only |

---

### AD-5: SyncManager (background loop)

| | Choice | Alternatives | Rationale |
|--|--------|-------------|-----------|
| **E** | `IHostedService` in `Engram.Sync` | Separate project | Matches .NET ecosystem pattern. SyncManager reads mutations from SqliteStore via a new `ILocalSyncStore` interface subset. Deployed via `services.AddHostedService<SyncManager>()` |

**File**: `src/Engram.Sync/SyncManager.cs` (new)
**File**: `src/Engram.Sync/ILocalSyncStore.cs` (new)
**File**: `src/Engram.Sync/SyncManagerConfig.cs` (new)
**File**: `src/Engram.Sync/SyncPhase.cs` (new — enum)

```csharp
public enum SyncPhase { Idle, Pushing, Pulling, PushFailed, PullFailed, Backoff, Healthy, Disabled }

public sealed record SyncManagerConfig
{
    public string TargetKey { get; init; } = "cloud";
    public string LeaseOwner { get; init; } = Environment.MachineName;
    public TimeSpan DebounceDuration { get; init; } = TimeSpan.FromMilliseconds(500);
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(30);
    public int PushBatchSize { get; init; } = 100;
    public int PullBatchSize { get; init; } = 100;
    public int MaxConsecutiveFailures { get; init; } = 10;
    public TimeSpan BaseBackoff { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan MaxBackoff { get; init; } = TimeSpan.FromMinutes(5);
}
```

**Loop** (in `ExecuteAsync`):
```
while (!stoppingToken.IsCancellationRequested)
  select { case dirtyCh: debounce; case debounce.C: cycle; case poll.C: cycle }
```

**`cycle()`**:
1. Check disabled / failure ceiling / backoff → return if skipped
2. `AcquireSyncLeaseAsync` → return if not acquired
3. `PushAsync()` → group by project, drain batch, transport.PushMutationsAsync, AckSyncMutationSeqsAsync
4. If no pending mutations → `CountPendingNonEnrolledAsync` → if >0 → `MarkSyncBlockedAsync`
5. `ReplayDeferredAsync()` (non-fatal)
6. `PullAsync()` → cursor loop: transport.PullMutationsAsync → ApplyPulledMutationAsync → advance sinceSeq
7. `MarkSyncHealthyAsync`
8. On any error → `recordFailure()` (phase tracking, backoff)

---

### AD-6: ILocalSyncStore (client-side store interface)

| | Choice | Alternatives | Rationale |
|--|--------|-------------|-----------|
| **F** | Interface subset in `Engram.Sync`, implemented by `SqliteStore` | Add to IStore | SyncManager needs ~10 sync-specific methods. Adding them to IStore pollutes all 3 store implementations. Interface segregation |

**File**: `src/Engram.Sync/ILocalSyncStore.cs` (new)
**File**: `src/Engram.Store/SqliteStore.cs` (modified — implement ILocalSyncStore methods)

```csharp
public interface ILocalSyncStore
{
    Task<SyncState?> GetSyncStateAsync(string targetKey);
    Task<List<SyncMutation>> ListPendingSyncMutationsAsync(string targetKey, int limit);
    Task<List<PendingProjectCount>> CountPendingNonEnrolledAsync(string targetKey);
    Task AckSyncMutationSeqsAsync(string targetKey, IReadOnlyList<long> seqs);
    Task<bool> AcquireSyncLeaseAsync(string targetKey, string owner, TimeSpan ttl);
    Task ReleaseSyncLeaseAsync(string targetKey, string owner);
    Task ApplyPulledMutationAsync(string targetKey, SyncMutation mutation);
    Task<ReplayDeferredResult> ReplayDeferredAsync();
    Task MarkSyncFailureAsync(string targetKey, string message, DateTime backoffUntil);
    Task MarkSyncBlockedAsync(string targetKey, string reasonCode, string message);
    Task MarkSyncHealthyAsync(string targetKey);
}
```

New local SQLite table (additive migration in `SqliteStore.Migrate()`):

| Table | Columns | Notes |
|-------|---------|-------|
| `sync_apply_deferred` | `id INTEGER PK`, `entity TEXT`, `entity_key TEXT`, `op TEXT`, `payload TEXT`, `source TEXT`, `pulled_at TEXT`, `retry_count INT DEFAULT 0`, `last_error TEXT` | Relation FK misses land here. `ReplayDeferredAsync` retries on retry. |

---

### AD-7: Conflict Resolution — Relation FK Deferral

| | Choice | Alternatives | Rationale |
|--|--------|-------------|-----------|
| **G** | Write FK misses to `sync_apply_deferred`, DO NOT halt cursor | Halt pull on FK miss | The cursor MUST advance or pull never converges. Deferred rows are replayed before the next pull cycle |

**ApplyPulledMutation logic**:
1. Session upsert → `INSERT OR IGNORE INTO sessions` (idempotent)
2. Observation upsert → `INSERT INTO observations` by `sync_id`, if session FK fails → write to `sync_apply_deferred` (NOT an exception)
3. Prompt upsert → same pattern
4. Delete ops → soft-delete by sync_id
5. Relation entities → attempt INSERT, FK fail → defer

**ReplayDeferredAsync**: SELECT all rows with `retry_count < 5`, re-attempt apply, on success DELETE row, on failure increment `retry_count`. Rows at 5 are "dead" and logged.

---

## 3. Project Structure Changes

| File | Action | Purpose |
|------|--------|---------|
| `src/Engram.Sync/Transport/IMutationTransport.cs` | **NEW** | Transport interface |
| `src/Engram.Sync/Transport/MutationTransport.cs` | **NEW** | HTTP implementation |
| `src/Engram.Sync/Transport/MutationTransportException.cs` | **NEW** | Error type with status code |
| `src/Engram.Sync/SyncManager.cs` | **NEW** | `BackgroundService` loop |
| `src/Engram.Sync/ILocalSyncStore.cs` | **NEW** | Store interface subset |
| `src/Engram.Sync/SyncManagerConfig.cs` | **NEW** | Config record |
| `src/Engram.Sync/SyncPhase.cs` | **NEW** | Phase enum |
| `src/Engram.Store/ICloudMutationStore.cs` | **NEW** | Server-side cloud store interface |
| `src/Engram.Store/PostgresStore.cs` | **MODIFY** | Add cloud tables + implement `ICloudMutationStore` |
| `src/Engram.Store/SqliteStore.cs` | **MODIFY** | Add `sync_apply_deferred` + implement `ILocalSyncStore` |
| `src/Engram.Server/CloudSyncEndpoints.cs` | **NEW** | Push/pull endpoint handlers |
| `src/Engram.Server/Dtos/MutationDtos.cs` | **NEW** | Request/response DTOs |
| `src/Engram.Server/EngramServer.cs` | **MODIFY** | Add `MapCloudSyncRoutes()` call |

## 4. Open Questions

1. **Auth**: Go cloudserver uses bearer JWT. .NET EngramServer currently uses `X-Engram-User` header. Should cloud endpoints use JWT middleware (`AddAuthentication().AddJwtBearer()`) or keep the simple header? — Proposal says Phase 1 task 1.5, deferred to implementation.
2. **Engram.Sync.csproj**: Needs `Microsoft.Extensions.Hosting.Abstractions` package reference for `BackgroundService`. Must add to project file.
3. **Lease owner**: Go uses `fmt.Sprintf("autosync-%d", time.Now().UnixNano())`. For .NET, `Environment.MachineName` + process ID is sufficient.

## 5. Rollback

- Phase 1 (server): remove route registrations, no data loss
- Phase 2 (sync manager): remove `AddHostedService<SyncManager>()`, or set `ENGRAM_SYNC_ENABLED=false`
- `sync_apply_deferred` rows: safe to `DELETE` — only unapplied relations
- `cloud_mutations`: append-only, no destructive rollback needed

## 6. Next Steps

Implement Phase 1 tasks:
1.1 Cloud tables (PG migrations in PostgresStore)
1.2 `POST /sync/mutations/push` handler
1.3 `GET /sync/mutations/pull` handler
1.4 `MutationTransport` HTTP client
1.5 Auth middleware
1.6 Tests
