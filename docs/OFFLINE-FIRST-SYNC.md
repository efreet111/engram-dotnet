# Offline-First Sync — Feature Index

> **Feature Status**: Planned (SDD artifacts complete, PR #14 pending merge)
> **Last Updated**: 2026-05-12
> **Branch**: [`feat/offline-first-sync`](https://github.com/efreet111/engram-dotnet/tree/feat/offline-first-sync)
> **PR**: [#14](https://github.com/efreet111/engram-dotnet/pull/14)
> **Issue**: [#13](https://github.com/efreet111/engram-dotnet/issues/13)

---

## Overview

Bidirectional mutation-based sync between local SQLite and a cloud PostgreSQL server.
When online, server is source of truth. When offline, local is source of truth.
Last-write-wins conflict resolution.

**Infrastructure**: TrueNAS PostgreSQL at `192.168.0.178:7437`

**Total Effort**: 32–44h across 4 phases.

---

## SDD Artifacts

| Artifact | File | Version | Description |
|----------|------|---------|-------------|
| **Proposal** | [`sdd/offline-first-sync/propose/proposal.md`](sdd/offline-first-sync/propose/proposal.md) | v0.2.0 | Intent, API contracts, data model, phases, rollback |
| **Design** | [`sdd/offline-first-sync/design/design.md`](sdd/offline-first-sync/design/design.md) | v1.0.0 | 7 architecture decisions, data flow, interfaces, 11 new files |
| **Tasks** | [`sdd/offline-first-sync/tasks/tasks.md`](sdd/offline-first-sync/tasks/tasks.md) | v1.0.0 | 43 tasks organized by phase with dependencies |

---

## Phases

### Phase 1: Mutation Journal + Server Endpoints — 10–14h

Implements the MVP push/pull server endpoints and HTTP client.

| Task Range | Focus | Key Deliverables |
|------------|-------|------------------|
| 1.1.1 – 1.1.4 | Infrastructure | `ICloudMutationStore` interface + `cloud_mutations`, `cloud_sync_audit_log`, `cloud_project_controls` tables in PostgresStore |
| 1.2.1 – 1.2.4 | MutationTransport | `IMutationTransport` HTTP client via `IHttpClientFactory`, 409 pause gate, retry on transient errors |
| 1.3.1 – 1.3.4 | Server Endpoints | `POST /sync/mutations/push` (pause gate, batch auth, relation validation) + `GET /sync/mutations/pull` (enrollment filter, cursor pagination) |
| 1.4.1 – 1.4.3 | Local Store | `sync_apply_deferred` table + `ILocalSyncStore` interface + SqliteStore implementation |
| 1.5.1 – 1.5.4 | Tests | Unit + integration tests for transport, endpoints, store |

**Start here**: 1.1.1 (Hosting.Abstractions package) → 1.4.x (local store) → 1.2.x (transport) → 1.3.x (endpoints) → 1.5.x (tests)

### Phase 2: Autosync Manager — 12–16h

Background service that orchestrates push + pull cycles with debounce, backoff, and failure ceiling.

| Task Range | Focus | Key Deliverables |
|------------|-------|------------------|
| 2.1.1 – 2.1.5 | SyncManager Core | `BackgroundService` with debounce/poll channels, phase tracking (Idle/Pushing/Pulling/etc), panic recovery |
| 2.2.1 – 2.2.3 | Push Cycle | Group by project, drain batch, handle pause error, ack seqs |
| 2.3.1 – 2.3.3 | Pull Cycle | Cursor loop with `has_more`, apply mutations, non-enrolled blocking |
| 2.4.1 – 2.4.2 | Deferred Replay | `ReplayDeferredAsync` with retry_count < 5, dead row logging |
| 2.5.1 – 2.5.2 | DI Registration | `AddHostedService<SyncManager>()` + `ENGRAM_SYNC_ENABLED` feature flag |
| 2.6.1 – 2.6.4 | Tests | Phase transitions, failure ceiling, deferred replay, non-enrolled blocking |

**Start here**: 2.1.x (core loop) → 2.2.x (push) → 2.3.x (pull) → 2.4.x (deferred) → 2.5.x (DI) → 2.6.x (tests)

### Phase 3: Enrollment + Conflict Handling — 6–8h

Enrollment management and relation FK deferral strategy.

- `/sync/enroll` endpoint
- `/sync/pause` endpoint
- `EnrolledProjectsProvider` for pull scoping
- Relation FK deferral → `sync_apply_deferred`

### Phase 4: Observability — 4–6h

Monitoring, CLI, and docs.

- `/sync/status` endpoint
- SyncManager metrics via `ILogger`
- `engram sync status` CLI command
- Sync setup documentation

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
