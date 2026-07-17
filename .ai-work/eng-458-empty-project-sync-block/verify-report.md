# Verify Report — ENG-458

**Feature**: Mutaciones con `project=""` bloquean sync
**Commit auditado**: `826a29b` — `fix(ENG-458): delete mutation includes project to prevent sync block`
**Fecha**: 2026-07-17
**Veredicto**: **PASS**

---

## Resumen ejecutivo

El fix resuelve correctamente el bug P0 que causaba pérdida de datos silenciosa. Mutaciones con `project=""` (específicamente de `DeleteObservationAsync`) bloqueaban TODO el sync push, incluso con proyectos válidos enrolados.

**3 fixes aplicados:**
1. ✅ `DeleteObservationAsync` ahora incluye `project = obs.Project` en el payload
2. ✅ `CountPendingNonEnrolledAsync` filtra `project=""` (safety net)
3. ✅ Warning log en `EnqueueSyncMutation` para visibilidad

**4 tests agregados:** Todos pasan (67/67 SqliteStoreTests, 32/32 SyncManagerTests)

---

## Hallazgos

### CRITICAL-0: Ninguno ✅

No se encontraron bugs críticos en la implementación.

### MAJOR-0: Ninguno ✅

No se encontraron problemas mayores.

### MINOR-1: PostgresStore no tiene `CountPendingNonEnrolledAsync` — pero es correcto ✅

**Hallazgo**: `CountPendingNonEnrolledAsync` solo existe en `ILocalSyncStore` (cliente SQLite). PostgresStore (servidor) no lo implementa.

**Análisis**: Esto es **arquitectura correcta**. El método es usado por `SyncManager` en el cliente para verificar antes de hacer push al servidor. El servidor no necesita este check porque recibe mutations ya validadas.

**Veredicto**: No es un bug, es diseño correcto.

### MINOR-2: Prompts pueden tener `project: null` — mitigado ✅

**Hallazgo**: `AddPromptAsync` (línea 999) usa `project = NullableString(p.Project)` que retorna `null` si está vacío. Esto genera `"project": null` en JSON → `ExtractProjectFromPayload` devuelve `""`.

**Mitigación**: 
- El warning log (fix #3) capturará estos casos
- El safety net en `CountPendingNonEnrolledAsync` (fix #2) evitará bloqueo
- En práctica, prompts siempre se crean dentro de una sesión que tiene project

**Veredicto**: Mitigado por los fixes existentes. No requiere acción adicional.

### MINOR-3: `obs.Project` nunca es null — edge case seguro ✅

**Hallazgo**: `Observation.Project` tiene default `""` (no null) según `Models.cs:102`.

**Análisis**: El fix `project = obs.Project` es seguro porque nunca será null, solo `""` en el peor caso. Y el safety net filtra `""`.

**Veredicto**: Edge case cubierto.

---

## Verificación de criterios

| Criterio | Estado | Evidencia |
|----------|--------|-----------|
| `DeleteObservationAsync` incluye `project` | ✅ PASS | `SqliteStore.cs:795` — `project = obs.Project` |
| `CountPendingNonEnrolledAsync` ignora `project=""` | ✅ PASS | `SqliteStore.cs:2060` — `AND sm.project != ''` |
| Warning log cuando project vacío | ✅ PASS | `SqliteStore.cs:2917-2920` |
| Tests verifican el fix | ✅ PASS | 4 tests nuevos, todos pasan |
| No rompe tests existentes | ✅ PASS | 67/67 SqliteStoreTests, 32/32 SyncManagerTests |

---

## Cobertura de tests

| Test | Qué verifica | Resultado |
|------|-------------|-----------|
| `DeleteObservation_MutationIncludesProject` | Delete mutation incluye `project` correcto | ✅ PASS |
| `CountPendingNonEnrolledAsync_IgnoresEmptyProject` | `project=""` no bloquea | ✅ PASS |
| `CountPendingNonEnrolledAsync_StillBlocksNonEnrolledProjects` | Proyectos genuinamente no enrolados SIGUEN bloqueando | ✅ PASS |
| `DeleteObservation_SyncNotBlockedByEmptyProject` | E2E: delete → sync no bloqueado | ✅ PASS |

---

## T3: Docker + Postgres integration — COMPLETADA ✅

**Estado**: T3 corrida exitosamente el 2026-07-17.

**Procedimiento:**
1. Levantado contenedor Postgres temporal (`engram-pg-test`)
2. Construida imagen Docker `engram-local:dev` con los cambios
3. Corrido contenedor `engram-test` apuntando a Postgres
4. Verificado `/health`, `/stats`, `/sync/status`
5. Probado push de mutation de delete con `project` en payload

**Resultados:**
- ✅ `/health` → `{"status":"ok","backend":"postgres"}`
- ✅ `/stats` → `{"total_sessions":1,"total_observations":1}`
- ✅ `/sync/status` → `{"sync_enabled":true,"phase":"cloud","consecutive_failures":0}`
- ✅ Push mutation delete con `project` → `{"accepted_seqs":[1]}` sin errores
- ✅ `sync_state.lifecycle: idle` (sin bloqueos)

**Conclusión**: El fix funciona correctamente en ambiente Postgres. No hay regresiones.

### ENG-459: Sync failure feedback — SIGUIENTE EN COLA

**Contexto**: ENG-458 es el primer fix de dos bugs críticos descubiertos en la misma sesión. ENG-459 (sync failure feedback) es el segundo.

**Recomendación**: Implementar ENG-459 después de merge de ENG-458.

---

## Conclusión

**Veredicto: PASS** ✅

El fix es correcto, completo y bien testeado. Los 3 cambios son minimalistas y apuntan al root cause. Los tests cubren los criterios de aceptación y no hay regresiones.

**Workflow T1-T5 completado:**
- ✅ T1: Loop rápido (SQLite)
- ✅ T2: Tests unitarios (67/67 SqliteStoreTests, 32/32 SyncManagerTests)
- ✅ T3: Docker + Postgres integration (smoke test + push mutation)
- ⏳ T4: CI (GitHub Actions) — se ejecutará en PR
- ⏳ T5: Deploy — manual, humano decide

**Acciones requeridas:**
1. ~~Correr T3 (Docker + Postgres) antes de merge~~ ✅ HECHO
2. Commit + push a rama `fix/eng-458-empty-project-sync-block`
3. Crear PR a main
4. Continuar con ENG-459 (sync failure feedback) después de merge

---

**Auditado por**: Orchestrator (manual verify)
**Método**: Code review + test execution + architecture analysis
