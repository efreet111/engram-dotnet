---
cycle_count: 2
max_cycles: 3
status: "resolved"
---
# Rework Ticket — ENG-435: Legacy Identity Migration Toolkit

## 1. Failure Reason

Two CRITICAL blockers found that violate spec requirements:

### CRITICAL-1: PostgresStore transaction is empty — zero atomicity
**File:** `src/Engram.Store/PostgresStore.cs:1610-1626`

The three `NpgsqlCommand` objects created for the UPDATE statements do NOT set `cmd.Transaction = tx`. In Npgsql, commands are not automatically associated with the ambient transaction — you must explicitly assign `cmd.Transaction = tx`. Without this, all three UPDATEs execute outside the transaction, each in its own implicit auto-commit.

This means:
- `tx.Commit()` on line 1628 commits an **empty** transaction
- If `sessions` UPDATE fails after `observations` UPDATE succeeds, observations are already committed and can't roll back
- The `tx.Rollback()` on line 1632 is useless

**Violates REQ-435-004**: "all three tables SHALL be updated in a single transaction" and "if any update fails, all SHALL be rolled back."

**Evidence:** Every other transactional method in PostgresStore.cs sets this explicitly. Compare `DeleteSessionAsync` lines 421, 432, 443, 456 — all use `cmd.Transaction = tx`.

### CRITICAL-2: --dry-run actually performs the migration
**File:** `src/Engram.Cli/Program.cs:641`

```csharp
var result = await store.MigrateProjectAsync(source, target);
// Note: dry-run still migrates but we could enhance this later
Console.WriteLine($"Would migrate {result.ObservationsMigrated} observations...");
```

The dry-run path calls `MigrateProjectAsync()` which executes all UPDATEs, then prints "Would migrate". The developer even acknowledged this with the comment "dry-run still migrates but we could enhance this later."

**Violates REQ-435-003** (Dry-run scenario): "AND no data SHALL be modified."

---

## 2. Affected Files
- `src/Engram.Store/PostgresStore.cs` — lines 1610, 1616, 1622 (missing `cmd.Transaction = tx`)
- `src/Engram.Cli/Program.cs` — lines 638-644 (dry-run executes migration)

---

## 3. Correction Instructions

### Fix 1: Associate commands with transaction in PostgresStore
Add `cmdObs.Transaction = tx`, `cmdSess.Transaction = tx`, `cmdPrompt.Transaction = tx` before each `ExecuteNonQuery()` call, following the existing pattern in `DeleteSessionAsync` (lines 421, 432, 443, 456).

```csharp
cmdObs.Transaction = tx;  // ← ADD after line 1613
cmdObs.CommandText = "UPDATE observations ...";
```

### Fix 2: Implement true dry-run
Replace the `MigrateProjectAsync` call with SELECT COUNT(*) queries that only count affected rows without modifying them:

```csharp
if (dryRun)
{
    using var store = OpenStore();
    var count = await store.CountProjectDataAsync(source);
    Console.WriteLine($"Would migrate {count.observations} observations, {count.sessions} sessions, {count.prompts} prompts");
    return;
}
```

If a `CountProjectDataAsync` method doesn't exist in IStore, add it. Alternatively, implement dry-run at the store level with a boolean parameter to `MigrateProjectAsync`.

### Additional recommendation (not a blocker):
- Add `cmdObs.Transaction = tx` also to the `PruneProjectAsync` method if it follows the same pattern (verify `PostgresStore.cs:1827` area).
- Add an integration test that seeds data, triggers a mid-migration failure, and verifies rollback.
