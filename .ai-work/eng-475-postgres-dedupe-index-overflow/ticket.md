# Ticket: PostgreSQL idx_obs_dedupe Index Overflow

**Fecha:** 2026-07-24  
**Prioridad:** P0 (bloquea sync)  
**Tipo:** Bug  
**Origen:** Sesión de verificación de sync ENG-474  

---

## Problema

El sync push falla con HTTP 500 cuando se intenta sincronizar observaciones con contenido largo (>2000 bytes).

**Error:**
```
PostgresException: 54000: index row size 2800 exceeds btree version 4 maximum 2704 for index "idx_obs_dedupe"
```

**Stack trace:**
```
at Engram.Store.PostgresStore.ApplyObservationUpsertAsync(...) in PostgresStore.cs:line 2525
at Engram.Store.PostgresStore.InsertMutationBatchAsync(...) in PostgresStore.cs:line 1899
at Engram.Server.CloudSyncEndpoints.HandleMutationPushAsync(...) in CloudSyncEndpoints.cs:line 191
```

---

## Root Cause

El índice `idx_obs_dedupe` en `PostgresStore.cs:99` es compuesto:

```sql
CREATE INDEX IF NOT EXISTS idx_obs_dedupe 
ON observations(normalized_hash, project, scope, type, title, created_at DESC) 
WHERE normalized_hash IS NOT NULL;
```

**Campos del índice:**
- `normalized_hash` (SHA-256, 64 chars)
- `project` (variable)
- `scope` (team/personal)
- `type` (variable)
- `title` (variable, puede ser muy largo)
- `created_at` (timestamp)

**Límite de PostgreSQL:** B-tree versión 4 no permite filas de índice >2704 bytes (1/3 de buffer page de 8KB).

Cuando `title` + `project` + `type` excede ~2600 bytes, el índice falla.

---

## Impacto

- **Sync bloqueado:** 48 mutaciones pendientes no pueden hacer push
- **Data loss silenciosa:** Las memorias no se sincronizan con el servidor
- **13 observaciones afectadas** en la DB local del usuario

### Observaciones problemáticas identificadas:

| seq | entity_key | project | title_len | content_len |
|-----|------------|---------|-----------|-------------|
| 227086 | obs-75cfd055e9fcf4db | team/engram-dotnet | 62 | 8287 |
| 227082 | obs-17d0490c2f3bdee9 | team/flowforge | 31 | 6349 |
| 227078 | obs-f3b5a51de37f1c6e | team/flowforge | 67 | 4861 |
| 227072 | obs-120b1c11167d2ef4 | team/engram-dotnet | 35 | 4325 |
| 227092 | obs-ecf62c9f9cb648b9 | team/flowforge | 79 | 3527 |
| 227096 | obs-ac29d9ffa2d0948b | team/flowforge | 90 | 3406 |
| 227048 | obs-2368bfd042e9833e | team/engram-dotnet | 35 | 2940 |
| 227090 | obs-f13c41938f7a494a | team/engram-dotnet | 55 | 2844 |
| 227094 | obs-bb3a8df6d7b55114 | team/flowforge | 89 | 2665 |
| 227074 | obs-f3b5a51de37f1c6e | team/flowforge | 49 | 2473 |
| 227076 | obs-f3b5a51de37f1c6e | team/flowforge | 49 | 2473 |
| 227088 | obs-c9227d1c549724ae | team/flowforge | 55 | 2213 |
| 227080 | obs-2f303f6d8d3094a1 | team/flowforge | 37 | 2133 |

---

## Soluciones Propuestas

### Opción A: Cambiar índice a hash (recomendada)

```sql
-- En el servidor PostgreSQL:
DROP INDEX IF EXISTS idx_obs_dedupe;
CREATE INDEX idx_obs_dedupe ON observations(md5(normalized_hash), project, scope, type, created_at DESC) WHERE normalized_hash IS NOT NULL;
```

**Pros:**
- Resuelve el problema permanentemente
- `md5()` genera 32 chars fijos, nunca excede límite
- Mantiene funcionalidad de deduplicación

**Contras:**
- Requiere acceso al servidor para ejecutar SQL
- Necesita recrear índice (puede tomar tiempo en tablas grandes)

### Opción B: Quitar `title` del índice

```sql
DROP INDEX IF EXISTS idx_obs_dedupe;
CREATE INDEX idx_obs_dedupe ON observations(normalized_hash, project, scope, type, created_at DESC) WHERE normalized_hash IS NOT NULL;
```

**Pros:**
- Simple, solo quita un campo
- `title` no es necesario para deduplicación

**Contras:**
- Si el código busca por `title` en dedup, puede fallar

### Opción C: Truncar title antes de insertar

Modificar `ApplyObservationUpsertAsync` para truncar `title` a 200 chars antes de insertar.

**Pros:**
- No requiere cambios en el servidor
- Previene el problema en el futuro

**Contras:**
- Pierde información del título
- No resuelve observaciones ya existentes

### Opción D: Usar índice parcial solo en hash

```sql
DROP INDEX IF EXISTS idx_obs_dedupe;
CREATE INDEX idx_obs_dedupe ON observations(normalized_hash) WHERE normalized_hash IS NOT NULL;
```

**Pros:**
- Índice minimalista, nunca excede límite
- Deduplicación funciona solo por hash

**Contras:**
- Pierde capacidad de buscar por project/scope/type en dedup
- Puede haber falsos positivos si dos observaciones tienen el mismo hash pero diferente project

---

## Acción Inmediata (Workaround)

Marcar las 13 observaciones problemáticas como `acked_at` en la DB local para desbloquear el sync del resto:

```bash
sqlite3 ~/.engram/engram.db "UPDATE sync_mutations SET acked_at = datetime('now') WHERE seq IN (227086, 227082, 227078, 227072, 227092, 227096, 227048, 227090, 227094, 227074, 227076, 227088, 227080);"
```

**Nota:** Esto pierde la sincronización de esas 13 observaciones con el servidor. Se pueden re-sincronizar después de aplicar el fix.

---

## Próximos Pasos

1. **Corto plazo:** Aplicar workaround (marcar como acked) ✅ Hecho
2. **Mediano plazo:** Crear ADR con la decisión de fix ✅ Hecho (en ticket)
3. **Largo plazo:** Implementar fix en PostgresStore.cs y migración de índice ✅ Hecho

---

## Resolución (2026-07-25)

**Status:** ✅ Done — PR #22 mergeado (`62eca98`)

**Fix aplicado:**
- Removido `title` de `idx_obs_dedupe` en PostgresStore.cs y SqliteStore.cs
- Migración idempotente `MigrateDedupeIndex()` para DBs existentes
- Tests regresión: 2/2 SQLite, 2/2 PostgreSQL (Testcontainers)

**Verificación:**
- Sync funcionando: 35 mutaciones pushed, 70 pulled
- Sin errores, phase=healthy
- CI pasó: SQLite 54s, PostgreSQL 1m19s

**Commit:** `62eca98` (merge PR #22)

---

## Archivos Relacionados

- `src/Engram.Store/PostgresStore.cs:99` — definición del índice
- `src/Engram.Store/PostgresStore.cs:2525` — ApplyObservationUpsertAsync (donde falla)
- `src/Engram.Server/CloudSyncEndpoints.cs:191` — HandleMutationPushAsync

---

## Referencias

- PostgreSQL B-tree limitations: https://www.postgresql.org/docs/current/btree.html
- Error 54000: index row size exceeds maximum
- ENG-474: Obsidian Memory Graph (donde se descubrió el problema)
