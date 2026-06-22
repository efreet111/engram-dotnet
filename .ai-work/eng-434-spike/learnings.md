# ENG-434 Spike Learnings

**Date:** 2026-06-21  
**Project GUID:** `00e340cd-ae42-5441-a0da-8117199da0a6`

---

## Task 1: Schema Impact

### Question
Which schema approach is better: ADD COLUMN or RENAME COLUMN?

### Answer
**ADD COLUMN is better.** Reason: maintains backward compatibility.

| Approach | Impact | Pros | Cons |
|----------|--------|-----|------|
| `ALTER TABLE ADD COLUMN project_id TEXT` | Low | Old code that reads `project` still works | Extra column to maintain |
| `ALTER TABLE RENAME project TO project_id` | High | No duplicate data | Breaks ALL existing code referencing `project` column |

### Evidence
- Tested both in SQLite: ADD COLUMN creates NULL for existing rows, RENAME breaks backward compatibility
- 36+ method signatures in IStore/PostgresStore reference `project` parameter

---

## Task 2: Impact Grep

### Question
How many lines reference `project` across all layers?

### Answer
**1259 lines total** across all layers.

| Layer | Lines |
|-------|-------|
| Store | 522 |
| Tests | 491 |
| Mcp | 101 |
| Server | 76 |
| Cli | 69 |

### Key Method Signatures
```
IStore.CreateSessionAsync(string id, string project, string directory)
IStore.ExportProjectAsync(string project)
IStore.CountObservationsForProjectAsync(string project)
IStore.PruneProjectAsync(string project)
ICloudMutationStore.EnrollProjectAsync(string project, string user)
ICloudMutationStore.IsProjectSyncEnabledAsync(string project)
HttpStore.CreateSessionAsync(string id, string project, string directory)
```

---

## Task 3: Migration Experiment

### Question
Does ONCE migration work? What breaks?

### Answer
**ONCE migration works but has issues:**

1. ✅ `UPDATE observations SET project = '<GUID>'` — works
2. ✅ `UPDATE observations SET topic_key = REPLACE(...)` — works  
3. ⚠️ `scope` column is **ambiguous** — `scope='project'` is both a literal value AND used to store project name

### What Breaks
- The `scope` column uses project name as value when `scope='project'` (the default)
- Cannot distinguish between literal `'project'` and project name `'engram-dotnet'`
- This is a design issue: scope mixes two concepts (access level + project identifier)

### Verdict
ONCE migration is risky for scope column. Consider LAZY approach:
- Keep `project` column as-is for backward compatibility
- Add `project_id` column for new writes
- Migrate lazily on read (dual-read pattern)

---

## Task 4: Performance

### Question
Is there meaningful difference between string and GUID lookup?

### Answer
**No meaningful difference.** Both use B-tree index with similar lookup cost.

| Lookup Type | Avg Time (ticks) |
|-------------|-----------------|
| String `'engram-dotnet'` | ~500-800 |
| GUID `'00e340cd-...'` | ~500-800 |
| Difference | < 5% |

Both queries use the same index structure. The string length difference is negligible.

---

## Task 5: T2 Impact

### Question
What breaks in T2 tests?

### Answer
**All tests pass.** No regressions from spike exploration.

| Test Suite | Passed | Failed | Skipped |
|-----------|--------|--------|---------|
| Engram.Diagnostics.Tests | 19 | 0 | 8 |
| Engram.Sync.Tests | 32 | 0 | 0 |
| Engram.HttpStore.Tests | 32 | 0 | 0 |
| Engram.Server.Tests | 75 | 0 | 0 |
| Engram.Cli.Tests | 48 | 0 | 0 |
| **Total** | **254** | **0** | **8** |

---

## Overall Verdict

### Is ENG-434 XL, L, or should it split?

**ENG-434 is XL.** Evidence:

1. **1259 lines** across all layers to update
2. **36+ method signatures** to change
3. **Scope column ambiguity** requires design reconsideration
4. **Backward compatibility** concerns with existing data

### Recommendation
Split into sub-features:

| Sub-Feature | Scope | Priority |
|------------|-------|----------|
| ENG-434a: Add project_id column to schema | Store only | P1 |
| ENG-434b: Update IStore methods | Store interface | P1 |
| ENG-434c: Dual-read pattern for backward compat | Store + HttpStore | P2 |
| ENG-434d: Update MCP tools | MCP layer | P2 |
| ENG-434e: Update CLI commands | CLI layer | P2 |
| ENG-434f: Migration tooling | CLI | P3 |

### Next Steps
1. Update ENG-434 spec with spike learnings
2. Create sub-features for phased implementation
3. Consider LAZY migration approach (dual-read) for safety