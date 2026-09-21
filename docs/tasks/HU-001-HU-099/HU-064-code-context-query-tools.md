# HU-064 — Code-Context Query Tools

**ENG:** ENG-484
**Tipo:** Feature
**Prioridad:** P2
**Esfuerzo:** L (~1 semana)
**Estado:** Ready
**Origen:** ← requirements/FlowForge / ← HU-059 prerequisite
**Depende de:** HU-063 (ENG-416 Schema Evolution)

---

## Problema que resuelve

Los MCP tools actuales de engram son **memory-shaped** (`mem_recall`, `mem_search`), no **code-shaped**. Un developer o AI coding agent preguntando "¿qué sabemos sobre este archivo?" tiene que hacer el bridging manualmente. Los tools code-aware permiten queries directas por file, symbol y namespace.

---

## Criterios de aceptación

- [ ] `mem_recall_for_file(path)` — retorna memorias donde `file_path = path` OR `file_path LIKE '{path}/%'`
- [ ] `mem_recall_for_module(module)` — retorna memorias donde `namespace LIKE '{module}%'`
- [ ] `mem_recall_for_symbol(symbol)` — retorna memorias donde `symbol = symbol`
- [ ] Graceful degradation cuando no hay memorias para el contexto
- [ ] Tests para cada tool
- [ ] CLI support (`--file-path`, `--symbol`, `--namespace`)

---

## Tasks (Implementation)

- [ ] **T1** (requiere HU-063): Schema extension — `file_path`, `symbol`, `namespace` en `observations` (ya hecho en HU-063)
- [ ] **T2**: Store queries — `GetMemoriesByFilePathAsync`, `GetMemoriesByModuleAsync`, `GetMemoriesBySymbolAsync` en ambos stores
- [ ] **T3**: MCP tools — `MemRecallForFile`, `MemRecallForModule`, `MemRecallForSymbol` en `EngramTools.cs`
- [ ] **T4**: CLI flags — `--file-path`, `--symbol`, `--namespace` en `save` y `search`
- [ ] **T5**: Tests — `CodeAwareToolsTests.cs` (4 tools × scenarios)
- [ ] **T6**: Documentación — actualizar `API-REFERENCE.md`

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
