[← Volver al README](../README.md)

# Roadmap — engram-dotnet

> Ideas y mejoras identificadas para las próximas versiones del proyecto. Este documento es un backlog vivo — las ideas se mueven a issues de GitHub cuando están listas para implementarse.

---

## ✅ Completadas

### Diagramas de flujo + documentación VSCode

Agregar diagramas Mermaid al README y docs existentes para visualizar:
- Flujo completo: Developer → MCP plugin → EngramTools → HttpStore → Servidor → SQLite
- Modelo de namespaces team/personal
- Ciclo de vida de una observación

Actualizar `.vscode/settings.json` con configuración recomendada para el proyecto.

**Estado**: Completo en rama `feat/mermaid-diagrams-vscode-config`.

---

## 🟢 Alta Prioridad — Activas

### #1 — Project Drift Detection

Detectar el nombre de proyecto automáticamente desde git remote, normalizar con warnings cuando hay drift, y sugerir consolidación cuando hay nombres similares.

**Estado**: ✅ Completo — merged en PR #2.

**Funcionalidad**:
- `DetectProject(dir)` — detecta nombre desde git remote origin → git root → filepath.Base (prioridad en ese orden)
- `FindSimilar(name, existing, maxDistance)` — busqueda por case-insensitive, substring, y Levenshtein distance
- `NormalizeProject(name)` → `(normalized, warning)` — lowercase + trim + warning si cambió
- Warning al guardar si el proyecto normalizado difiere del original
- CLI `engram projects list|consolidate|prune`

**Por qué**: Los agentes y los devs escriben el nombre del proyecto de formas inconsistentes (`Mi-API`, `mi-api`, `mi_api`). Esto fragmenta la memoria y contamina las búsquedas. El Go original ya lo tiene implementado.

**Referencia**: Go original `internal/project/detect.go` + `internal/project/similar.go`

---

### #2 — PostgreSQL backend

Implementar `PostgresStore` como tercer implementor de `IStore`, junto a `SqliteStore` e `HttpStore`.

**Estado**: ✅ Completo — implementado en rama `feat/postgres-backend-impl`.

**Implementación**:
- `PostgresStore.cs` (~900 líneas) — todos los 22 métodos de `IStore`
- FTS via `tsvector GENERATED ALWAYS AS STORED` + GIN indexes
- Deduplicación de 3 caminos (topic_key, hash window, fresh insert)
- Schema con índices optimizados y partial GIN index `WHERE deleted_at IS NULL`
- CLI soporta `ENGRAM_DB_TYPE=postgres` + `ENGRAM_PG_CONNECTION`
- Tests de paridad con Testcontainers.PostgreSql (26 tests)
- Npgsql 9.0.* como dependencia

**Por qué**: SQLite tiene límites reales de escritura concurrente. Con 5+ desarrolladores activos en simultáneo se genera contención. La interfaz `IStore` ya está diseñada para soportar esto — el trabajo está acotado.

**Documentación de diseño**:
- [RFC-001 — PostgreSQL Backend](rfcs/RFC-001-postgresql-backend.md) — motivación, diseño técnico, riesgos
- [PRD-001 — PostgreSQL Backend](rfcs/PRD-001-postgresql-backend.md) — requisitos, criterios de aceptación
- [ADR-001 — SQL directo sin ORM](adr/ADR-001-no-orm.md) — decisión de no usar EF Core / Dapper
- [SDD exploration, proposal, spec, tasks](../sdd/postgres-backend/)

---

### #3 — Obsidian Export (Fase A)

Exportar memorias a un vault de Obsidian como archivos `.md` con frontmatter YAML.

**Estado**: ✅ Completo — implementado en rama `feat/obsidian-export`.

**Implementación**:
- CLI `engram obsidian-export` con flags: `--vault`, `--project`, `--include-personal`, `--force`, `--graph-config`, `--limit`
- Cada observación → un archivo `.md` con YAML frontmatter + wikilinks
- Sesiones y topic clusters → hub notes que generan graph view
- Mapa de tipos: `architecture/decision → Architecture/`, `bugfix → Bugs & Fixes/`, etc.
- Scope security: `scope=personal` nunca se exporta sin `--include-personal`
- Incremental export con state file (`.engram-sync-state.json`)
- Deleted observation cleanup
- 61 tests en `Engram.Obsidian.Tests`

**Por qué**: El Go original ya lo tiene implementado (`internal/obsidian/`). Portear esta lógica es directo — no es diseño desde cero, es adaptación. Hace que las memorias sean auditables por humanos.

