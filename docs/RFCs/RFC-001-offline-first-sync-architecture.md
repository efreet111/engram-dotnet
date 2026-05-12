# RFC-001: Offline-First Sync Architecture — Chunk + Mutation Hybrid Protocol

**Status**: Draft  
**Author**: SDD Analysis Team  
**Date**: 2026-05-12  
**Related**: PR #14, Issue #13, Go upstream v1.15.10

---

## Summary

Implement bidirectional sync between local SQLite and cloud PostgreSQL using a **hybrid protocol**: mutation-based sync for operational efficiency + chunk-based sync for bulk data transfer and recovery.

**Total Effort**: 32–44h (4 phases)  
**Decision**: Adopt Go upstream cloudstore patterns for server-side, keep client-side simple.

---

## Background

### The Problem

SQLite doesn't scale to multi-user concurrent access. A team needs a shared "brain" where:
- Each developer's local SQLite is source of truth **offline**
- PostgreSQL server is source of truth **online**
- Changes sync bidirectionally when connectivity returns

### What Go upstream discovered

After analyzing Go engram v1.15.10 (29 commits ahead), we found:

1. **cloudstore.go** (817 lines) — Separate PostgreSQL layer for cloud server
2. **Two sync protocols**, not one:
   - **Mutation-based**: Fine-grained WAL (Write-Ahead Log) for incremental sync
   - **Chunk-based**: Compressed JSONL snapshots for bulk transfer and recovery

---

## Design Decisions

### AD-1: Mutation Journal as Primary Protocol

**Choice**: Mutation-based sync (`cloud_mutations` table) as primary operational protocol.

**Rationale**:
- Incremental: Only syncs changes since `since_seq`, not full dataset
- Conflict resolution: Last-write-wins via `occurred_at` timestamps
- Small payload: Each mutation is ~200-500 bytes vs chunks at ~50KB-2MB

**Tradeoff**: Requires careful sequencing and cursor management.

### AD-2: Chunk Storage for Bulk Transfer and Recovery

**Choice**: Keep chunk storage (`cloud_chunks` table) for initial sync and disaster recovery.

**Rationale** (from Go upstream analysis):
- Initial team member sync: Download full project state as chunks, not 10,000 individual mutations
- Disaster recovery: If mutation journal is corrupted, chunks are self-contained snapshots
- Client compatibility: Existing `EngramSync.Export/Import` uses chunks — backward compatible

**Tradeoff**: Dual protocol complexity. More tables, more code paths.

### AD-3: CloudStore as Separate Server-Side Layer

**Choice**: Create `ICloudMutationStore` interface + `ICloudChunkStore` interface, separate from `IStore`.

**Rationale** (from cloudstore.go architecture):
- PostgreSQL CloudStore is **server-only**, never client-side
- Client uses SQLite `IStore` (sync_mutations, sync_chunks tables)
- Server uses PostgreSQL `ICloudStore` (cloud_mutations, cloud_chunks tables)
- Clean separation: Client doesn't need to know server schema details

**Tradeoff**: Two interfaces instead of one, but proper separation of concerns.

### AD-4: Enrollment-Based Pull (Fail-Closed Security)

**Choice**: Pull endpoint filters by enrolled projects. Empty enrolled list = empty response.

**Rationale** (from Go upstream `mutations.go:244`):
> "mutation pull returns empty to prevent cross-tenant leak"

Security principle: Deny by default. Only return data for explicitly enrolled projects.

**Tradeoff**: Requires enrollment step before sync works. Extra UI/API for enrollment.

### AD-5: Sync Pause Gate with Audit Trail

**Choice**: Per-project `sync_enabled` flag + `cloud_sync_audit_log` for rejected pushes.

**Rationale** (from Go upstream conflict resolution needs):
- Team admin can pause sync for maintenance
- Audit trail shows WHO tried to push WHEN and WHY it was rejected
- 409 Conflict with structured error envelope

**Tradeoff**: Extra table, extra check on every push. But necessary for team management.

---

## Architecture

### Data Flow

```
Client (SQLite)                          Server (PostgreSQL)
┌─────────────────────┐                 ┌─────────────────────┐
│ sync_mutations      │ ──Push───────→ │ cloud_mutations     │
│ (pending queue)     │                 │ (mutation journal)  │
├─────────────────────┤                 ├─────────────────────┤
│ sync_chunks         │ ←─Pull──────── │ cloud_chunks        │
│ (exported chunks)   │   (recovery)   │ (snapshot storage)  │
├─────────────────────┤                 ├─────────────────────┤
│ sync_state          │                 │ cloud_project_      │
│ (cursor position)   │                 │   controls          │
└─────────────────────┘                 │ (sync_enabled)      │
                                        └─────────────────────┘
```

### Protocol Comparison

| Aspect | Mutation-Based | Chunk-Based |
|--------|---------------|-------------|
| Payload size | ~200-500 bytes | ~50KB-2MB |
| Use case | Incremental sync | Initial sync, recovery |
| Conflict resolution | Last-write-wins by timestamp | Full replacement |
| Cursor | `since_seq` (BIGSERIAL) | `chunk_id` + `created_at` |
| Tables | `cloud_mutations` | `cloud_chunks` |
| Atomicity | Transaction per batch | Single chunk = atomic |

