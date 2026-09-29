# HU-061 — code-context-arch-agent

**As**: AI Arch Agent  
**I want**: query past architectural decisions for a module before designing  
**To**: build on existing decisions instead of re-deriving them every session

**Status**: In Progress

---

## Acceptance Criteria

- [ ] `mem_recall_for_module(module, type="decision")` validates as working filter
- [ ] `mem_decisions_for_module(module, limit?)` implemented as MCP tool dedicated
- [ ] FlowForge integration protocol documented in RFC
- [ ] Unit tests for `mem_decisions_for_module` pass
- [ ] Arch agent can query prior decisions before new design work
- [ ] Graceful handling when no prior decisions exist (proceed normally)
- [ ] Graceful handling when Engram service is unavailable (stateless derivation)

---

## Tasks (Re-interpreted)

| # | Tarea | Estado |
|---|-------|--------|
| 1 | Validar que `mem_recall_for_module(module, type="decision")` funciona como filtro existente | 🔴 Por hacer |
| 2 | Implementar `mem_decisions_for_module(module, limit?)` como MCP tool dedicado | 🔴 Falta |
| 3 | Documentar protocolo de integración FlowForge (RFC en `docs/architecture/rfc/`) | 🔴 Falta |
| 4 | Tests unitarios para `mem_decisions_for_module` | 🔴 Falta |

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
| ENG-484 | Code-context query tools | 🟡 Parcial (HU-064 implementó file/module/symbol, falta `mem_decisions_for_module`) |
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
