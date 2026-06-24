# Roadmap — engram-dotnet

> **Last updated**: 2026-06-01  
> **Current version**: `main` (post MCP sync fix + multi-editor setup)

**Orden de trabajo (qué hacer ahora):** [BACKLOG.md](BACKLOG.md) — cola única con IDs `ENG-xxx`.  
**Al cerrar un ítem:** checklist en [`.cursor/skills/engram-docs-on-done/SKILL.md`](../.cursor/skills/engram-docs-on-done/SKILL.md).  
Este ROADMAP es visión y contexto; no sustituye la cola.

---

## ✅ Completed

| Feature | Commit/PR | Description |
|---------|-----------|-------------|
| Connection pooling | `2806c30` | NpgsqlConnection → NpgsqlDataSource for thread-safe DB access |
| OpenStore() HttpStore fix | `57937d5` | CLI commands now use ENGRAM_URL when configured |
| Sync cursor fix | `5a5b360` | last_acked_seq/last_pulled_seq now advance after push/pull |
| Bug specs documented | `923b295` | SDD docs for connection pooling, tests, Docker permissions |
| Offline-First Sync | `e24fe85` | Phases 1-4: mutation journal, autosync, enrollment, observability |
| Doctor Diagnostic | `dc9e5d1` | `engram doctor` CLI + `mem_doctor` MCP tool |
| Multi-User Isolation | `80aac44` | RFC-002: `personal:{user}` namespacing, `X-Engram-User` header |
| TTL Configurable | `d4eca54` | Configurable auto-expiration by observation type |
| Promotion Level 2 | direct | `mem_promote_to_md`, `mem_sync_md_to_repo`, `mem_generate_index` |
| Verification Tools | direct | `mem_verify_artifact`, `mem_traceability` |
| Upstream Parity Phase 2 | [#8](https://github.com/efreet111/engram-dotnet/pull/8) | DELETE endpoints for sessions and prompts |
| Upstream Parity Phase 2 (ENG-208) | `e7e5736` | Structured MCP errors, server-side incremental export, --watch, --since, --project filtering |
| Upstream Parity Phase 1 | [#7](https://github.com/efreet111/engram-dotnet/pull/7) | Project detection, CLI `projects list\|consolidate\|prune` |
| PostgreSQL Backend | [#3](https://github.com/efreet111/engram-dotnet/pull/3) | PostgresStore with FTS, GIN indexes, 32 IStore methods |
| Obsidian Export | [#4](https://github.com/efreet111/engram-dotnet/pull/4) | CLI exporter, hub notes, incremental sync, 47 tests |
| Doc/code sync + MCP tools | `69e83d7` | Version 0.3.0, 26 tools, `mem_current_project`, Obsidian CRLF |
| Docker MCP permissions | `bf01f18` | `/app/docs` en ambos Dockerfiles |
| MCP sync startup fix | `a7f45eb` | `engram mcp` + `ENGRAM_SYNC_ENABLED` sin crash DI |
| MCP multi-editor setup | (pending commit) | `config/mcp/`, `scripts/setup.ps1`, `INSTALL.md` |

---

## 📋 Backlog

> **Ejecutar en orden:** [BACKLOG.md](BACKLOG.md). Lo siguiente es resumen por tema (puede estar desactualizado vs la cola).

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

#### Upstream Phase 2 — API Parity ✅ DONE

> **Status**: All 10 features complete. Ver [ENG-208](BACKLOG.md#cola-de-ejecución).

| Done | Status |
|------|--------|
| `DeleteSessionAsync`, `DeletePromptAsync` (Store) | ✅ |
| `handleDeleteSession`, `handleDeletePrompt` (Server) | ✅ |
| `mem_current_project` MCP tool | ✅ |
| `McpErrors.cs` helper + 19 migrations | ✅ |
| `ExportProjectAsync` (store-level) | ✅ |
| `Obsidian --since` filter | ✅ |
| `Obsidian --project` filter | ✅ |
| `?project=` server integration | ✅ |
| `GET /export/since` endpoint | ✅ |
| Obsidian `--watch` mode | ✅ |

#### PostgreSQL Backend — Bug Fixes (backlog)

> **Status**: 3 tests skipped + 1 connection pooling issue  
> **Effort**: 4-6h total  
> **Engram**: `architecture/postgres-store-bugs`

| # | Bug | Problem | Fix | Effort |
|---|-----|---------|-----|--------|
| 1 | ~~Connection pooling~~ | ~~SyncManager + HTTP misma conexión~~ | ✅ NpgsqlDataSource (`2806c30`) | — |
| 2 | FTS5 ranking | `Search_TopicKeyShortcut_RanksFirst` espera -1000, Postgres devuelve 0.06 | Actualizar valor esperado del test | 30min |
| 3 | FK rollback | `DeleteSession_HasActiveObservations_Throws` — Postgres hace rollback en FK violation, SQLite no | Ajustar test o usar SAVEPOINT | 1h |
| 4 | Transaction visibility | `MergeProjects_ReassignsObservations` — GetObservationAsync devuelve null post-merge | Usar misma transacción o REFRESH | 1h |

Specs: [`sdd/postgres-bug-fixes/`](../sdd/postgres-bug-fixes/)

---

#### ~~Docker Deployment Fix~~ ✅ **COMPLETED**

> **Effort**: 5min  
> **Spec**: [`sdd/docker-deployment-fix/`](../sdd/docker-deployment-fix/)

| # | Issue | Fix | Status |
|---|-------|-----|--------|
| 1 | `mem_sync_md_to_repo` falla con permission denied en `/app/docs` | `RUN mkdir -p /app/docs && chown engram:engram /app/docs` en ambos Dockerfiles | ✅ Fixed |

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

---

### 🔄 Offline-First — Revisar flujo real vs documentado

> **Origen**: sesión `critical-rest-api-bugfix` (2026-06-01) — [SESSION-REPORT](SESSION-REPORT-2026-05-31-REST-API-BUGFIX.md)  
> **Status**: 🔲 **Investigar** — solo roadmap; no implementar aún  
> **Relacionado**: [ENG-210](BACKLOG.md) (test manual offline), [SYNC-SETUP.md](SYNC-SETUP.md), [MCP-CONFIG.md](MCP-CONFIG.md)

**Problema observado:** en la práctica muchas operaciones van **directo al servidor** (`http://192.168.0.178:7437` — curl, health, regression REST), pero el diseño offline-first dice **local primero**, sync al server en background.

| Capa | Comportamiento esperado (docs) | Lo que pasó en la sesión |
|------|-------------------------------|---------------------------|
| **MCP / `mem_save`** | SQLite local (`~/.engram`) → SyncManager → push | `engram save` → local #2444 ✅; push al server **no verificado** |
| **Testing manual REST** | Smoke al server (OK para API cloud) | Todo contra TrueNAS — correcto para *servidor*, no prueba el camino *cliente* |
| **Variables** | `ENGRAM_SERVER_URL` = sync; `ENGRAM_URL` = remoto directo (HttpStore) | MCP de Gantz bien configurado (local+sync); agente Cursor a veces sin tools Engram expuestas |
| **TrueNAS** | Solo PostgreSQL + API `/sync/*`; **no** SQLite local | Deploy + migración PG — rol correcto como *cloud* |

**Flujo canónico (recordatorio):**

```
Dev: mem_save / engram save  →  SQLite local (instantáneo, offline OK)
         ↓
     sync_mutations (journal)
         ↓
     SyncManager (en engram mcp o engram serve + ENGRAM_SYNC_ENABLED)
         ↓
     POST /sync/mutations/push  →  TrueNAS PostgreSQL
         ↓
     Otros devs: GET pull  →  su SQLite local
```

**Alcance de la revisión (futuro):**

| # | Pregunta | Por qué importa |
|---|----------|-----------------|
| 1 | ¿`engram save` (CLI) encola mutaciones sin `engram serve`/`mcp` corriendo? | `/flow-close` guardó local pero search en server = `[]` |
| 2 | ¿Cuándo arranca SyncManager — solo `serve`, también `mcp`, también CLI? | Documentación vs código |
| 3 | Matriz "quién habla con quién" (dev MCP, CLI, CI, TrueNAS, curl manual) | Evitar confundir *test de API cloud* con *test de sync* |
| 4 | Checklist: separar pruebas **REST server** vs **offline-first client** | [MANUAL-TESTING-CHECKLIST.md](MANUAL-TESTING-CHECKLIST.md) hoy mezcla ambos |
| 5 | E2E: offline → 3 saves → reconectar → aparecen en server | [ENG-210](BACKLOG.md), roadmap manual test #2 |

**No confundir:**

- **Servidor arriba** (TrueNAS `:7437`) ≠ **sync completado** (obs local puede no estar en PG aún).
- **Docs en Git** (session report, checklist) ≠ **memoria Engram** (SQLite + sync).

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
> **[Checklist detallado → MANUAL-TESTING-CHECKLIST.md](MANUAL-TESTING-CHECKLIST.md)** (41 endpoints individuales con trazabilidad)

| # | Test Case | How to verify | Requires | Status |
|---|-----------|---------------|----------|--------|
| 1 | **Pull entre 2 clientes** | Dev1 crea memoria local → SyncManager push → Dev2 hace pull → Dev2 ve la memoria | 2 developers | 🔲 |
| 2 | **Offline + reconexión** | Dev1 offline → crea 3 memorias → reconecta → aparecen en server | Server restart | 🔲 |
| 3 | **MCP Tools (26 tools)** | Ver [MANUAL-TESTING-CHECKLIST.md](MANUAL-TESTING-CHECKLIST.md) | curl / MCP | 🔲 Sin trazabilidad |
| 4 | **CLI commands** | search, save, doctor, export, stats, context, projects | ✅ | 🔲 Sin trazabilidad |
| 5 | **REST API smoke test** | 33 core + 8 sync endpoints | ✅ | 🔲 Sin trazabilidad — ver checklist detallado |
| 6 | **Sync endpoints** | enroll, status, push/pull, pause/resume | curl | 🔲 Sin probar |
| 7 | ⭐ `/sync/status` (fix v2) | `phase: cloud`, `sync_enabled: true` en Postgres | curl | ✅ Verificado 2026-05-28 |

---

## 🗺️ Suggested Work Order

**Reemplazado por la cola única:** [BACKLOG.md — Cola de ejecución](BACKLOG.md#cola-de-ejecución).

Resumen P0/P1 (mayo–junio 2026):

1. ✅  ~~Cerrar commit pendiente MCP/setup (ENG-201)~~
2. ✅  ~~OSS + templates GitHub (ENG-202–203)~~
3. ✅  ~~Pinear MCP SDK + auditoría docs (ENG-204–205)~~
4. ✅  ~~PostgreSQL tests (ENG-206)~~  
5. 🔲 Logging infrastructure (ENG-207)
6. ✅ Instalador (ENG-301 — done in FlowForge v0.1.0-alpha.2); 🔲 wizard/unified-guide (ENG-302, ENG-303 pending)
