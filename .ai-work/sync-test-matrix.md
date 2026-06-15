# Sync Test Matrix — Engram

> **Propósito:** Verificar que el sync offline-first funciona correctamente para equipos multi-cliente.
> **Última actualización:** 2026-06-15
> **Tests dockerizados:** `scripts/test-2client-pull.sh` · `scripts/test-offline-reconnect.sh`

---

## Estado general

| ENG | Test | Estado | Notas |
|-----|------|--------|-------|
| ENG-428 | Payload session_id | ✅ PASS | Fix: SnakeCaseLower en JsonPullOpts |
| ENG-209 | Pull entre 2 clientes | ✅ PASS | `bash scripts/test-2client-pull.sh` |
| ENG-210 | Offline + reconexión | ✅ PASS | `bash scripts/test-offline-reconnect.sh` |

---

## Matriz de escenarios de sync

### Escenario 1: Push básico (crear → server recibe)

| # | Descripción | Pasos | Resultado esperado | Cobertura |
|---|-------------|-------|-------------------|-----------|
| 1.1 | Crear observación → server la aplica | Client-A: `engram save "test" "x" --project P --scope team` | Server: `search "test"` devuelve la obs | ✅ `test-2client-pull.sh` paso 3-4 |
| 1.2 | Crear sesión → server la aplica | Via `engram save` (crea session implícitamente) | Server tiene la sesión | ✅ cubierto por 1.1 |
| 1.3 | Crear prompt → server lo aplica | Via `engram mem_save_prompt` (MCP) | Server tiene el prompt | 🔲 no cubierto (requiere MCP client) |
| 1.4 | Mutation con scope team | `--scope team` | Server aplica (scope compartido) | ✅ `test-2client-pull.sh` |
| 1.5 | Mutation con scope personal | `--scope personal` | Server NO aplica (privada) | 🔲 no cubierto |
| 1.6 | Múltiples proyectos | 2 proyectos diferentes enrolados | Mutations de cada proyecto llegan correctamente | 🔲 no cubierto |

### Escenario 2: Pull entre 2 clientes (A escribe → B lee)

| # | Descripción | Pasos | Resultado esperado | Cobertura |
|---|-------------|-------|-------------------|-----------|
| 2.1 | A crea memory → B la ve en server | A: `save "x" --project P --scope team`, B: `search "x" --project P` | B encuentra la memoria de A | ✅ `test-2client-pull.sh` |
| 2.2 | A crea memory → B la ve localmente | A: `save`, B: espera ciclo sync, B: `search` local | B encuentra en SQLite local (pulled) | ✅ `test-2client-pull.sh` paso 5 |
| 2.3 | Bidireccional: A y B crean simultáneamente | A: `save "a"`, B: `save "b"`, ambos esperan sync | Ambos ven ambas memorias | 🔲 no cubierto |
| 2.4 | Pull incremental: solo mutations nuevas | Después de un pull, nuevas mutations deben llegar en el próximo ciclo | `last_pulled_seq` avanza | 🔲 no cubierto (unit test sí) |

### Escenario 3: Offline + reconexión

| # | Descripción | Pasos | Resultado esperado | Cobertura |
|---|-------------|-------|-------------------|-----------|
| 3.1 | Cliente offline → 3 memorias → reconexión | desconectar red, crear 3, reconectar, esperar sync | Server tiene las 3 | ✅ `test-offline-reconnect.sh` |
| 3.2 | pending_push reflecta cola offline | Durante offline, `sync status` muestra pending_push > 0 | pending_push = 3 | ⚠️ parcial: script reporta pending_push pero el comando puede fallar offline |
| 3.3 | Reconexión automática (sin reiniciar) | Cliente reconecta red, SyncManager reanuda ciclos | Mutations se pushean sin intervención | ✅ `test-offline-reconnect.sh` paso 5-7 |
| 3.4 | Offline prolongado (varios minutos) | 5+ minutos offline, muchas mutations acumuladas | Todas se pushean al reconectar | 🔲 no cubierto |
| 3.5 | Offline + 2 clientes distintos | A offline crea, B offline crea, ambos reconectan | Todas las mutations de ambos llegan | 🔲 no cubierto |

### Escenario 4: Enrolamiento y configuración

| # | Descripción | Pasos | Resultado esperado | Cobertura |
|---|-------------|-------|-------------------|-----------|
| 4.1 | Enrolar proyecto | `engram sync enroll --project P` | `sync_enrolled_projects` tiene P | ✅ unit test + docker test |
| 4.2 | Desenrolar proyecto | `engram sync unenroll --project P` | Push bloqueado para P | 🔲 no cubierto |
| 4.3 | Múltiples proyectos enrolados | Enrolar P1, P2, P3 | Todos pueden hacer push | 🔲 no cubierto |
| 4.4 | Proyecto no enrolado → push bloqueado | No enrolar, `save "x" --project P` | Push no intenta (blocked_unenrolled) | ✅ verificado en ENG-209 investigación |
| 4.5 | Enrolar después de crear mutations | Crear mutations, luego enrolar | Mutations pendientes se pushean | 🔲 no cubierto |

