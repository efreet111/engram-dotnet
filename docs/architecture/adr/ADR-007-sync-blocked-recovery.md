---
adr: 007
title: "SyncManager: re-aplicar mutaciones pulled pendientes al recuperar de estado blocked"
date: 2026-06-29
status: accepted
authors:
  - victor
origin: "FlowForge spike-engram-sync — .ai-work/spike-engram-sync/spec.md#FR-007"
implemented: 2026-07-01
verified: 2026-07-01
---

# ADR-007: SyncManager — recovery de mutaciones pulled pendientes

## Origen

Descubierto durante el spike `spike-engram-sync` en el repo FlowForge:
- Spec completo: `.ai-work/spike-engram-sync/spec.md` (FR-007, FR-008)
- Contexto: pruebas del instalador FlowForge revelaron que el sync quedaba en estado
  `blocked` y al recuperarse no aplicaba las mutaciones ya descargadas del servidor.

## Bugs a resolver

### BUG-1 (FR-007): Mutaciones pulled sin `acked_at` quedan huérfanas al recuperar

**Root cause:**
El `SyncManager` detecta proyectos no enrolados → `lifecycle=blocked`.
Al enrolar los proyectos, `lifecycle` vuelve a `healthy`, pero las mutaciones
que quedaron en `sync_mutations` con `source='pull'` y `acked_at IS NULL`
nunca se re-aplican a la tabla `observations`.

El pull loop solo trae mutaciones nuevas (`after_seq=last_pulled_seq`),
no re-procesa las ya descargadas.

**Evidencia en SQLite:**
```sql
-- Estado observado en producción tras recovery
SELECT * FROM sync_state;
-- cloud|healthy|0|0|1124|0|...  (last_pulled_seq=1124, pero...)

SELECT COUNT(*) FROM sync_mutations WHERE source='pull' AND acked_at IS NULL;
-- 3  ← estas quedaron huérfanas

SELECT * FROM observations WHERE project='team/flowforge';
-- (vacío) ← la observación jamás se aplicó
```

**Fix propuesto:**
En `SyncManager.CycleAsync()`, antes del pull, ejecutar:
`ReapplyPendingPulledMutationsAsync()` que procese todo registro con
`source='pull' AND acked_at IS NULL` y lo aplique a `observations`.

**Archivos clave:**
- `src/Engram.Sync/SyncManager.cs` — agregar método y llamarlo en el ciclo
- `src/Engram.Store/` — método de apply que actualice `acked_at` tras insertar

---

### BUG-2 (FR-008): `engram sync status` muestra contadores en 0 (lee memoria, no SQLite)

**Root cause:**
El comando `engram sync status` lee el estado del proceso en memoria.
Al ejecutarse como proceso CLI separado, los contadores arrancan en 0
aunque SQLite tenga `last_pulled_seq=1124`.

**Evidencia:**
```bash
# Desde terminal (proceso fresco):
engram sync status
# Total pulled: 0  ← INCORRECTO

# Desde SQLite:
sqlite3 ~/.engram/engram.db "SELECT last_pulled_seq FROM sync_state;"
# 1124  ← CORRECTO
```

**Fix propuesto:**
`engram sync status` debe leer directamente de SQLite (`sync_state` table)
en lugar del estado en memoria del proceso corriente.

**Archivos clave:**
- `src/Engram.Cli/` — comando `sync status`
- Leer de `ILocalSyncStore` directamente en lugar del `ISyncStatusProvider` en memoria

## Decisión

Implementar ambos fixes antes del próximo release de `engram-dotnet`.

## Consecuencias

- BUG-1 fix: los usuarios que reinstalan o tienen un sync bloqueado verán sus datos
  aparecer correctamente en `observations` sin intervención manual
- BUG-2 fix: `engram sync status` será confiable desde cualquier proceso,
  incluyendo scripts y CI

## Tests requeridos

- [x] Fixture: DB con `sync_mutations WHERE source='pull' AND acked_at IS NULL`
      → ciclo → verificar que `observations` tiene el registro
  - **Verificado 2026-07-01**: 3 mutaciones huérfanas en `~/.engram/engram.db` del cliente
    fueron recuperadas por `ReapplyPendingPulledMutationsAsync()` tras reiniciar
    `engram serve` con el binario nuevo (MD5 `518be1f9...`). Log:
    `SyncManager recovered 3 orphaned pulled mutations`.
- [x] `engram sync status` desde CLI fresco → mismo resultado que SQLite directo
  - **Verificado 2026-07-01**: `Total pulled: 5580` después de reiniciar (antes mostraba 0).
    Ahora lee desde `GetSyncMutationCountsAsync()` en SQLite.
- [x] No-duplicación: si la observación ya existe, el re-apply no debe duplicarla
  - Cubierto por tests existentes en `tests/Engram.Store.Tests/SqliteStoreApplyPulledTests.cs`:
    todos los métodos `Apply*` son idempotentes (upsert con `ON CONFLICT DO UPDATE`).

## Commits

| Bug | Commit | Descripción |
|-----|--------|-------------|
| BUG-1 (recovery) | `6ba2674` | `InsertPulledMutationAsync` + `ReapplyPendingPulledMutationsAsync` + `ApplyPulledMutationAsync` marca `acked_at` |
| BUG-1 (cycle order) | `5e20f80` | Mover `ReapplyPendingPulledMutationsAsync` ANTES del push (antes del blocked) |
| BUG-2 (counts) | `12b97a9` | `GetSyncMutationCountsAsync` desde BD en `HandleSyncStatusAsync` |

## Status

✅ **Implementado y verificado en producción** (2026-07-01).

## Referencias

- Spec FlowForge: `.ai-work/spike-engram-sync/spec.md`
- ADR FlowForge: `docs/decisions/ADR-007-cross-repo-traceability-gap.md`
