# Context Map — ENG-441: `--dry-run` Executes Real Migration

## Verdict: 🟢 ALREADY FIXED — not a current bug

---

## Bug Description (from backlog)

> `Program.cs` — the `--dry-run` path calls `MigrateProjectAsync()` which executes real UPDATE queries, then prints "Would migrate". Violates REQ-435-003 ("AND no data SHALL be modified").

## Finding: The bug does NOT exist in the current codebase

The `project migrate --dry-run` implementation was **already corrected** as part of the ENG-435 rework cycle. The current code correctly uses SELECT COUNT(*) queries, not UPDATEs.

---

## Evidence

| Aspect | Current Code | Status |
|--------|-------------|--------|
| **Dry-run path** | `Program.cs:658-690` — 3x`SELECT COUNT(*)` queries | ✅ Correct |
| **Real migration path** | `Program.cs:692-703` — calls `store.MigrateProjectAsync()` | ✅ Only when `dryRun == false` |
| **Commit** | `e906041` (ENG-435) — dry-run was implemented correctly from the initial commit of the migrate command | ✅ |
| **Rework history** | `.ai-work/eng-435-legacy-migration/rework_ticket.md` — CRITICAL-2 described the original bug (dry-run called MigrateProjectAsync) | 🔍 Was caught in review |
| **Verify report** | `.ai-work/eng-435-legacy-migration/verify-report.md` — ✅ PASS, includes `--dry-run shows preview without changes` | ✅ Verified |
| **Backlog** | `docs/BACKLOG.md:111` — ENG-441 marked "Ready" with stale description | ⚠️ Stale — should be marked Done or closed as duplicate |

## What the correct dry-run does

```csharp
// Program.cs:658-690
if (dryRun)
{
    using var store = OpenStore();
    var conn = ((dynamic)store)._dataSource.OpenConnection();
    try
    {
        // SELECT COUNT(*) FROM observations WHERE project = @proj AND deleted_at IS NULL
        // SELECT COUNT(*) FROM sessions WHERE project = @proj
        // SELECT COUNT(*) FROM user_prompts WHERE project = @proj AND deleted_at IS NULL
        Console.WriteLine($"Would migrate {obsCount} observations, {sessCount} sessions, {promptCount} prompts");
    }
    finally { conn.Close(); }
    return;
}
```

No INSERT/UPDATE/DELETE executed. REQ-435-003 satisfied.

## How the current code differs from the original (broken) version

| Aspect | Original (CRITICAL-2) | Current |
|--------|----------------------|---------|
| Dry-run calls | `store.MigrateProjectAsync()` → UPDATEs | `SELECT COUNT(*)` queries |
| Data modified | Yes (observations, sessions, user_prompts) | No |
| Developer comment | `// Note: dry-run still migrates` | `// Dry-run preview — use COUNT queries without modifying data` |

## Residual issues (not blockers)

1. **Confirmation prompt order** (Program.cs:647-656): The "Migrate from X to Y? [y/N]" prompt runs BEFORE the dry-run check. A dry-run user is asked to confirm an action that hasn't been previewed yet. The `-y` flag also affects this. Low-severity UX issue.

2. **Fragile dynamic dispatch** (Program.cs:662): `((dynamic)store)._dataSource.OpenConnection()` accesses a private field via dynamic. Works for `SqliteStore` and `PostgresStore` but would throw `RuntimeBinderException` for `HttpStore`. In practice, `OpenStore()` in CLI never returns HttpStore, so this is a theoretical risk only.

3. **No `IStore.CountProjectDataAsync()` method**: The dry-run queries are hardcoded in Program.cs rather than abstracted through the store interface. If a new store implementation were added, the dry-run would need updates in Program.cs.

## Recommendation

1. **Close ENG-441 as Done/Duplicate** — the fix was already applied and verified in the ENG-435 rework cycle (commit `e906041`).
2. **Update BACKLOG.md** — change ENG-441 status from "Ready" to "Done" and add reference to the ENG-435 verify report.
3. **Optionally** fix the confirmation prompt ordering (move the prompt after the dry-run check, or skip it when `--dry-run` is set).
4. **Optionally** refactor dry-run into `IStore` as a `CountProjectDataAsync()` or add `dryRun` parameter to `MigrateProjectAsync`.

---

## References

- **File with dry-run:** `src/Engram.Cli/Program.cs:658-690`
- **File with MigrateProjectAsync:** `src/Engram.Store/PostgresStore.cs:1593-1643`, `src/Engram.Store/SqliteStore.cs:1784-1811`
- **Rework ticket (CRITICAL-2):** `.ai-work/eng-435-legacy-migration/rework_ticket.md`
- **Verify report (✅ PASS):** `.ai-work/eng-435-legacy-migration/verify-report.md`
- **Backlog entry:** `docs/BACKLOG.md:111`
- **Commit:** `e906041` — ENG-435 project identity complete stack
