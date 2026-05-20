# Offline-First Sync — Casos de Uso y Validación

**Feature**: Offline-First Sync (Phases 1-4 ✅ Complete)  
**Server**: TrueNAS PostgreSQL en `192.168.0.178:7437`  
**Cliente**: Local SQLite + SyncManager background service

---

## 📋 Pre-requisitos

### Variables de Entorno

```bash
# Cliente (local)
export ENGRAM_DATA_DIR=~/.engram
export ENGRAM_SERVER_URL=http://192.168.0.178:7437
export ENGRAM_USER=victor.silgado
export ENGRAM_SYNC_ENABLED=true
export ENGRAM_SYNC_TARGET_KEY=cloud
export ENGRAM_SYNC_POLL_INTERVAL=30s
export ENGRAM_SYNC_DEBOUNCE_DURATION=5s
export ENGRAM_SYNC_MAX_CONSECUTIVE_FAILURES=10

# Server (TrueNAS)
export ENGRAM_DATA_DIR=/data/engram
export ENGRAM_DB_TYPE=postgres
export ENGRAM_PG_CONNECTION_STRING="Host=localhost;Database=engram;Username=engram;Password=***"
export ENGRAM_SERVER_PORT=7437
```

### Verificación Inicial

```bash
# 1. Verificar que el server está corriendo
curl http://192.168.0.178:7437/health

# Expected: {"status":"ok","service":"engram","version":"1.1.0","backend":"PostgreSQL"}

# 2. Verificar sync status local
engram sync status

# Expected: Sync enabled, target=cloud, cursor positions

# 3. Verificar enrolled projects
curl http://192.168.0.178:7437/sync/enroll
# Expected: {"projects":[],"count":0}
```

---

## 🧪 CASOS DE USO

### CASO 1: Primer Enrollment de Proyecto

**Objetivo**: Enroll un proyecto para que participe del sync.

**Pre-condición**: 
- Server corriendo
- Sync habilitado localmente
- Proyecto `team/mi-api` existe localmente

**Pasos**:

```bash
# 1. Enroll del proyecto
curl -X POST http://192.168.0.178:7437/sync/enroll \
  -H "Content-Type: application/json" \
  -H "X-Engram-User: victor.silgado" \
  -d '{"project":"team/mi-api"}'

# 2. Verificar enrollment
curl http://192.168.0.178:7437/sync/enroll \
  -H "X-Engram-User: victor.silgado"

# 3. Verificar sync status
engram sync status --json
```

**Resultado Esperado**:
```json
{
  "project": "team/mi-api",
  "enrolled_at": "2026-05-19T20:00:00Z",
  "enrolled_by": "victor.silgado"
}
```

**Criterio de Aceptación**:
- ✅ Proyecto aparece en lista de enrolled
- ✅ Sync status muestra proyecto enrolled
- ✅ No hay errores en logs del SyncManager

---

### CASO 2: Push de Mutaciones (Online)

**Objetivo**: Verificar que las memorias locales se sincronizan al server.

**Pre-condición**:
- Proyecto enrolled
- SyncManager corriendo

**Pasos**:

```bash
# 1. Crear memoria local
engram mem_save "Test sync observation" \
  --title "Offline-First Test" \
  --type manual \
  --project team/mi-api

# 2. Esperar sync (poll interval 30s)
sleep 35

# 3. Verificar en server
curl http://192.168.0.178:7437/search?q=Offline-First+Test \
  -H "X-Engram-User: victor.silgado"

# 4. Verificar sync status
engram sync status
```

**Resultado Esperado**:
- `pending_push` debería ser 0 después del sync
- Memoria aparece en búsqueda del server
- SyncManager logs muestran `CycleComplete` con pushed count

**Criterio de Aceptación**:
- ✅ Memoria replicada en server
- ✅ Cursor `last_pushed_seq` actualizado
- ✅ No hay errores de conexión

---

### CASO 3: Pull de Mutaciones (Otro Cliente)

**Objetivo**: Verificar que un segundo cliente recibe las memorias del primero.

**Pre-condición**:
- Cliente 1 ya sincronizó memoria
- Cliente 2 tiene mismo proyecto enrolled

**Pasos**:

```bash
# Cliente 2:
export ENGRAM_USER=juan.perez
export ENGRAM_DATA_DIR=~/.engram-juan

# 1. Enroll del mismo proyecto
curl -X POST http://192.168.0.178:7437/sync/enroll \
  -H "Content-Type: application/json" \
  -H "X-Engram-User: juan.perez" \
  -d '{"project":"team/mi-api"}'

# 2. Esperar pull sync
sleep 35

# 3. Buscar memoria creada por Cliente 1
engram search "Offline-First Test"

# 4. Verificar sync status
engram sync status
```

**Resultado Esperado**:
- Memoria de Cliente 1 aparece en Cliente 2
- `last_pulled_seq` actualizado
- Logs muestran `PullBatch` event

