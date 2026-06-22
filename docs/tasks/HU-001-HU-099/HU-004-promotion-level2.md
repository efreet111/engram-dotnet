# HU-004: promotion-level2

**Status**: 🟡 In Progress
**Owner**: @owner
**Created**: 2026-06-01
**Priority**: Should

---

## 🎯 Intent

Las observaciones en engram-dotnet viven en una base de datos SQLite/Postgres. Son buscables por FTS5 pero no son versionables, revisables por humanos ni enlazables desde el código. Esto limita su valor como "memoria organizacional" — las decisiones importantes quedan atrapadas en una DB que nadie lee fuera del agente.

Esta propuesta introduce un **segundo nivel de memoria**: observaciones promovidas a archivos .md estructurados dentro del repositorio del proyecto, con link bidireccional (observation ↔ .md) y un template canónico.

---

## 📋 Scope

### In Scope
- Campo `md_path` en Observation (link observation → .md)
- Template engine para generar .md con frontmatter canónico
- Tool MCP `mem_promote_to_md` (observation_id → .md file)
- Tool MCP `mem_sync_md_to_repo` (batch: todas las observaciones sin .md → .md)
- Link bidireccional: observation.md_path + .md frontmatter observation_id
- Índice autogenerado `docs/decisions/index.md`
- Directorio destino configurable via `ENGRAM_MD_DIR` (default: `docs/decisions/`)

### Out of Scope
- Edición humana de .md con sync reverso a DB (fase posterior)
- Obsidian vault integration (ya existe en Fase B del roadmap)
- Templates personalizados por proyecto (se usa template fijo v1)

---

## 🔗 Origin

Migrated from `sdd/promotion-level2/`

Original artifacts:
- Proposal: `sdd/promotion-level2/propose/proposal.md`
- Spec: `sdd/promotion-level2/specs/md-promotion/spec.md`

---

## 📝 Notes

This HU was created during FlowDoc adoption (2026-06-01) to consolidate documentation into the FlowDoc structure.

---

## 🔄 Migration Reference

Original location: `sdd/promotion-level2/`
Current status: Migrated to FlowDoc

See `sdd/README.md` for full migration mapping.