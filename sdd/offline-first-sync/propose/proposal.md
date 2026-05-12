# Proposal: Offline-First Sync with Cloud Server

**Change**: `offline-first-sync`
**Version**: 0.2.0
**Status**: Draft (corrected from Go upstream analysis)

---

## Intent

Implement bidirectional mutation-based sync between local SQLite/PostgreSQL and a cloud server, matching the Go engram protocol exactly. .NET has `sync_mutations`, `sync_state`, `sync_chunks`, and `EnqueueSyncMutation` on every write, but **missing**: `MutationTransport`, `SyncManager`, server endpoints.

---

## 📐 API Contract (from Go engram — source of truth)

### Push

```
POST /sync/mutations/push
Headers: Authorization: Bearer <token>, Content-Type: application/json
Body: {
  "entries": [{"project","entity","entity_key","op","payload"}, ...],
  "created_by": "optional"
}
Response 200: { "accepted_seqs": [1,2,3], "project":"", "project_source":"request_body", "project_path":"" }
Response 409: { "error_class":"policy","error_code":"sync-paused","error":"...", "project":"...", ... }
```

### Pull

```
GET /sync/mutations/pull?since_seq={N}&project={P}&limit={100}
Response 200: {
  "mutations": [{"seq","project","entity","entity_key","op","payload","occurred_at"}],
  "has_more": bool, "latest_seq": N,
  "project":"", "project_source":"request_body", "project_path":""
}
```

### Critical Fixes from Go Source

| What Proposal 0.1 Said | What Go Actually Does |
|------------------------|----------------------|
| `{"mutations": [...]}` push body | `{"entries": [...]}` |
| `{"rejected": []}` response | No rejected array |
| `{"next_seq": N}` pull response | `{"has_more": bool, "latest_seq": N}` |
| `Mutation(Entity, EntityId, Op, Payload)` | `MutationEntry { Project, Entity, EntityKey, Op, Payload }` — **Project is required** |
| No project envelope | Both endpoints return `project`/`project_source`/`project_path` |
| No 409 for paused sync | Go returns 409 with `writeActionableError` on `sync_enabled=false` |
| No payload validation | Go validates 6 relation fields (sync_id, source_id, target_id, judgment_status, marked_by_actor, marked_by_kind) |
| Pull returns all | Pull is scoped to caller's enrolled projects via `EnrolledProjectsProvider` |

---

## Data Model — Missing Tables

| Table | Where | Purpose |
|-------|-------|---------|
| `cloud_mutations` | Server PG | Server-side append-only mutation journal (Go: `cloud_mutations`, .NET PostgresStore currently only has client-side `sync_mutations`) |
| `cloud_sync_audit_log` | Server PG | Audit trail for push-rejection events (pause, auth failure) |
| `cloud_project_controls` | Server PG | Per-project sync_enabled flag + pause reason |
| `sync_apply_deferred` | Local SQLite | Deferred relation FK misses — retried before pull cycle (`ReplayDeferred`) |
| `sync_enrolled_projects` | Both | ✅ Already exists in .NET (both stores) |

---

## Phases — Corrected Estimates

### Phase 1: Mutation Journal + Server Endpoints **10-14h** ← WAS 6-8h

Missing from v0.1: pause gate (409 + audit), batch-authorize every project, empty batch rejection, 8 MiB body limit, relation payload validation (6 required fields), `created_by` field, project envelope in responses.

| # | Task | Complexity |
|---|------|-----------|
| 1.1 | `cloud_mutations`, `cloud_sync_audit_log`, `cloud_project_controls` tables | Med |
| 1.2 | `POST /sync/mutations/push` — pause gate, batch auth, audit logging, relation validation, empty batch rejection | High |
| 1.3 | `GET /sync/mutations/pull` — enrollment filter, cursor pagination, project envelope | Med |
| 1.4 | `MutationTransport` — `PushMutations([]MutationEntry)` and `PullMutations(sinceSeq, limit)` | Med |
| 1.5 | Auth middleware for endpoints | Low |
| 1.6 | Tests: push auth, pause gate, validation, pull cursor, transport | Med |

### Phase 2: Autosync Manager **12-16h** ← WAS 8-10h

Missing from v0.1: deferred replay (`sync_apply_deferred`), non-enrolled pending mutation blocking, failure ceiling (10 max), panic recovery with stack trace, phase tracking (idle/pushing/pulling/push_failed/pull_failed/backoff/healthy/disabled), `ReplayDeferred()` before pull, `CountPendingNonEnrolled()` blocking push.

| # | Task | Complexity |
|---|------|-----------|
| 2.1 | `SyncManager` — loop with debounce, poll, lease, backoff, phases | High |
| 2.2 | Push cycle — group by project, drain batch, ack seqs | Med |
| 2.3 | Pull cycle — cursor loop, `ApplyPulledMutation`, advance cursor | High |
| 2.4 | Deferred replay (`sync_apply_deferred` table + `ReplayDeferred()`) | Med |
| 2.5 | Non-enrolled blocking (`CountPendingNonEnrolled`) | Low |
| 2.6 | Failure ceiling + panic recovery | Low |
| 2.7 | DI registration (IHostedService) | Low |
| 2.8 | Tests: mock transport, failure scenarios, deferred replay | High |

### Phase 3: Enrollment + Conflict Handling **6-8h** ← WAS 4-6h

Missing from v0.1: enrollment filter on pull (server-side), relation FK deferral on apply (writes to `sync_apply_deferred`, does NOT halt cursor).

| # | Task | Complexity |
|---|------|-----------|
| 3.1 | `/sync/enroll` and `/sync/pause` endpoints (pause gate done in P1) | Med |
| 3.2 | Enrollment filter on pull: `EnrolledProjectsProvider` interface | Med |
| 3.3 | Relation FK deferral on apply (write to `sync_apply_deferred`, return nil) | Med |
| 3.4 | Tests: enrollment flow, FK deferral, apply conflict | Med |

### Phase 4: Observability **4-6h** ✅ Accurate

| # | Task | Complexity |
|---|------|-----------|
| 4.1 | `/sync/status` endpoint | Low |
| 4.2 | SyncManager metrics (push/pull count, errors, phase) | Low |
| 4.3 | CLI: `engram sync status` | Low |
| 4.4 | Docs: sync setup + architecture | Low |

---

## Rollback

- Phase 1 endpoints: remove routes, no data loss
- Phase 2: feature flag `ENGRAM_SYNC_ENABLED=false` (Go pattern), or `StopForUpgrade()`
- Deferred rows: `DELETE FROM sync_apply_deferred` is safe — only unapplied relations
- Server data: `cloud_mutations` is append-only, no destructive rollback needed

## Success Criteria

- [ ] Push: local mutations appear in server's `cloud_mutations` journal
- [ ] Pull: remote mutations applied locally with cursor advancement
- [ ] Pause: 409 returned with audit log entry
- [ ] Deferred: relation FK misses land in `sync_apply_deferred`, replay succeeds
- [ ] Enrolled-only: pull scoped to caller's enrolled projects