**Criterio de Aceptación**:
- ✅ Bidireccionalidad verificada
- ✅ No hay duplicados
- ✅ Timestamps preservados

---

### CASO 4: Trabajo Offline + Sync Diferido

**Objetivo**: Verificar que las memorias creadas offline se sincronizan al reconectar.

**Pre-condición**:
- Proyecto enrolled
- SyncManager corriendo

**Pasos**:

```bash
# 1. Detener server (simular offline)
ssh root@192.168.0.178 "systemctl stop engram-server"

# 2. Crear memorias offline
engram mem_save "Offline observation 1" --project team/mi-api
engram mem_save "Offline observation 2" --project team/mi-api

# 3. Verificar pending mutations
engram sync status --json | jq '.counts.pending_push'
# Expected: 2

# 4. Logs del SyncManager (debería mostrar reintentos)
journalctl -u engram -f | grep "Connection refused"

# 5. Reiniciar server
ssh root@192.168.0.178 "systemctl start engram-server"
sleep 5

# 6. Esperar sync
sleep 35

# 7. Verificar que se sincronizó
engram sync status --json | jq '.counts.pending_push'
# Expected: 0

curl http://192.168.0.178:7437/search?q=Offline+observation
```

**Resultado Esperado**:
- Offline: `pending_push` incrementa
- Reconexión: SyncManager detecta server y hace push
- Memorias aparecen en server

**Criterio de Aceptación**:
- ✅ Offline: no hay errores fatales, solo reintentos
- ✅ Reconexión: sync automático
- ✅ No se pierden memorias

---

### CASO 5: Pause/Resume Sync (Admin)

**Objetivo**: Verificar que admin puede pausar sync para mantenimiento.

**Pre-condición**:
- Proyecto enrolled
- Usuario es admin

**Pasos**:

```bash
# 1. Pausar sync
curl -X POST http://192.168.0.178:7437/sync/pause \
  -H "Content-Type: application/json" \
  -H "X-Engram-User: admin" \
  -d '{"project":"team/mi-api","reason":"Database maintenance"}'

# 2. Intentar push (debería fallar con 409)
curl -X POST http://192.168.0.178:7437/sync/mutations/push \
  -H "Content-Type: application/json" \
  -H "X-Engram-User: victor.silgado" \
  -d '{"entries":[{"project":"team/mi-api","entity":"observation","entity_key":"test","op":"upsert","payload":"{}"}]}'

# Expected: HTTP 409 Conflict
# {"error_class":"policy","error_code":"sync-paused","error":"..."}

# 3. Verificar paused projects
curl http://192.168.0.178:7437/sync/status | jq '.paused_projects'

# 4. Reanudar sync
curl -X DELETE "http://192.168.0.178:7437/sync/pause?project=team/mi-api" \
  -H "X-Engram-User: admin"

# 5. Verificar reanudado
curl http://192.168.0.178:7437/sync/status | jq '.paused_projects'
# Expected: []
```

**Resultado Esperado**:
- Pause: HTTP 409 con error `sync-paused`
- Resume: Sync se reanuda automáticamente

**Criterio de Aceptación**:
- ✅ Pause bloquea push
- ✅ Resume permite push
- ✅ Audit log registra pause/resume events

---

### CASO 6: Deferred Replay (FK Misses)

**Objetivo**: Verificar que las mutaciones que fallan por FK constraints se reintentan.

**Pre-condición**:
- Proyecto enrolled
- Tabla `sync_apply_deferred` existe

**Pasos**:

```bash
# 1. Crear observación con referencia a sesión inexistente (simular FK miss)
# Esto debería ir a sync_apply_deferred

# 2. Verificar deferred queue
sqlite3 ~/.engram/engram.db "SELECT COUNT(*) FROM sync_apply_deferred;"

# 3. Crear sesión faltante
engram mem_save_session "test-session" --project team/mi-api

# 4. Esperar replay
sleep 35

# 5. Verificar deferred queue vacía
sqlite3 ~/.engram/engram.db "SELECT COUNT(*) FROM sync_apply_deferred;"
# Expected: 0

# 6. Verificar logs
journalctl -u engram -f | grep "DeferredReplay"
```

**Resultado Esperado**:
- FK miss va a `sync_apply_deferred`
- Al crear sesión, replay automático
- `retry_count` incrementa en fallos

**Criterio de Aceptación**:
- ✅ FK misses no bloquean cursor
- ✅ Replay automático cuando FK se resuelve
- ✅ Dead rows logueadas después de 5 reintentos

---

### CASO 7: Sync Status Endpoint

**Objetivo**: Verificar observabilidad del sync.

**Pasos**:

