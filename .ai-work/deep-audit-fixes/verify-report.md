# Verify Report — deep-audit-fixes

- **Date**: 2026-06-22
- **Cycle**: 1 of 3
- **Verdict**: **REWORK** ⛔

---

## 1. Summary

| FR | Description | Status |
|----|-------------|--------|
| FR-001 | `.Result` → `await` in SqliteStore.cs + PostgresStore.cs | ✅ PASS |
| FR-002 | Remove `/debug-test` from API-REFERENCE.md | ✅ PASS |
| FR-003 | Update `/health` version to `"1.1.0"` | ✅ PASS |
| FR-004 | Update MCP tool count 26 → 28 | ❌ FAIL (partial) |
| FR-005 | JWT marked `[planned]` in ARCHITECTURE.md | ✅ PASS |

---

## 2. Detailed Evidence

### FR-001 — `.Result` → `await` ✅

| File | Line | Before | After |
|------|------|--------|-------|
| `src/Engram.Store/SqliteStore.cs` | 1639 | `.Result` | `await` |
| `src/Engram.Store/PostgresStore.cs` | 1445 | `.Result` | `await` |

- Method signatures correctly changed from `public Task<int>` to `public async Task<int>`
- `Task.FromResult()` wrappers removed; direct `return` now used
- `grep -rn "\.Result" src/Engram.Store/` → **zero results** (clean)

### FR-002 — Remove `/debug-test` ✅

- `docs/API-REFERENCE.md`: `## 🧪 Debug` section with `POST /debug-test` completely removed
- `grep -rn "debug-test\|debug_test\|/debug" docs/API-REFERENCE.md` → **zero results**

### FR-003 — Version `"1.1.0"` ✅

- `docs/API-REFERENCE.md:21`: `{"status":"ok","service":"engram","version":"1.1.0","backend":"postgres"}`
- Matches actual server response

### FR-004 — Tool count 26 → 28 ❌ PARTIAL

| File | Item | Status |
|------|------|--------|
| `README.md:110` | `(26 tools)` → `(28 tools)` | ✅ Fixed |
| `README.es.md:110` | `(26 tools)` → `(28 tools)` | ✅ Fixed |
| `ARCHITECTURE.md:31` (src tree comment) | `26 tools` → `28 tools` | ✅ Fixed |
| `ARCHITECTURE.md:176` (MCP Section header) | `26 tools` → `28 tools` | ✅ Fixed |
| `ARCHITECTURE.md:178-201` (MCP tool listing) | Add `mem_relations`, `mem_lineage_obs` | ❌ **NOT DONE** |
| `EngramMcpServer.cs:10` | Comment `19` → `28` | ✅ Fixed |
| `EngramTools.cs:47` | Class comment still says `26` | ⚠️ Latent stale |

**Failure details**:

1. **ARCHITECTURE.md tool listing incomplete**: The spec explicitly required adding `mem_relations` and `mem_lineage_obs` to the MCP tool list. The header was updated to say "28 tools", but the actual enumerated list still contains only 26 tool names. The two new tools (`mem_relations` at `EngramTools.cs:1000`, `mem_lineage_obs` at `EngramTools.cs:1076`) exist in source code but are absent from the documentation listing.

   Current ARCHITECTURE.md grouping totals 26:
   ```
   Production: 7 + Read-only: 7 + Diagnostics: 1 + Promotion: 2
   + Verification: 4 + Retention: 2 + Projects: 3 = 26
   ```
   Missing: `mem_relations`, `mem_lineage_obs` (need a "Relations" group, or add under Verification).

2. **EngramTools.cs:47**: Class-level comment (`/// All 26 Engram MCP tools`) still says 26. This wasn't in the spec's explicit file table for FR-004, so it's not a blocking violation, but it's a latent inconsistency that should be corrected alongside the other fix.

### FR-005 — JWT `[planned]` ✅

- `docs/ARCHITECTURE.md:269`:
  - Before: `| Auth | JWT (optional, via ENGRAM_JWT_SECRET) |`
  - After:  `| Auth | JWT [planned, optional via ENGRAM_JWT_SECRET] |`
- Correctly aligns with `HttpStore.cs:52-53` `// future` comment

---

## 3. Build & Tests

| Check | Result |
|-------|--------|
| `dotnet build -c Release` | ✅ 0 warnings, 0 errors |
| `dotnet test -c Release --filter "Category!=RequiresDocker"` | ✅ 649 passed, 0 failed, 15 skipped (RequiresDocker) |
| `grep -rn "\.Result" src/Engram.Store/` | ✅ Clean (0 results) |
| `grep -rn "debug-test" docs/API-REFERENCE.md` | ✅ Clean (0 results) |

### Test breakdown

- Engram.Obsidian.Tests: 77 passed
- Engram.MdGeneration.Tests: 17 passed
- Engram.Verification.Tests: 41 passed
- Engram.Mcp.Tests: 104 passed
- Engram.Diagnostics.Tests: 19 passed, 8 skipped
- Engram.Store.Tests: 204 passed, 7 skipped
- Engram.Sync.Tests: 32 passed
- Engram.Cli.Tests: 48 passed
- Engram.HttpStore.Tests: 32 passed
- Engram.Server.Tests: 75 passed
- Engram.Postgres.Tests: 0 run (all RequiresDocker, filtered)

---

## 4. NFR-003 Compliance

FR-001 (`.Result` → `await`) must pass T3 (Postgres integration) before merge. This was NOT verified in this cycle since T3 requires Docker + Postgres. The T2 tests (SQLite) passed. T3 verification is a merge gate, not a verify gate per the spec.

---

## 5. CKP / Context Map Check

`.ai-work/deep-audit-fixes/context-map.md` does not exist. This is a direct audit fix — no exploration/discovery phase was needed. Not a CKP-0 violation for this change type.

---

## Pending Manual Tests

NFR-003 requires manual T3 verification (`PG_PASS=tu_password bash scripts/dev-test.sh`) before merge to confirm the `await` fix doesn't introduce Postgres-specific issues.

---

## 🔍 Manual Verification Steps

1. **T3 Postgres integration**: Run `PG_PASS=tu_password bash scripts/dev-test.sh` to verify FR-001 under Postgres (deadlock-free async `SyncMdToRepoAsync`)
2. **Check `/debug-test` removed at runtime**: Start server, `curl -X POST http://localhost:7437/debug-test` → should return 404
3. **Check `/health` version**: `curl http://localhost:7437/health` → confirm `"version":"1.1.0"`
4. **Tool count**: In an MCP client connected to engram, verify 28 tools are exposed (check for `mem_relations` and `mem_lineage_obs`)
