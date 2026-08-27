# RFC-007 — Fix `created_by` schema mismatch en observations (SQLite)

**Status**: Superseded by HU-016
**Author**: @kaito
**Created**: 2026-08-14
**Related HU**: HU-016 — Observations Schema Migration Safety
**Related**: MemoryIntegrationTests failure, SQLite schema

---

## Context

El test `MemoryIntegrationTests.LocalAndRemoteStore_SaveAndRetrieveObservation` falla con:

```
Microsoft.Data.Sqlite.SqliteException : SQLite Error 1: 'no such column: created_by'.
```

El test crea un `SqliteStore` y llama `AddObservationAsync` → `GetObservationAsync`. En algún punto del flujo (probablemente a través de `HttpStore` o el pipeline de sync), el código intenta acceder a la columna `created_by` en la tabla `observations`, pero:

```sql
CREATE TABLE observations (
    id, sync_id, session_id, type, title, content, tool_name,
    project, scope, topic_key, ... -- NO created_by
);
```

La columna `created_by` **existe** en la tabla `user_prompts` pero **no** en `observations`.

```csharp
// SqliteStore.cs línea 157 — user_prompts SÍ tiene created_by:
created_by TEXT,

// Pero observations (línea 108-133) NO tiene created_by:
CREATE TABLE observations (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    sync_id TEXT,
    session_id TEXT NOT NULL,
    ...
);
```

---

## Problem

Existe una inconsistencia de schema entre:

| Tabla | ¿Tiene `created_by`? |
|-------|---------------------|
| `observations` | ❌ No |
| `user_prompts` | ✅ Sí |
| `cloud_mutations` | ✅ Sí |

El test `LocalAndRemoteStore_SaveAndRetrieveObservation` usa `AddObservationAsync` / `GetObservationAsync` y espera que `created_by` esté disponible — pero no lo está en SQLite.

Possibly related: el `HttpStore` (remote store en memoria) puede estar asumiendo que `observations` tiene `created_by` cuando se serializa/deserializa.

---

## Options

### Option A — Agregar columna `created_by` a `observations` (SQLite)

**Descripción**: Agregar `created_by TEXT` a la tabla `observations` en SQLite, igual que existe en `user_prompts` y `cloud_mutations`.

**Pros**:
- Consistencia de schema entre todas las tablas
- Aligns con PostgreSQL que sí puede tener el campo

**Cons**:
- Requiere migration en SQLite (`AddColumnIfNotExists`)
- Puede requerir cambios en código que inserta/consulta observations

**Impacto**: Migration mínima, bajo riesgo

---

### Option B — Fix en el test o en HttpStore

**Descripción**: Si el problema está en cómo `HttpStore` maneja observations (asumiendo `created_by` que no existe), fixear ahí.

**Pros**:
- No requiere alterar el schema de SQLite

**Cons**:
- Puede ser un fix incompleto si el problema es más profundo

**Impacto**: Bajo — solo cambia test o adapter

---

### Option C — Investigar más (diferido)

**Descripción**: Antes de decidir, hacer debugging más profundo para entender exactamente dónde y por qué se consulta `created_by` en observations.

**Pros**:
- Evita cambios innecesarios

**Cons**:
- retrasa la solución

---

## Recommended Approach

**Option A** — Consistency > workarounds. Agregar `created_by TEXT` a `observations` vía migration `AddColumnIfNotExists` en `Migrate()`, igual que se hace con `user_prompts` en `SqliteStore.cs:226`.

El flujo sería:
1. En `Migrate()`, agregar:
   ```csharp
   AddColumnIfNotExists("observations", "created_by", "TEXT");
   ```
2. Verificar que `AddObservationAsync` inserte `created_by` (puede ser nullable/default null)
3. Verificar que `GetObservationAsync` no falle al leer observations sin esa columna

---

## Verification

Después del fix:
```bash
dotnet test -c Release --filter "LocalAndRemoteStore_SaveAndRetrieveObservation"
# Expected: PASS
```

---

## References

- Test: `tests/Engram.Store.Tests/MemoryIntegrationTests.cs:14`
- Schema actual: `src/Engram.Store/SqliteStore.cs:108-133`
- Migration actual: `src/Engram.Store/SqliteStore.cs:226` (user_prompts)