```bash
# 1. Obtener status completo
curl http://192.168.0.178:7437/sync/status | jq

# 2. Verificar estructura
{
  "sync_enabled": true,
  "phase": "healthy",
  "target": "cloud",
  "cursor": {
    "last_pushed_seq": 142,
    "last_pulled_seq": 89,
    "last_enqueued_seq": 145
  },
  "health": {
    "status": "healthy",
    "consecutive_failures": 0,
    "backoff_until": null,
    "last_error": null,
    "last_sync_at": "2026-05-19T20:00:00Z"
  },
  "counts": {
    "pending_push": 0,
    "total_pushed": 142,
    "total_pulled": 89,
    "deferred_pending": 0
  },
  "enrolled_projects": ["team/mi-api"],
  "paused_projects": []
}

# 3. CLI status
engram sync status

# 4. CLI status JSON
engram sync status --json | jq
```

**Resultado Esperado**:
- Todos los campos presentes
- Cursors actualizados
- Health status correcto

**Criterio de Aceptación**:
- ✅ Endpoint responde < 100ms
- ✅ Datos coherentes con DB
- ✅ CLI formateo correcto

---

### CASO 8: Multi-User Isolation + Sync

**Objetivo**: Verificar que usuarios distintos no ven memorias personales entre sí.

**Pre-condición**:
- Dos usuarios enrolled en mismo proyecto team

**Pasos**:

```bash
# Usuario 1: victor.silgado
export ENGRAM_USER=victor.silgado
engram mem_save "Personal de Victor" --scope personal --project mi-api

# Usuario 2: juan.perez
export ENGRAM_USER=juan.perez
engram mem_save "Personal de Juan" --scope personal --project mi-api

# Usuario 1: Buscar memorias
export ENGRAM_USER=victor.silgado
engram search "Personal"

# Expected: Solo ve "Personal de Victor"

# Usuario 2: Buscar memorias
export ENGRAM_USER=juan.perez
engram search "Personal"

# Expected: Solo ve "Personal de Juan"

# Ambos: Ver memorias team
export ENGRAM_USER=victor.silgado
engram mem_save "Team memory" --scope team --project mi-api

export ENGRAM_USER=juan.perez
engram search "Team memory"

# Expected: Ambos ven "Team memory"
```

**Resultado Esperado**:
- `personal:victor.silgado/*` solo visible para Victor
- `personal:juan.perez/*` solo visible para Juan
- `team/mi-api/*` visible para ambos

**Criterio de Aceptación**:
- ✅ Aislamiento personal verificado
- ✅ Team compartido verificado
- ✅ Namespacing correcto en sync

---

## 📊 MÉTRICAS DE VALIDACIÓN

| Métrica | Target | Cómo medir |
|---------|--------|------------|
| **Push latency (p95)** | < 500ms | `/sync/status` endpoint |
| **Pull latency (p95)** | < 1000ms | SyncManager logs |
| **Sync success rate** | > 99% | `cloud_sync_audit_log` table |
| **Deferred replay success** | > 95% | `sync_apply_deferred.retry_count` |
| **Offline tolerance** | Indefinido | Pending mutations queue |
| **Failure ceiling** | 10 consecutive failures | SyncManager phase transitions |

---

## 🐛 TROUBLESHOOTING

### Sync no arranca

```bash
# 1. Verificar ENGRAM_SYNC_ENABLED
echo $ENGRAM_SYNC_ENABLED
# Expected: true

# 2. Verificar logs
journalctl -u engram -f | grep "SyncManager"

# 3. Verificar server reachable
curl http://192.168.0.178:7437/health

# 4. Reiniciar SyncManager
systemctl restart engram
```

### 409 Sync Paused

```bash
# Verificar paused projects
curl http://192.168.0.178:7437/sync/status | jq '.paused_projects'

# Resume si es necesario
curl -X DELETE "http://192.168.0.178:7437/sync/pause?project=team/mi-api" \
  -H "X-Engram-User: admin"
```

### Pending mutations no bajan

```bash
# Verificar pending count
engram sync status --json | jq '.counts.pending_push'

# Verificar logs de error
journalctl -u engram -f | grep "CycleFailed"

# Forzar sync manual (si existe comando)
engram sync --force
```

### Deferred queue crece

```bash
# Verificar deferred rows
sqlite3 ~/.engram/engram.db "SELECT COUNT(*), AVG(retry_count) FROM sync_apply_deferred;"

# Verificar dead rows (retry_count >= 5)
sqlite3 ~/.engram/engram.db "SELECT * FROM sync_apply_deferred WHERE retry_count >= 5;"

# Investigar causa de FK misses
sqlite3 ~/.engram/engram.db "SELECT DISTINCT entity_key FROM sync_apply_deferred;"
```

---

## ✅ CHECKLIST DE VALIDACIÓN

- [ ] CASO 1: Enrollment funciona
- [ ] CASO 2: Push online funciona
- [ ] CASO 3: Pull entre clientes funciona
- [ ] CASO 4: Offline + reconexión funciona
- [ ] CASO 5: Pause/Resume funciona
- [ ] CASO 6: Deferred replay funciona
- [ ] CASO 7: Sync status endpoint funciona
- [ ] CASO 8: Multi-user isolation + sync funciona
- [ ] Métricas dentro de targets
- [ ] Logs sin errores críticos
- [ ] Audit trail completo

---

**Ready for production validation!** 🚀
