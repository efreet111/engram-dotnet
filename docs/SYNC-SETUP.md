# Sync Setup — Mutation-Based Offline-First Sync

> Guía de configuración para habilitar la sincronización bidireccional de mutaciones entre SQLite local y PostgreSQL remoto.

---

## Cuándo usar Sync

| Escenario | Recomendación |
|---|---|
| 1 desarrollador, single machine | No activar sync (default) |
| Team 2+ developers con TrueNAS server | Activar sync con `ENGRAM_SYNC_ENABLED=true` |
| Offline-first / conexión intermitente | Sync obligatorio |
| Alta disponibilidad multi-instancia | Sync obligatorio |

---

## Requisitos

| Componente | Versión |
|---|---|
| PostgreSQL 15+ en TrueNAS | `192.168.0.178:7437` |
| engram-dotnet | 1.3.0+ |
| Proyecto enrolado en sync vía `POST /sync/enroll` | Phase 3+ |

---

## Variables de entorno

| Variable | Default | Descripción |
|---|---|---|
| `ENGRAM_SYNC_ENABLED` | `false` | Activar el background SyncManager |
| `ENGRAM_SYNC_TARGET_KEY` | `cloud` | Clave del target de sync |
| `ENGRAM_SYNC_POLL_INTERVAL` | `00:01:00` | Intervalo entre ciclos de sync |
| `ENGRAM_SYNC_DEBOUNCE_DURATION` | `00:00:30` | Debounce antes de arrancar |
| `ENGRAM_SYNC_PUSH_BATCH_SIZE` | `500` | Mutaciones máximas por push |
| `ENGRAM_SYNC_PULL_BATCH_SIZE` | `500` | Mutaciones máximas por pull |
| `ENGRAM_SYNC_MAX_CONSECUTIVE_FAILURES` | `5` | Umbral de failure ceiling |
| `ENGRAM_SYNC_BASE_BACKOFF` | `00:00:05` | Backoff inicial (exponencial) |
| `ENGRAM_SYNC_MAX_BACKOFF` | `00:05:00` | Backoff máximo |
| `ENGRAM_SYNC_LEASE_OWNER` | hostname | Owner del lease de sync |
| `ENGRAM_SERVER_URL` | `http://localhost:7437` | URL del servidor (para CLI) |
| `ENGRAM_DB_TYPE` | `sqlite` | `postgres` para server-side |
| `ENGRAM_PG_CONNECTION` | — | Connection string de PostgreSQL |

---

## Setup paso a paso

### 1. Configurar PostgreSQL en TrueNAS

Seguí [`POSTGRES-SETUP.md`](POSTGRES-SETUP.md) para tener PostgreSQL funcionando con schema completo.

### 2. Configurar variables de entorno del servidor

```bash
export ENGRAM_DB_TYPE=postgres
export ENGRAM_PG_CONNECTION="Host=192.168.0.178;Port=7437;Database=engram;Username=engram;Password=tu_password"
export ENGRAM_SYNC_ENABLED=true
export ENGRAM_SERVER_URL=http://localhost:7437
```

### 3. Iniciar el servidor

```bash
engram serve --port 7437
# Output esperado:
# [engram] starting HTTP server on :7437 (PostgreSQL)
# SyncManager starting (target=cloud, poll=00:01:00)
```

### 4. Enrolar proyectos (Phase 3+)

```bash
curl -X POST http://localhost:7437/sync/enroll \
  -H "Content-Type: application/json" \
  -d '{"project": "team/mi-proyecto"}'
# {"project":"team/mi-proyecto","enrolled_at":"2026-05-19T...","enrolled_by":""}
```

### 5. Verificar sync status

```bash
engram sync status
```

Output esperado:
```
Sync status (mutation-based):
  Enabled:              True
  Phase:                Healthy
  Health:               healthy
  Consecutive failures: 0
  Backoff until:        —
  Last sync:            2026-05-19T12:00:00.0000000Z
  Last error:           —
  Pending push:         0
  Total pushed:         42
  Total pulled:         17
  Last pushed seq:      42
  Last pulled seq:      17
```

Formato machine-readable:
```bash
engram sync status --json
# {"sync_enabled":true,"phase":"healthy","target":"cloud",...}
```

---

## Arquitectura

