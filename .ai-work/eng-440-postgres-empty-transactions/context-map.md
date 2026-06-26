# Context Map — ENG-440: PostgresStore Empty Transactions Bug

> **Feature slug:** `eng-440-postgres-empty-transactions`
> **Work item:** ENG-440 (P0 Bug, Effort: M)
> **Source:** OSS Launch Audit 2026-06-23
> **Status:** Discovery Complete

---

## FlowDoc context
- PRD: No `.flowforge.json` found — project uses backlog + SDD workflow, not FlowDoc
- HU referenced: None (ENG-440 is audit-born, P0 bug)
- Related: rework_ticket for ENG-435 (CRITICAL-1) identified same bug surface

---

## ⚠️ CRITICAL FINDING: The reported bug location is ALREADY FIXED

The original bug description cited `PostgresStore.cs:1610-1626` (`MigrateProjectAsync`) as missing `cmd.Transaction = tx`. **The current code at those lines IS correct:**

```
Line 1611: cmdObs.Transaction = tx;    ✓
Line 1618: cmdSess.Transaction = tx;   ✓
Line 1625: cmdPrompt.Transaction = tx; ✓
```

This was fixed between the audit (2026-06-23) and this session. The rework_ticket (`eng-435-legacy-migration/rework_ticket.md`) still references the broken code — it needs updating.

---

## Reusable Patterns Found
- `PostgresStore.cs:419-460` (`DeleteSessionAsync`): The **reference pattern** for correct transaction usage. Every `CreateCommand()` is immediately followed by `cmd.Transaction = tx` inside a transaction block.
- `PostgresStore.cs:1610-1626` (`MigrateProjectAsync`): Also correct — follows the same pattern.

---

## Affected Methods — Missing Transaction Atomicity

### Category A: No transaction at all (data integrity risk)

| # | Method | Lines | Problem | Impact |
|---|--------|-------|---------|--------|
| A1 | `MergeProjectsAsync` | 1558-1591 | No `BeginTransaction` — 3 UPDATEs auto-commit independently | Partial merge: observations can update but sessions/prompts can fail after |
| A2 | `PruneProjectAsync` | 1759-1792 | No `BeginTransaction` — 2 DELETEs auto-commit independently | Partial prune: prompts deleted but sessions survive (unrecoverable) |
| A3 | `PruneOldObservationsAsync` | 1042-1087 | No `BeginTransaction` — per-type UPDATEs auto-commit | Partial prune: some types pruned, others not |

### Category B: Transaction exists but `cmd.Transaction = tx` NOT SET

| # | Method | Lines | Problem | Impact |
|---|--------|-------|---------|--------|
| B1 | `InsertMutationBatchAsync` (loop) | 1837 | `cmd` created from `txConn` but `cmd.Transaction = tx` MISSING | The tx.BeginTransaction / tx.Commit wraps NOTHING — all commands auto-commit |
| B2 | `ApplySessionUpsertAsync` | 2382 | Receives `txConn` but not `tx` object — cannot enlist | Upsert outside transaction |
| B3 | `ApplyObservationUpsertAsync` | 2422, 2430, 2452 | Same — 3 commands (checkCmd, updateCmd, insertCmd) | Check-then-insert not atomic |
| B4 | `ApplyObservationDeleteAsync` | 2485 | Same — `cmd` created without transaction | Delete outside transaction |
| B5 | `ApplyPromptUpsertAsync` | 2507, 2515, 2530 | Same — 2-3 commands | Check-then-insert not atomic |
| B6 | `ApplyPromptDeleteAsync` | 2549 | Same — `cmd` created without transaction | Delete outside transaction |

### Command site count

**Total command sites missing `cmd.Transaction = tx`: ~12-15** across B1-B6.

---

## Pattern Comparison

### Correct pattern (reference: `DeleteSessionAsync` line 419)

```csharp
using var tx = txConn.BeginTransaction();
using (var cmd = txConn.CreateCommand())
{
    cmd.Transaction = tx;          // ← REQUIRED
    cmd.CommandText = "SELECT ...";
    cmd.Parameters.AddWithValue("@id", id);
    var result = cmd.ExecuteScalar();
}
```

