# Roadmap — engram-dotnet

> **Last updated**: 2026-05-20  
> **Current version**: `main` (post Offline-First Sync Phase 4)

---

## ✅ Completed

| Feature | Commit/PR | Description |
|---------|-----------|-------------|
| Offline-First Sync | `e24fe85` | Phases 1-4: mutation journal, autosync, enrollment, observability |
| Doctor Diagnostic | `dc9e5d1` | `engram doctor` CLI + `mem_doctor` MCP tool |
| Multi-User Isolation | `80aac44` | RFC-002: `personal:{user}` namespacing, `X-Engram-User` header |
| TTL Configurable | `d4eca54` | Configurable auto-expiration by observation type |
| Promotion Level 2 | direct | `mem_promote_to_md`, `mem_sync_md_to_repo`, `mem_generate_index` |
| Verification Tools | direct | `mem_verify_artifact`, `mem_traceability` |
| Upstream Parity Phase 2 | [#8](https://github.com/efreet111/engram-dotnet/pull/8) | DELETE endpoints for sessions and prompts |
| Upstream Parity Phase 1 | [#7](https://github.com/efreet111/engram-dotnet/pull/7) | Project detection, CLI `projects list\|consolidate\|prune` |
| PostgreSQL Backend | [#3](https://github.com/efreet111/engram-dotnet/pull/3) | PostgresStore with FTS, GIN indexes, 32 IStore methods |
| Obsidian Export | [#4](https://github.com/efreet111/engram-dotnet/pull/4) | CLI exporter, hub notes, incremental sync, 47 tests |

---

## 📋 Backlog

### 🌲 Logging Infrastructure (spec created ✅)

> **Spec**: [`sdd/logging-infrastructure/specs/logging-infrastructure.md`](../sdd/logging-infrastructure/specs/logging-infrastructure.md)  
> **Effort**: 2-3h  
> **Status**: Spec ready — endpoints lack error visibility

| # | Feature | Description |
|---|---------|-------------|
| 1 | Request/Response logging middleware | Log method, path, status, duration (partial — needs POST body debug) |
| 2 | POST body error debugging | Log body preview on deserialization errors |
| 3 | Structured JSON logs | Machine-parseable log format |

---

### 🔲 Pending Features

#### Upstream Phase 2 — API Parity (backlog)

> **Status**: ~5/10 tasks done. Missing: structured errors, mem_current_project, export project filter, watch/since modes.

| Done | Missing |
|------|---------|
| `DeleteSessionAsync`, `DeletePromptAsync` (Store) | Structured error integration in tools |
| `handleDeleteSession`, `handleDeletePrompt` (Server) | `mem_current_project` MCP tool |
| `Obsidian --project` filter | `ExportProjectAsync` (store-level) |
| | `?project=` integration in server `/export` |
| | Obsidian `--watch` mode |
| | Obsidian `--since` filter |

#### PostgreSQL Backend — Bug Fixes (backlog)

> **Status**: 3 tests skipped — need investigation  
> **Effort**: 2-3h  
> **Engram**: `architecture/postgres-store-bugs`

| Test | Problem | Likely Cause |
|------|---------|-------------|
| `Search_TopicKeyShortcut_RanksFirst` | FTS5 ranking differs (0.06 vs -1000) | PostgreSQL FTS ranking function differs from SQLite |
| `DeleteSession_HasActiveObservations_Throws` | Session not found after failed delete | FK constraint rolls back in Postgres |
| `MergeProjects_ReassignsObservations` | GetObservationAsync returns null post-merge | Transaction scope or isolation level issue |

#### Phase 3 — Breaking Changes

> **Effort**: 6-8h  
> **Go upstream**: `internal/mcp/mcp.go` — project envelope + remove project from write tools

| # | Feature | Why Breaking |
|---|---------|--------------|
| 1 | Remove `project` from write tools | Agents no longer pass `project` — auto-detected via `DetectProjectFull` |
| 2 | Project envelope in responses | Every response includes `{ project, project_source, warning }` |

#### Phase 4 — Memory Relations

> **Go upstream**: `internal/store/store.go` + `internal/mcp/mcp.go` — BM25Floor, Limit, memory_relations table

| # | Feature | Description |
|---|---------|-------------|
| 1 | Memory conflict surfacing | Detect contradictory observations (same topic_key, opposite content) |
| 2 | Decay with `review_after` / `expires_at` | Uses Phase 1 columns (already exist ✅) |

#### 🐘 Giant Class Refactoring

> **Effort**: 4-6h

Split `PostgresStore.cs` (~2100 lines) and `SqliteStore.cs` (~2400 lines) by domain:
```
PostgresStore.cs          → constructor, Dispose, Migrate
PostgresStore.Sessions.cs → session CRUD
PostgresStore.Observations.cs → observation CRUD
PostgresStore.Sync.cs     → sync mutations
PostgresStore.Enrollment.cs → enrollment
```

#### Backend Config File (proposal: ✅ created)

> **Proposal**: [`sdd/backend-config-switch/proposal.md`](../sdd/backend-config-switch/proposal.md)  
> **Effort**: 4-6h

Config file `~/.engram/config.json` to switch between backends:
```json
{"backend": "sqlite", "sqlite_path": "~/.engram/engram.db"}
```

---

## 🟠 Future Ideas

### Obsidian Export — Phase B (with AI)

Specialized agent that generates synthesized documents from memories.

### Python Port

HTTP server port to Python for Python-first teams.

### Tool Deferral

Move tools from eager to deferred loading to reduce session token usage by ~40%.

**Blocked**: .NET SDK (`ModelContextProtocol` v1.2.0) doesn't have `WithDeferLoading`.

---

## 🗺️ Suggested Work Order

| Order | Feature | Effort | Why |
|-------|---------|--------|-----|
| 1 | 🌲 **Logging Infrastructure** 🔥 | 2-3h | CRITICAL — without it, all errors are invisible |
| 2 | **Upstream Phase 2 (resume)** | 4-6h | 5/10 tasks done |
| 3 | **Backend Config File** | 4-6h | Proposal ready, improves DX |
| 4 | 🐘 **Giant Class Refactoring** | 4-6h | Improves maintainability |
| 5 | **PostgreSQL Bug Fixes** | 2-3h | 3 skipped tests |
| 6 | **Phase 3 — Breaking** | 6-8h | Requires Phase 2 (structured errors) |
| 7 | **Phase 4 — Memory Relations** | 8-10h | Complex — cloud + LLM judge |
