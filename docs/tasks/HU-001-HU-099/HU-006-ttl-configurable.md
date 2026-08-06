# HU-006: TTL Configurable

**Status**: 🟡 In Progress
**Owner**: @owner
**Created**: 2026-06-01
**Priority**: Should

---

## As a user...

**As**: Developer / IT Admin
**I want**: que las observaciones de Engram tengan TTL configurable y que haya visibilidad del estado de la memoria
**To**: mantener la memoria útil a largo plazo, expirar automáticamente observaciones viejas, y manejar proyectos renombrados/consolidados con redirect hints

---

## Acceptance Criteria

- [ ] Capa 1: Métricas de retención visibles via endpoint, CLI y MCP tool
- [ ] Capa 2: TTL configurable por store method, CLI y config
- [ ] Capa 3: Redirect hints en search results (store + server)
- [ ] Agente puede seguir redirects automáticamente (opcional, decide el agente)
- [ ] CLI y JSON output para métricas (sin UI visual)
- [ ] Archive/export de observaciones expiradas (opcional, fase posterior)

---

## Tasks (Implementation)

- [ ] Implementar capa 1: endpoint/CLI/MCP tool de métricas de retención
- [ ] Implementar capa 2: TTL configurable en store method
- [ ] Implementar capa 3: redirect hints en search results
- [ ] Agregar config de TTL al archivo de configuración
- [ ] Documentar schema de redirect hints

---

## Notes

### Implementation Notes

 HU migrada de `sdd/ttl-configurable/`. Fue creada durante adopción FlowDoc (2026-06-01) para consolidar documentación en la estructura FlowDoc. No se implementó código — la HU refleja el diseño planificado de las 3 capas de retención.

### 🔄 Migration Reference

- Original location: `sdd/ttl-configurable/`
- Original artifacts:
  - Proposal: `sdd/ttl-configurable/propose/proposal.md`
  - Spec: `sdd/ttl-configurable/specs/memory-retention/spec.md`
- Current status: Migrated to FlowDoc
- See `sdd/README.md` for full migration mapping.
