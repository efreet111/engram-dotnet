# HU-016 — Observations Schema Migration Safety

**Status**: 🟢 Done
**Owner**: @owner
**Created**: 2026-08-14

**As**: User migrating from engram vanilla to engram-dotnet
**I want**: my existing SQLite database to be safely migrated without data loss or corruption
**To**: continue using my memory history without losing any observations after upgrading

---

## Acceptance Criteria

- [x] **R1 — Schema consistente**: la tabla `observations` tiene columna `created_by` (TEXT, nullable) vía `AddColumnIfNotExists`
- [x] **R2 — Safe para fresh install**: installations nuevas no tienen problemas
- [x] **R3 — Safe para migración**: usuarios migrando desde engram vanilla no pierden datos
- [x] **R4 — Test pasa**: `LocalAndRemoteStore_SaveAndRetrieveObservation` passa
- [x] **R5 — Sin breaking changes**: queries existentes siguen funcionando

---

## Tasks (Implementation)

- [x] **T1**: Agregar `AddColumnIfNotExists("observations", "created_by", "TEXT")` en `SqliteStore.Migrate()`
- [x] **T2**: Verificar que queries existentes que leen `observations` no se rompen
- [x] **T3**: Verificar que el test `LocalAndRemoteStore_SaveAndRetrieveObservation` pasa
- [x] **T4**: Documentar el schema de `observations` actualizado

---

## Notes

### Problem Statement

El test `LocalAndRemoteStore_SaveAndRetrieveObservation` falla con:
```
SQLite Error 1: 'no such column: created_by'.
```

La tabla `observations` **nunca** tuvo `created_by` en el schema base de engram-dotnet. La columna existe en `user_prompts` y `cloud_mutations`, pero no en `observations`. Esto causa inconsistencia de schema para usuarios migrando desde engram vanilla.

### Root Cause

| Tabla | ¿Tiene `created_by`? |
|-------|----------------------|
| `observations` | ❌ No |
| `user_prompts` | ✅ Sí |
| `cloud_mutations` | ✅ Sí |

### Solución propuesta

```csharp
// En SqliteStore.Migrate(), después de las otras migraciones de observations:
AddColumnIfNotExists("observations", "created_by", "TEXT");
```

`AddColumnIfNotExists` es idempotente y seguro:
- Si la columna ya existe → no hace nada
- Si la columna no existe → la agrega
- No causa pérdida de datos

### Files affected

- `src/Engram.Store/SqliteStore.cs` — método `Migrate()`

---

## Implementation (2026-08-14)

### Root cause real descubierto durante implementación

El diagnóstico original (observations sin `created_by`) era **incompleto**. El error
`no such column: created_by` provenía de una sentencia **distinta**: el índice
`idx_prompts_created_by ON user_prompts(created_by)` que se creaba en el bloque
inicial de `Migrate()` **antes** de que la columna `created_by` de `user_prompts`
existiera en bases legacy.

En bases **existentes**, `CREATE TABLE IF NOT EXISTS user_prompts` no añade la
columna nueva, pero los `CREATE INDEX` del mismo bloque SQL sí se ejecutan → fallan
con `no such column: created_by`. Este es **el mismo bug** documentado para
PostgreSQL en [`SESSION-REPORT-2026-05-31-REST-API-BUGFIX.md`](../../SESSION-REPORT-2026-05-31-REST-API-BUGFIX.md)
(commits `f8ba8f6`/`e1a9cf9`), que en SQLite quedó sin corregir.

### Cambios aplicados

1. **T1** — Agregada `AddColumnIfNotExists("observations", "created_by", "TEXT")`
   (paridad de schema con `user_prompts` y `cloud_mutations`).
2. **Fix del orden de migración** — Movido `idx_prompts_created_by` fuera del bloque
   inicial; ahora se crea **después** de `AddColumnIfNotExists("user_prompts", "created_by", ...)`
   (paridad con `PostgresStore.Migrate()` líneas 279-284).

### Schema `observations` actualizado (SQLite)

Columnas nuevas sobre el schema base:

| Columna | Tipo | Default | Vía |
|---------|------|---------|-----|
| `created_by` | `TEXT` | `NULL` | `AddColumnIfNotExists` |

Columnas ya existentes del schema base (sin cambios): `id`, `sync_id`, `session_id`,
`type`, `title`, `content`, `tool_name`, `project`, `scope`, `topic_key`,
`normalized_hash`, `revision_count`, `duplicate_count`, `last_seen_at`, `created_at`,
`updated_at`, `deleted_at`, `review_after`, `expires_at`, `embedding`,
`embedding_model`, `embedding_created_at`, `md_path`.

> Nota: `created_by` en `observations` es aditivo y nullable. No lo lee ni lo escribe
> ningún query existente (el modelo `Observation` no tiene `CreatedBy`); su propósito
> es paridad de schema. `idx_prompts_created_by` sí se crea tras la columna para que
> bases legacy migren sin fallar.

### Related Documents

- RFC-00X: `docs/architecture/rfc/RFC-00X-observations-created-by-fix.md` (superseded by HU-016)
- Test: `tests/Engram.Store.Tests/MemoryIntegrationTests.cs`
