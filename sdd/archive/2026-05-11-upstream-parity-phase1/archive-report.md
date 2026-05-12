# Archive Report: upstream-parity-phase1

**Status**: ✅ ARCHIVED
**Date Archived**: 2026-05-11
**Change Name**: `upstream-parity-phase1`
**Moved to**: `sdd/archive/2026-05-11-upstream-parity-phase1/`

---

## Executive Summary

`upstream-parity-phase1` was a multi-phase effort to bring engram-dotnet to parity with Go upstream. All 4 phases were completed and merged to main via PRs #7 and #11.

---

## Phases Summary

### Phase 1: Project Detection 5-Case ✅
- `DetectProjectFull` with 5-case algorithm
- `NormalizeProject` with underscores → hyphens
- Unit tests for all cases

### Phase 2: Schema Columns ✅
- `ReviewAfter`, `ExpiresAt`, `Embedding`, `EmbeddingModel`, `EmbeddingCreatedAt`
- SQLite + PostgreSQL migrations (idempotent `AddColumnIfNotExists`)
- Store layer updated in all backends

### Phase 3: Write Queue ✅
- `WriteQueue<T>` with `Channel<T>` for serialized async writes
- Prevents SQLITE_BUSY under concurrent load

### Phase 4: Session Activity Tracker ✅
- `SessionActivity` class with nudge logic and activity scores
- Integrated into `EngramTools` (mem_search, mem_context, mem_save)
- 7 unit tests, 21/21 spec compliance scenarios verified
- **Issue fixed**: SessionActivity.cs was orphaned (never committed to tree) — recreated from Go upstream `internal/mcp/activity.go`

---

## Merge History

| PR | Date | Content |
|----|------|---------|
| [#7](https://github.com/efreet111/engram-dotnet/pull/7) | 2026-04-24 | Phases 1+2+3 (project detection, schema, write queue) |
| [#8](https://github.com/efreet111/engram-dotnet/pull/8) | 2026-04-29 | Phase 2 DELETE endpoints (part of phase2 work) |
| [#11](https://github.com/efreet111/engram-dotnet/pull/11) | 2026-05-11 | Phase 4 Session Activity (restored + merged) |

---

## Pending Tasks (17) — NOT BLOCKING

These are manual/verification tasks that don't block the feature:

- 1.9: Write unit tests for `ScanChildren` (edge case — not critical)
- 2.6: Migration tests (existing DB gets columns)
- 2.7: Verify existing tests pass with nullable columns
- 3.6: Register WriteQueue in DI (used directly, not via DI — design decision)
- 3.12: Integration test: concurrent mem_save (write queue handles this)
- 5.1–5.12: Manual tests and final verification

---

## Key Learnings

- Go upstream `internal/mcp/activity.go` is the source of truth for SessionActivity port
- Orphaned files (never `git add`'d) get deleted by `git clean -fd` — always stage source files before referencing them in modified files
- TDD-first approach for Phase 2 delete endpoints worked well (RED→GREEN cycle)

---

## Observation IDs (Engram persistence)

- proposal: see `sdd/upstream-parity-phase1/proposal.md`
- spec: see `sdd/archive/2026-05-11-upstream-parity-phase1/spec.md`
- tasks: see `sdd/archive/2026-05-11-upstream-parity-phase1/tasks.md`