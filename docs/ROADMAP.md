[← Volver al README](../README.md)

# Roadmap — engram-dotnet

> Backlog de mejoras y features planificadas. Este documento es vivo — las ideas se mueven a issues de GitHub cuando están listas para implementación.
>
> **Última actualización**: 2026-04-30
> **Versión actual**: `main` (post PR #7)

---

## ✅ Completadas

| Feature | PR | Descripción |
|---------|----|-------------|
| Project Drift Detection | [#2](https://github.com/efreet111/engram-dotnet/pull/2) | DetectProject, FindSimilar, Levenshtein, CLI `projects list\|consolidate\|prune` |
| PostgreSQL Backend | [#3](https://github.com/efreet111/engram-dotnet/pull/3) | PostgresStore con FTS, GIN indexes, 22 métodos IStore, Testcontainers |
| Obsidian Export | [#4](https://github.com/efreet111/engram-dotnet/pull/4) | CLI exporter, hub notes, incremental sync, graph.json, 61 tests |
| Upstream Parity Phase 1 | [#7](https://github.com/efreet111/engram-dotnet/pull/7) | Project detection 5-case, schema columns, write queue, session activity tracker |

---

## 🚀 Sprint Activo — Upstream Parity

Portear cambios del Go upstream (v1.12 → v1.14.8, 61 commits) en fases incrementales.

### Phase 2 — API Parity (proposal: ✅ creada)

> **Proposal**: [`sdd/upstream-parity-phase2/proposal.md`](../sdd/upstream-parity-phase2/proposal.md)
> **Branch**: `feat/upstream-parity-phase2` (por crear)
> **Esfuerzo estimado**: 6-8h

| # | Feature | Alcance | Archivos |
|---|---------|---------|----------|
| 1 | `DELETE /sessions/{id}` | Borrar sesión con FK guard (409 si tiene observaciones) | Server, Store |
| 2 | `DELETE /prompts/{id}` | Soft-delete de prompts | Server, Store |
| 3 | `mem_current_project` | MCP tool read-only — dice qué proyecto se va a usar | MCP |
| 4 | Structured error responses | Errores MCP con `error_code`, `available_projects`, `hint` | MCP |
| 5 | Obsidian `--watch` | Daemon continuo con `--interval` configurable | CLI, Obsidian |
| 6 | Obsidian `--since` | Filtro por fecha (`30d`, `7d`, `2025-01-01`) | CLI, Obsidian |
| 7 | Export por proyecto | `GET /export?project=X` — un solo proyecto | Server, Obsidian |

**Out of scope**: Remover `project` de write tools (Phase 3), project envelope (Phase 3).

---

### Phase 3 — Breaking Changes (proposal: ❌ no creada)

> Cambios que rompen compatibilidad con la API actual. Requieren versión semver mayor o al menos menor con nota de breaking.

| # | Feature | Por qué es breaking |
|---|---------|-------------------|
| 1 | Remover `project` de write tools | Los agentes ya no pasan `project` — se detecta automáticamente via `DetectProjectFull` |
| 2 | Project envelope en responses | Cada respuesta incluye `{ project, project_source, warning }` — cambia el JSON de salida |

**Dependencias**: Requiere Phase 2 completada (structured errors como base).

---

### Phase 4 — Memory Relations (proposal: ❌ no creada)

| # | Feature | Descripción |
|---|---------|-------------|
| 1 | Memory conflict surfacing | Detectar observaciones contradictorias entre sesiones (mismo topic_key, contenido opuesto) |
| 2 | Decay con `review_after` / `expires_at` | Usar las columnas de Phase 1 para expiración automática y sugerir revisiones |

**Dependencias**: Requiere Phase 1 completada (columnas `review_after`, `expires_at` ya existen).

---

## 📋 Backlog — Features Independientes

Features que no dependen del upstream parity y pueden trabajarse en paralelo.

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

### Offline-First Sync (proposal: ❌ no creada)

> **Origen**: Discusión 2026-04-29
> **Esfuerzo estimado**: ~900 líneas, 3 fases

Bidireccional local SQLite ↔ servidor PostgreSQL.

**Problema**: Con `ENGRAM_URL` todo va directo al servidor. Si se cae la red, el agente pierde la capacidad de guardar memoria.

**Arquitectura**:
1. Siempre escribe en SQLite local (source of truth primaria)
2. Si `ENGRAM_URL` está set y hay conexión → `SyncWorker` hace push/pull en background
3. Server necesita: `POST /sync/push` y `GET /sync/pull?since={seq}`
4. Conflict resolution: last-write-wins por timestamp

**Infraestructura existente** (ya implementada, nunca consumida):
- `sync_mutations` journal en SQLite
- `sync_state` table con lifecycle por target
- `sync_id` en cada entidad
- `ExportAsync()` / `ImportAsync()`
- `EngramSync` class con chunks JSONL

**Fases**:
- **Fase 1** (Push): ~400 líneas — consume `sync_mutations`, envía al servidor
- **Fase 2** (Pull): ~300 líneas — baja cambios de otros devs
- **Fase 3** (CLI): ~200 líneas — `engram sync status/push/pull`, backoff, métricas

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
| Rebuild binario local MCP | 15min | ⬜ Pendiente — cerrar VS Code, `dotnet publish`, copiar |
| Limpiar datos de prueba (ID 1, session verify-001) | 5min | ⬜ Pendiente |

---

## 📋 Auditoría de Compatibilidad con Go Original

Última auditoría: 2026-04-30

| Feature del Go original | Estado en .NET | Notas |
|------------------------|:---:|-------|
| Scoped topic upserts (scope, topic_key, revision_count) | ✅ | |
| FTS5 con topic_key + direct search fallback | ✅ | |
| NormalizeScope (team/personal/project legacy) | ✅ | |
| `mem_merge_projects` tool | ✅ | |
| SuggestTopicKey | ✅ | |
| Project drift detection (DetectProject, FindSimilar) | ✅ | PR #2 |
| Project detection 5-case (git_child, ambiguous) | ✅ | PR #7 |
| Write queue (Channel<T> serialization) | ✅ | PR #7 |
| Session activity tracker + nudge | ✅ | PR #7 |
| Schema columns (review_after, expires_at, embedding) | ✅ | PR #7 |
| Obsidian exporter | ✅ | PR #4 |
| PostgreSQL backend | ✅ | PR #3 |
| Docker Compose para PG | ✅ | |
| Backend indicator en `/health` y `/stats` | ✅ | PR #7 |
| **DELETE /sessions/{id}, /prompts/{id}** | ❌ | **Phase 2** |
| **mem_current_project** | ❌ | **Phase 2** |
| **Structured error responses** | ❌ | **Phase 2** |
| **Obsidian --watch, --since, --project** | ❌ | **Phase 2** |
| Tool deferral (deferred loading) | ❌ | En investigación |
| TUI (Bubbletea) | ❌ | Excluido de v1 |
| Memory conflict surfacing | ❌ | **Phase 4** |
| Decay / auto-expiration | ❌ | **Phase 4** |

---

## 🗺️ Orden Sugerido de Trabajo

| Orden | Feature | Por qué primero |
|-------|---------|----------------|
| 1 | **Phase 2 — API Parity** | Proposal ya existe. Completa la paridad con Go. Aditivo, sin breaking. |
| 2 | **TTL Configurable** | Proposal ya existe. Independiente. Usa columnas de Phase 1. |
| 3 | **Backend Config File** | Proposal ya existe. Mejora DX significativamente. |
| 4 | **Offline-First Sync** | Feature más compleja. Diferencia producto hobby de enterprise. |
| 5 | **Phase 3 — Breaking** | Requiere Phase 2. Cambia contratos de API. |
| 6 | **Phase 4 — Memory Relations** | Requiere Phase 1 columns. Feature avanzada de calidad de memoria. |
| 7 | **Observability** | Útil pero no bloqueante. |
| 8 | **Admin CLI** | Herramienta de emergencia, no crítica. |
| 9 | **Tool Deferral** | Esperar datos de tokens primero. |
