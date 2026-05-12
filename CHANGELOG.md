# Changelog

> Formato basado en [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
> Historial por PRs de GitHub para detalles completos.

## [Unreleased]

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