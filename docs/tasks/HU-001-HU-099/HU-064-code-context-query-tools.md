# HU-064 — Code-Context Query Tools

**ENG:** ENG-484
**Tipo:** Feature
**Prioridad:** P2
**Esfuerzo:** L (~1 semana)
**Estado:** Done
**Origen:** ← requirements/FlowForge / ← HU-059 prerequisite
**Depende de:** HU-063 (ENG-416 Schema Evolution)

---

## Problema que resuelve

Los MCP tools actuales de engram son **memory-shaped** (`mem_recall`, `mem_search`), no **code-shaped**. Un developer o AI coding agent preguntando "¿qué sabemos sobre este archivo?" tiene que hacer el bridging manualmente. Los tools code-aware permiten queries directas por file, symbol y namespace.

---

## Criterios de aceptación

- [x] `mem_recall_for_file(path)` — retorna memorias donde `file_path = path OR file_path LIKE '{path}/%'`
- [x] `mem_recall_for_module(module)` — retorna memorias donde `namespace LIKE '{module}%'`
- [x] `mem_recall_for_symbol(symbol)` — retorna memorias donde `symbol = symbol`
- [x] Graceful degradation cuando no hay memorias para el contexto
- [x] Tests para cada tool
- [x] CLI support (`--file-path`, `--symbol`, `--namespace` en `save` y `search`)

---

## Tasks (Implementation)

- [x] **T1** (requiere HU-063): Schema extension — `file_path`, `symbol`, `namespace` en `observations` (ya hecho en HU-063)
- [x] **T2**: Store queries — `GetMemoriesByFilePathAsync`, `GetMemoriesByModuleAsync`, `GetMemoriesBySymbolAsync` en ambos stores
- [x] **T3**: MCP tools — `MemRecallForFile`, `MemRecallForModule`, `MemRecallForSymbol` en `EngramTools.cs`
- [x] **T4**: CLI flags — `--file-path`, `--symbol`, `--namespace` en `save` y `search`
- [x] **T5**: Tests — `CodeAwareToolsTests.cs` (17 tests: 17 passing)
- [x] **T6**: Documentación — `MCP-TEST-CASES.md` actualizado con 3 nuevos casos (CASE 18-20)

---

## Dependencias

- 🔴 **BLOCKER**: HU-063 (ENG-416 Schema Evolution) — sin schema no hay columnas para query

---

## MCP Tools spec

```
mem_recall_for_file(path: string, limit?: number, type?: string)
  → memorias donde file_path = path OR file_path LIKE '{path}/%'
  → usa índice idx_obs_file_path

mem_recall_for_module(module: string, limit?: number, type?: string)
  → memorias donde namespace LIKE '{module}%'
  → usa índice idx_obs_namespace

mem_recall_for_symbol(symbol: string, limit?: number)
  → memorias donde symbol = symbol
  → usa índice idx_obs_symbol
```

### Query patterns (HU-054)

| Pattern | SQL |
|---------|-----|
| Exact file | `file_path = 'src/Auth/JwtBearer.cs'` |
| Prefix files | `file_path LIKE 'src/Auth/%'` |
| Module | `namespace LIKE 'Engram.Auth%'` |
| Symbol | `symbol = 'JwtBearerHandler'` |

---

## Fuera de alcance

- ❌ Auto-extracción de símbolos desde código (requiere parser)
- ❌ Full-text search de código (solo metadata-based filtering)
- ❌ Integración LSP para auto-tagging

---

## Referencias

- [HU-054](../HU-001-HU-099/HU-054-code-context-queries.md) — spec detallada (ENG-484)
- [HU-063](../HU-001-HU-099/HU-063-schema-evolution.md) — prerequisito (ENG-416)
- [HU-059](../HU-001-HU-099/HU-059-code-aware-dev-agent.md) — consumidor principal
- [HU-061](../HU-001-HU-099/HU-061-code-context-arch-agent.md) — comparte ENG-416/484

---

## Spec (Post-Development)

### Implementación real vs planificada

