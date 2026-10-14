# Informe de Features de engram-dotnet para FlowForge

**Proyecto:** engram-dotnet  
**Rama:** `requirements-from-flowforge`  
**Rango de commits:** `976d2b9` → `799241e`  
**Fecha:** 2026-09-30  
**Audiencia:** FlowForge Orchestrator y agentes (forge-arch, forge-dev, forge-verify, forge-memory)  

---

## Resumen Ejecutivo

Desde `976d2b9` hasta `799241e` se implementaron **14 HU** orientadas a hacer de engram un store de memoria **code-aware**, **stateful** y **team-ready**. El hilo conductor es que FlowForge necesita consultar y persistir memorias con contexto de código (archivo, símbolo, namespace) para que sus agentes mantengan consistencia arquitectónica entre sesiones.

Las HU se agrupan en tres ejes:

| Eje | HUs | Descripción |
|-----|-----|-------------|
| **Code-Aware Memory** | HU-053, HU-059, HU-060, HU-064 | Captura y query de memorias por contexto de código |
| **Arch Agent Stateful** | HU-061, HU-062, HU-064 | Decisiones arquitectónicas consultables por módulo + detección de contradicciones |
| **Developer Experience** | HU-050, HU-051, HU-052, HU-055 | Quick-capture, git hooks, stats, onboarding |
| **Sync Reliability** | HU-065 | Auto-enroll y fallback de remote_url desde config.json |

---

## Índice de HU Implementadas

| HU | ENG | Título | Status |
|----|-----|--------|--------|
| HU-050 | ENG-480 | Quick-capture CLI | ✅ Done |
| HU-051 | ENG-481 | Git hooks integration (`engram init`) | ✅ Done |
| HU-052 | ENG-482 | Dev observability (`engram stats` detallado) | ✅ Done |
| HU-053 | ENG-483 | Code-aware capture (`engram watch`) | ✅ Done |
| HU-055 | ENG-485 | Team onboarding (`engram onboard`) | ✅ Done |
| HU-059 | ENG-484 | Code-aware Dev Agent (pre/post-edit hooks) | ✅ Done |
| HU-060 | ENG-483 | Code-aware capture Parte B (metadata extraction) | ✅ Done |
| HU-061 | ENG-484 | Code-context Arch Agent (`mem_decisions_for_module`) | ✅ Done |
| HU-062 | ENG-414 | Contradiction detection (`mem_check_contradictions`) | ✅ Done |
| HU-064 | ENG-484 | Code-context query tools (MCP + REST + CLI) | ✅ Done |
| HU-065 | ENG-492 | Sync auto-push (auto-enroll + config fallback) | ✅ Done |

---

## 1. HU-050 — Quick-Capture CLI (`engram "<memo>"`)

