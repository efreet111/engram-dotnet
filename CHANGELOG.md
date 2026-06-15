# Changelog

> Formato basado en [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
> Historial por PRs de GitHub para detalles completos.

## [Unreleased]

### Added

- **ENG-410**: Project identity fingerprint — UUID v5 determinista desde git remote URL + primer commit SHA. Archivo `.engram-id` en raíz del repo. `DetectionResult.ProjectId` integrado en `DetectProjectFull()`. RFC-001.
- **ENG-411**: Polly resilience pipeline para SQLITE_BUSY (SQLite error code 5). 3 retries con exponential backoff (100ms-400ms-800ms). Wraps `Exec()` y `WithTx()` en SqliteStore. Complementa `PRAGMA journal_mode=WAL` + `busy_timeout=5000` ya existentes.
- **ENG-208**: Structured MCP error responses — all 18 error sites in EngramTools.cs now return JSON with `error_code`, `message`, optional `hint`, and `available_projects`. New `McpErrors` helper enforces a 9-code catalog.
- **ENG-208**: `mem_current_project` MCP tool (already existed, now with full test coverage)
- **ENG-208**: `ExportProjectAsync` and `ExportSinceAsync` store methods. `GET /export?project=X` and `GET /export/since?after_seq=N` server endpoints.
- **ENG-208**: `engram obsidian-export --watch [--interval 30s] [--since 2025-01-01] [--project X]` — daemon mode with seq cursor when server reachable, timestamp fallback when offline. State persisted per-project.
- **ENG-208**: `engram obsidian-export --since 2025-01-01` — filter by date (ISO 8601 or relative `30d`/`7d`/`24h`)
- **ENG-208**: Per-project state files (`state-{project}.json`) for watch mode isolation
- **ENG-211**: SQLite schema migration for `sync_apply_deferred` — `AddColumnIfNotExists` for `retry_count` and `last_error` columns in Migrate(). ReplayDeferredAsync no longer fails with "no such column" on old DBs.
- **ENG-428**: Fix sync push — `JsonPullOpts` now uses `PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower`. Mutation payloads use snake_case keys (`session_id`) but were being deserialized with camelCase, causing null `session_id` and PostgresException 23502.
- **ENG-209**: Dockerized multi-client sync test (`scripts/test-2client-pull.sh`) — PostgreSQL + server + 2 clients with sync, end-to-end verification.
- **ENG-210**: Dockerized offline + reconnection test (`scripts/test-offline-reconnect.sh`) — network disconnect, 3 offline memories, reconnect, verify on server.
- **Sync enroll/unenroll**: New CLI commands `engram sync enroll --project X` and `engram sync unenroll --project X` for local sync_enrolled_projects table management.

### Fixed

- **ENG-428**: Mutation payload `session_id` not deserialized — `ObsPayload` uses `session_id` (snake_case) but `ObservationPullPayload` expected `sessionId`. Fix: `SnakeCaseLower` naming policy in both SqliteStore and PostgresStore.
- **ENG-208 audit**: `WatchLoop.cs` had `IncludePersonal=false` hardcoded; `--include-personal` flag never reached WatchConfig. Fixed.
- **ENG-208 audit**: `RunCycleAsync` overwrote exporter state (`Files`, `Version`). Fixed: exporter persists, loop only handles `LastSeq`.
- **ENG-211**: ReplayDeferredAsync "no such column" on old SQLite databases. Fixed: AddColumnIfNotExists for `retry_count` and `last_error`.

- **Logging infrastructure** — structured JSON logging with fields: `@timestamp`, `level`, `method`, `path`, `status`, `duration_ms`, `client_ip`
  - FR-LOG-01: Request/response middleware captures client_ip
  - FR-LOG-02: JSON structured output via `JsonConsoleFormatter`
  - FR-LOG-03: POST body preview on JSON deserialization errors
  - FR-LOG-04: Global exception handler returns JSON `{error, type}`
  - FR-LOG-05: `ENGRAM_LOG_LEVEL` environment variable

- **Hub MCP multi-editor** — `config/mcp/` (plantillas local + offline-sync, guías por editor, `INSTALL.md`)
- **Setup wizard** — `scripts/setup.ps1` y `scripts/setup.sh` generan configs en `config/mcp/generated/`
- **Backlog y flujo Git** — `docs/BACKLOG.md`, `docs/GIT-WORKFLOW.md`, `docs/SETUP-WIZARD.md`
- **Cursor (proyecto)** — reglas `engram-docs-on-done`, `engram-git-workflow`, skill docs-on-done, `scripts/sync-cursor-rules.*`

### Changed

- **MCP docs** — `docs/MCP-CONFIG.md`, quick start, README (enlaces backlog/git/MCP hub)
- **ROADMAP** — apunta al backlog como cola de ejecución

### Fixed

- **Sync status en servidor PostgreSQL** — `GET /sync/status` reporta `sync_enabled: true` y `phase: cloud` cuando el backend es cloud relay (antes parecía deshabilitado sin `ENGRAM_SYNC_ENABLED` en Docker)
- **Docs sync servidor vs cliente** — `SYNC-SETUP.md`, `MCP-CONFIG.md`, `POSTGRES-SETUP.md`, `docker-compose.yml`: aclaran que `ENGRAM_SYNC_*` y `ENGRAM_USER` aplican al MCP; riesgos de omitir `ENGRAM_USER` en equipos
- **PostgreSQL tests** — 2 tests arreglados (ID secuencial, ranking topic-key), 1 eliminado (FK constraint incompatibilidad Postgres)
- **Docs versiones** — corregidas de `1.1.0`/`1.0.0`/`v1.3.0` a `0.3.0` en 7 archivos

- **`mem_current_project` MCP tool** — implementado en `EngramTools.cs` (había sido declarado en v0.3.0 pero omitido del código)
- **Versión CLI** — `Program.cs` corregido de `1.1.0` a `0.3.0` para alinear con CHANGELOG y tags de release
- **Conteo MCP tools en docs** — corregido de "27" a "26" en README.md, README.es.md, ARCHITECTURE.md, DEVELOPMENT.md y MCP-TEST-CASES.md; eliminada referencia a `mem_generate_index` que no existe en el código

## [0.3.0] — 2026-05-11 — Session Activity Tracker + Phase 2 API Parity

### Added

- **`SessionActivity` tracker** — nueva clase en `Engram.Mcp` que registra uso de herramientas y saves, provee nudge prompts cuando el usuario lleva 10+ min sin persistir decisiones importantes
- **Activity scores en responses** — `mem_search`, `mem_context`, `mem_session_summary` incluyen "Session activity: N tool calls, M saves" y warnings de actividad alta
- **`mem_current_project` MCP tool** — retorna el proyecto actual detectado, su fuente (git_child, git_parent, env, manual, unknown), y advertencias para casos ambigüos
- **`DELETE /sessions/{id}` endpoint** — elimina sesión (409 si tiene observaciones activas, 404 si no existe, 200 si exitoso). Soft-deleta prompts asociados.
- **`DELETE /prompts/{id}` endpoint** — soft-delete de prompt (404 si no existe, 200 si exitoso)
- **`McpErrors.cs`** — helper para respuestas de error estructuradas con `error_code`, `message`, `available_projects`, `hint`
- **`ExportProjectAsync(project)`** — exporta solo un proyecto específico (filtro `?project=` en server y `--project` en CLI)
- **Obsidian `--since` filter** — filtro por fecha (`--since 30d`, `--since 2025-01-01`)
- **`ParseSinceArgument(string)`** — parser para formatos relativos y ISO 8601

### Changed

- **`EngramTools` — constructor DI** — ahora inyecta `SessionActivity` además de `IStore`, `McpConfig`, `WriteQueue`
- **`Program.cs` — CLI DI** — `SessionActivity` registrado como singleton con threshold de 10 minutos
- **ROADMAP.md** — actualizado con estado real post PR #8 y #11

### Fixed

- **`IStore.SearchAsync` 3-argument overload** — el overload con `IList<string> projects` ahora existe en la interfaz (antes solo en implementaciones concretas)
- **Audit de compatibilidad Go** — todas las features marcadas con estado real (completo/partial/pendiente)

### Tests Added

- `SessionActivityTests.cs` — 7 tests: RecordAndNudge, RecordSave_ResetsNudge, ActivityScore, ActivityScore_Pluralization, NoNudgeForIdleSessions, ClearSession, ConcurrentAccess
- Tests de integración para `EngramServer` (DeleteSession, DeletePrompt handlers)
- Tests para `ExportProjectAsync` en SqliteStore y PostgresStore
- Tests para `ParseSinceArgument` (ISO8601, relative, invalid)

### Verified (manual, 2026-06-04)

Smoke test + 5 regression tests ejecutados contra `http://192.168.0.178:7437` (PostgreSQL, commit `e1a9cf9`):

- **Smoke**: `/health` (v1.1.0, postgres), `/stats` (226 sessions, 522 obs, 528 prompts, 18 proyectos), `/sync/status` (sync_enabled, phase cloud), push inválido → HTTP 400
- **R1-R2** — push sin `entries` / `entries: null` → HTTP 400 `empty-batch` (fix `a0ff6ee`)
- **R3** — delete session con obs soft-deleted → HTTP 200, session borrada
- **R4** — delete session con obs activa → HTTP 409 con mensaje correcto
- **R5** — `prompts/recent` con `X-Engram-User` filtra correctamente (userA ve solo suyo, userB ve solo suyo)

Detalles en `docs/MANUAL-TESTING-CHECKLIST.md`.

## [0.2.0] — 2026-04-30 — PostgreSQL Backend + Upstream Phase 1

### Added

- **`PostgresStore`** — backend PostgreSQL con FTS5, GIN indexes, testcontainers para CI
- **Project detection 5-case** — `DetectProjectFull` con casos: git_child, git_parent, ambiguous (múltiples .git), env override, manual
- **Schema columns** — `review_after`, `expires_at`, `embedding` en tabla observations
- **Write queue** — `WriteQueue<T>` con Channel<T> para serialización asíncrona de writes
- **SessionActivity tracker** — Phase 1 (básico, restaurado y completado en v0.3.0)
- **`mem_merge_projects` tool** — merge de múltiples proyectos en uno canónico
- **`SuggestTopicKey` helper** — sugiere topic_key basado en contenido
- **`BackendName`** en `/health` y `/stats` — indica si usa SQLite o PostgreSQL

### Changed

- **`EngramServer`** — nuevos endpoints `DELETE /sessions/{id}`, `DELETE /prompts/{id}`, `GET /export?project=`
- **`SqliteStore`** — consistencia completa con `IStore` interface

## [0.1.0] — 2026-04-20 — Obsidian Export

### Added

- **CLI exporter** — `engram obsidian export` con exportación completa a formato Obsidian
- **Hub notes** — generación de `index.md` con resumen por proyecto
- **Incremental sync** — detecta qué archivos cambiaron desde última exportación
- **Graph JSON** — `graph.json` compatible con Obsidian Graph View
- **61 tests** de integración para el exporter
- **CLI `projects` command** — `list`, `consolidate`, `prune` para gestión de project drift
- **Levenshtein distance** — para sugerencia de correcciones ortográficas en project names
- **`FindSimilar` algorithm** — detecta proyectos con nombres similares pero distintos

## Prior releases

> Releases anteriores a v0.1.0 fueron prototipos internos sin changelog formal.

[unreleased]: https://github.com/efreet111/engram-dotnet/compare/v0.3.0...HEAD
[0.3.0]: https://github.com/efreet111/engram-dotnet/releases/tag/v0.3.0
[0.2.0]: https://github.com/efreet111/engram-dotnet/releases/tag/v0.2.0
[0.1.0]: https://github.com/efreet111/engram-dotnet/releases/tag/v0.1.0