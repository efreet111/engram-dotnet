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

**Funcionalidad**:
- `DetectProject(dir)` — detecta nombre desde git remote origin → git root → filepath.Base (prioridad en ese orden)
- `FindSimilar(name, existing, maxDistance)` — busqueda por case-insensitive, substring, y Levenshtein distance
- `NormalizeProject(name)` → `(normalized, warning)` — lowercase + trim + warning si cambió
- Warning al guardar si el proyecto normalizado difiere del original
- CLI `engram projects list|consolidate|prune`

**Por qué**: Los agentes y los devs escriben el nombre del proyecto de formas inconsistentes (`Mi-API`, `mi-api`, `mi_api`). Esto fragmenta la memoria y contamina las búsquedas. El Go original ya lo tiene implementado.

**Dependencias**: Necesita método nuevo `IStore.ListProjectNames()` para `FindSimilar`. `PostgresStore` también lo implementará.

**Referencia**: Go original `internal/project/detect.go` + `internal/project/similar.go`

**Documentación de diseño**: Pendiente — crear RFC cuando se empiece.

---

### #2 — PostgreSQL backend

Implementar `PostgresStore` como tercer implementor de `IStore`, junto a `SqliteStore` e `HttpStore`.

**Por qué**: SQLite tiene límites reales de escritura concurrente. Con 5+ desarrolladores activos en simultáneo se genera contención. La interfaz `IStore` ya está diseñada para soportar esto — el trabajo está acotado.

**Cuándo**: Después de Project Drift Detection (agrega `ListProjectNames` a `IStore` que PostgresStore también necesita).

**Documentación de diseño**:
- [RFC-001 — PostgreSQL Backend](rfcs/RFC-001-postgresql-backend.md) — motivación, diseño técnico, riesgos
- [PRD-001 — PostgreSQL Backend](rfcs/PRD-001-postgresql-backend.md) — requisitos, criterios de aceptación
- [ADR-001 — SQL directo sin ORM](adr/ADR-001-no-orm.md) — decisión de no usar EF Core / Dapper
- [SDD exploration, proposal, spec, tasks](../sdd/postgres-backend/)

---

### #3 — Obsidian Export (Fase A)

Exportar memorias a un vault de Obsidian como archivos `.md` con frontmatter YAML.

**Funcionalidad**:
- CLI `engram obsidian-export` con flags: `--vault`, `--project`, `--scope`, `--limit`, `--since`, `--watch`
- Cada observación → un archivo `.md` con YAML frontmatter
- Sesiones y topic clusters → hub notes que generan graph view
- Mapa de tipos: `architecture/decision → Architecture/`, `bugfix → Bugs & Fixes/`, etc.
- Watch mode para sincronización continua

**Por qué**: El Go original ya lo tiene implementado (`internal/obsidian/`). Portear esta lógica es directo — no es diseño desde cero, es adaptación. Hace que las memorias sean auditables por humanos.

**Seguridad**: `scope=personal` nunca se exporta sin permiso explícito. Solo `scope=team` por defecto.

**Dependencias**: Ninguna — usa `IStore.ExportAsync()` y queries existentes.

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

## 🟠 Fase Posterior

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

Última auditoría: 2026-04-21

| Feature del Go original | Estado en .NET Port | Notas |
|------------------------|--------------------|----|
| Scoped topic upserts (scope, topic_key, revision_count, duplicate_count) | ✅ Porteado | `Models.cs`, `SqliteStore.cs` |
| FTS5 con topic_key + direct search fallback ("/" shortcut) | ✅ Porteado | `MigrateFtsTopicKey()`, `SearchAsync` |
| NormalizeScope (team/personal/project legacy) | ✅ Porteado | `SqliteStore.NormalizeScope()` |
| mem_merge_projects tool | ✅ Porteado | `IStore.MergeProjectsAsync` |
| SuggestTopicKey | ✅ Porteado | `Normalizers.SuggestTopicKey` |
| Project drift detection (DetectProject, FindSimilar, Levenshtein) | ❌ No porteado | **Backlog #1** |
| Tool deferral (deferred loading) | ❌ No porteado | En investigación — SDK .NET no lo soporta nativamente |
| Obsidian brain exporter | ❌ No porteado | **Backlog #3** |
| Cloud/PostgresStore + JWT auth | ⬅️ Removido del Go repo | Repo privado separado — nuestra implementación es independiente |
| Docker Compose para PG | ⬅️ Removido del Go repo | Lo crearemos en nuestro Docker |
| TUI (Bubbletea) | ❌ Excluido de v1 | El Go original lo tiene, .NET port no |