**Spec:** [`docs/tasks/HU-050-quick-capture-cli.md`](https://github.com/efreet111/engram-dotnet/blob/requirements-from-flowforge/docs/tasks/HU-001-HU-099/HU-050-quick-capture-cli.md)

### Problema que resuelve

Capturar una memoria requería un comando de 5+ argumentos (`engram save --title X --content Y --type Z --project W`), lo que tomaba ~30 segundos y mataba la adopción espontánea. Los desarrolladores perdían insights valiosos porque el friction de captura era demasiado alto.

### ¿Por qué se hizo?

La fricción de captura es el mayor asesino de adopción de herramientas de memoria. Si capturar toma más de 5 segundos, no lo hacen. El quick-capture reduce el time-to-first-memory a **<3 segundos**.

### Decisiones y trade-offs

- **Smart defaults en lugar de prompts interactivos**: Se auto-detecta el proyecto desde `.engram-id` o git root, se genera el título automáticamente, y se usa `type=note` como default. Trade-off: menos control, más velocidad.
- **No editor interactivo (`-e`)**: Quedó fuera de alcance deliberadamente. El editor interactivo rompe scripts y automatización. Se priorizó la simplicidad absoluta del caso de uso.
- **No historial de quick-captures**: Postergado. Requiere almacenamiento de session_id por invocation, más complejo.

### Interfaz nueva

#### CLI (nuevo comando)

```
engram "<memo>"
```

**Parámetros:**

| Parámetro | Tipo | Default | Descripción |
|-----------|------|---------|-------------|
| `texto` (positional) | `string` | — | Contenido completo de la memoria. Puede ser multilínea con `$'...'` o heredoc |
| `-t <type>` | `string` | `note` | Tipo de memoria. Valores: `decision`, `insight`, `note`, `blocker`, `convention`, `learning`, `discovery`, `bugfix`, `pattern`, `tool_use`, `file_change`, `command`, `session_summary`, `commit`, `code_change`, `manual` |

**Smart defaults aplicados automáticamente:**

| Campo | Cómo se determina |
|-------|-------------------|
| `title` | Primeras 5-7 palabras del contenido, truncado a 50 caracteres |
| `type` | `note` (o `-t` override) |
| `project` | Auto-detectado: `.engram-id` → git root → `default` |
| `session_id` | `quick-capture-{timestamp}` |

**Ejemplo:**

```bash
engram "decisión: elegimos PostgreSQL por JSONB"
# → ✓ Memory saved: #1234 "decisión: elegimos PostgreSQL..." (note)
```

### Commit

`0e99070` — `feat: add HU-050 quick-capture CLI (engram "<memo>")`

---

## 2. HU-051 — Git Hooks Integration (`engram init`)

**Spec:** [`docs/tasks/HU-051-git-hooks-integration.md`](https://github.com/efreet111/engram-dotnet/blob/requirements-from-flowforge/docs/tasks/HU-001-HU-099/HU-051-git-hooks-integration.md)

### Problema que resuelve

Los desarrolladores piensan en commits. El momento en que escriben un commit message es cuando están más propensos a expresar una decisión arquitectónica, un aprendizaje, o un "remember this". Sin integración con git, engram era un tool separado del workflow natural.

### ¿Por qué se hizo?

Onboarding y captura de contexto de decisiones son los dos mayores generadores de adopción. Los git hooks hacen que engram **se active naturalmente** en el momento en que el desarrollador ya está en modo "reflexión sobre código".

### Decisiones y trade-offs

- **Hooks shell scripts, no binarios compilados**: Más portable (funciona en Linux, macOS, Git Bash en Windows). Trade-off: requiere que `engram` esté en PATH.
- **Backup de hooks existentes en lugar de merge**: Se hace backup a `*.engram-backup`. No se intenta merge de hooks existentes porque es demasiado complejo para el caso de uso. Si el usuario quiere full hooks custom, puede hacer merge manual.
- **`prepare-commit-msg` es opcional (`--interactive`)**: No se fuerza el prompt interactivo porque rompe scripts CI/CD.

### Interfaz nueva

#### CLI (nuevo comando)

```
engram init [--interactive]
```

**Parámetros:**

| Parámetro | Tipo | Default | Descripción |
|-----------|------|---------|-------------|
| `--interactive` | `flag` | `false` | Instala también el hook `prepare-commit-msg` con prompt interactivo "¿Guardar este commit como memoria?" |

**Comportamiento:**

1. Detecta si estamos en un git repo (busca `.git/`)
2. Si no es git repo → error: `"Not a git repository. Run \`git init\` first."`
3. Crea `post-commit` hook en `.git/hooks/post-commit`
4. Si ya existe un hook → backup a `.git/hooks/post-commit.engram-backup`
5. Output confirma instalación

**El hook creado (`post-commit`):**

```bash
#!/bin/sh
COMMIT_MSG=$(git log -1 --pretty=%B)
CHANGED_FILES=$(git diff-tree --no-commit-id --name-only -r HEAD | tr '\n' ', ')
engram save \
  --title "commit: $COMMIT_MSG" \
  --content "Changed files: $CHANGED_FILES\n\nFull message:\n$COMMIT_MSG" \
  --type commit \
  --project "$(engram project id --quiet)"
```

### Commit

`44a98a1` — `feat: add engram init — git hooks auto-capture commits as memories`

---

## 3. HU-052 — Dev Observability (`engram stats` detallado)

**Spec:** [`docs/tasks/HU-052-dev-observability.md`](https://github.com/efreet111/engram-dotnet/blob/requirements-from-flowforge/docs/tasks/HU-001-HU-099/HU-052-dev-observability.md)

### Problema que resuelve

`engram stats` mostraba información muy básica (total de observaciones, sesiones, prompts). No respondía las preguntas que los desarrolladores realmente tienen: ¿cuántas memorias de cada tipo? ¿cuáles son las más viejas? ¿cuánto espacio ocupa la DB?

### ¿Por qué se hizo?

Sin visibilidad, no hay confianza. Los power users no sabían qué tenían ni cuándo limpiar. Los stats detallados permiten tomar decisiones sobre el contenido de la base de conocimiento.

### Decisiones y trade-offs

- **No se agregó `access_count` ni `last_accessed_at` al schema**: Esto habría requerido schema migration. Se priorizó la implementación sin schema change, usando solo queries sobre `created_at` y `updated_at` (que ya existían). Trade-off: no se puede hacer hot/cold analysis por acceso.
- **SQLite `PRAGMA page_count * page_size` vs Postgres `pg_database_size()`**: Se usa la función nativa de cada motor para obtener el tamaño de DB.

### Interfaz nueva

#### CLI (comando existente, output mejorado)

```
engram stats [--json]
```

**Output mejorada (human-readable):**

```
Engram Memory Stats
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

📊 Overview
  Total observations: 156
  Total sessions:     42
  Total prompts:      89
  Projects:           3 (my-project, other-project, demo)
  Database:           /path/to/engram.db (2.3 MB)

📝 By Type
  decision:    23 (14.7%)
  insight:     45 (28.8%)
  note:        67 (42.9%)
  blocker:     12 (7.7%)
  convention:   9 (5.8%)

🕐 Recent Activity (last 30 days)
  Created: 34 memories
  Most active project: my-project (18 memories)
  Most active type: insight (12 memories)

🧊 Oldest Memories (90+ days)
  23 memories not updated in 90+ days
  Run `engram stats --prune-preview` to see candidates for cleanup

💾 Storage
  Database size: 2.3 MB
  Average memory size: 1.2 KB
  Largest memory: 8.4 KB (id: 42, "ADR-007 sync recovery")
```

**Parámetros:**

| Parámetro | Tipo | Default | Descripción |
|-----------|------|---------|-------------|
| `--json` | `flag` | `false` | Output machine-readable en JSON |

**JSON output:**

```json
{
  "overview": { "observations": 156, "sessions": 42, "prompts": 89, "projects": ["my-project", "other-project", "demo"], "database_size_bytes": 2416000 },
  "by_type": { "decision": 23, "insight": 45, "note": 67, "blocker": 12, "convention": 9 },
  "recent_30d": { "created": 34, "most_active_project": "my-project", "most_active_type": "insight" },
  "oldest_90d": { "count": 23 },
  "storage": { "size_bytes": 2416000, "avg_memory_bytes": 1200, "largest": { "id": 42, "title": "ADR-007 sync recovery", "size_bytes": 8400 } }
}
```

**Endpoint REST afectado:**

| Método | Endpoint | Cambio |
|--------|----------|--------|
| `GET` | `/stats` | Ahora devuelve el schema JSON completo de `DetailedStats` |

### Commit

`366f048` — `feat: add detailed stats endpoint and deterministic tie-break (HU-052/ENG-482)`

---

## 4. HU-053 — Code-Aware Capture (`engram watch`)

**Spec:** [`docs/tasks/HU-053-code-aware-capture.md`](https://github.com/efreet111/engram-dotnet/blob/requirements-from-flowforge/docs/tasks/HU-001-HU-099/HU-053-code-aware-capture.md)

### Problema que resuelve

La captura de memorias era 100% manual. Los desarrolladores no recordaban usar `engram save` durante el trabajo de código. El momento más natural para capturar (cuando editas un archivo) no generaba memorias.

### ¿Por qué se hizo?

Onboarding a code-aware memory requiere que las memorias capturadas automáticamente incluyan metadata de código (file path, diff resumido). `engram watch` es el mecanismo de captura pasiva que habilita HU-059 y HU-060.

### Decisiones y trade-offs

- **FileSystemWatcher de .NET sobre herramientas externas**: Abstrae inotify (Linux), FSEvents (macOS), ReadDirectoryChangesW (Windows). Trade-off: FileSystemWatcher tiene limitaciones conocidas en Windows con ciertos tipos de archivos.
- **Threshold de 10 líneas como default**: Suficientemente alto para filtrar ruido (keystrokes, auto-save), suficientemente bajo para capturar cambios significativos.
- **Throttling implícito**: FileSystemWatcher puede disparar múltiples eventos por un solo cambio. El debounce de 5 segundos se implementa en la capa de captura.
- **No se implementó LSP integration**: Postergado. Es un proyecto completo en sí mismo que requiere entender el protocolo LSP.

### Interfaz nueva

#### CLI (nuevo comando)

```
engram watch <path> [<path>...] [--threshold <lines>] [--ignore-pattern <glob>] [--project <name>]
```

**Parámetros:**

| Parámetro | Tipo | Default | Descripción |
|-----------|------|---------|-------------|
| `path` (positional, 1+) | `string[]` | — | Archivo, directorio (no recursivo), o glob simple (`*`/`?`) a observar |
| `--threshold` | `int` | `10` | Mínimo de líneas cambiadas para capturar (comparación `>=`) |
| `--ignore-pattern` | `string[]` | `[]` | Glob pattern a excluir (repetible; aplica a filename o path relativo) |
| `--project` | `string` | auto-detected | Override del nombre de proyecto |

**Project Detection Chain:** `--project` → `ENGRAM_PROJECT` → git remote → git root → cwd basename

**Comportamiento:**

- Observa cambios en archivos especificados
- Detecta cambios significativos (threshold configurable)
- Captura memoria con:
  - `type` = `code_change`
  - `title` = `"Changed: {file_path}"`
  - `content` = diff resumido + líneas cambiadas
  - `project` = auto-detectado
  - `file_path` = ruta del archivo
- Graceful shutdown con `Ctrl+C` → `"✓ Watch stopped. {n} memories captured."`

**Ejemplo:**

```bash
# Watch un archivo
engram watch src/Auth/JwtBearer.cs
# → 👀 Watching src/Auth/JwtBearer.cs...
# → ✓ Memory saved: #1234 "Changed: src/Auth/JwtBearer.cs" (code_change)

# Watch con threshold custom
engram watch src/ --threshold 20

# Watch con ignore patterns
engram watch src/ --ignore-pattern "*.log" --ignore-pattern "*.tmp"
```

### Commit

`22a72c3` — `feat: add engram watch for code-aware memory capture (HU-053/ENG-483)`

---

## 5. HU-055 — Team Onboarding (`engram onboard`)

**Spec:** [`docs/tasks/HU-055-onboarding-flow.md`](https://github.com/efreet111/engram-dotnet/blob/requirements-from-flowforge/docs/tasks/HU-001-HU-099/HU-055-onboarding-flow.md)

### Problema que resuelve

Cuando un desarrollador se une a un equipo, pasa **semanas aprendiendo**: decisiones arquitectónicas, convenciones, "¿por qué hicimos X así?", gotchas conocidos. Este conocimiento está disperso o en engram pero no surfaceado como flow de onboarding.

### ¿Por qué se hizo?

Onboarding es caro ($30K-50K USD por desarrollador en fully-loaded cost) y lento. Existing solutions (wikis, Notion docs) están desactualizados. Un comando que genera un resumen de onboarding basado en team memory es el **killer feature para team adoption**.

### Decisiones y trade-offs

- **Solo CLI, no UI web**: Se priorizó simplicidad. La UI web puede venir después.
- **Heuristic de "importancia" simple**: recencia + type weight + reference count. No se usa embedding similarity porque requiere ENG-418 (pendiente).
- **No se personaliza por rol (dev vs product vs design)**: Postergado. Requiere taxonomy más fina.
- **Generación regenerateable, no snapshot**: El output refleja el estado actual de team memory en el momento de ejecución. No se guarda como archivo estático.

### Interfaz nueva

#### CLI (nuevo comando)

```
engram onboard --user <handle> [--format markdown|json] [--days <N>] [--output <file>] [--project <name>]
```

**Parámetros:**

| Parámetro | Tipo | Default | Descripción |
|-----------|------|---------|-------------|
| `--user` (requerido) | `string` | — | Handle del nuevo desarrollador |
| `--format` | `markdown` \| `json` | `markdown` | Formato de output |
| `--days` | `int` | `30` | Ventana de "recent insights" en días |
| `--output` | `string` | stdout | Archivo de salida |
| `--project` | `string` | todos los proyectos | Filtrar por proyecto específico |

**Output (markdown):**

````markdown
# Welcome to the team! 🎉

Here's what you need to know based on our team memory:

## 🏗️ Top 10 Architectural Decisions
1. **ADR-007: Sync recovery** (2026-06-29) — Usamos server-side mutation apply...
2. **ADR-002: Sync mutation application** (2026-06-15) — ID mapping strategy...

## 📏 Active Conventions
- **Code style**: Usamos MediatR para CQRS en todos los proyectos
- **Error handling**: Result<T> pattern en vez de exceptions

## 🚧 Known Blockers / Gotchas
- **ENG-473**: `relations add` FK constraint violation — workaround: crear session primero

## 💡 Recent Insights (last 30 days)
- **2026-08-10**: Descubrimos que Postgres index overflow...

## 🗺️ Where to Start
- Most-referenced files: `src/Engram.Store/SqliteStore.cs`
- Key concepts: sync mutations, project identity, memory relations

---
Generated from 156 memories across 3 projects.
Last updated: 2026-08-12
````

### Commit

`36cd4bf` — `feat: add engram onboard for team onboarding (HU-055/ENG-485)`

---

## 6. HU-059 — Code-Aware Dev Agent (pre/post-edit hooks)

**Spec:** [`docs/tasks/HU-059-code-aware-dev-agent.md`](https://github.com/efreet111/engram-dotnet/blob/requirements-from-flowforge/docs/tasks/HU-001-HU-099/HU-059-code-aware-dev-agent.md)

### Problema que resuelve

El Dev Agent de FlowForge editaba archivos sin conocer las decisiones arquitectónicas existentes. Esto llevaba a código inconsistente con decisiones previas y a re-derivación de decisiones ya documentadas.

### ¿Por qué se hizo?

HU-059 es la pieza central de la integración FlowForge ↔ Engram. Antes de editar código, el Dev Agent necesita consultar memorias asociadas al archivo. Después de editar, necesita capturar la decisión con metadata de código. Sin esto, cada sesión es stateless y las decisiones se pierden entre sesiones.

### Decisiones y trade-offs

- **HU-059 no implementó código nuevo**: Las 3 dependencias (HU-063 schema evolution, HU-064 query tools, HU-053 engram watch) ya estaban Done cuando se cerró esta HU. El trabajo real fue **6 tests de integración + documentación de hooks**.
- **Los hooks pre-edit y post-edit son responsabilidad del Dev Agent (FlowForge), no de engram-dotnet**: engram-dotnet provee las herramientas (MCP + CLI) y la documentación. El Dev Agent las invoca.
- **Graceful degradation cuando no hay memorias**: Mensaje descriptivo `"No memories found for file: {path}"` en lugar de error. El Dev Agent puede proceder stateless si no hay contexto.

### Interfaz — Integración con FlowForge

Consultar [`docs/FLOWFORGE-INTEGRATION.md`](https://github.com/efreet111/engram-dotnet/blob/requirements-from-flowforge/docs/FLOWFORGE-INTEGRATION.md) para el protocolo completo.

**Pre-edit hook (antes de modificar un archivo):**

```
mem_recall_for_file("src/Auth/JwtBearer.cs", project="mi-proyecto")
mem_recall_for_file("src/Services/", project="mi-proyecto", type="decision")
```

**Post-edit hook (después de editar, capturar decisión):**

```
mem_save(
  title="Refactor: JwtBearerHandler ahora soporta RS384",
  content="**What**: Agregamos soporte RS384...\n**Why**: Compatibilidad con clientes legacy...",
  type="decision",
  topic_key="impl/jwt-refactor",
  project="mi-proyecto",
  file_path="src/Auth/JwtBearer.cs",
  symbol="JwtBearerHandler",
  namespace="Engram.Auth"
)
```

### Commit

`eea18b1` — `feat: complete HU-059 code-aware dev agent (integration tests + hooks docs)`

---

## 7. HU-060 — Code-Aware Capture Parte B (Metadata Extraction)

**Spec:** [`docs/tasks/HU-060-code-aware-capture-parte-b.md`](https://github.com/efreet111/engram-dotnet/blob/requirements-from-flowforge/docs/tasks/HU-001-HU-099/HU-060-code-aware-capture-parte-b.md)

### Problema que resuelve

Las decisiones extraídas de `plan.md` durante CKP-2 se capturaban sin metadata de código. No eran trazables a ubicaciones específicas en el código, haciendo difícil recuperarlas cuando se necesitaba contexto arquitectónico.

### ¿Por qué se hizo?

HU-030 (Parte A) ya capturaba decisiones de plan.md, pero sin contexto de código. Parte B extiende la captura con `file_path`, `symbol` y `namespace`, habilitando queries como "dame las decisiones arquitectónicas del módulo `Engram.Auth`".

### Decisiones y trade-offs

- **Extraction regex-based, no parser de lenguaje**: `CodeMetadataExtractor.cs` usa regex sobre el contenido del archivo para extraer clases, funciones e imports. Trade-off: no funciona para todos los lenguajes y tiene limitaciones con código complejo. La alternativa (parser LSP) se consideró fuera de alcance para esta HU.
- **Partial metadata capture**: Si solo `file_path` es conocido, `symbol` y `namespace` son `null`. No se falla la captura por falta de metadata completa.
- **Graceful fallback cuando ENG-483 tools unavailable**: Si `engram watch` no está disponible, se usa el behavior de Parte A (text-only capture) con debug log.

### Campos code-aware en `mem_save`

| Campo | Tipo | Descripción |
|-------|------|-------------|
| `file_path` | `string?` | Ruta relativa del archivo modificado (siempre se provee en captura code-aware) |
| `symbol` | `string?` | Nombre del símbolo modificado (clase, función, interface) — `null` si no se puede inferir |
| `namespace` | `string?` | Namespace/module del código — `null` si la ruta no sigue `src/<Namespace>/...` |

### Commit

`799241e` — `feat: HU-060 code-aware capture — symbol/namespace extraction via regex`

---

## 8. HU-061 — Code-Context Arch Agent (`mem_decisions_for_module`)

**Spec:** [`docs/tasks/HU-061-code-context-arch-agent.md`](https://github.com/efreet111/engram-dotnet/blob/requirements-from-flowforge/docs/tasks/HU-001-HU-099/HU-061-code-context-arch-agent.md)  
**RFC:** [`docs/architecture/rfc/RFC-008-flowforge-arch-agent-integration.md`](https://github.com/efreet111/engram-dotnet/blob/requirements-from-flowforge/docs/architecture/rfc/RFC-008-flowforge-arch-agent-integration.md)

### Problema que resuelve

El Arch Agent de FlowForge derivaba decisiones arquitectónicas desde cero en cada sesión, incluso cuando existían decisiones previas para el mismo módulo. Esto llevaba a:
- Duplicated reasoning
- Inconsistent design (nuevas decisiones podían contradecir existentes)
- Wasted context (rationale histórico ignorado)
- Broken continuity (cada sesión start stateless)

### ¿Por qué se hizo?

El Arch Agent necesita ser **stateful**: consultar engram por decisiones relevantes antes de diseñar, proceder informado — o proceder stateless si no hay decisiones previas.

### Descubrimiento clave durante implementación

HU-064 ya había implementado el filtro `type="decision"` en `mem_recall_for_module`. El gap real era la falta de un **tool dedicado con nombre explícito** para decisiones arquitectónicas. HU-061 no necesitaba implementar lógica nueva, sino exponer la funcionalidad existente de forma dirigida.

### Interfaz nueva

#### MCP Tool (nuevo)

```
mem_decisions_for_module(module: string, project?: string, limit?: number) → string
```

**Parámetros:**

| Parámetro | Tipo | Default | Descripción |
|-----------|------|---------|-------------|
| `module` (requerido) | `string` | — | Namespace o prefijo de módulo a consultar (ej: `"Engram.Auth"`, `"Engram"`) |
| `project` | `string` | `null` | Filtrar por nombre de proyecto |
| `limit` | `int` | `10` | Máximo de resultados (max: 50) |

**Qué hace internamente:**
1. Ejecuta dos queries al store: `type=decision` Y `type=architecture`
2. Merge de resultados + deduplicación por ID
3. Ordena por `CreatedAt` descendente (decisiones más recientes primero)
4. Formatea con `FormatSearchResults`

**Output ejemplo (con resultados):**

```
Found 2 decisions for module "Engram.Store":
[1] #42 (decision) — Use repository pattern
    We chose the repository pattern to abstract...
    2026-09-28T10:00:00Z | scope: personal | namespace: Engram.Store

[2] #38 (architecture) — SQLite backend design
    Decision to use SQLite as default backend...
    2026-09-20T14:30:00Z | scope: personal | namespace: Engram.Store
```

**Output ejemplo (vacío):**

```
No decisions found for module: "Engram.Store"
```

**Graceful handling:**

| Scenario | Behavior |
|----------|----------|
| No decisions for module | Return friendly message — proceed stateless |
| Engram unavailable | Return friendly message — proceed without memory context |
| Limit exceeded | Clamped to 50; results sorted by recency |

### Commit

`c38bc44` — `feat: add mem_decisions_for_module MCP tool (HU-061)`

---

## 9. HU-062 — Contradiction Detection (`mem_check_contradictions`)

**Spec:** [`docs/tasks/HU-062-contradiction-detection.md`](https://github.com/efreet111/engram-dotnet/blob/requirements-from-flowforge/docs/tasks/HU-001-HU-099/HU-062-contradiction-detection.md)

### Problema que resuelve

A medida que la base de memorias crece, las contradicciones se acumulan silenciosamente. Decisiones que contradicen decisiones previas no se detectan, erosionando la confianza en engram como source of truth.

### ¿Por qué se hizo?

La detección de contradicciones es lo que separa un "store de notas" de un "sistema de conocimiento verificable". Sin ella, engram puede tener dos decisiones activas que se oponen, y el usuario no lo sabe.

### Decisiones y trade-offs

- **Prioridad P3, solo relevante con 500+ memorias**: Implementar detección de contradicciones cuando la base de memorias es pequeña genera más ruido que valor. El threshold de aplicabilidad es explícito en la spec.
- **Tres heurísticas de detección:**
  1. **Direct conflict**: mismo topic_key, contenido diferente
  2. **Temporal supersedence**: decisión más nueva contradice una anterior sobre el mismo tema
  3. **Embedding similarity conflicts**: embeddings similares pero contenido diferente (soft — requiere ENG-418)
- **Periodic execution (cron) diferida**: On-demand only para v1. La ejecución automática requiere scheduler, más complejidad operativa.
- **ENG-412 (memory taxonomy) como hard prerequisite**: Sin lifecycle status (`active`/`deprecated`), no se puede hacer conflict detection que respete decisiones obsoletas.

### Interfaz nueva

#### MCP Tool (nuevo)

```
mem_check_contradictions(limit?: number, confidence_threshold?: number, types?: string) → string
```

**Parámetros:**

| Parámetro | Tipo | Default | Descripción |
|-----------|------|---------|-------------|
| `limit` | `int` | `10` | Máximo de contradicciones a retornar |
| `confidence_threshold` | `double` | `0.7` | Threshold de confianza para auto-mark supersedence (0.0-1.0) |
| `types` | `string` | `null` | Filtrar por tipos de memoria (separados por coma) |

**Output:**

```
Found 2 contradictions:

[1] HIGH CONFIDENCE (0.92) — Temporal supersedence
    #67 supersedes #42
    Topic: decision/auth-strategy
    Old (2026-08-10): "We use JWT with RS256"
    New (2026-09-01): "We use OAuth2 + JWT with RS384"
    Resolution options: [keep both] [mark superseded] [merge] [ignore]

[2] MEDIUM CONFIDENCE (0.71) — Direct conflict
    #55 conflicts with #38
    Topic: pattern/error-handling
    #55: "Use exceptions for all errors"
    #38: "Use Result<T> pattern for domain errors"
    Resolution options: [keep both] [merge] [ignore]
```

### Commit

`9789122` — `feat: add mem_check_contradictions MCP tool (HU-062)`

---

## 10. HU-064 — Code-Context Query Tools (MCP + REST + CLI)

**Spec:** [`docs/tasks/HU-064-code-context-query-tools.md`](https://github.com/efreet111/engram-dotnet/blob/requirements-from-flowforge/docs/tasks/HU-001-HU-099/HU-064-code-context-query-tools.md)

### Problema que resuelve

Los MCP tools existentes de engram son **memory-shaped** (`mem_recall`, `mem_search`). Un developer o AI coding agent preguntando "¿qué sabemos sobre este archivo?" tiene que hacer el bridging manualmente. Los tools code-aware permiten queries directas por file, symbol y namespace.

### ¿Por qué se hizo?

HU-064 es el prerequisito arquitectónico de HU-059 (Dev Agent) y HU-061 (Arch Agent). Sin query tools code-aware, los agentes no pueden recuperar memorias con contexto de código.

### Decisiones y trade-offs

- **`mem_conventions_for` no implementado**: Estaba en la spec original (HU-054) pero se diferió. No bloquea la integración FlowForge.
- **HttpStore thin-client mode con stubs**: Los stubs vacíos fueron el estado inicial. Se corrigieron con endpoints HTTP `/search/by-file`, `/search/by-module`, `/search/by-symbol`.
- **Índices con `text_pattern_ops` en Postgres**: Para queries prefix LIKE (`namespace LIKE 'Engram.Auth%'`), se necesita el operador `text_pattern_ops` en Postgres. Se implementó explícitamente.

### Interfaz nueva

#### MCP Tools (3 nuevos)

```
mem_recall_for_file(path: string, project?: string, type?: string, limit?: int) → string
mem_recall_for_module(module: string, project?: string, type?: string, limit?: int) → string
mem_recall_for_symbol(symbol: string, project?: string, limit?: int) → string
```

**Parámetros de `mem_recall_for_file`:**

| Parámetro | Tipo | Default | Descripción |
|-----------|------|---------|-------------|
| `path` (requerido) | `string` | — | Ruta de archivo (exacta o prefijo) |
| `project` | `string` | `null` | Filtrar por proyecto |
| `type` | `string` | `null` | Filtrar por tipo de memoria |
| `limit` | `int` | `10` | Máximo de resultados |

**Query SQL equivalente:** `file_path = '{path}' OR file_path LIKE '{path}/%'`

---

**Parámetros de `mem_recall_for_module`:**

| Parámetro | Tipo | Default | Descripción |
|-----------|------|---------|-------------|
| `module` (requerido) | `string` | — | Namespace o prefijo de módulo |
| `project` | `string` | `null` | Filtrar por proyecto |
| `type` | `string` | `null` | Filtrar por tipo de memoria |
| `limit` | `int` | `10` | Máximo de resultados |

**Query SQL equivalente:** `namespace LIKE '{module}%'`

---

**Parámetros de `mem_recall_for_symbol`:**

| Parámetro | Tipo | Default | Descripción |
|-----------|------|---------|-------------|
| `symbol` (requerido) | `string` | — | Nombre exacto del símbolo (clase, función, interface) |
| `project` | `string` | `null` | Filtrar por proyecto |
| `limit` | `int` | `10` | Máximo de resultados |

**Query SQL equivalente:** `symbol = '{symbol}'`

#### REST API (3 endpoints nuevos)

| Método | Endpoint | Parámetros requeridos | Parámetros opcionales |
|--------|----------|----------------------|---------------------|
| `GET` | `/search/by-file` | `path` | `project`, `type`, `limit` |
| `GET` | `/search/by-module` | `module` | `project`, `type`, `limit` |
| `GET` | `/search/by-symbol` | `symbol` | `project`, `limit` |

**Ejemplo:**

```bash
curl "http://localhost:7437/search/by-file?path=src/Auth/JwtBearer.cs&project=team/mi-api&type=decision&limit=5"
curl "http://localhost:7437/search/by-module?module=Engram.Store&project=team/mi-api&limit=10"
curl "http://localhost:7437/search/by-symbol?symbol=JwtBearerHandler&project=team/mi-api"
```

#### CLI (flags en comandos existentes)

```bash
# Search con filtros code-aware
engram search "" --file-path src/Auth/JwtBearer.cs
engram search "" --symbol IStore
engram search "" --namespace Engram.Store

# Save con metadata code-aware
engram save "JWT decision" "We use RS256 algorithm" \
  --type decision \
  --project team/mi-api \
  --file-path src/Auth/JwtBearer.cs \
  --symbol JwtBearerHandler \
  --namespace Engram.Auth
```

### Schema de soporte

| Columna | Tipo | Index | Descripción |
|---------|------|-------|-------------|
| `file_path` | `TEXT` | `idx_obs_file_path` (con `text_pattern_ops` en Postgres) | Ruta del archivo asociado |
| `symbol` | `TEXT` | `idx_obs_symbol` | Nombre del símbolo (clase, función, interface) |
| `namespace` | `TEXT` | `idx_obs_namespace` (con `text_pattern_ops` en Postgres) | Namespace/module |

### Commits

`aa965b2` — `feat: HU-064 code-context query tools — fix HttpStore stubs and expand tests`  
`d48e867` — `feat: implement code-context queries for file paths, modules, and symbols (HU-064)`

---

## 11. HU-065 — Sync Auto-Push (Auto-Enroll + Config Fallback)

**Spec:** [`docs/tasks/HU-065-sync-auto-push.md`](https://github.com/efreet111/engram-dotnet/blob/requirements-from-flowforge/docs/tasks/HU-001-HU-099/HU-065-sync-auto-push.md)

### Problema que resuelve

Después de levantar el servidor de engram, el sync **no funcionaba automáticamente**. Las memorias quedaban atrapadas en el cliente local aunque `sync.remote_url` estuviera configurado en `config.json`. La causa raíz: SyncManager no leía `sync.remote_url` de `config.json` y dependía exclusivamente de la variable de entorno `ENGRAM_SERVER_URL`.

### ¿Por qué se hizo?

Descubierto durante FF-003 (FlowForge onboarding flow). Si el sync no funciona automáticamente, las memorias de FlowForge no se sincronizan al servidor del equipo, rompiendo la promesa de team memory.

### Root cause

1. **SyncManager no leía `sync.remote_url` de config.json**: El campo existía pero no se usaba. Requería `ENGRAM_SERVER_URL` como env var.
2. **`sync.enroll` no triggeraba sync automáticamente**: Un proyecto podía estar enrolled pero sin sync activo.

### Decisiones y trade-offs

- **Se priorizó Feature 1 (config fallback) + Feature 2 (auto-enroll)**: Feature 3 (push automático post-startup) se dejó como enhancement futuro. No todas las implementaciones de sync quieren push automático al levantar.
- **Env var explícita siempre gana**: Si `ENGRAM_SERVER_URL` está seteada como env var, se ignora `sync.remote_url` en config.json. El usuario que setea env vars tiene intención explícita.
- **Compatibilidad hacia atrás**: Se mantienen todos los flags existentes de `engram sync enroll`.

### Interfaz nueva

#### CLI (flag nuevo en comando existente)

```
engram sync enroll --project <name> --auto-enroll
```

**Parámetros:**

| Parámetro | Tipo | Default | Descripción |
|-----------|------|---------|-------------|
| `--project` | `string` | — | Proyecto a enrollar |
| `--auto-enroll` | `flag` | `false` | También enrolla el proyecto en el servidor remoto, habilitando auto-sync en el primer save |
| `--interactive` | `bool` | `false` | Enrollment interactivo |
| `--behavior` | `string` | `fail-loud` | Comportamiento de sync: `silent-skip` o `fail-loud` |
| `--exclude-server` | `string[]` | `[]` | Servers a excluir del sync (repetible) |

**Mutual exclusivity:** `--auto-enroll` no es compatible con `--all` ni `--interactive`.

#### Configuración (`~/.engram/config.json`)

```json
{
  "sync": {
    "remote_url": "http://192.168.0.178:7437"
  },
  "auto_enroll": true,
  "auto_sync": true
}
```

**Lógica de lectura (HU-065 F1):**

```
ApplySyncConfigFromFile():
  1. Si ENGRAM_SYNC_AUTO_SYNC no está seteada → usar auto_sync de config.json
  2. Si ENGRAM_SERVER_URL no está seteada → usar sync.remote_url de config.json como fallback
  3. Loggear: "[engram] Using sync.remote_url from config.json: {url}"
```

### Commits

`a62d6c1` — `feat(sync): read sync.remote_url from config.json as ENGRAM_SERVER_URL fallback (HU-065 F1)`  
`fb9a6cb` — `feat(sync): add --auto-enroll flag to sync enroll command (HU-065 F2)`

---

## Anexo: Campos `status` y `topic_key` en Observations (ENG-412)

Adicional a las HU, ENG-412 introdujo un sistema de **lifecycle status** para decisiones arquitectónicas que afecta todas las interfaces existentes:

### Campo `status`

| Valor | Significado | Cuándo usar |
|-------|-------------|-------------|
| `active` | Decisión vigente (default) | Nueva decisión o decisión en uso |
| `deprecated` | Decisión obsoleta | Cuando una nueva decisión reemplaza la anterior |
| `deleted` | Soft-delete | Cuando la decisión ya no es relevante |

### Parámetros nuevos en `mem_search` (existente)

| Parámetro | Tipo | Descripción |
|-----------|------|-------------|
| `status` | `string` | Filtrar por estado específico |
| `include_deprecated` | `bool` | Incluir decisiones obsoletas en resultados |
| `grouped` | `bool` | Agrupar resultados por `topic_key` |

### Nuevo MCP Tool

```
mem_decision_tree(project?: string, type?: string) → string
```

Muestra panorama de decisiones agrupadas por componente/tema. Ver [`docs/FLOWFORGE-INTEGRATION.md`](https://github.com/efreet111/engram-dotnet/blob/requirements-from-flowforge/docs/FLOWFORGE-INTEGRATION.md).

### Convenciones de `topic_key`

```
<categoria>/<nombre-corto>

Categorías: decision/, impl/, pattern/, bugfix/, session/, architecture/
```

- **Evolución (mismo tema):** usar mismo `topic_key` → upsert
- **Reemplazo:** nuevo `topic_key` + deprecar el antiguo con `mem_update(id, status="deprecated")`

---

## Anexo: Dependencias entre HU

```
                    ENG-416 (Schema Evolution)
                           │
           ┌──────────────┼──────────────┐
           ▼              ▼              ▼
      HU-063         HU-064          HU-059
    (Schema cols)  (Query tools)   (Dev Agent)
           │              │              │
           │         ┌────┴────┐          │
           │         ▼         ▼          │
           │      HU-061    HU-062         │
           │   (Arch Agent)(Contradict)   │
           │         │         │          │
           │         ▼         ▼          │
           │      HU-059  (HU-062 dep)    │
           │   (Dev Agent)                │
           │         │                    │
           ▼         ▼                    ▼
      HU-053 ───► HU-060 ──────────► HU-059
   (engram watch)  (Meta-extract)  (Pre/post hooks)
                           │
                           ▼
                    FLOWFORGE-INTEGRATION.md
                     (Protocolo de hooks)
```

---

## Referencias cruzadas

| HU | Spec | RFC | Integration Doc |
|----|------|-----|----------------|
| HU-050 | HU-050-quick-capture-cli.md | — | — |
| HU-051 | HU-051-git-hooks-integration.md | — | — |
| HU-052 | HU-052-dev-observability.md | — | CLI-REFERENCE.md |
| HU-053 | HU-053-code-aware-capture.md | — | FLOWFORGE-INTEGRATION.md |
| HU-055 | HU-055-onboarding-flow.md | — | — |
| HU-059 | HU-059-code-aware-dev-agent.md | — | FLOWFORGE-INTEGRATION.md |
| HU-060 | HU-060-code-aware-capture-parte-b.md | — | FLOWFORGE-INTEGRATION.md |
| HU-061 | HU-061-code-context-arch-agent.md | RFC-008 | FLOWFORGE-INTEGRATION.md |
| HU-062 | HU-062-contradiction-detection.md | — | — |
| HU-064 | HU-064-code-context-query-tools.md | — | FLOWFORGE-INTEGRATION.md, API-REFERENCE.md |
| HU-065 | HU-065-sync-auto-push.md | — | — |

Todos los links de specs apuntan a `https://github.com/efreet111/engram-dotnet/blob/requirements-from-flowforge/docs/tasks/HU-001-HU-099/{filename}`.
