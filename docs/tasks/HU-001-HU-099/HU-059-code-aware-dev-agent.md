# HU-059 — code-aware-dev-agent

**As**: AI Dev Agent  
**I want**: query memories by code context (file, module, symbol) before and after edits  
**To**: produce code consistent with architectural decisions and team conventions

---

**ENG:** ENG-416 / ENG-484  
**Tipo:** Feature  
**Prioridad:** P1  
**Esfuerzo:** L (trabajo real: S — ver Notes)  
**Estado:** ✅ Done (2026-09-28)  
**Origen:** ← requirements/FlowForge  
**Depende de:** HU-063 (ENG-416 Schema Evolution), HU-064 (ENG-484 Query Tools), HU-053 (ENG-483 engram watch)

---

## Acceptance Criteria

- [x] `mem_recall_for_file(file_path, limit?, type?)` returns memories associated with a file
- [x] `mem_recall_for_module(module, limit?, type?)` returns memories for a namespace/module
- [x] `mem_recall_for_symbol(symbol, limit?)` returns memories for a class or function
- [x] Pre-edit hook surfaces relevant memories before file modification
- [x] Post-edit hook offers to capture the change as a memory
- [x] Symbol-level recall integrated when modifying classes/functions
- [x] Graceful degradation when no memories exist for target

---

## Tasks (Implementation)

- [x] ENG-416: Schema evolution — add file_path, symbol, namespace columns to observations table
- [x] ENG-416: Create indexes on file_path, symbol, namespace for query performance
- [x] ENG-484: Implement `mem_recall_for_file` MCP tool
- [x] ENG-484: Implement `mem_recall_for_module` MCP tool
- [x] ENG-484: Implement `mem_recall_for_symbol` MCP tool
- [x] ENG-484: Add `mem_recall_for_module` to Dev agent initialization
- [x] ENG-484: Wire pre-edit hook to `mem_recall_for_file`
- [x] ENG-483: Implement `engram watch` for automatic metadata capture (soft blocker)
- [x] Add unit tests for each query tool
- [x] Add integration tests for Dev agent memory flow

---

## Dependencies

- 🔴 ENG-416: Schema evolution (prerequisite for all)
- 🔴 ENG-484: Code-context query tools
- 🟡 ENG-483: engram watch (soft — without it, memories lack automatic metadata)

---

## Notes

- ENG-416 is the universal prerequisite — no code-aware metadata without schema evolution
- HU-061 (Arch Agent) shares the same ENG dependencies — implement ENG-416/484 once, benefit twice
- FlowForge HU-033 maps to this HU

---

## Implementation Notes (Post-Development)

### Lo que realmente se hizo

| Componente | Plan original | Implementado | Nota |
|-----------|-------------|-------------|------|
| Schema evolution | Tarea HU-059 | ✅ Ya hecho en HU-063 | Dependencia ya resuelta |
| MCP tools | Tarea HU-059 | ✅ Ya hecho en HU-064 | Dependencia ya resuelta |
| engram watch | Tarea HU-059 | ✅ Ya hecho en HU-053 | Dependencia ya resuelta |
| Integration tests | Nueva tarea | ✅ 6 tests en `CodeAwareDevAgentTests.cs` | 6/6 passing |
| Docs hooks pre/post-edit | Nueva tarea | ✅ `FLOWFORGE-INTEGRATION.md` secciones 107-144 | |

### Esfuerzo real
El BACKLOG dice "L (~1 semana)" pero el trabajo real fue **S (~1h)**: 6 tests de integración + documentación de hooks + actualizar Tasks. Las 3 dependencias (HU-063, HU-064, HU-053) estaban Done desde 2026-09-21/22/24.

### Nota sobre hooks
Los hooks pre-edit y post-edit son responsabilidad del **Dev Agent** (FlowForge), no de engram-dotnet. engram-dotnet provee las herramientas (MCP + CLI) y la documentación de cómo invocarlas.
