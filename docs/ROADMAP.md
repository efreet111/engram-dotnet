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

> **Status**: 3 tests skipped + 1 connection pooling issue  
> **Effort**: 4-6h total  
> **Engram**: `architecture/postgres-store-bugs`

| # | Bug | Problem | Fix | Effort |
|---|-----|---------|-----|--------|
| 1 | Connection pooling | `NpgsqlOperationInProgressException` cuando SyncManager y HTTP compiten por la misma conexión | Usar `NpgsqlConnection` pool (crear conexión por operación) | 2-3h |
| 2 | FTS5 ranking | `Search_TopicKeyShortcut_RanksFirst` espera -1000, Postgres devuelve 0.06 | Actualizar valor esperado del test | 30min |
| 3 | FK rollback | `DeleteSession_HasActiveObservations_Throws` — Postgres hace rollback en FK violation, SQLite no | Ajustar test o usar SAVEPOINT | 1h |
| 4 | Transaction visibility | `MergeProjects_ReassignsObservations` — GetObservationAsync devuelve null post-merge | Usar misma transacción o REFRESH | 1h |

Specs: [`sdd/postgres-bug-fixes/`](../sdd/postgres-bug-fixes/)

---

#### Docker Deployment Fix

> **Effort**: 5min  
> **Spec**: [`sdd/docker-deployment-fix/`](../sdd/docker-deployment-fix/)

| # | Issue | Fix |
|---|-------|-----|
| 1 | `mem_sync_md_to_repo` falla con permission denied en `/app/docs` | Agregar `RUN mkdir -p /app/docs` en Dockerfile |

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

### 🔐 Authentication & Access Control

> **Effort**: 4-6h  
> **Status**: Backlog — no proposal yet

Add user/password authentication to protect the server from unauthorized access. Critical for teams that don't use VPN.

| # | Feature | Description |
|---|---------|-------------|
| 1 | Basic auth or API key | Simple token-based auth for REST endpoints |
| 2 | User registration | `POST /auth/register` with username + password |
| 3 | Role-based access | Admin vs regular user (pause/resume requires admin) |
| 4 | Session auth | JWT or cookie-based auth for web clients |

**Why**: Currently any device on the network can access the server. A VPN is the recommended approach, but many teams don't use one. Auth would make the server safe to expose on the internet.

---

## 🧪 Manual Testing Backlog

> Test cases that require a second developer or specific setup. Do not close until verified.

| # | Test Case | How to verify | Requires | Status |
|---|-----------|---------------|----------|--------|
| 1 | **Pull entre 2 clientes** | Dev1 crea memoria local → SyncManager push → Dev2 hace pull → Dev2 ve la memoria | 2 developers | 🔲 |
| 2 | **Offline + reconexión** | Dev1 offline → crea 3 memorias → reconecta → aparecen en server | Server restart | 🔲 |
| 3 | **MCP Tools** | Ejecutar `docs/MCP-TEST-CASES.md` (17 casos) | — | ✅ 16/17 passing |
| 4 | **CLI commands** | `engram search`, `save`, `doctor`, `export`, `import` | Tiempo dedicado | 🔲 |
| 5 | **REST API smoke test** | Probar los 41 endpoints (al menos GET/POST principales) | Checklist en API-REFERENCE | 🔲 |

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
