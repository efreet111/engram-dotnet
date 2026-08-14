# HU-054 — Code-context query tools

**ENG:** ENG-484  
**Tipo:** Feature  
**Prioridad:** P2  
**Esfuerzo:** L (~1 semana)  
**Estado:** Idea  
**Origen:** ← feature ideas 2026-08 (ENGRAM-IDEA-002)

---

## Problema que resuelve

Los MCP tools actuales de engram son **memory-shaped** (`mem_recall`, `mem_search`), no **code-shaped**. Un desarrollador (o AI coding agent) preguntando "¿qué sabemos sobre este archivo?" tiene que hacer el bridging manualmente: buscar por tag, esperar que el tag correcto exista, etc.

**La efectividad del agent está limitada por qué tan bien puede navegar las memorias.**

AI coding agents (Claude Code, Cursor, etc.) consumen engram como un generic memory store. No pueden preguntar "¿qué sabe el equipo sobre `src/Auth/JwtBearer.cs`?" sin filtrado manual.

---

## Propuesta de solución

Agregar una familia de **MCP tools code-aware** a engram:

```
mem_recall_for_file(path)         → memorias taggeadas con o linkedas a un file path específico
mem_recall_for_symbol(symbol)     → memorias sobre una función, clase o símbolo
mem_decisions_for_module(module)  → architectural decisions para un módulo
mem_conventions_for(namespace)    → coding conventions scoped a un namespace
```

### Prerequisito crítico: Schema evolution

**Estos tools requieren que las memorias puedan ser taggeadas con metadata de código** (path, symbol, namespace). Hoy, el schema de engram NO tiene estos campos.

**ENG-416 (Schema evolution con migraciones versionadas)** es un prerequisito.

### Cómo se populate el code metadata

**Opción 1: Manual** (el usuario taggea)
```bash
engram save --title "Decisión auth" --content "..." --file-path src/Auth/JwtBearer.cs
```
- ❌ Fricción alta, el usuario no lo va a hacer

**Opción 2: Auto** (el agent pasa el contexto)
```json
{
  "tool": "mem_save",
  "content": "Decisión: usar JWT con RS256",
  "context": {
    "file_path": "src/Auth/JwtBearer.cs",
    "symbol": "JwtBearerHandler",
    "namespace": "Engram.Auth"
  }
}
```
- ✅ Sin fricción, el agent pasa el contexto automáticamente
- ✅ Requiere que los AI agents soporten pasar metadata de contexto

**Opción 3: Híbrido** (manual + auto)
- Manual: CLI flags (`--file-path`, `--symbol`)
- Auto: MCP tool acepta `context` object
- ✅ Flexibilidad máxima

**Recomendación:** Opción 3 (híbrido)

---

## Criterios de aceptación

- [ ] **Schema extension** (requiere ENG-416):
  - [ ] Agregar campos opcionales a `observations`:
    - `file_path TEXT` (ej: "src/Auth/JwtBearer.cs")
    - `symbol TEXT` (ej: "JwtBearerHandler")
    - `namespace TEXT` (ej: "Engram.Auth")
  - [ ] Migración idempotente (SQLite + Postgres)
  - [ ] Backward-compatible (memorias existentes funcionan, solo no tienen code metadata)

- [ ] **MCP tools code-aware**:
  - [ ] `mem_recall_for_file(path)`:
    - [ ] Acepta `path` (exact match o prefix)
    - [ ] Retorna memorias donde `file_path = path` OR `file_path LIKE '{path}/%'`
    - [ ] Response time <50ms (cached)
  - [ ] `mem_recall_for_symbol(symbol)`:
    - [ ] Acepta `symbol` (function, class, etc.)
    - [ ] Retorna memorias donde `symbol = symbol`
  - [ ] `mem_decisions_for_module(module)`:
    - [ ] Acepta `module` (namespace o directorio)
    - [ ] Retorna memorias de `type = decision` donde `namespace LIKE '{module}%'`
  - [ ] `mem_conventions_for(namespace)`:
    - [ ] Acepta `namespace`
    - [ ] Retorna memorias de `type = convention` donde `namespace = namespace`

- [ ] **CLI support**:
  - [ ] `engram save --file-path <path>` — taggear con file path
  - [ ] `engram save --symbol <symbol>` — taggear con symbol
  - [ ] `engram save --namespace <ns>` — taggear con namespace
  - [ ] `engram search --file-path <path>` — buscar por file path

- [ ] **Tests**:
  - [ ] Schema migration (SQLite + Postgres)
  - [ ] Cada MCP tool (4 tools × 3-5 tests cada uno)
  - [ ] CLI flags funcionan
  - [ ] Backward compatibility (memorias viejas sin code metadata)

- [ ] **Documentación**:
  - [ ] `docs/API-REFERENCE.md` — nuevos MCP tools
  - [ ] `docs/01-QUICK-START.md` — ejemplo de code-aware queries
  - [ ] `README.md` — mención de code-aware features

