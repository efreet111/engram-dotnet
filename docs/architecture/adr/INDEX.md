# ADR Index

> Architecture Decision Records — índice de todas las decisiones

## Decisiones Arquitectónicas

| ADR | Título | Estado | Fecha | Autores |
|-----|--------|--------|-------|---------|
| [ADR-001](./ADR-001-no-orm.md) | SQL directo sin ORM | Accepted | 2026-04-20 | — |
| ADR-002 | — | — | — | — | *(gap — reservado o eliminado)* |
| ADR-003 | — | — | — | — | *(gap — reservado o eliminado)* |
| [ADR-004](./ADR-004-post-install-registration.md) | Post-install registration con FlowForge installer | Accepted | 2026-06-23 | equipo engram |
| ADR-005 | — | — | — | — | *(gap — reservado o eliminado)* |
| ADR-006 | — | — | — | — | *(gap — reservado o eliminado)* |
| [ADR-007](./ADR-007-sync-blocked-recovery.md) | SyncManager recovery de mutaciones pulled pendientes | Accepted | 2026-06-29 | victor |
| [ADR-008](./ADR-008-sync-self-loop-detection.md) | Self-loop detection for SyncManager | Accepted | 2026-07-01 | victor |
| [ADR-009](./ADR-009-two-version-model.md) | Two-version model: product version vs API/schema version | Accepted | 2026-07-06 | victor |
| [ADR-010](./ADR-010-historical-docs-immutability.md) | Historical documentation immutability policy | Accepted | 2026-07-06 | victor |
| [ADR-011](./ADR-011-engram-url-env-var.md) | Estandarización de `ENGRAM_SERVER_URL` como variable canónica | Accepted | 2026-08-06 | victor |
| [ADR-012](./ADR-012-remote-server-localhost-blocking.md) | Bloqueo de conexiones localhost en perfil `remote-server` | Accepted | 2026-08-06 | victor |

---

## Decisiones Deprecated

| ADR | Título | Reemplazado por | Fecha |
|-----|--------|-----------------|-------|

*(Ninguna por ahora)*

---

## Notas

- **Gaps (002, 003, 005, 006)**: Reservados o eliminados. No reutilizar estos números.
- **Nuevo ADR**: Usar el siguiente número disponible (011, 012...)
- **Formatos aceptados**: ADR-XXX-title-in-kebab-case.md

---

## Agregar un ADR

1. Crear archivo en `docs/architecture/adr/`
2. Usar template: `docs/templates/architecture/ADR_template.md`
3. Agregar entrada a esta tabla
4. Mantener orden cronológico

---

*Última actualización: 2026-08-06*
