# Offline-First Sync — Feature Index

> **Feature Status**: ✅ Phase 1 & 2 Complete | 🔴 Phase 3 & 4 Pending
> **Last Updated**: 2026-05-17
> **Branch**: Merged to `main` (commit `7e2c900`)
> **PR**: #14 (merged)
> **Issue**: [#13](https://github.com/efreet111/engram-dotnet/issues/13)

---

## Overview

Bidirectional mutation-based sync between local SQLite and a cloud PostgreSQL server.
When online, server is source of truth. When offline, local is source of truth.
Last-write-wins conflict resolution.

**Infrastructure**: TrueNAS PostgreSQL at `192.168.0.178:7437`

**Total Effort**: 32–44h across 4 phases.

**Current Progress**: Phase 1 & 2 complete (~22-30h). Phase 3 & 4 pending (~10-14h).

---

## Implementation Status

| Phase | Status | Completion | Tests |
|-------|--------|------------|-------|
| **Phase 1**: Mutation Journal + Server Endpoints | ✅ Complete | 100% | 26 tests |
| **Phase 2**: Autosync Manager | ✅ Complete | 100% | Included |
| **Phase 3**: Enrollment + Conflict Handling | 🔴 Pending | 0% | - |
| **Phase 4**: Observability | 🔴 Pending | 0% | - |

---

## SDD Artifacts

| Artifact | File | Version | Description |
|----------|------|---------|-------------|
| **Proposal** | [`sdd/offline-first-sync/propose/proposal.md`](sdd/offline-first-sync/propose/proposal.md) | v0.2.0 | Intent, API contracts, data model, phases, rollback |
| **Design** | [`sdd/offline-first-sync/design/design.md`](sdd/offline-first-sync/design/design.md) | v1.0.0 | 7 architecture decisions, data flow, interfaces, 11 new files |
| **Tasks** | [`sdd/offline-first-sync/tasks/tasks.md`](sdd/offline-first-sync/tasks/tasks.md) | v1.0.0 | 43 tasks organized by phase with dependencies |

**Note**: SDD artifacts archived in `/sdd/archive/2026-05-14-offline-first-sync/` (empty, needs restoration from FlowForge)

---

## Phases

### Phase 1: Mutation Journal + Server Endpoints — ✅ COMPLETE (10–14h)

Implements the MVP push/pull server endpoints and HTTP client.

| Task Range | Focus | Key Deliverables | Status |
|------------|-------|------------------|--------|
| 1.1.1 – 1.1.4 | Infrastructure | `ICloudMutationStore` + `ICloudChunkStore` interfaces + `cloud_mutations`, `cloud_sync_audit_log`, `cloud_project_controls` tables | ✅ |
| 1.2.1 – 1.2.4 | MutationTransport | `IMutationTransport` HTTP client via `IHttpClientFactory`, 409 pause gate, retry on transient errors | ✅ |
| 1.3.1 – 1.3.4 | Server Endpoints | `POST /sync/mutations/push` (pause gate, batch auth, relation validation) + `GET /sync/mutations/pull` (enrollment filter, cursor pagination) | ✅ |
| 1.4.1 – 1.4.3 | Local Store | `sync_apply_deferred` table + `ILocalSyncStore` interface + SqliteStore implementation | ✅ |
| 1.5.1 – 1.5.4 | Tests | Unit + integration tests for transport, endpoints, store | ✅ 26 tests |

**Implemented Files**:
- `src/Engram.Store/ICloudMutationStore.cs`
- `src/Engram.Store/ICloudChunkStore.cs`
- `src/Engram.Store/ILocalSyncStore.cs`
- `src/Engram.Store/PostgresStore.cs` (ICloudMutationStore + ICloudChunkStore implementation)
- `src/Engram.Store/SqliteStore.cs` (ILocalSyncStore implementation)
- `src/Engram.Server/CloudSyncEndpoints.cs`
- `src/Engram.Sync/Transport/IMutationTransport.cs`
- `src/Engram.Sync/Transport/MutationTransport.cs`
- `src/Engram.Sync/Transport/MutationDtos.cs`
- `src/Engram.Sync/Transport/MutationTransportException.cs`
- `tests/Engram.Server.Tests/CloudSyncEndpointsTests.cs`
- `tests/Engram.Server.Tests/CloudSyncIntegrationTests.cs`
- `tests/Engram.Sync.Tests/MutationTransportTests.cs`

**Known Gaps**:
- ⚠️ `ReplayDeferredAsync` returns stub `(0, 0)` — not implemented
- ⚠️ Enrollment filter is hardcoded, no `enrolled_projects` table check

---

### Phase 2: Autosync Manager — ✅ COMPLETE (12–16h)

Background service that orchestrates push + pull cycles with debounce, backoff, and failure ceiling.

| Task Range | Focus | Key Deliverables | Status |
|------------|-------|------------------|--------|
| 2.1.1 – 2.1.5 | SyncManager Core | `BackgroundService` with debounce/poll channels, phase tracking (Idle/Pushing/Pulling/etc), panic recovery | ✅ |
| 2.2.1 – 2.2.3 | Push Cycle | Group by project, drain batch, handle pause error, ack seqs | ✅ |
| 2.3.1 – 2.3.3 | Pull Cycle | Cursor loop with `has_more`, apply mutations, non-enrolled blocking | ✅ |
| 2.4.1 – 2.4.2 | Deferred Replay | `ReplayDeferredAsync` with retry_count < 5, dead row logging | ⚠️ Stub only |
| 2.5.1 – 2.5.2 | DI Registration | `AddHostedService<SyncManager>()` + `ENGRAM_SYNC_ENABLED` feature flag | ✅ |
| 2.6.1 – 2.6.4 | Tests | Phase transitions, failure ceiling, deferred replay, non-enrolled blocking | ✅ |

**Implemented Files**:
- `src/Engram.Sync/SyncManager.cs`
- `src/Engram.Sync/SyncManagerConfig.cs`
- `src/Engram.Sync/SyncPhase.cs`
- `tests/Engram.Sync.Tests/SyncManagerTests.cs`

**Known Gaps**:
- ⚠️ `ReplayDeferredAsync` not implemented (Phase 1.4 gap)

---

### Phase 3: Enrollment + Conflict Handling — 🔴 PENDING (6–8h)

Enrollment management and relation FK deferral strategy.

**Specification**: [`docs/PHASE3-ENROLLMENT-SPEC.md`](PHASE3-ENROLLMENT-SPEC.md) — Full API contracts, implementation details, and testing strategy.

**Missing Endpoints**:
- ❌ `/sync/enroll` — Add project to enrollment list (POST, DELETE, GET)
- ❌ `/sync/pause` — Admin pause with reason (POST to pause, DELETE to resume)
- ❌ `EnrolledProjectsProvider` for pull scoping
- ❌ `ReplayDeferredAsync` implementation — FK deferral → `sync_apply_deferred`

**Tables** (already exist, need endpoints):
- ✅ `cloud_project_controls` (sync_enabled flag)
- ✅ `sync_enrolled_projects` (enrollment list)
- ✅ `sync_apply_deferred` (FK misses)

**To Start Phase 3**:
1. Implement `/sync/enroll` endpoint (POST, DELETE, GET) — Task 3.1 (2h)
2. Implement `/sync/pause` endpoint (POST, DELETE) — Task 3.2 (1.5h)
3. Implement `EnrolledProjectsProvider` service — Task 3.1
4. Update `CloudSyncEndpoints.Pull` to use enrolled projects table — Task 3.3 (1h)
5. Implement `ReplayDeferredAsync` in SqliteStore — Task 3.4 (1.5h)
6. Add tests for enrollment, pause, and deferred replay — All tasks

**Estimated Effort**: 6h total (see PHASE3-ENROLLMENT-SPEC.md for detailed breakdown)

---

### Phase 4: Observability — 🔴 PENDING (4–6h)

Monitoring, CLI, and docs.

**Missing**:
- ❌ `/sync/status` endpoint — cursor position, last sync, health
- ❌ `engram sync status` CLI command — full implementation
- ❌ SyncManager metrics via `ILogger` — push/pull counts, errors, phase
- ❌ Sync setup documentation

**Existing** (partial):
- ⚠️ `--status` option in CLI (line 294 of Program.cs) — may not be fully implemented

**Specification**: Phase 4 spec TBD after Phase 3 completion.

---

## API Contracts

> **Source of truth**: Go engram upstream (`internal/cloud/cloudserver/mutations.go`)

### Push

```
POST /sync/mutations/push
Headers: Authorization: Bearer <token>
Body: {
  "entries": [
    { "project": "team/mi-proyecto", "entity": "observation", "entity_key": "sync_abc123",
      "op": "upsert", "payload": "{...}" }
  ],
  "created_by": "optional-user"
}
Response 200: {
  "accepted_seqs": [1, 2, 3],
  "project": "team/mi-proyecto",
  "project_source": "request_body",
  "project_path": ""
}
Response 409: {
  "error_class": "policy",
  "error_code": "sync-paused",
  "error": "...",
  "project": "..."
}
```

### Pull

```
GET /sync/mutations/pull?since_seq=0&project=team/mi-proyecto&limit=100
Response 200: {
  "mutations": [
    { "seq": 1, "project": "...", "entity": "observation", "entity_key": "sync_abc123",
      "op": "upsert", "payload": "{...}", "occurred_at": "2026-05-12T..." }
  ],
  "has_more": false,
  "latest_seq": 3,
  "project": "team/mi-proyecto",
  "project_source": "request_body",
  "project_path": ""
}
```

---

## Architecture Decisions Summary

| AD | Title | Choice |
|----|-------|--------|
| AD-1 | Transport location | `Engram.Sync.Transport/IMutationTransport.cs` |
| AD-2 | HTTP impl | `IHttpClientFactory` + retry on `HttpRequestException` only (not 4xx) |
| AD-3 | Server endpoints | Separate `CloudSyncEndpoints.cs` (not crammed into EngramServer.cs) |
| AD-4 | Cloud store | `ICloudMutationStore` in `Engram.Store`, implemented by `PostgresStore` |
| AD-5 | SyncManager | `BackgroundService` in `Engram.Sync` (not separate project) |
| AD-6 | Local sync interface | `ILocalSyncStore` in `Engram.Sync` (not added to `IStore`) |
| AD-7 | FK deferral | Write to `sync_apply_deferred`, cursor NEVER halts on FK miss |

Full rationale in [`design.md`](sdd/offline-first-sync/design/design.md) §2.

---

## Data Model

### Server-Side (PostgreSQL — PostgresStore)

| Table | Purpose |
|-------|---------|
| `cloud_mutations` | Append-only mutation journal (seq, project, entity, entity_key, op, payload, created_by, occurred_at) |
| `cloud_sync_audit_log` | Audit trail for push-rejection events |
| `cloud_project_controls` | Per-project `sync_enabled` flag + pause reason |

### Client-Side (SQLite — SqliteStore)

| Table | Purpose |
|-------|---------|
| `sync_mutations` | ✅ Already exists — pending mutation queue |
| `sync_chunks` | ✅ Already exists — idempotency |
| `sync_state` | ✅ Already exists — cursor + target state |
| `sync_enrolled_projects` | ✅ Already exists — enrollment list |
| `sync_apply_deferred` | **NEW** — FK misses from pulled mutations |

---

## Go Upstream Reference

| File | Lines | What it does |
|------|-------|--------------|
| `internal/sync/sync.go` | 1324 | MutationTransport + sync protocol + store interface |
| `internal/cloud/remote/transport.go` | 421 | HTTP transport impl |
| `internal/cloud/autosync/manager.go` | 703 | SyncManager background loop |
| `internal/cloud/cloudserver/mutations.go` | 349 | Server push/pull endpoints |

**Total**: ~2797 lines Go → ~2500 lines .NET estimated

---

## Implementation Checklist

When you're ready to start, run through this:

- [ ] Merge PR #14 to main
- [ ] Create branch `feat/offline-first-sync-phase1`
- [ ] Add `Microsoft.Extensions.Hosting.Abstractions` to `Engram.Sync.csproj`
- [ ] Read [`tasks.md`](sdd/offline-first-sync/tasks/tasks.md) §Phase 1 (tasks 1.1–1.5)
- [ ] Read [`design.md`](sdd/offline-first-sync/design/design.md) §AD-4 and §AD-6 first (infrastructure dependencies)
- [ ] Implement tasks 1.1.x (cloud tables) → 1.4.x (local store) → 1.2.x (transport) → 1.3.x (endpoints)
- [ ] Run tests after each task
- [ ] Open PR for Phase 1 code review

---

## Key Corrections from Original Proposal

The original proposal (v0.1) had several errors discovered during Go upstream analysis:

| Issue | Was (v0.1) | Now (v0.2) |
|-------|------------|------------|
| Push body field | `{"mutations": [...]}` | `{"entries": [...]}` |
| Pull response | `{"next_seq": N}` | `{"has_more": bool, "latest_seq": N}` |
| Mutation struct | `Entity`, `EntityId` | `Project`, `Entity`, `EntityKey` |
| Response envelope | No project | `project` + `project_source` + `project_path` |
| Phase 1 estimate | 6-8h | **10-14h** |
| Phase 2 estimate | 8-10h | **12-16h** |
| Phase 3 estimate | 4-6h | **6-8h** |
| Missing behaviors | None documented | Pause gate, batch auth, deferred replay, failure ceiling, non-enrolled blocking |
| Missing tables | None | `cloud_mutations`, `cloud_sync_audit_log`, `cloud_project_controls`, `sync_apply_deferred` |

---

## Related Documentation

- [`docs/ARCHITECTURE.md`](ARCHITECTURE.md) — System architecture, sync tables section (§sync)
- [`docs/TEAM-SETUP.md`](TEAM-SETUP.md) — Team setup with TrueNAS server
- [`docs/ROADMAP.md`](ROADMAP.md) — Roadmap with this feature as priority
- [`docs/POSTGRES-SETUP.md`](POSTGRES-SETUP.md) — PostgreSQL server setup on TrueNAS
- [`docs/PHASE3-ENROLLMENT-SPEC.md`](PHASE3-ENROLLMENT-SPEC.md) — **Phase 3 specification** (enrollment + pause endpoints)
- [`docs/RFC-003-offline-first-sync-architecture.md`](rfcs/RFC-003-offline-first-sync-architecture.md) — RFC with architecture decisions
