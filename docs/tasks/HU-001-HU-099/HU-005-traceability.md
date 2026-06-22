# HU-005: traceability

**Status**: 🟡 In Progress
**Owner**: @owner
**Created**: 2026-06-01
**Priority**: Should

---

## 🎯 Intent

Hoy un spec.md describe qué construir pero no dice **por qué** se construye ni **de dónde** viene el requerimiento. Cuando un issue de GitHub, un bug report, o una decisión técnica genera un RF, esa conexión se pierde en el primer salto a spec.md.

Esto impide:
- Saber si un requisito sigue siendo relevante (porque el issue que lo originó se cerró o cambió)
- Rastrear el linaje de un requisito a través de ciclos de rework
- Relacionar requisitos entre sí (dependencias, reemplazos, conflictos)
- Que `mem_traceability` pueda verificar no solo cobertura sino vigencia

Esta propuesta introduce un campo de trazabilidad en spec.md y las tools MCP para preservar el linaje desde la fuente original hasta el código.

---

## 📋 Scope

### In Scope
- **Formato de trazabilidad** en spec.md: secciones Traceability con Source, Author, Date, Rationale, Relations
- **Tool MCP `mem_trace_source`**: persigue el origen de un RF/RNF, lo persiste en engram-dotnet con topic_key, y lo linkea al spec
- **Tool MCP `mem_lineage`**: dado un RF, retorna su linaje completo (fuente original → spec → reworks → código)
- **Persistencia**: observaciones con `topic_key: trace/{project}/{rf-id}` que mantienen el linaje
- **Relaciones entre requisitos**: `depends_on`, `supersedes`, `conflicts_with`, `related_to`
- **Actualización del formato spec.md** para incluir `## Traceability` como sección canónica

### Out of Scope
- Auto-detección de fuente (el Arch Agent declara la fuente, no se infiere automáticamente)
- Integración directa con GitHub/Jira API para fetch automático de issues (fase posterior)
- Verificación de que la fuente sigue activa (el humano decide en checkpoint)

---

## 🔗 Origin

Migrated from `sdd/traceability/`

Original artifacts:
- Proposal: `sdd/traceability/propose/proposal.md`
- Spec: `sdd/traceability/specs/requirement-traceability/spec.md`

---

## 📝 Notes

This HU was created during FlowDoc adoption (2026-06-01) to consolidate documentation into the FlowDoc structure.

---

## 🔄 Migration Reference

Original location: `sdd/traceability/`
Current status: Migrated to FlowDoc

See `sdd/README.md` for full migration mapping.