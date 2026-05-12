# Archive Report: upstream-parity-phase2 (Backlog)

**Status**: ⬜ MOVED TO BACKLOG
**Date Archived**: 2026-05-11
**Change Name**: `upstream-parity-phase2`
**Moved to**: `sdd/archive/2026-05-11-upstream-parity-phase2-backlog/`

---

## Executive Summary

`upstream-parity-phase2` was the second phase of Go upstream parity, focused on API additions (delete endpoints, mem_current_project, structured errors, Obsidian filters).

**~18/45 tasks completed** — implementation is partially done but ~27 RED tests remain. The change is not blocking anything critical and has been moved to backlog to free up SDD space for new priorities.

---

## Completion Status

| Layer | Tasks | Complete | Pending |
|-------|-------|----------|---------|
| Store (DeleteSession, DeletePrompt) | 5 | 3 | 2 |
| Server (DELETE routes) | 4 | 2 | 2 |
| MCP (mem_current_project, McpErrors) | 4 | 2 | 2 |
| Obsidian (watch, since, project) | 7 | 4 | 3 |
| Integration + Docs | 5 | 1 | 4 |
| **Total** | **50** | **~18** | **~27** |

---

## What WAS Completed (and merged to main)

- `DeleteSessionAsync` in SqliteStore + PostgresStore ✅
- `DeletePromptAsync` in SqliteStore + PostgresStore ✅
- `handleDeleteSession` + `handleDeletePrompt` in EngramServer ✅
- `McpErrors.cs` helper class ✅
- `ParseSinceArgument`, `ExportProjectAsync` ✅
- Obsidian `--since` and `--project` filters (partial) ✅
- Watch loop skeleton (partial) ✅

---

## What Remains (Backlog)

- ~27 RED unit tests (TDD cycle incomplete)
- Structured error integration in write/read tools
- Watch mode integration tests
- Full Obsidian watch + since + project integration
- Server `GET /export?project=` integration tests
- Docs updates (README, DOCS.md)

---

## Dependencies

- Depends on `upstream-parity-phase1` columns (review_after, expires_at) — already done ✅
- No cloud dependencies
- No breaking changes

---

## Recommended Priority for Future

This change is **additive** (no breaking changes) and **low risk**. The remaining work is primarily completing the TDD RED→GREEN cycle (~27 tests).

Estimated remaining effort: **4-6h** of test writing + integration.

If resumed: start with Phase 3 tests (mem_current_project) since that feature is already done.