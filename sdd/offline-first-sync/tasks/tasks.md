# Tasks: Offline-First Sync

**Change**: `offline-first-sync`
**Version**: 1.0.1
**Status**: Draft
**Design**: `sdd/offline-first-sync/design` (7 ADs, 12 new files)
**RFC**: [RFC-003: Architecture — Chunk + Mutation Hybrid](../../../docs/rfcs/RFC-003-offline-first-sync-architecture.md)

---

## Phase 1: Mutation Journal + Server Endpoints (10–14h)

### Infrastructure

- [ ] 1.1.1 Add `Microsoft.Extensions.Hosting.Abstractions` to `Engram.Sync.csproj`
- [ ] 1.1.2 Create `ICloudMutationStore.cs` in `src/Engram.Store/` (interface with 4 methods: InsertMutationBatchAsync, ListMutationsSinceAsync, IsProjectSyncEnabledAsync, InsertAuditEntryAsync)
- [ ] 1.1.3 Add `cloud_mutations`, `cloud_sync_audit_log`, `cloud_project_controls`, `cloud_chunks` tables to `PostgresStore.cs` as additive migrations
- [ ] 1.1.4 Implement `ICloudMutationStore` in `PostgresStore.cs`
- [ ] 1.1.5 Create `ICloudChunkStore.cs` in `src/Engram.Store/` (interface — schema only, Phase 1)
- [ ] 1.1.6 Implement `ICloudChunkStore` schema methods (table creation, existence check) in `PostgresStore.cs` (full chunk protocol deferred to Phase 2+)

### MutationTransport

- [ ] 1.2.1 Create `src/Engram.Sync/Transport/MutationDtos.cs` (PushRequest, PushResponse, PullResponse records matching Go API contract)
- [ ] 1.2.2 Create `src/Engram.Sync/Transport/IMutationTransport.cs` (interface: PushMutationsAsync, PullMutationsAsync)
- [ ] 1.2.3 Create `src/Engram.Sync/Transport/MutationTransport.cs` (HTTP impl via IHttpClientFactory, retry on HttpRequestException, 409 → PauseError)
- [ ] 1.2.4 Create `src/Engram.Sync/Transport/MutationTransportException.cs`

### Server Endpoints

- [ ] 1.3.1 Create `src/Engram.Server/CloudSyncEndpoints.cs` with `HandleMutationPushAsync` (pause gate, batch auth, relation validation, audit log, 8 MiB limit, empty batch rejection)
- [ ] 1.3.2 Create `HandleMutationPullAsync` in `CloudSyncEndpoints.cs` (enrollment filter, cursor pagination, project envelope)
- [ ] 1.3.3 Register routes in `EngramServer.cs` via `MapCloudSyncRoutes()` extension
- [ ] 1.3.4 Add auth middleware (deferred: simple header vs JWT — decision during implementation)

### Local Store Additions

- [ ] 1.4.1 Add `sync_apply_deferred` table to `SqliteStore.cs` as additive migration
- [ ] 1.4.2 Create `src/Engram.Sync/ILocalSyncStore.cs` (interface with 10 sync-specific methods)
- [ ] 1.4.3 Implement `ILocalSyncStore` methods in `SqliteStore.cs` (ListPendingSyncMutations, AckSeqs, AcquireLease, ReleaseLease, ApplyPulledMutation, ReplayDeferred, CountPendingNonEnrolled, MarkSync*)

### Tests

- [ ] 1.5.1 Unit tests: MutationTransport handles 200, 409, 4xx correctly
- [ ] 1.5.2 Unit tests: CloudSyncEndpoints push validation (empty batch, 6-field validation, pause gate)
- [ ] 1.5.3 Unit tests: CloudSyncEndpoints pull cursor + enrollment filter
- [ ] 1.5.4 Integration tests: push → cloud_mutations → pull roundtrip (if test server available)

---

## Phase 2: Autosync Manager (12–16h)

### SyncManager Core

