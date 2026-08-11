# HU-005: Traceability

**Status**: 🟡 In Progress
**Owner**: @owner
**Created**: 2026-06-01
**Priority**: Should

---

## 🎯 Intent

Traceability maintains the complete lineage of a requirement, from its original source (issue, bug report, technical decision) to the code that implements it, through all cycles of rework — so you always know if a requirement is still relevant, how it evolved, and how requirements relate to each other.

## 📋 Scope

- MCP tool `mem_trace_source`: persists the origin of a requirement with `topic_key` and links it to the spec
- MCP tool `mem_lineage`: returns the full lineage chain (source → spec → reworks → code)
- `## Traceability` section in spec.md format with fields: Source, Author, Date, Rationale, Relations
- Requirement relationship support: `depends_on`, `supersedes`, `conflicts_with`, `related_to`
- Persistence via observations with `topic_key: trace/{project}/{rf-id}`

---

## As a user...

**As**: Developer / IT Admin
**I want**: mantener el linaje completo de un requerimiento, desde su fuente original (issue, bug report, decisión técnica) hasta el código que lo implementa
**To**: saber si un requisito sigue siendo relevante, rastrear su historia a través de ciclos de rework, y relacionar requisitos entre sí

---

## Acceptance Criteria

- [ ] spec.md incluye sección `## Traceability` con campos Source, Author, Date, Rationale, Relations
- [ ] Tool MCP `mem_trace_source` persiste el origen de un RF/RNF con topic_key y lo linkea al spec
- [ ] Tool MCP `mem_lineage` retorna el linaje completo (fuente original → spec → reworks → código)
- [ ] Persistencia usa observaciones con `topic_key: trace/{project}/{rf-id}`
- [ ] Relaciones entre requisitos soportadas: `depends_on`, `supersedes`, `conflicts_with`, `related_to`
- [ ] Formato spec.md actualizado para incluir `## Traceability` como sección canónica

---

## Tasks (Implementation)

- [ ] Diseñar formato de sección `## Traceability` en spec.md
- [ ] Implementar tool MCP `mem_trace_source`
- [ ] Implementar tool MCP `mem_lineage`
- [ ] Definir schema de relaciones entre requisitos en el store
- [ ] Actualizar spec.md de Engram con sección Traceability canónica

---

## Notes

### Implementation Notes

 HU migrada de `sdd/traceability/`. Fue creada durante adopción FlowDoc (2026-06-01) para consolidar documentación en la estructura FlowDoc. No se implementó código — la HU refleja el diseño planificado.

### Out of Scope (de origen)

- Auto-detección de fuente (el Arch Agent declara la fuente, no se infiere automáticamente)
- Integración directa con GitHub/Jira API para fetch automático de issues (fase posterior)
- Verificación de que la fuente sigue activa (el humano decide en checkpoint)

### 🔄 Migration Reference

- Original location: `sdd/traceability/`
- Original artifacts:
  - Proposal: `sdd/traceability/propose/proposal.md`
  - Spec: `sdd/traceability/specs/requirement-traceability/spec.md`
- Current status: Migrated to FlowDoc
- See `sdd/README.md` for full migration mapping.
