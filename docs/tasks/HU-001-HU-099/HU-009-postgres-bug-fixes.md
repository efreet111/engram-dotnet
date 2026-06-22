# HU-009: PostgreSQL Bug Fixes

**Status**: 🟡 In Progress
**Owner**: @owner
**Created**: 2026-06-01
**Priority**: Medium
**Effort**: 4-6h total (30min + 1h + 1h + 2-3h)
**Origin**: Migrated from `sdd/postgres-bug-fixes/`

---

## 🎯 Intent

Resolver 4 bugs en PostgresStore que causan tests fallidos o comportamientos inconsistentes vs SQLite.

---

## 📋 Scope

### In Scope
- FTS5 ranking test fix
- FK rollback test fix
- Transaction visibility test fix
- Connection pooling (COMPLETED - already fixed per commit 2806c30)

### Out of Scope
- Changes to production code (only test fixes unless code change required)
- New features

---

## ✅ Requirements

### MUST

- [ ] `Search_TopicKeyShortcut_RanksFirst` passes — update expected rank value for PostgreSQL ts_rank
- [ ] `DeleteSession_HasActiveObservations_Throws` passes — handle PostgreSQL FK rollback behavior
- [ ] `MergeProjects_ReassignsObservations` passes — ensure transaction visibility across operations

---

## 🧪 Scenarios

### Bug 1: FTS5 Ranking (30min)

**Test**: `Search_TopicKeyShortcut_RanksFirst`
**Error**: Expected: -1000, Actual: 0.0607927106320858

- GIVEN a search query with topic key shortcut
- WHEN PostgreSQL ts_rank returns positive value (0-1)
- THEN the test should accept positive rank values (not -1000)

**Fix**: Update test assertion:
```csharp
// Antes:
Assert.Equal(-1000.0, results[0].Rank);
// Después:
Assert.True(results[0].Rank > 0);
```

---

### Bug 2: FK Rollback (1h)

**Test**: `DeleteSession_HasActiveObservations_Throws`
**Error**: `Assert.NotNull(session)` — session is null after failed delete

- GIVEN a session with active observations
- WHEN trying to delete the session
- THEN PostgreSQL FK constraint causes automatic transaction rollback
- AND the subsequent SELECT returns null

**Fix options**:
1. **Test fix**: Add `BEGIN;` before the delete to isolate the rollback
2. **Code fix**: Use `SAVEPOINT` before the FK-violating delete, then `ROLLBACK TO SAVEPOINT` instead of full rollback

---

### Bug 3: Transaction Visibility (1h)

**Test**: `MergeProjects_ReassignsObservations`
**Error**: `Assert.NotNull(obs)` — observation is null after merge

- GIVEN a merge operation
- WHEN the subsequent GET is in different transaction scopes
- THEN PostgreSQL's default isolation level (Read Committed) means GET sees state after merge committed

**Fix**: Ensure both operations share the same transaction:
```csharp
using var tx = _db.BeginTransaction();
// merge + get inside same transaction
tx.Commit();
```

---

### Bug 4: Connection Pooling (COMPLETED ✅)

**Status**: Already fixed per commit `2806c30`

- Connection pooling issue with `NpgsqlConnection` shared across operations
- Solution: NpgsqlDataSource for thread-safe DB access

---

## 📦 Affected Areas

- `tests/Engram.Store.Tests/PostgresStoreTests.cs` — test assertions
- `src/Engram.Store/PostgresStore.cs` — only if code changes needed for Bug 2 or 3

---

## 🔗 Origin

Migrated from `sdd/postgres-bug-fixes/` (specs ready)

Original specs:
- `sdd/postgres-bug-fixes/connection-pooling.md` (COMPLETED)
- `sdd/postgres-bug-fixes/skipped-tests.md` (Bugs 1-3)

---

## 📝 Notes

Connection pooling bug was already fixed in commit `2806c30`. The other 3 bugs (FTS5 ranking, FK rollback, transaction visibility) still need to be resolved.

---

## 🔄 Migration Reference

Original location: `sdd/postgres-bug-fixes/`
Current status: Migrated to FlowDoc

See `sdd/README.md` for full migration mapping.