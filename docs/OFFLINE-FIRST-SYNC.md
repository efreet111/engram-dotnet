# Offline-First Sync — Feature Index

> **Feature Status**: ✅ ALL PHASES COMPLETE  
> **Last Updated**: 2026-05-20  
> **Commit**: Merged to `main`  
> **PR**: #14 (merged)  
> **Issue**: [#13](https://github.com/efreet111/engram-dotnet/issues/13)  
> **Total Effort**: ~35h across 4 phases  
> **Tests**: 84 tests (72 unit + 12 integration)

---

## Overview

Bidirectional mutation-based sync between local SQLite and a cloud PostgreSQL server.
When online, server is source of truth. When offline, local is source of truth.
Last-write-wins conflict resolution.

**Total Effort**: 32–44h across 4 phases.

---

## Implementation Status

| Phase | Status | Completion | Tests |
|-------|--------|------------|-------|
| **Phase 1**: Mutation Journal + Server Endpoints | ✅ Complete | 100% | 26 tests |
| **Phase 2**: Autosync Manager | ✅ Complete | 100% | Included |
| **Phase 3**: Enrollment + Conflict Handling | ✅ Complete | 100% | Included |
| **Phase 4**: Observability | ✅ Complete | 100% | Included |

---

## Phases

### Phase 1: Mutation Journal + Server Endpoints — ✅ COMPLETE (10–14h)

Implements the MVP push/pull server endpoints and HTTP client.

| Task Range | Focus | Key Deliverables | Status |
|------------|-------|------------------|--------|
| 1.1 – 1.4 | Infrastructure | `ICloudMutationStore` + `ICloudChunkStore` + `ILocalSyncStore` interfaces | ✅ |
| 1.2 – 1.4 | MutationTransport | `IMutationTransport` HTTP client via `IHttpClientFactory`, 409 pause gate | ✅ |
| 1.3 – 1.4 | Server Endpoints | `POST /sync/mutations/push` + `GET /sync/mutations/pull` | ✅ |
| 1.4 – 1.4 | Local Store | `sync_apply_deferred` table + `ILocalSyncStore` | ✅ |
| 1.5 – 1.5 | Tests | 26 unit + integration tests | ✅ |

**Files**: 11 files across `Engram.Store`, `Engram.Server`, `Engram.Sync`

### Phase 2: Autosync Manager — ✅ COMPLETE (12–16h)

Background service that orchestrates push + pull cycles.

| Task Range | Focus | Key Deliverables | Status |
|------------|-------|------------------|--------|
| 2.1 – 2.5 | SyncManager Core | `BackgroundService` with debounce/poll, phase tracking | ✅ |
| 2.2 – 2.3 | Push/Pull Cycle | Group by project, drain batch, cursor loop | ✅ |
| 2.4 – 2.4 | Deferred Replay | `ReplayDeferredAsync` with retry < 5 | ✅ Phase 3 |
| 2.5 – 2.5 | DI Registration | `AddHostedService<SyncManager>()` + feature flag | ✅ |

**Files**: `SyncManager.cs`, `SyncManagerConfig.cs`, `SyncPhase.cs`

### Phase 3: Enrollment + Conflict Handling — ✅ COMPLETE (6–8h)

Enrollment management and FK deferral strategy.

| Endpoint | Method | Status |
|----------|--------|--------|
| `/sync/enroll` | POST, DELETE, GET | ✅ |
| `/sync/pause` | POST, DELETE | ✅ |
| `/sync/mutations/pull` | GET (enrollment filter) | ✅ |

**Spec**: [`docs/PHASE3-ENROLLMENT-SPEC.md`](../docs/PHASE3-ENROLLMENT-SPEC.md) (archived in SDD)

### Phase 4: Observability — ✅ COMPLETE (3.5h)

Monitoring, CLI, and docs.

| Feature | Status | Description |
|---------|--------|-------------|
| `/sync/status` endpoint | ✅ | Cursor, health, counts, enrolled/paused |
| `SyncMetrics` | ✅ | Thread-safe counters via `Interlocked` |
| `ISyncStatusProvider` | ✅ | Phase, failures, backoff, metrics |
| `engram sync status` CLI | ✅ | Formatted + `--json` flag |
| LoggerMessage source-gen | ✅ | 8 events (CycleComplete, CycleFailed, etc.) |
| `docs/SYNC-SETUP.md` | ✅ | Full setup guide |

---

## Architecture Decisions

| AD | Title | Choice |
|----|-------|--------|
| AD-1 | Transport location | `Engram.Sync.Transport/IMutationTransport.cs` |
| AD-2 | HTTP implementation | `IHttpClientFactory` + retry on `HttpRequestException` |
| AD-3 | Server endpoints | Separate `CloudSyncEndpoints.cs` |
| AD-4 | Cloud store | `ICloudMutationStore` in `Engram.Store` |
| AD-5 | SyncManager | `BackgroundService` in `Engram.Sync` |
| AD-6 | Local sync interface | `ILocalSyncStore` (not added to `IStore`) |
| AD-7 | FK deferral | Write to `sync_apply_deferred`, cursor NEVER halts |

---

## Data Model

### Server-Side (PostgreSQL)

| Table | Purpose |
|-------|---------|
| `cloud_mutations` | Append-only mutation journal |
| `cloud_sync_audit_log` | Audit trail for push-rejection events |
| `cloud_project_controls` | Per-project `sync_enabled` flag |

### Client-Side (SQLite)

| Table | Purpose |
|-------|---------|
| `sync_mutations` | Pending mutation queue |
| `sync_state` | Cursor + target state |
| `sync_enrolled_projects` | Enrollment list |
| `sync_apply_deferred` | FK misses from pulled mutations |

---

## Testing

```bash
# Run sync tests (requires Docker for Postgres)
dotnet test --filter "FullyQualifiedName~Sync" --verbosity normal

# Exclude Postgres (Docker)
dotnet test --filter "Category!=RequiresDocker"
```

---

## Related Docs

- [`docs/SYNC-SETUP.md`](SYNC-SETUP.md) — Setup guide
- [`docs/API-REFERENCE.md`](API-REFERENCE.md) — REST endpoints
- [`docs/AGENT-PROTOCOL.md`](AGENT-PROTOCOL.md) — Agent protocol
- [`docs/MULTI-USER.md`](MULTI-USER.md) — User isolation
- [`docs/rfcs/RFC-003-offline-first-sync-architecture.md`](rfcs/RFC-003-offline-first-sync-architecture.md) — Technical RFC