| Componente | Planificado (HU-054) | Implementado | Desviación |
|------------|---------------------|--------------|------------|
| `mem_recall_for_file` | ✅ | ✅ `EngramTools.cs:252` | + `project` param |
| `mem_recall_for_module` | ✅ | ✅ `EngramTools.cs:282` | + `project` param |
| `mem_recall_for_symbol` | ✅ | ✅ `EngramTools.cs:312` | + `project` param |
| `mem_conventions_for` | ✅ | ❌ | Deferred (no planificado en HU-064) |
| `GetMemoriesByFilePathAsync` | ✅ | ✅ SQLite + Postgres + HttpStore | — |
| `GetMemoriesByModuleAsync` | ✅ | ✅ SQLite + Postgres + HttpStore | — |
| `GetMemoriesBySymbolAsync` | ✅ | ✅ SQLite + Postgres + HttpStore | — |
| CLI flags | `--file-path`, `--symbol`, `--namespace` | ✅ | Solo en CLI, no en REST API |

### Gaps resueltos (2026-09-22)

1. ✅ **HttpStore thin-client mode** — implementado con nuevos endpoints HTTP `/search/by-file`, `/search/by-module`, `/search/by-symbol` (`EngramServer.cs:422-463`, `HttpStore.cs:200-233`)

2. ✅ **REST API code-context queries** — nuevos endpoints REST agregados; CASE 18-20 en `MCP-TEST-CASES.md` actualizados para usar los endpoints correctos

3. ✅ **Tests expandidos** — 7 tests nuevos en `EngramToolsTests.cs` (MCP wrappers) + 7 tests nuevos en `HttpStoreTests.cs` (integración HTTP)

### Firmas MCP reales

```csharp
// EngramTools.cs:252, 282, 312
MemRecallForFile(path: string, project?: string, type?: string, limit?: int)
MemRecallForModule(module: string, project?: string, type?: string, limit?: int)
MemRecallForSymbol(symbol: string, project?: string, limit?: int)
```

> Nota: El param `project` adicional no estaba en el spec original pero es un superset harmless.

---

## Implementation Notes

- HU-063 (ENG-416) schema evolution completada: columnas `file_path`, `symbol`, `namespace` + índices (`idx_obs_file_path`, `idx_obs_symbol`, `idx_obs_namespace`) agregadas a `observations`
- Indexes con `text_pattern_ops` en Postgres para queries prefix LIKE
- Graceful degradation: mensajes descriptivos cuando no hay memorias (`"No memories found for file: {path}"`)
- CLI renderiza metadata code-context en output (`PrintSearchResults` muestra `symbol:` y `namespace:`)
- 17/17 tests passing (SQLite store-level)

---

## Deviations from Plan

| Tipo | Descripción | Impacto |
|------|-------------|---------|
| Scope reduction | `mem_conventions_for` no implementado (estaba en HU-054 spec) | BAJO — feature deferred, no blocking |
| Stub no documentado | HttpStore retorna stubs vacíos sin error | ALTO — thin-client code-aware queries no funcionan |
| Doc stale | CASE 18-20 en MCP-TEST-CASES.md usan REST API que ignora los params | ALTO — doc no refleja comportamiento real |
| Test gap | Tests solo SQLite store-level, no MCP wrapper ni Postgres | MEDIO — cobertura insuficiente para claim "tests para cada tool" |

---

## Referencias

- [HU-054](../HU-001-HU-099/HU-054-code-context-queries.md) — spec detallada (ENG-484)
- [HU-063](../HU-001-HU-099/HU-063-schema-evolution.md) — prerequisito (ENG-416)
- [HU-059](../HU-001-HU-099/HU-059-code-aware-dev-agent.md) — consumidor principal
- [HU-061](../HU-001-HU-099/HU-061-code-context-arch-agent.md) — comparte ENG-416/484
- [CodeAwareToolsTests.cs](tests/Engram.Store.Tests/CodeAwareToolsTests.cs) — 17 tests passing
- [MCP-TEST-CASES.md](../MCP-TEST-CASES.md) — CASE 18-20 actualizados (endpoints corregidos)

---

## Estado final

**HU-064: ✅ Done**

Todos los gaps resueltos:
1. ✅ HttpStore — implementado con endpoints `/search/by-file`, `/search/by-module`, `/search/by-symbol`
2. ✅ MCP-TEST-CASES.md — CASE 18-20 usan endpoints correctos
3. ✅ Tests — 14 tests nuevos (7 MCP + 7 HttpStore)

**Auditoría + fix completados:** 2026-09-22