```
┌──────────────────────────────┐     push/pull HTTP     ┌──────────────────────┐
│   SQLite (local)             │ ◄────────────────────► │  PostgreSQL (server) │
│   ┌──────────────────────┐   │   POST /sync/mutations/ │  ┌────────────────┐ │
│   │ SyncManager          │   │         push            │  │ CloudSync      │ │
│   │ (BackgroundService)  │───┼─────────────────────────┼─►│ Endpoints      │ │
│   │ ILocalSyncStore      │   │   GET /sync/mutations/  │  │ ICloudMutation │ │
│   │ IMutationTransport   │◄──┼─────────────────────────┼──│ Store          │ │
│   └──────────────────────┘   │         pull            │  └────────────────┘ │
└──────────────────────────────┘                         └──────────────────────┘
```

### Ciclo de sync

1. **Lease**: `AcquireSyncLeaseAsync` — prevení concurrencia
2. **Push**: enviar mutaciones pendientes al server
   - Agrupar por proyecto
   - Drenar batch (`PushBatchSize`)
   - Manejar pause (409 Conflict)
   - Ack seqs aceptadas
3. **Deferred Replay**: reintentar relaciones FK diferidas
4. **Pull**: recibir mutaciones del server
   - Loop cursor (`since_seq` → `has_more`)
   - Aplicar mutaciones localmente
5. **Healthy**: marcar sync exitoso
6. **Failure**: backoff exponencial + failure ceiling

---

## Troubleshooting

### Sync no arranca

```
SyncManager disabled (ENGRAM_SYNC_ENABLED=false)
```

**Solución**: Configurá `ENGRAM_SYNC_ENABLED=true`.

### 409 Pause

```
SyncManager push paused for project team/mi-proyecto: sync-paused
```

**Causa**: El server pausó el sync para ese proyecto (ej: deploy, mantenimiento).
**Solución**: Resumí desde el server:
```bash
curl -X DELETE http://localhost:7437/sync/pause \
  -H "Content-Type: application/json" \
  -d '{"project": "team/mi-proyecto"}'
```

### Failure ceiling reached

```
Sync cycle failed (failure 5/5)
SyncManager disabled: failure ceiling reached (5/5)
```

**Causa**: 5 ciclos consecutivos fallidos.
**Solución**: Revisá logs del server, verificá conectividad de red, reiniciá el servidor.

### Connection refused (CLI)

```
error: No se pudo conectar al servidor — ¿está engram server corriendo?
```

**Causa**: `engram sync status` no encuentra el servidor HTTP.
**Solución**: Verificá que `engram serve` esté corriendo y `ENGRAM_SERVER_URL` apunte al puerto correcto.

### Lease no adquirido

```
SyncManager cycle skipped: lease not acquired
```

**Causa**: Otra instancia de SyncManager tiene el lease activo.
**Solución**: Es normal si hay múltiples instancias. El lease expira en 1 minuto.

---

## Monitoreo

### Eventos de logging (EventId)

| ID  | Evento             | Level     | Descripción |
|-----|---------------------|-----------|-------------|
| 2000| SyncCycleStart      | Info      | Ciclo comenzó |
| 2001| SyncCycleComplete   | Info      | Ciclo completado exitosamente |
| 2002| SyncCycleFailed     | Error     | Ciclo falló |
| 2003| SyncPushBatch       | Info      | Push batch enviado |
| 2004| SyncPullBatch       | Info      | Pull batch recibido |
| 2005| SyncDeferredReplay  | Info      | Replay de relaciones deferidas |
| 2006| SyncPanicExit       | Critical  | Error no manejado en SyncManager |
| 2007| SyncPhaseTransition | Debug     | Cambio de fase |

### Sync status endpoint

```bash
curl http://localhost:7437/sync/status | jq .
```

```json
{
  "sync_enabled": true,
  "phase": "healthy",
  "target": "cloud",
  "cursor": {
    "last_pushed_seq": 42,
    "last_pulled_seq": 17,
    "last_enqueued_seq": 42
  },
  "health": {
    "status": "healthy",
    "consecutive_failures": 0,
    "backoff_until": null,
    "last_error": null,
    "last_sync_at": "2026-05-19T12:00:00.0000000Z"
  },
  "counts": {
    "pending_push": 0,
    "total_pushed": 42,
    "total_pulled": 17,
    "deferred_pending": 0
  },
  "enrolled_projects": ["team/mi-proyecto"],
  "paused_projects": []
}
```
