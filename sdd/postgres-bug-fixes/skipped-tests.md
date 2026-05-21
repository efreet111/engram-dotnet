# Bug: PostgresStore Tests Skipped

**Severity**: 🟡 Media  
**Effort**: 2-3h total  
**Reported**: 2026-05-20

---

## Bug 1: FTS5 Ranking (30min)

**Test**: `Search_TopicKeyShortcut_RanksFirst`  
**Error**: Expected: -1000, Actual: 0.0607927106320858

**Cause**: PostgreSQL's `ts_rank` function returns positive values (0-1), unlike SQLite FTS5 which uses negative values for BM25.

**Fix**: Update the expected value in the test:
```csharp
// Antes:
Assert.Equal(-1000.0, results[0].Rank);
// Después:
Assert.True(results[0].Rank > 0);
```

---

## Bug 2: FK Rollback (1h)

**Test**: `DeleteSession_HasActiveObservations_Throws`  
**Error**: `Assert.NotNull(session)` — session is null after failed delete

**Cause**: PostgreSQL's FK constraint violation causes an **automatic transaction rollback**, which reverts the SELECT that follows. SQLite doesn't do this.

**Fix options**:
1. **Test fix**: Add `BEGIN;` before the delete to isolate the rollback
2. **Code fix**: Use `SAVEPOINT` before the FK-violating delete, then `ROLLBACK TO SAVEPOINT` instead of full rollback

---

## Bug 3: Transaction Visibility (1h)

**Test**: `MergeProjects_ReassignsObservations`  
**Error**: `Assert.NotNull(obs)` — observation is null after merge

**Cause**: The merge operation and the subsequent GET might be in different transaction scopes. PostgreSQL's default isolation level (Read Committed) means the GET sees the state AFTER the merge committed, but if they share a connection, there might be uncommitted changes.

**Fix**: Ensure both operations share the same `NpgsqlTransaction`:
```csharp
using var tx = _db.BeginTransaction();
// merge + get inside same transaction
tx.Commit();
```