**Referencia**: Go original `internal/obsidian/` (exporter, hub, slug, markdown, state, watcher)

---

## 🟡 Media Prioridad

### TTL configurable por tipo de observación

Permitir configurar expiración automática por tipo:

| Tipo | TTL sugerido |
|------|-------------|
| `tool_use`, `file_change`, `command` | 30 días |
| `decision`, `architecture` | Nunca |
| `bugfix`, `pattern` | 90 días |
| `learning`, `discovery` | 60 días |

**Por qué**: Las observaciones se acumulan indefinidamente. Contexto viejo contamina búsquedas y reduce la calidad de las respuestas del agente.

---

### Herramienta administrativa acotada

Interfaz simple (TUI o CLI) para gestión de emergencia:
- Listar observaciones con filtros (project, scope, type, rango de fechas)
- Eliminar por ID o por criterio (proyecto entero, rango de fechas)
- Ver duplicados y observaciones con bajo valor

**Por qué**: El agente decide qué guardar en el flujo normal, pero hay casos donde se necesita limpieza manual — sesiones rotas, datos incorrectos, limpieza antes de archivar un proyecto.

**Alcance**: No es una UI de gestión cotidiana. Es una herramienta de emergencia.

---

### Observability básica

Endpoint `/metrics` con:
- Tiempos de respuesta por operación (search, save, context)
- Tamaño actual de la base de datos
- Queries por segundo
- Observaciones por proyecto/scope

**Por qué**: Actualmente no hay señales temáscanas de degradación. Con PostgreSQL o SQLite bajo carga, saber cuándo hay un problema antes de que impacte a los agentes es crítico.

---

### Tool Deferral (investigación)

Mover 4+8 herramientas de eager a deferred loading para reducir tokens de inicio de sesión en ~40%.

**Core (siempre disponibles)**:
`mem_save`, `mem_search`, `mem_context`, `mem_session_summary`, `mem_get_observation`, `mem_save_prompt`

**Deferred (via ToolSearch)**:
`mem_update`, `mem_suggest_topic_key`, `mem_session_start`, `mem_session_end`, `mem_stats`, `mem_delete`, `mem_timeline`, `mem_capture_passive`

**Estado**: Bajo investigación. Necesitamos medir el consumo de tokens actual por herramienta ANTES de optimizar. El SDK de .NET (`ModelContextProtocol` v1.2.0) no tiene `WithDeferLoading` como el SDK de Go — se necesitaría implementar via separación de clases o espera a que el SDK lo soporte.

**Por qué es importante analizar**: El consumo de tokens de MCP tools es KGEM (Known Good Estimation Metric) pero varía por agente. Sin datos reales de nuestro deployment, corridas de optimización pueden ser prematuras.

**Datos de uso actual**: Uso continuo 33%, semanal 13%, mensual 6%. Necesitamos desglosar por herramienta para saber dónde están los tokens.

**Referencia**: Go original commit `b6b1f6f` — `perf(mcp): defer 4 rare tools to reduce session startup tokens`

---

## 🔧 Mantenimiento

Tareas operativas que no son features pero mantienen el proyecto sano.

### Arreglar tasks.md de PostgreSQL SDD

El archivo `sdd/postgres-backend/tasks/tasks.md` tiene 28 tareas sin marcar como `[x]` aunque están todas implementadas. Solo se marcaron 4.7 (CI) y 5.5 (.gitignore).

**Esfuerzo**: 30min — marcar las 28 tareas como completadas.

---

### Rebuild del binario local MCP

El binario en `/home/gantz/.local/bin/engram-dotnet` está desactualizado (no incluye los últimos fixes de `FormatContextAsync` y `BackendName`). El `cp` falla porque VS Code tiene el proceso MCP abierto.

**Esfuerzo**: 15min — cerrar VS Code, `dotnet publish`, copiar binario.

---

### Backend Config Switch (Nivel 2 — config file)

Proposal ya creada en `sdd/backend-config-switch/proposal.md`. Permitir un archivo `~/.engram/config.json` para cambiar entre backends con un solo valor.

**Esfuerzo**: 4-6h — parser de config, precedence con env vars, docs.

---

### Limpiar datos de prueba

La sesión `verify-001` y observación ID 1 son datos de prueba del deploy inicial. Se pueden eliminar si no aportan valor.

**Esfuerzo**: 5min — `DELETE FROM observations WHERE id = 1` + cleanup de sesión.

---

