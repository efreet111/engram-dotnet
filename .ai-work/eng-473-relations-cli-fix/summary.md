# Session Summary — MCP Tool Verification + ENG-473 Relations Bugfix

**Date**: 2026-07-22  
**Feature slug**: `eng-473-relations-cli-fix`  
**Type**: Verification + Bugfix  
**Commit**: `c88d31e` — `fix: ENG-473 relations CLI FK constraint violation`

---

## Goal

Functional verification of 13 MCP tools that hadn't been tested recently, plus discovery and repair of any blocking bugs found during testing.

---

## Discoveries

### 🔴 Bug Found — ENG-473: Relations CLI FK Constraint Violation

**Root cause**: `Program.cs:1260` generates `sessionId = $"rel-cli-{DateTime.UtcNow:yyyyMMdd}"` but **never creates it in the sessions table**. The `observations` table has `FOREIGN KEY (session_id) REFERENCES sessions(id)`, causing FK constraint violation when `MemoryRelationRepository.SaveRelationAsync()` calls `AddObservationAsync()`.

**Fix**: 1 line — `await store.CreateSessionAsync(sessionId, project, "");` before `SaveRelationAsync()` (same pattern as `engram save` at line 259).

### 📊 MCP Tool Verification Results

| Category | Tools Tested | Result |
|----------|-------------|--------|
| Relations | `mem_relations`, `mem_lineage_obs` | 🔴 BUG (ENG-473) — now fixed |
| MD Promotion | `mem_promote_to_md`, `mem_sync_md_to_repo` | 🟢 OK |
| Verification | `mem_verify_artifact`, `mem_traceability`, `mem_trace_source`, `mem_lineage` | 🟢 OK |
| Retention | `mem_retention_stats`, `mem_retention_prune` | 🟢 OK |
| Projects | `mem_merge_projects`, `mem_project_redirects` | 🟢 OK |
| Diagnostics | `mem_doctor` | 🟢 OK |
| Sync | `/sync/status` | 🟡 INFO (requires PostgreSQL) |

### 🟡 Sync Limitation Confirmed

Sync endpoints (`/sync/mutations/push`, `/sync/mutations/pull`) return **501 Not Implemented** when the backend is SQLite. Only PostgreSQL (`phase: cloud`) supports sync. Documented — not a bug, by design.

### 🧠 Backlog Additions

Registered 13 new feature ideas (ENG-460 to ENG-472) in BACKLOG.md from a brainstorming session:
- Giant class refactor (split SqliteStore/PostgresStore)
- Split EngramTools by domain
- Project Context Storage
- Obsidian export without AI
- Memory templates
- Auto-summarization
- Tags
- Consolidation
- Fuzzy search
- Quick wins

---

## Accomplished

1. ✅ **ENG-473 fixed and verified** — 712/712 tests pass (zero regressions), CLI tests 51/51 pass
2. ✅ **13 MCP tools verified** — 11/13 🟢, 1 🔴 (fixed), 1 🟡 (by design)
3. ✅ **End-to-end relations verified** — add → get → lineage → delete all work correctly
4. ✅ **Commit pushed** — `c88d31e`
5. ✅ **BACKLOG.md updated** — ENG-460-472 added, ENG-473 documented

---

## Relevant Files Modified

| File | Change |
|------|--------|
| `src/Engram.Cli/Program.cs` | +1 line: `CreateSessionAsync()` before `SaveRelationAsync()` |
| `docs/BACKLOG.md` | +65 lines: ENG-460-472 features + ENG-473 bug + changelog |
| `docs/ROADMAP.md` | ENG-473 status updated to committed (`c88d31e`) |

---

## Next Steps

1. **Prioritize backlog** — decide which ENG-460-472 features to tackle next
2. **Giant class refactor** (ENG-460) has highest spontaneous interest
3. **PostgreSQL sync testing** when cloud backend is available

---

## ✅ PM-* Gate

This session was not tied to a formal spec.md. No PM-* gates apply.

All 712 tests pass. Manual verification of relations add/get/lineage/delete confirmed end-to-end.

---

*Session closed by forge-memory agent (Phase 4 / CKP-4 🟢)*
