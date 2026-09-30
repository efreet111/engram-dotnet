# HU-061 — code-context-arch-agent

**As**: AI Arch Agent  
**I want**: query past architectural decisions for a module before designing  
**To**: build on existing decisions instead of re-deriving them every session

**Status**: ✅ Done (2026-09-29)

---

## Acceptance Criteria

- [x] `mem_recall_for_module(module, type="decision")` validates as working filter
- [x] `mem_decisions_for_module(module, limit?)` implemented as MCP tool dedicated
- [x] FlowForge integration protocol documented in RFC
- [x] Unit tests for `mem_decisions_for_module` pass
- [x] Arch agent can query prior decisions before new design work
- [x] Graceful handling when no prior decisions exist (proceed normally)
- [x] Graceful handling when Engram service is unavailable (stateless derivation)

---

## Tasks (Re-interpreted)

| # | Tarea | Estado |
|---|-------|--------|
| 1 | Validar que `mem_recall_for_module(module, type="decision")` funciona como filtro existente | ✅ Confirmado — GetMemoriesByModuleAsync ya filtra por type |
| 2 | Implementar `mem_decisions_for_module(module, limit?)` como MCP tool dedicado | ✅ Hecho — `EngramTools.cs:312` dual-type query |
| 3 | Documentar protocolo de integración FlowForge (RFC en `docs/architecture/rfc/`) | ✅ Hecho — RFC-008 |
| 4 | Tests unitarios para `mem_decisions_for_module` | ✅ Hecho — 7 tests en `MemDecisionsForModuleTests.cs` |

### Detalle de tareas

- [ ] **Tarea 1**: Validar que `mem_recall_for_module(module, type="decision")` funciona como filtro
  - Escribir script de prueba/manual testing
  - Verificar que el filtro `type="decision"` retorna solo decisiones del módulo
  - Si no funciona, arreglar HU-064 o crear bugfix

- [ ] **Tarea 2**: Implementar `mem_decisions_for_module(module, limit?)` como MCP tool dedicado
  - Crear nuevo tool en el servidor MCP de Engram
  -签过滤: `topic_key` o metadata que identifique decisiones por módulo
  - Soportar `limit` opcional (default: 50)
  - Retornar estructura consistente con otros tools de recall

- [ ] **Tarea 3**: Documentar protocolo de integración FlowForge (RFC)
  - Crear `docs/architecture/rfc/RFC-XXX-flowforge-arch-agent-integration.md`
  - Definir: cuándo el Arch Agent consulta decisiones, qué pasa con los resultados, cómo se muestran al usuario
  - Incluir secuencia de llamada y ejemplos de respuesta

- [ ] **Tarea 4**: Tests unitarios para `mem_decisions_for_module`
  - Test: filtro por módulo retorna solo decisiones de ese módulo
  - Test: límite funciona correctamente
  - Test: retorna vacío graceful cuando no hay decisiones
  - Test: maneja errores de conexión

---

## Dependencies

| ID | Descripción | Estado |
|----|-------------|--------|
| ENG-416 | Schema evolution | ✅ Done |
| ENG-484 | Code-context query tools | ✅ Done — HU-064 (file/module/symbol) + HU-061 (`mem_decisions_for_module`) |
| ENG-412 | Memory taxonomy (HU-062) | ✅ Done |

---

## Notes

### Re-interpretación (2026-09-29)

HU-061 fue re-interpretada porque ENG-484 **no implementó `mem_decisions_for_module`** — HU-064 solo implementó `mem_recall_for_file`, `mem_recall_for_module`, y `mem_recall_for_symbol`, pero no el tool dedicado para decisiones.

**Scope real de HU-061**:
1. Validar que el filtro `type="decision"` en `mem_recall_for_module` existe y funciona
2. Implementar `mem_decisions_for_module(module, limit?)` como tool MCP dedicado
3. Documentar protocolo de integración FlowForge en RFC
4. Tests para el nuevo tool

### Notas adicionales

- Esta HU es principalmente **documentación + un MCP tool dedicado**
- La integración real con FlowForge Arch Agent se documenta en el RFC (Tarea 3)
- El tool `mem_decisions_for_module` es más especializado que el filtro genérico: permite filtrar por `topic_key` de decisión y tiene semantics explícitas para el contexto del Arch Agent
- HU-064 y HU-061 comparten ENG-484 como dependencia parcial — implementar el tool una vez

### FlowForge Mapping

- FlowForge HU-035 (Arch Agent stateful) → mapea a esta HU-061
- La integración con FlowForge se documenta en el RFC de la Tarea 3

---

## Implementation Notes (Post-Development)

### Lo que se implementó

| Componente | Ubicación | Detalle |
|-----------|-----------|---------|
| MCP tool | `src/Engram.Mcp/EngramTools.cs:312` | `mem_decisions_for_module(module, project?, limit?)` |
| Dual-type query | `EngramTools.cs:321-326` | Consulta `type=decision` + `type=architecture`, merge + sort desc |
| RFC | `docs/architecture/rfc/RFC-008-flowforge-arch-agent-integration.md` | Protocolo de integración Arch Agent |
| Tests | `tests/Engram.Mcp.Tests/MemDecisionsForModuleTests.cs` | 7 tests covering all scenarios |

### Commit

`c38bc44` — `feat: add mem_decisions_for_module MCP tool (HU-061)`

### Descubrimiento clave

Tarea 1 (`mem_recall_for_module` con filtro `type=decision`) **ya funcionaba** — HU-064 implementó el filtro en el store layer. El gap era la falta de un **tool dedicado** con nombre explícito para decisiones arquitectónicas.