---

## Implementation Phases

### Phase 1: Mutation Journal + Server Endpoints (10–14h)

Core infrastructure for operational sync.

**New server tables**:
- `cloud_mutations` — mutation journal (seq, project, entity, entity_key, op, payload, occurred_at)
- `cloud_sync_audit_log` — push rejection audit
- `cloud_project_controls` — per-project sync pause

**New interfaces**:
- `ICloudMutationStore` — `InsertMutationBatch()`, `ListMutationsSince()`, `IsProjectSyncEnabled()`

**Endpoints**:
- `POST /sync/mutations/push` — batch insert with validation
- `GET /sync/mutations/pull` — cursor-based pull with enrollment filter

**Chunk consideration**: Phase 1 includes chunk storage **schema only** (table creation). Full chunk protocol deferred to Phase 2+ if needed.

### Phase 2: Autosync Manager (12–16h)

Background sync orchestration.

- `SyncManager` as `IHostedService`
- Push cycle: drain `sync_mutations` → `InsertMutationBatch()` → ack seqs
- Pull cycle: `ListMutationsSince()` → apply locally → advance cursor
- Deferred replay: `sync_apply_deferred` for FK misses
- Failure ceiling: 10 failures → stop sync

**Chunk consideration**: Initial sync on first connection uses chunks. If user has 10,000 mutations pending, download as 5 chunks (2,000 mutations each) instead of 10,000 individual mutations.

### Phase 3: Enrollment + Conflict Handling (6–8h)

Project management and conflict resolution.

- `/sync/enroll` — add project to enrollment list
- `/sync/pause` — admin pause with reason
- Relation FK deferral — write to `sync_apply_deferred`, retry before next pull

### Phase 4: Observability (4–6h)

Monitoring and CLI.

- `/sync/status` — cursor position, last sync, health
- `engram sync status` — CLI command
- Sync metrics: push/pull counts, errors, phase

---

## Open Questions

### Q1: Do we need chunk protocol in v1?

**Options**:
1. **Minimal**: Mutations only. Initial sync downloads 10,000 individual mutations. Simpler code.
2. **Hybrid** (Go upstream): Mutations + chunks. Initial sync as 5 chunks. More complex but faster for large datasets.

**Recommendation**: Start with mutations only (Phase 1). Add chunk protocol in Phase 2 if performance requires it. Chunk table schema included in Phase 1 for forward compatibility.

### Q2: How to handle schema migrations on server?

**Options**:
1. Manual migrations (run SQL scripts on deploy)
2. Automatic migrations (store runs `CREATE TABLE IF NOT EXISTS` on startup)

**Recommendation**: Automatic (like Go upstream `migrate()`). Safer for team deployments where manual SQL is error-prone.

### Q3: Chunk vs mutation deduplication?

**Observation**: Go upstream has both:
- Mutations dedupe by `seq` (journal is append-only)
- Chunks dedupe by `chunk_id` (idempotent writes)

**Decision**: Same approach. Mutation journal never deletes. Chunks overwrite on conflict.

---

## Security Considerations

1. **JWT authentication** for all `/sync/*` endpoints
2. **Project scoping** — server only returns enrolled projects (fail-closed)
3. **Audit logging** — every rejected push logged with contributor, timestamp, reason
4. **Token-based recovery** (separate RFC) for ambiguous project scenarios

---

## Migration Path

From current engram-dotnet (no cloud sync) to offline-first:

1. **Schema migration**: Add `cloud_*` tables to PostgreSQL (if server backend)
2. **Feature flag**: `ENGRAM_SYNC_ENABLED=false` by default
3. **Opt-in**: User runs `engram cloud enroll` to enable sync for a project
4. **Initial sync**: Download all existing data as chunks (fast) or mutations (accurate)

---

## References

- Go upstream: `internal/cloud/cloudstore/cloudstore.go` (817 lines)
- Go upstream: `internal/cloud/cloudserver/mutations.go` (349 lines)
- Go upstream: `internal/sync/sync.go` (1324 lines)
- .NET design: `sdd/offline-first-sync/design/design.md`
- .NET tasks: `sdd/offline-first-sync/tasks/tasks.md`

---

## Decision Log

| Date | Decision | Rationale |
|------|----------|-----------|
| 2026-05-12 | Mutation journal as primary | Incremental sync, efficient for operational use |
| 2026-05-12 | Chunk storage for bulk | Initial sync performance, recovery scenario |
| 2026-05-12 | CloudStore separate from IStore | Server/client separation, clean architecture |
| 2026-05-12 | Enrollment-based pull | Security: fail-closed, prevent cross-tenant leaks |
| 2026-05-12 | Automatic migrations | Safer for team deployments |

---

## Next Steps

1. Update `design.md` with chunk storage schema (forward compatibility)
2. Update `tasks.md` Phase 1 to include chunk table schema
3. Create RFC-002 for ambiguous project recovery
4. Create RFC-003 for prompt auto-capture

**Approved for implementation**: Phase 1 (mutations) with chunk schema forward-compatible.