### Broken pattern (current: `InsertMutationBatchAsync` line 1837)

```csharp
using var tx = txConn.BeginTransaction();
await using var cmd = txConn.CreateCommand();
// MISSING: cmd.Transaction = tx;
cmd.CommandText = "INSERT INTO ...";
var seq = (long)(await cmd.ExecuteScalarAsync(ct))!;
```

**Root cause**: Npgsql does NOT auto-enlist commands in the ambient transaction. You MUST explicitly set `cmd.Transaction = tx`. Without it, each `Execute*` call auto-commits independently.

---

## Architecture Note: The `InsertMutationBatchAsync` chain

The deepest problem is in the sync pipeline:

```
InsertMutationBatchAsync (tx, but no cmd.Transaction)
  └─ ApplyMutationsToDataStoreAsync (receives conn only)
       ├─ ApplySessionUpsertAsync     → 1 cmd
       ├─ ApplyObservationUpsertAsync → up to 3 cmds (check + update/insert)
       ├─ ApplyObservationDeleteAsync → 1 cmd
       ├─ ApplyPromptUpsertAsync      → up to 3 cmds (check + update/insert)
       └─ ApplyPromptDeleteAsync      → 1 cmd
```

**Fix design constraint**: `Apply*` methods receive `NpgsqlConnection` but NOT `NpgsqlTransaction`. To fix properly, either:
1. **Option A**: Pass `NpgsqlTransaction tx` to every `Apply*` method and add `cmd.Transaction = tx` — cleaner but touches 6+ signatures
2. **Option B**: Set `cmd.Transaction = tx` in `InsertMutationBatchAsync` loop only, and refactor Apply methods to create their own transaction — requires restructuring
3. **Option C (minimal)**: Set `cmd.Transaction = tx` inline in `InsertMutationBatchAsync`, and nest a `BeginTransaction` inside each `Apply*` that receives only `conn` — still breaks atomicity if Apply* methods run multiple statements independently

**Recommendation**: Option A — pass `NpgsqlTransaction tx` down the chain. This is the only way to keep the entire batch truly atomic.

---

## Scope of Fix

### Must fix (P0 — data integrity risk)

| Method | Fix type | Complexity |
|--------|----------|------------|
| `InsertMutationBatchAsync` + all `Apply*` | Pass `NpgsqlTransaction tx` + add `cmd.Transaction = tx` | M (touches 6 method sigs + ~10 command sites) |
| `MergeProjectsAsync` | Wrap in `BeginTransaction` + add `cmd.Transaction = tx` | S |

### Should fix (P1 — best-effort atomicity)

| Method | Fix type | Complexity |
|--------|----------|------------|
| `PruneProjectAsync` | Wrap in `BeginTransaction` + add `cmd.Transaction = tx` | S |
| `PruneOldObservationsAsync` | Wrap in `BeginTransaction` + add `cmd.Transaction = tx` | S |

---

## Constraints & Risks

1. **`InsertMutationBatchAsync` is the sync path** — the busiest write path in PostgresStore. Any change must be well-tested.
2. **Transaction lifetime**: `NpgsqlTransaction` objects must be disposed. The `using var tx = txConn.BeginTransaction()` pattern in `InsertMutationBatchAsync` is correct — just needs `cmd.Transaction = tx`.
3. **`Apply*` methods are `private`** — signature changes are contained within `PostgresStore.cs`. No external API breakage.
4. **Existing tests for sync path** (ENG-425/426/436) should cover the transaction fix — verify they catch the atomicity gap.

---

## Backlog Reference

```
#23 | ENG-440 | P0 | Bug | PostgresStore empty transactions | Ready | M
Companion to ENG-436 — both surfaces of the same data-loss vector
```

---

## Security Assessment
- Dependencies reviewed: Npgsql v8.x (transaction behavior known)
- Critical CVEs found: 0
- High CVEs found: 0
- Past security issues: The `cmd.Transaction = tx` omission in Npgsql is a known dotnet/npgsql gotcha — not a CVE
- Security verdict: ✅ SAFE (but data integrity risk until fixed)

---

**CLEAR**
