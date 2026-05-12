[← Volver al README](../README.md)

# Roadmap — engram-dotnet

> Backlog de mejoras y features planificadas. Este documento es vivo — las ideas se mueven a issues de GitHub cuando están listas para implementación.
>
> **Última actualización**: 2026-05-12
> **Versión actual**: `main` (post PR #11 — Session Activity Phase 4 merged)

---

## ✅ Completadas

| Feature | PR | Descripción |
|---------|----|-------------|
| Upstream Parity Phase 2 — API Parity (Delete, mem_current_project, Structured Errors, Obsidian --watch/--since/--project) | [#8](https://github.com/efreet111/engram-dotnet/pull/8), [#11](https://github.com/efreet111/engram-dotnet/pull/11) | Delete endpoints (sessions/prompts), MemCurrentProject MCP tool, Structured MCP errors, Obsidian watch/since/project filters, ExportProjectAsync, EngramServer DELETE routes |
| Upstream Parity Phase 1 — Session Activity Tracker | [#11](https://github.com/efreet111/engram-dotnet/pull/11) | SessionActivity tracker, nudge prompts, activity scores, upstream conflict surfacing |
| Upstream Parity Phase 1 — Project Detection Fixes | [#7](https://github.com/efreet111/engram-dotnet/pull/7) | DetectProject, FindSimilar, Levenshtein, CLI `projects list\|consolidate\|prune` |
| PostgreSQL Backend | [#3](https://github.com/efreet111/engram-dotnet/pull/3) | PostgresStore con FTS, GIN indexes, 22 métodos IStore, Testcontainers |
| Obsidian Export | [#4](https://github.com/efreet111/engram-dotnet/pull/4) | CLI exporter, hub notes, incremental sync, graph.json, 61 tests |

---

## 🚀 Sprint Activo — Doctor Diagnostic

> **Proposal**: [`sdd/doctor-diagnostic/proposal.md`](sdd/doctor-diagnostic/propose/proposal.md)
> **Esfuerzo estimado**: 4-6h

Operational diagnostics and repair tools — port from Go upstream (`internal/diagnostic/` + `cmd/engram/doctor.go`).

| # | Feature | Descripción |
|---|---------|-------------|
| 1 | Check registry | Ejecutar checks individuales (DB integrity, sync state, orphan chunks) |
| 2 | Repair actions | Auto-fix para problemas detectables (reconstruir índice, limpiar orphans) |
| 3 | `engram doctor` CLI | Comando unificado `check` + `repair` con output estructurado |

**Go source**: `internal/diagnostic/`, `cmd/engram/doctor.go` (~1264 líneas Go → ~800cs .NET)

---

### Offline-First Sync (planned)

> **Feature Index**: [`docs/OFFLINE-FIRST-SYNC.md`](OFFLINE-FIRST-SYNC.md)
> **Branch**: [`feat/offline-first-sync`](https://github.com/efreet111/engram-dotnet/tree/feat/offline-first-sync)
> **PR**: [#14](https://github.com/efreet111/engram-dotnet/pull/14)
> **SDD Artifact**: [`sdd/offline-first-sync/`](sdd/offline-first-sync/)
> **Esfuerzo estimado**: **32-44h** (4 fases)

Team sync: local SQLite ↔ PostgreSQL server (TrueNAS `192.168.0.178:7437`).
Local es source of truth offline, server es source of truth online. Last-write-wins.

> ⚠️ **Estimaciones corregidas vs propuesta original**. Análisis comparativo contra Go upstream reveló que Phase 1 y Phase 2 estaban subestimadas.

| Fase | Contenido | Esfuerzo | Artefactos |
|------|-----------|----------|------------|
| 1 | Mutation journal + server endpoints (MVP push/pull) | **10-14h** | [`proposal.md`](sdd/offline-first-sync/propose/proposal.md) §Phase 1 |
| 2 | Autosync manager + debounce + backoff | **12-16h** | [`design.md`](sdd/offline-first-sync/design/design.md) §AD-5 |
| 3 | Enrollment + conflict resolution (deferred replay) | **6-8h** | [`tasks.md`](sdd/offline-first-sync/tasks/tasks.md) §Phase 3 |
| 4 | Dashboard + observability + CLI | **4-6h** | [`tasks.md`](sdd/offline-first-sync/tasks/tasks.md) §Phase 4 |

**Go reference**: `internal/sync/sync.go` (1324l), `internal/cloud/remote/transport.go` (421l),
`internal/cloud/autosync/manager.go` (703l), `internal/cloud/cloudserver/mutations.go` (349l)

**API contracts** (alineados con Go upstream):
```
POST /sync/mutations/push   → body: { entries: [...] }, response: { accepted_seqs, project, project_source, project_path }
GET  /sync/mutations/pull  → query: since_seq, project, limit → response: { mutations, has_more, latest_seq, project, project_source, project_path }
```

**7 Architecture Decisions documentadas** en [`design.md`](sdd/offline-first-sync/design/design.md):
AD-1 Transport location, AD-2 IHttpClientFactory, AD-3 CloudEndpoints separation, AD-4 ICloudMutationStore, AD-5 SyncManager BackgroundService, AD-6 ILocalSyncStore, AD-7 FK deferral

---

## 📋 Backlog — Features Prioritarias

### Upstream Parity Phase 2 — API Parity (backlog, ~18/50 tasks done)

> Moved to `sdd/archive/2026-05-11-upstream-parity-phase2-backlog/`
> Remaining work: ~27 RED unit tests + integration. ~4-6h effort.

| Lo que ya está hecho | Lo que falta |
|---------------------|--------------|
| `DeleteSessionAsync`, `DeletePromptAsync` (Store) | RED test cycle for all handlers |
| `handleDeleteSession`, `handleDeletePrompt` (Server) | Structured error integration in tools |
| `McpErrors.cs`, `ExportProjectAsync` | Watch mode integration tests |
| Obsidian `--since`, `--project` filters (partial) | Full `?project=` integration in server |

**Resume strategy**: Start with Phase 3 tests (mem_current_project) — feature is already done.

### Phase 3 — Breaking Changes (Go: ✅ done, .NET: ❌ pending)

> Cambios que rompen compatibilidad con la API actual. Requieren versión semver mayor.
> **Go upstream**: `internal/mcp/mcp.go` — project envelope + remove project from write tools

| # | Feature | Por qué es breaking |
|---|---------|-------------------|
| 1 | Remover `project` de write tools | Los agentes ya no pasan `project` — se detecta automáticamente via `DetectProjectFull` |
| 2 | Project envelope en responses | Cada respuesta incluye `{ project, project_source, warning }` — cambia el JSON de salida |

**Dependencias**: Requiere Phase 2 (structured errors como base).

---

### Phase 4 — Memory Relations (Go: ✅ Phases 1-4, .NET: ❌ pending)

| # | Feature | Descripción |
|---|---------|-------------|
| 1 | Memory conflict surfacing | Detectar observaciones contradictorias (mismo topic_key, contenido opuesto) |
| 2 | Decay con `review_after` / `expires_at` | Usar las columnas de Phase 1 para expiración automática |

**Go upstream**: `internal/store/store.go` + `internal/mcp/mcp.go` — BM25Floor, Limit, memory_relations table
**Dependencias**: Columnas `review_after`, `expires_at` ya existen ✅

### TTL Configurable por Tipo (proposal: ✅ creada)

> **Proposal**: [`sdd/ttl-configurable/propose/proposal.md`](../sdd/ttl-configurable/propose/proposal.md)
> **Branch**: `feat/ttl-configurable` (por crear)
> **Esfuerzo estimado**: 3-4h

Permitir configurar expiración automática por tipo de observación:

| Tipo | TTL sugerido |
|------|-------------|
| `tool_use`, `file_change`, `command` | 30 días |
| `decision`, `architecture` | Nunca |
| `bugfix`, `pattern` | 90 días |
| `learning`, `discovery` | 60 días |

**Por qué**: Las observaciones se acumulan indefinidamente. Contexto viejo contamina búsquedas.

---

### Backend Config File (proposal: ✅ creada)

> **Proposal**: [`sdd/backend-config-switch/proposal.md`](../sdd/backend-config-switch/proposal.md)
> **Branch**: `feat/backend-config-file` (por crear)
> **Esfuerzo estimado**: 4-6h

Archivo `~/.engram/config.json` para cambiar entre backends con un solo valor:

```json
{
  "backend": "sqlite",
  "sqlite_path": "~/.engram/engram.db"
}
```

Precedencia: env vars > config file > defaults.

---

### Offline-First Sync (proposal: ✅ creada)

> **Proposal**: [`sdd/offline-first-sync/propose/proposal.md`](sdd/offline-first-sync/propose/proposal.md)
> **Branch**: `feat/offline-first-sync`
> **Esfuerzo estimado**: 22-30h (4 fases)

Bidireccional local SQLite ↔ servidor PostgreSQL (TrueNAS `192.168.0.178:7437`).

**Problema**: Con `ENGRAM_URL` todo va directo al servidor. Si se cae la red, el agente pierde la capacidad de guardar memoria.

**Arquitectura**:
1. Offline: escribe en SQLite local, registra `sync_mutations`
2. Online: `SyncManager` hace push/pull en background
3. Server: `POST /sync/mutations/push` y `GET /sync/mutations/pull?since_seq=N`
4. Conflict resolution: last-write-wins por timestamp

**Fases**:
- **Fase 1** (6-8h): Mutation journal + server endpoints (MVP)
- **Fase 2** (8-10h): Autosync manager + debounce + backoff
- **Fase 3** (4-6h): Enrollment + last-write-wins
- **Fase 4** (4-6h): Dashboard + observability + CLI

**Go reference**: `internal/sync/sync.go`, `internal/cloud/remote/transport.go`, `internal/cloud/autosync/manager.go`

---

### Phase 5 — Multi-User Isolation (proposal: ✅ creada)
> **RFC**: [`docs/rfcs/RFC-002-multi-user-isolation.md`](docs/rfcs/RFC-002-multi-user-isolation.md)
> **Esfuerzo estimado**: 4-5h

Aislamiento de scopes personales mediante identidad del usuario.

| # | Feature | Descripción |
|---|---------|-------------|
| 1 | `X-Engram-User` Header | Captura de identidad en middleware/handlers |
| 2 | Internal Namespacing | Transformación de `personal` -> `personal:{user}` |
| 3 | Multi-tenant tests | Tests de integración con múltiples identidades |

---

### Herramienta Administrativa CLI (proposal: ❌ no creada)


> **Esfuerzo estimado**: 4-6h

CLI de emergencia para gestión manual:
- Listar observaciones con filtros (project, scope, type, rango de fechas)
- Eliminar por ID o por criterio (proyecto entero, rango de fechas)
- Ver duplicados y observaciones con bajo valor

**Alcance**: No es UI cotidiana. Es herramienta de emergencia para limpieza.

---

### Observability Básica (proposal: ❌ no creada)

> **Esfuerzo estimado**: 2-3h

Endpoint `/metrics` con:
- Tiempos de respuesta por operación (search, save, context)
- Tamaño actual de la base de datos
- Queries por segundo
- Observaciones por proyecto/scope

---

### Tool Deferral (investigación)

> **Estado**: En investigación — no empezar hasta tener datos de tokens

Mover herramientas de eager a deferred loading para reducir tokens de inicio de sesión en ~40%.

**Core** (siempre disponibles): `mem_save`, `mem_search`, `mem_context`, `mem_session_summary`, `mem_get_observation`, `mem_save_prompt`

**Deferred**: `mem_update`, `mem_suggest_topic_key`, `mem_session_start`, `mem_session_end`, `mem_stats`, `mem_delete`, `mem_timeline`, `mem_capture_passive`

**Bloqueo**: El SDK .NET (`ModelContextProtocol` v1.2.0) no tiene `WithDeferLoading` como el SDK de Go. Se necesitaría implementar via separación de clases.

**Prerrequisito**: Medir consumo de tokens actual por herramienta antes de optimizar.

---

## 🟠 Fase Posterior (ideas)

### Obsidian Export — Fase B (con IA)

Agente especializado que genera documentos sintetizados:
- Agrupa bugfixes relacionados → *"Patrones de error comunes en el módulo X"*
- Agrupa decisiones de arquitectura → *"Evolución del diseño del sistema de memoria"*
- Genera glosario técnico del proyecto

Requiere LLM, tiene costo de tokens. Justifica su propio ciclo SDD.

### Python Port

Port del servidor HTTP a Python para equipos con stack Python-first.

**Tiene sentido si**: Quieren embeddings semánticos (buscar por similitud conceptual).
**NO tiene sentido si**: Solo quieren almacenamiento + FTS5 — el beneficio es marginal.

---

## 🔧 Mantenimiento

| Tarea | Esfuerzo | Estado |
|-------|----------|--------|
| Limpiar ramas merged remotas | 5min | ✅ Hecho (2026-04-30) |
| Rebuild binario local MCP | 15min | ✅ Hecho (2026-05-11) |
| Archivar upstream-parity-phase1 y phase2 | 10min | ✅ Hecho (2026-05-11) |
| Publicar release notes post PR #11 | 10min | ✅ Hecho (2026-05-11) — CHANGELOG.md creado |

## 🗺️ Orden Sugerido de Trabajo

| Orden | Feature | Por qué primero |
|-------|---------|----------------|
| 1 | **Doctor Diagnostic** | Go upstream tiene impl completa (~1264 líneas). Feature isolated, no deps. |
| 2 | **Offline-First Sync** | Team necesita sync. ~22-30h, 4 fases. Prioridad alta para equipo. |
| 3 | **TTL Configurable** | Proposal ya existe. Independiente. |
| 4 | **Backend Config File** | Proposal ya existe. Mejora DX significativamente. |
| 5 | **Upstream Phase 2 (resume)** | ~18/50 tasks done, 4-6h para completar. |
| 6 | **Offline-First Sync (Phase 2-4)** | Autosync, debounce, enrollment, dashboard. |
| 7 | **Phase 3 — Breaking** | Requiere Phase 2. Cambia contratos de API. |
| 8 | **Phase 4 — Memory Relations** | Go upstream ✅ done. Complex — cloud + LLM judge. |
| 9 | **Observability** | Útil pero no bloqueante. |
| 10 | **Tool Deferral** | SDK .NET no soporta — en investigación. |