- [ ] 2.1.1 Create `src/Engram.Sync/SyncPhase.cs` (enum: Idle, Pushing, Pulling, PushFailed, PullFailed, Backoff, Healthy, Disabled)
- [ ] 2.1.2 Create `src/Engram.Sync/SyncManagerConfig.cs` (config record with all fields from design §AD-5)
- [ ] 2.1.3 Create `src/Engram.Sync/SyncManager.cs` as `BackgroundService` with debounce + poll channels
- [ ] 2.1.4 Implement `cycle()`: lease → push → check non-enrolled → replay deferred → pull → mark healthy
- [ ] 2.1.5 Implement failure ceiling (MaxConsecutiveFailures=10) + panic recovery (try/catch with stack trace)

### Push Cycle

- [ ] 2.2.1 Group pending mutations by project, drain batch (PushBatchSize=100)
- [ ] 2.2.2 Call `transport.PushMutationsAsync`, handle `PauseError` → mark blocked
- [ ] 2.2.3 Call `AckSyncMutationSeqsAsync` on successful push

### Pull Cycle

- [ ] 2.3.1 Cursor loop: `transport.PullMutationsAsync` → `ApplyPulledMutationAsync` → advance sinceSeq
- [ ] 2.3.2 Handle `has_more = true` (continue until all fetched)
- [ ] 2.3.3 Implement `CountPendingNonEnrolledAsync` → `MarkSyncBlockedAsync` if non-enrolled pending mutations exist

### Deferred Replay

- [ ] 2.4.1 Implement `ReplayDeferredAsync`: select rows with retry_count < 5, re-attempt, on success DELETE, on failure increment retry_count
- [ ] 2.4.2 Log "dead" rows (retry_count >= 5) but don't retry

### DI Registration

- [ ] 2.5.1 Register `SyncManager` as hosted service in DI container (AddHostedService)
- [ ] 2.5.2 Add `ENGRAM_SYNC_ENABLED` feature flag support (skip startup if false)

### Tests

- [ ] 2.6.1 Unit tests: SyncManager phase transitions (Idle → Pushing → Healthy, PushFailed → Backoff)
- [ ] 2.6.2 Unit tests: failure ceiling reached → Disabled phase
- [ ] 2.6.3 Unit tests: deferred replay retries, dead rows logged
- [ ] 2.6.4 Unit tests: non-enrolled blocking detected before push

---

## Phase 3: Enrollment + Conflict Handling (6–8h)

- [ ] 3.1 Add `/sync/enroll` endpoint (add project to `sync_enrolled_projects`)
- [ ] 3.2 Add `/sync/pause` endpoint (set `cloud_project_controls.sync_enabled = false`)
- [ ] 3.3 Implement `EnrolledProjectsProvider` interface → scope pull results to enrolled projects
- [ ] 3.4 Implement relation FK deferral on apply (write to `sync_apply_deferred`, do NOT throw)
- [ ] 3.5 Tests: enrollment flow end-to-end, FK deferral on missing relation

---

## Phase 4: Observability (4–6h)

- [ ] 4.1 Add `/sync/status` endpoint (cursor position, phase, last sync time)
- [ ] 4.2 Add SyncManager metrics (push/pull count, errors, phase) — expose via diagnostic endpoint or ILogger
- [ ] 4.3 Add CLI: `engram sync status` command
- [ ] 4.4 Update docs: sync setup, architecture diagram, troubleshooting guide

---

## Implementation Order

```
Phase 1 (foundation-first):
  1.1 → 1.4 → 1.2 → 1.3 → 1.5
  (store infra before transport before endpoints)

Phase 2 (manager wiring):
  2.1 → 2.2 → 2.3 → 2.4 → 2.5 → 2.6

Phase 3:
  3.1 → 3.2 → 3.3 → 3.4 → 3.5

Phase 4:
  4.1 → 4.2 → 4.3 → 4.4
```

---

## Total: 45 tasks across 4 phases

### Task Count by Phase

| Phase | Tasks | Focus |
|-------|-------|-------|
| Phase 1 | 25 | Infrastructure + Transport + Endpoints + Local Store + Tests |
| Phase 2 | 14 | SyncManager Core + Cycles + Deferred + DI + Tests |
| Phase 3 | 5 | Enrollment + Conflicts |
| Phase 4 | 4 | Observability |
| **Total** | **45** | **32–44h estimated** |
