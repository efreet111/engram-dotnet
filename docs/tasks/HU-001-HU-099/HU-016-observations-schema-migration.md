# HU-016 — Observations Schema Migration Safety

**Status**: 🟡 In Progress
**Owner**: @owner
**Created**: 2026-08-14

**As**: User migrating from engram vanilla to engram-dotnet
**I want**: my existing SQLite database to be safely migrated without data loss or corruption
**To**: continue using my memory history without losing any observations after upgrading

---

## Acceptance Criteria

- [ ] **R1 — Schema consistente**: la tabla `observations` tiene columna `created_by` (TEXT, nullable) vía `AddColumnIfNotExists`
- [ ] **R2 — Safe para fresh install**: installations nuevas no tienen problemas
- [ ] **R3 — Safe para migración**: usuarios migrando desde engram vanilla no pierden datos
- [ ] **R4 — Test pasa**: `LocalAndRemoteStore_SaveAndRetrieveObservation` passa
- [ ] **R5 — Sin breaking changes**: queries existentes siguen funcionando

---

## Tasks (Implementation)

- [ ] **T1**: Agregar `AddColumnIfNotExists("observations", "created_by", "TEXT")` en `SqliteStore.Migrate()`
- [ ] **T2**: Verificar que queries existentes que leen `observations` no se rompen
- [ ] **T3**: Verificar que el test `LocalAndRemoteStore_SaveAndRetrieveObservation` pasa
- [ ] **T4**: Documentar el schema de `observations` actualizado

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

### Related Documents

- RFC-00X: `docs/architecture/rfc/RFC-00X-observations-created-by-fix.md` (superseded by HU-016)
- Test: `tests/Engram.Store.Tests/MemoryIntegrationTests.cs`
