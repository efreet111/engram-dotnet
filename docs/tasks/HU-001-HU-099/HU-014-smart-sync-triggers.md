# HU-014 — Smart Sync Triggers

**Status**: ✅ Complete
**Owner**: @owner
**Created**: 2026-08-11

**As**: Developer working on multiple projects across multiple devices
**I want**: sync to be project-specific (not global), with automatic pull when I switch devices
**To**: avoid unnecessary sync of unrelated projects and have updated memory when I switch devices

---

## Acceptance Criteria

- [x] **R1 — Smart sync por proyecto**: el poll de 30s ya verifica si hay mutations pendientes por proyecto antes de hacer push. Si no hay pending para un proyecto, no se pushpea ese proyecto.
- [x] **R2 — CLI manual sync**: comando `engram sync push --project <nombre>` para hacer push manual de un proyecto específico. Incluye `--all` flag. MCP también puede disparar sync manual (fire-and-forget) pero por proyecto, no global.
- [x] **R3 — Pull en device wake**: el poll de 30s ya cubre este escenario. Cuando el equipo despierta y el MCP/servicio arranca, el próximo ciclo de 30s hace pull de todos los servidores. Si hay múltiples servidores, aplica dedup via [RFC-006](docs/architecture/rfc/RFC-006-multi-server-pull-deduplication.md).
- [x] **R4 — Validación de trigger MCP**: `mem_save()` acepta parámetro `sync_project` (bool, default false = global). Mantiene backward compatibility.
- [x] **R5 — Denylist por servidor**: desde HU-013, si un proyecto A está en denylist en Server 1 pero permitido en Server 2, los trigger pushes solo van a Server 2. Este comportamiento es ortogonal.
- [x] **R6 — Configuración inicial**: el usuario elige entre (a) auto-sync cada 30s solo para proyectos modificados, o (b) sync manual únicamente. Comando `engram sync setup` interactivo.

---

## Tasks (Implementation)

- [x] **T1**: ~~Diseñar e implementar tracking de `pending mutations` por proyecto~~ (cubierto por poll de 30s)
- [x] **T2**: Modificar `TriggerPushAsync()` en MCP para filtrar por proyecto con mutations pendientes
- [x] **T3**: Agregar comando CLI `engram sync push --project <nombre>` (+ `--all` flag)
- [x] **T4**: Implementar trigger de sync en `mem_save()` con parámetro `sync_project=true`
- [x] **T5**: ~~Implementar pull automático en device wake~~ (cubierto por poll de 30s)
- [x] **T6**: Implementar dedup/merge de múltiples servidores en pull on wake (ver RFC-006)
- [x] **T7**: Implementar opción de configuración inicial (auto-sync 30s vs manual-only)
- [x] **T8**: Validar integración con denylist por servidor (HU-013)
- [x] **T9**: Escribir tests de integración para T2-T8 (31 tests nuevos)

---

## Notes

### Pull on wake deduplication (ver RFC-006)

Cuando hay múltiples servidores, hacer pull de todos y dedupe/merge:
- Estrategia: last-write-wins por `occurred_at`
- Tiebreaker: `server_id` mayor si `occurred_at` igual
- Ver [RFC-006](docs/architecture/rfc/RFC-006-multi-server-pull-deduplication.md) para detalle completo

### MCP trigger change

El trigger actual de MCP después de `mem_save()` es global. Se necesita:
- Que `mem_save()` acepte un parámetro `sync_project` (bool)
- Cuando `true`, solo hacer push para ese proyecto específico
- Mantener backward compatibility: si no se pasa el flag, comportamiento actual (global)

### Initial setup options

El usuario elige en el setup inicial:
- **(a) Auto-sync cada 30s**: solo proyectos con `pending_mutations`
- **(b) Manual-only**: sin auto-sync, solo `engram sync push --project` o trigger explícito

---

## Related Documents

- HU-013 — Multi-project Sync (denylist behavior, sync_behavior)
- RFC-006 — Multi-Server Pull Deduplication (estrategia de dedup para R3)

---

## Status

| Campo | Valor |
|-------|-------|
| **HU** | HU-014 |
| **Título** | Smart Sync Triggers |
| **Fase** | ✅ Complete |
| **Creado** | 2026-08-11 |
| **Implementado** | 2026-08-11 |
