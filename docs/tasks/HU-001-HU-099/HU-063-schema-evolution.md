# HU-063 — Schema Evolution (code metadata + migraciones versionadas)

**ENG:** ENG-416
**Tipo:** Chore
**Prioridad:** P2
**Esfuerzo:** M (~3.5h)
**Estado:** ✅ Done (2026-09-21)
**Origen:** ← HU-059 prerequisite / ← PRD memoria semántica punto #7
**Flow:** `.ai-work/hu-063-schema-evolution/`

---

## Problema que resuelve

El schema de `observations` no tiene campos de metadata de código. Las features code-aware (HU-054/059/060/061) no pueden existir sin ellos. Además, hoy las migraciones son una secuencia idempotente sin versionar — no hay forma de saber qué migraciones se aplicaron.

---

## Criterios de aceptación

- [ ] **FR-001**: `file_path`, `symbol`, `namespace` TEXT nullable en `observations` (ambos stores), vía helpers idempotentes
- [ ] **FR-002**: Índices `idx_obs_file_path`, `idx_obs_symbol`, `idx_obs_namespace` en ambos stores, después de columnas (regla HU-016)
- [ ] **FR-003**: Tabla `schema_migrations` (ledger) con baseline `0000` + migración `1 eng416_code_metadata`
- [ ] **FR-004**: Roundtrip en `Observation` + `AddObservationParams` + `ObservationPullPayload`
- [ ] **FR-005**: Guard 512 chars fail-loud en todos los caminos de escritura
- [ ] Tests SQLite (T1/T2) pasan
- [ ] Tests Postgres (T3) pasan
- [ ] Sin regresión en suite existente

---

## Tasks (Implementation)

- [ ] **T1**: Modelo — `Observation` + `AddObservationParams` + `ObservationPullPayload` ganan `FilePath`/`Symbol`/`Namespace` nullable
- [ ] **T2**: Ledger — crear `schema_migrations` en ambos stores (DDL idéntico, house style BIGINT/TEXT/TEXT)
- [ ] **T3**: Migración SQLite — `MigrateCodeMetadata()` helper: columnas + índices + ledger
- [ ] **T4**: Escritura SQLite — `AddObservationAsync` + `ApplyPulledMutationAsync` persisten los 3 campos + guard 512
- [ ] **T5**: Lectura SQLite — hydration de readers para los 3 campos
- [ ] **T6**: Migración Postgres — espejo con `text_pattern_ops` para `file_path`/`namespace`
- [ ] **T7**: Escritura/lectura Postgres — mismo patrón
- [ ] **T8**: Sync payloads — `ObsPayload()` y `ObservationPullPayload` incluyen los 3 campos (gap catcheado en plan)
- [ ] **T9**: Tests — `SchemaEvolutionTests.cs` (SQLite + Postgres)
- [ ] **T10**: Regresión — suite existente verde

---

## Dependencias

- Ninguna (es prerequisito de HU-059/HU-064)

---

## Notas técnicas

### Mecanismo de versionado

**Tabla `schema_migrations` (ledger)** — confirmado en CKP-1:

```sql
-- SQLite
CREATE TABLE IF NOT EXISTS schema_migrations (
    version    INTEGER PRIMARY KEY,
    name       TEXT NOT NULL,
    applied_at TEXT NOT NULL DEFAULT (datetime('now'))
);

-- Postgres
CREATE TABLE IF NOT EXISTS schema_migrations (
    version    BIGINT PRIMARY KEY,
    name       TEXT NOT NULL,
    applied_at TEXT NOT NULL DEFAULT (NOW() AT TIME ZONE 'utc')
);
```

- `0000` = baseline del bloque idempotente pre-ENG-416
- `1 eng416_code_metadata` = primera migración versionada

### Índice Postgres

`text_pattern_ops` en `file_path` y `namespace` (parcial `WHERE col IS NOT NULL`) — sirve `=` y `LIKE 'prefix%'`.

### FTS excluido

Las columnas NO se incluyen en FTS5/GIN (HU-054 scope: solo filtrado por metadata).

---

## Referencias

- [spec.md](./spec.md) — especificación completa
- [plan.md](./plan.md) — plan de implementación
- [context-map.md](./context-map.md) — contexto de discovery
- [HU-016](../HU-001-HU-099/HU-016-observations-schema-migration.md) — patrón de migración precedente
- [HU-054](../HU-001-HU-099/HU-054-code-context-queries.md) — consumidor de esta schema
- [HU-059](../HU-001-HU-099/HU-059-code-aware-dev-agent.md) — consume ENG-416
