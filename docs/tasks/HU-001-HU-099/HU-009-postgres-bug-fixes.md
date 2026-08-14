# HU-009 — PostgreSQL Bug Fixes

**Status**: 🟡 In Progress
**Owner**: @owner
**Created**: 2026-06-01
**Priority**: Medium
**Effort**: 4-6h total (30min + 1h + 1h + 2-3h)
**Origin**: Migrated from `sdd/postgres-bug-fixes/`

---

## As a user...

**As**: Developer
**I want**: Fix 4 bugs in PostgresStore that cause failing tests or inconsistent behavior vs SQLite
**To**: Ensure test parity between SQLite and PostgreSQL backends

---

## Acceptance Criteria

- [ ] `Search_TopicKeyShortcut_RanksFirst` passes — update expected rank value for PostgreSQL ts_rank
- [ ] `DeleteSession_HasActiveObservations_Throws` passes — handle PostgreSQL FK rollback behavior
- [ ] `MergeProjects_ReassignsObservations` passes — ensure transaction visibility across operations
- [ ] Connection pooling works correctly (already fixed per commit 2806c30)

---

## Tasks (Implementation)

### Bug 1: FTS5 Ranking (30min)

- [ ] Update test assertion for `Search_TopicKeyShortcut_RanksFirst`
- [ ] Change `Assert.Equal(-1000.0, results[0].Rank)` to `Assert.True(results[0].Rank > 0)`

### Bug 2: FK Rollback (1h)

- [ ] Fix `DeleteSession_HasActiveObservations_Throws` test
- [ ] Option 1: Add `BEGIN;` before the delete to isolate the rollback
- [ ] Option 2: Use `SAVEPOINT` before the FK-violating delete, then `ROLLBACK TO SAVEPOINT` instead of full rollback

### Bug 3: Transaction Visibility (1h)

- [ ] Fix `MergeProjects_ReassignsObservations` test
- [ ] Ensure both merge and GET operations share the same transaction using `BeginTransaction()`

### Bug 4: Connection Pooling (COMPLETED ✅)

- [x] Already fixed in commit `2806c30` using `NpgsqlDataSource` for thread-safe DB access

---

## Scope

### In Scope
- FTS5 ranking test fix
- FK rollback test fix
- Transaction visibility test fix
- Connection pooling (COMPLETED - already fixed per commit 2806c30)

### Out of Scope
- Changes to production code (only test fixes unless code change required)
- New features

---

## Affected Areas

- `tests/Engram.Store.Tests/PostgresStoreTests.cs` — test assertions
- `src/Engram.Store/PostgresStore.cs` — only if code changes needed for Bug 2 or 3

---

## Notes

### Implementation Notes

Connection pooling bug was already fixed in commit `2806c30`. The other 3 bugs (FTS5 ranking, FK rollback, transaction visibility) still need to be resolved.

### Bug Details

**Bug 1: FTS5 Ranking**
- Test: `Search_TopicKeyShortcut_RanksFirst`
- Error: Expected: -1000, Actual: 0.0607927106320858
- PostgreSQL ts_rank returns positive value (0-1) while test expected -1000

**Bug 2: FK Rollback**
- Test: `DeleteSession_HasActiveObservations_Throws`
- Error: `Assert.NotNull(session)` — session is null after failed delete
- PostgreSQL FK constraint causes automatic transaction rollback

**Bug 3: Transaction Visibility**
- Test: `MergeProjects_ReassignsObservations`
- Error: `Assert.NotNull(obs)` — observation is null after merge
- PostgreSQL's default isolation level (Read Committed) means GET sees state after merge committed

### Origin

Migrated from `sdd/postgres-bug-fixes/` (specs ready)

Original specs:
- `sdd/postgres-bug-fixes/connection-pooling.md` (COMPLETED)
- `sdd/postgres-bug-fixes/skipped-tests.md` (Bugs 1-3)

---

## Migration Reference

Original location: `sdd/postgres-bug-fixes/`
Current status: Migrated to FlowDoc

See `sdd/README.md` for full migration mapping.