## 🟠 Fase Posterior

### Offline-First Sync (bidireccional local ↔ servidor)

**Origen**: Discusión del 2026-04-29 — se detectó que con `ENGRAM_URL` todo va directo al servidor y no hay backup local. Si el servidor se ca o no hay conexión, el agente pierde la capacidad de guardar memoria.

**Problema que resuelve**:
- Trabajar offline (casa, viaje, sin red) y sincronizar cuando hay conexión
- Backup local automático de todo lo que se escribe en el servidor
- Colaboración: recibir cambios de otros devs en tu store local

**Infraestructura existente (ya implementada)**:
- `sync_mutations` journal en SQLite — change log escrito pero **nunca consumido**
- `sync_state` table — trackea lifecycle por target (`target_key='cloud'`)
- `sync_id` en cada entidad — identificador estable cross-store
- `ExportAsync()` / `ImportAsync()` — export/import completo
- `EngramSync` class — sync file-based con chunks JSONL (sin red)

**Arquitectura propuesta**:
1. **Siempre** escribe en SQLite local (source of truth primaria)
2. Si `ENGRAM_URL` está set y hay conexión → `SyncWorker` hace push/pull en background
3. Server necesita 2 endpoints nuevos: `POST /sync/push` y `GET /sync/pull?since={seq}`
4. Conflict resolution: last-write-wins por timestamp

**Fases estimadas**:
- **Fase 1** (Push sync): ~400 líneas — consume `sync_mutations`, envía al servidor
- **Fase 2** (Pull sync): ~300 líneas — baja cambios de otros devs
- **Fase 3** (CLI + robustez): ~200 líneas — `engram sync status/push/pull`, backoff, métricas

**Total estimado**: ~900 líneas nuevas, 5-6 archivos afectados

**Prioridad**: Alta para equipos distribuidos. Diferencia un producto hobby de uno enterprise.

---

### Obsidian Export — Fase B (con IA)

Agente especializado que toma observaciones relacionadas y genera documentos de conocimiento sintetizados:
- Agrupa bugfixes relacionados → *"Patrones de error comunes en el módulo X"*
- Agrupa decisiones de arquitectura → *"Evolución del diseño del sistema de memoria"*
- Genera glosario técnico del proyecto

**Consideración**: Requiere LLM, tiene costo de tokens. Justifica su propio ciclo SDD completo.

---

### Python port

Port del servidor HTTP a Python para equipos con stack Python-first.

**Cuándo tiene sentido**:
- Si el equipo quiere embeddings semánticos (buscar por similitud conceptual, no solo keywords)
- Si el equipo ya trabaja con Python y no quiere introducir .NET

**Cuándo NO tiene sentido**:
- Como port 1:1 del servidor HTTP actual — el beneficio es marginal
- Si el caso de uso es solo almacenamiento y búsqueda FTS5

---

## 📋 Auditoría de compatibilidad con Go original

Última auditoría: 2026-04-27

| Feature del Go original | Estado en .NET Port | Notas |
|------------------------|--------------------|----|
| Scoped topic upserts (scope, topic_key, revision_count, duplicate_count) | ✅ Porteado | `Models.cs`, `SqliteStore.cs` |
| FTS5 con topic_key + direct search fallback ("/" shortcut) | ✅ Porteado | `MigrateFtsTopicKey()`, `SearchAsync` |
| NormalizeScope (team/personal/project legacy) | ✅ Porteado | `SqliteStore.NormalizeScope()` |
| mem_merge_projects tool | ✅ Porteado | `IStore.MergeProjectsAsync` |
| SuggestTopicKey | ✅ Porteado | `Normalizers.SuggestTopicKey` |
| Project drift detection (DetectProject, FindSimilar, Levenshtein) | ✅ Porteado | PR #2 — CLI `projects list|consolidate|prune` |
| Tool deferral (deferred loading) | ❌ No porteado | En investigación — SDK .NET no lo soporta nativamente |
| Obsidian brain exporter | ✅ Porteado | Fase A completo — CLI + exporter + hubs + tests |
| Cloud/PostgresStore + JWT auth | ✅ Porteado | PostgresStore implementado + HttpStore para modo remoto |
| Docker Compose para PG | ✅ Creado | `docker/docker-compose.yml` con PostgreSQL externo |
| Backend indicator en responses | ✅ Porteado | Campo `backend` en `/health` y `/stats` |
| TUI (Bubbletea) | ❌ Excluido de v1 | El Go original lo tiene, .NET port no |