### Escenario 5: Errores y recovery

| # | Descripción | Pasos | Resultado esperado | Cobertura |
|---|-------------|-------|-------------------|-----------|
| 5.1 | Server caído → cliente reintenta | Matar server, cliente intenta push | Backoff exponencial, reintento | ✅ verificado en investigación PRE-428 (500 → backoff) |
| 5.2 | Server vuelve → cliente reanuda | Server caído 30s, vuelve | Mutations acumuladas se pushean | 🔲 no cubierto |
| 5.3 | Schema mismatch SQLite (ENG-211) | DB con schema viejo | Migración corre, sync funciona | ✅ ENG-211 (unit test) |
| 5.4 | Payload inválido → server rechaza | Mutation con datos corruptos | Server retorna error, cliente reintenta | 🔲 no cubierto |
| 5.5 | Timeout de conexión | Red lenta, request demora > timeout | Cliente reintenta en próximo ciclo | 🔲 no cubierto |

### Escenario 6: Rest API sync endpoints

| # | Descripción | Pasos | Resultado esperado | Cobertura |
|---|-------------|-------|-------------------|-----------|
| 6.1 | POST /sync/enroll | `curl -X POST -H "X-Engram-User: user" -d '{"project":"P"}'` | 200, project enrolled | ✅ test-2client-pull.sh |
| 6.2 | GET /sync/enroll | Listar proyectos enrolados | Lista con P | 🔲 no cubierto |
| 6.3 | DELETE /sync/enroll | Desenrolar proyecto | Proyecto removido | 🔲 no cubierto |
| 6.4 | GET /sync/status | Health del sync | `{ enabled, enrolled_projects, cursor }` | ✅ test-2client-pull.sh (paso final) |
| 6.5 | GET /sync/status en server | Server sin sync habilitado | `{ enabled: false }` con reason | 🔲 no cubierto |

### Escenario 7: Unit / integration tests existentes

| # | Descripción | Archivo | Estado |
|---|-------------|---------|--------|
| 7.1 | SqliteStore ApplyPulledMutationAsync | `SqliteStoreApplyPulledTests.cs` (~11 tests) | ✅ existente |
| 7.2 | SyncManager ciclo básico | `SyncManagerTests.cs` | ✅ existente |
| 7.3 | MutationTransport | `MutationTransportTests.cs` | ✅ existente |
| 7.4 | PostgresStore push/pull mutations | `PostgresStoreTests.cs` (PushMutation_*) | ✅ existente |
| 7.5 | CloudSyncEndpoints | `CloudSyncEndpointsTests.cs` | ✅ existente |
| 7.6 | SyncMetrics | `SyncMetricsTests.cs` | ✅ existente |
| 7.7 | ReplayDeferredAsync old schema (ENG-211) | `SqliteStoreTests.cs` | ✅ nuevo |

---

## Resumen de cobertura

| Categoría | Total escenarios | Cubiertos | % |
|-----------|-----------------|-----------|---|
| Push básico | 6 | 3 | 50% |
| Pull 2 clientes | 4 | 2 | 50% |
| Offline + reconexión | 5 | 3 | 60% |
| Enrolamiento | 5 | 2 | 40% |
| Errores / recovery | 5 | 2 | 40% |
| REST API sync | 5 | 2 | 40% |
| Tests automáticos | 7 | 7 | 100% |
| **Total** | **37** | **21** | **57%** |

**Gaps principales** (para ENG futuro o release testing):
1. Bidireccional (A y B crean simultáneamente) — escenario 2.3
2. Offline + 2 clientes distintos — escenario 3.5
3. Enrolamiento avanzado (unenroll, multi-proyecto) — escenario 4.2-4.5
4. Recovery de server caído — escenario 5.2
5. Scope personal no se pushea — escenario 1.5

---

## Cómo ejecutar los tests dockerizados

```bash
# Test 1: Pull entre 2 clientes
bash scripts/test-2client-pull.sh

# Test 2: Offline + reconexión
bash scripts/test-offline-reconnect.sh
```

Ambos requieren Docker. No requieren docker compose. Construyen la imagen automáticamente si no existe.

## Tests unitarios (rápidos, sin Docker)

```bash
dotnet test -c Release --filter "FullyQualifiedName!~Engram.Postgres.Tests&Category!=RequiresDocker"
```