---

## Implementación técnica

### Dónde tocar código

1. **Schema migration** (ENG-416 prerequisite):
   - `src/Engram.Store/SqliteStore.cs` — ALTER TABLE
   - `src/Engram.Store/PostgresStore.cs` — ALTER TABLE
   - `src/Engram.Store/ILocalSyncStore.cs` — actualizar `AddObservationParams`

2. **MCP tools**:
   - `src/Engram.Mcp/EngramTools.cs` — agregar 4 nuevos tools:
     - `MemRecallForFile`
     - `MemRecallForSymbol`
     - `MemDecisionsForModule`
     - `MemConventionsFor`

3. **Store queries**:
   - `src/Engram.Store/SqliteStore.cs` — agregar métodos:
     - `GetMemoriesByFilePathAsync(string path)`
     - `GetMemoriesBySymbolAsync(string symbol)`
     - `GetDecisionsByModuleAsync(string module)`
     - `GetConventionsByNamespaceAsync(string ns)`
   - `src/Engram.Store/PostgresStore.cs` — mismo

4. **CLI**:
   - `src/Engram.Cli/Program.cs` — agregar flags a `save` y `search`

5. **Tests**:
   - `tests/Engram.Mcp.Tests/CodeAwareToolsTests.cs` — 12-20 tests
   - `tests/Engram.Store.Tests/CodeMetadataTests.cs` — 5-7 tests

### Edge cases a manejar

- ¿Qué pasa si `path` es prefix vs exact match?
  - `mem_recall_for_file("src/Auth/")` → retorna memorias de todo el módulo
  - `mem_recall_for_file("src/Auth/JwtBearer.cs")` → retorna memorias específicas del archivo
  - Documentar comportamiento

- ¿Qué pasa si no hay memorias con code metadata?
  - Retornar empty list con mensaje: "No memories found for file: {path}"

- ¿Qué pasa con backward compatibility?
  - Memorias viejas sin `file_path`/`symbol`/`namespace` funcionan normal
  - Solo no aparecen en code-aware queries

- ¿Qué pasa con performance?
  - Indexar `file_path`, `symbol`, `namespace` para queries rápidos
  - Cache en memoria para queries frecuentes

---

## Fuera de alcance

- ❌ Auto-extracción de símbolos desde código (requiere parser de lenguaje)
- ❌ Full-text search de código (solo metadata-based filtering)
- ❌ Integración con LSP para auto-tagging
- ❌ UI visual para explorar memorias por file/symbol

---

## Cómo probarlo

```bash
# 1. Build
dotnet build -c Release

# 2. Guardar memoria con code metadata
./src/Engram.Cli/bin/Release/net10.0/engram save \
  --title "Decisión auth" \
  --content "Usamos JWT con RS256" \
  --type decision \
  --file-path src/Auth/JwtBearer.cs \
  --symbol JwtBearerHandler \
  --namespace Engram.Auth

# 3. MCP tool: mem_recall_for_file
# (desde Claude Code o MCP client)
mem_recall_for_file(path: "src/Auth/JwtBearer.cs")
# → retorna la memoria #1234

# 4. MCP tool: mem_decisions_for_module
mem_decisions_for_module(module: "Engram.Auth")
# → retorna todas las decisions del módulo Auth

# 5. CLI search por file path
./src/Engram.Cli/bin/Release/net10.0/engram search --file-path src/Auth/
# → retorna todas las memorias del módulo Auth

# 6. Tests
dotnet test tests/Engram.Mcp.Tests/ --filter "CodeAware"
dotnet test tests/Engram.Store.Tests/ --filter "CodeMetadata"
```

---

## Métricas de éxito

- **Adopción**: 40%+ de memorias nuevas tienen code metadata (file_path, symbol, o namespace)
- **Performance**: `mem_recall_for_file` responde en <50ms (cached)
- **Diferenciador**: engram es el **primer memory server code-aware** (ningún competidor tiene esto)

---

## Dependencias

- 🔴 **BLOCKER:** ENG-416 (Schema evolution con migraciones versionadas) — prerequisito
- 🔗 Relacionado: ENG-480 (quick-capture), ENG-481 (git hooks)
- ⚠️ **Riesgo:** Schema migration puede ser más compleja de lo esperado

---

## ¿Por qué esto es un diferenciador?

**Ningún competidor en el espacio memory-for-AI tiene code-aware recall:**
- Second Brain → generic memory store
- letta → conversational memory
- Mem0 → generic memory layer

**engram siendo el primer code-aware memory server es un category-creating move.**

---

## Referencias

- [ENGRAM-IDEA-002](/mnt/86FC44B0FC449BF5/Proyectos/Desarrollo/engram-feature-ideas.md#engram-idea-002--code-context-query-tools-mem_recall_for_) — idea original
- [ENG-484 en BACKLOG](../../BACKLOG.md#eng-484)
- [ENG-416](../../BACKLOG.md#eng-416) — Schema evolution (prerequisito)
