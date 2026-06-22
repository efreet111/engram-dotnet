# HU-006: ttl-configurable

**Status**: 🟡 In Progress
**Owner**: @owner
**Created**: 2026-06-01
**Priority**: Should

---

## 🎯 Intent

Implementar un sistema de 3 capas para mantener la memoria de Engram útil a largo plazo:
1. **Métricas de retención** — visibilidad del estado de la memoria
2. **TTL configurable** — expiración automática de observaciones viejas
3. **Interconexión de proyectos** — redirect hints para proyectos renombrados/consolidados

---

## 📋 Scope

### In Scope (100% engram-dotnet)
- Capa 1: Métricas de retención (endpoint, CLI, MCP tool)
- Capa 2: TTL configurable (store method, CLI, config)
- Capa 3: Redirect hints en search results (store + server)

### Out Scope (depende del agente)
- El agente siguiendo redirects automáticamente (el agente decide si usa el campo `redirect`)
- UI visual para métricas (solo CLI + JSON por ahora)
- Archive/export automático de observaciones expiradas (fase posterior)

---

## 🔗 Origin

Migrated from `sdd/ttl-configurable/`

Original artifacts:
- Proposal: `sdd/ttl-configurable/propose/proposal.md`
- Spec: `sdd/ttl-configurable/specs/memory-retention/spec.md`

---

## 📝 Notes

This HU was created during FlowDoc adoption (2026-06-01) to consolidate documentation into the FlowDoc structure.

---

## 🔄 Migration Reference

Original location: `sdd/ttl-configurable/`
Current status: Migrated to FlowDoc

See `sdd/README.md` for full migration mapping.