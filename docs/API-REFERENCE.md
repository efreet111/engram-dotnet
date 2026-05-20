# Engram API Reference — Sync Endpoints

> **Propósito**: Referencia completa para humanos de todos los endpoints REST de engram-dotnet.  
> **Server**: `http://192.168.0.178:7437`  
> **Formato**: JSON con `snake_case`  
> **Header**: `X-Engram-User: {identidad}` (obligatorio para sync)

---

## 📋 Generales

### `GET /health`

Devuelve el estado del servidor y el backend activo.

```bash
curl http://192.168.0.178:7437/health
```

```json
{"status":"ok","service":"engram","version":"1.1.0","backend":"postgres"}
```

---

### `GET /stats`

Devuelve estadísticas de uso: total de observaciones, sesiones, prompts.

```bash
curl http://192.168.0.178:7437/stats
```

---

## 📋 Sessions

| Método | Endpoint | Descripción |
|--------|----------|-------------|
| POST | `/sessions` | Crear sesión |
| GET | `/sessions/{id}` | Obtener sesión |
| POST | `/sessions/{id}/end` | Finalizar sesión (con summary) |
| GET | `/sessions/recent` | Listar sesiones recientes |
| DELETE | `/sessions/{id}` | Eliminar sesión |

### POST /sessions

```bash
curl -X POST http://192.168.0.178:7437/sessions \
  -H "Content-Type: application/json" \
  -d '{"id":"session-1","project":"team/mi-api","directory":"/tmp"}'
```

```json
{"id":"session-1","status":"created"}
```

---

## 📋 Observations

| Método | Endpoint | Descripción |
|--------|----------|-------------|
| POST | `/observations` | Crear observación |
| GET | `/observations/{id}` | Obtener observación |
| PATCH | `/observations/{id}` | Actualizar observación |
| DELETE | `/observations/{id}` | Eliminar (soft-delete) |
| POST | `/observations/passive` | Captura pasiva automática |

### POST /observations

```bash
curl -X POST http://192.168.0.178:7437/observations \
  -H "Content-Type: application/json" \
  -d '{
    "session_id":"session-1",
    "title":"Mi decisión",
    "content":"**What**: ... **Why**: ... **Where**: ... **Learned**: ...",
    "type":"decision",
    "project":"team/mi-api",
    "topic_key":"architecture/auth-model"
  }'
```

Parámetros clave:
- `type`: `decision | architecture | bugfix | pattern | learning | discovery | config | manual`
- `scope`: `team` (compartido) o `personal` (privado del user)
- `topic_key`: para upserts — si existe, la actualiza
- `project`: namespace del proyecto

---

## 📋 Search

| Método | Endpoint | Parámetros |
|--------|----------|------------|
| GET | `/search` | `q`, `project`, `type`, `limit` |

```bash
curl "http://192.168.0.178:7437/search?q=arquitectura+sync&project=team/mi-api&limit=10"
```

```json
[
  {
    "observation": { "id": 1, "title": "...", "content": "...", ... },
    "rank": 0.95
  }
]
```

---

## 🔁 Offline-First Sync

### POST /sync/enroll — Inscribir proyecto

Inscribe un proyecto en el sync (sin esto, no participa).

```bash
curl -X POST http://192.168.0.178:7437/sync/enroll \
  -H "Content-Type: application/json" \
  -H "X-Engram-User: victor.silgado" \
  -d '{"project":"team/mi-api"}'
```

```json
{"project":"team/mi-api","enrolled_at":"2026-05-20 16:39:25.064291","enrolled_by":"victor.silgado"}
```

| Código | Significado |
|--------|-------------|
| 200 | ✅ Inscripto correctamente |
| 409 | ⚠️ Ya estaba inscripto |
| 400 | ❌ Faltó `project` |

### GET /sync/enroll — Listar proyectos inscritos

```bash
curl -H "X-Engram-User: victor.silgado" http://192.168.0.178:7437/sync/enroll
```

```json
{"projects":[{"project":"team/mi-api","enrolled_at":"...","enrolled_by":"victor.silgado"}],"count":1}
```

### DELETE /sync/enroll — Desinscribir proyecto

```bash
curl -X DELETE "http://192.168.0.178:7437/sync/enroll?project=team/mi-api" \
  -H "X-Engram-User: victor.silgado"
```

```json
{"project":"team/mi-api","unenrolled_at":"2026-05-20T13:55:22.811Z","status":"unenrolled"}
```

### POST /sync/pause — Pausar sync (admin)

Detiene temporalmente el sync para un proyecto (útil para mantenimiento).

```bash
curl -X POST http://192.168.0.178:7437/sync/pause \
  -H "Content-Type: application/json" \
  -H "X-Engram-User: admin" \
  -d '{"project":"team/mi-api","reason":"Mantenimiento programado"}'
```

```json
{"project":"team/mi-api","paused":true,"paused_at":"2026-05-20T...","paused_by":"admin","reason":"Mantenimiento programado"}
```

### DELETE /sync/pause — Reanudar sync

```bash
curl -X DELETE "http://192.168.0.178:7437/sync/pause?project=team/mi-api" \
  -H "X-Engram-User: admin"
```

```json
{"project":"team/mi-api","paused":false,"resumed_at":"2026-05-20T...","resumed_by":"admin"}
```

### GET /sync/status — Estado completo del sync

```bash
curl http://192.168.0.178:7437/sync/status
```

```json
{
  "sync_enabled": false,
  "phase": "idle",
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
    "last_sync_at": null
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
```

### POST /sync/mutations/push — Push manual

Envía mutaciones locales al servidor.

```bash
curl -X POST http://192.168.0.178:7437/sync/mutations/push \
  -H "Content-Type: application/json" \
  -H "X-Engram-User: victor.silgado" \
  -d '{
    "entries": [
      {"project":"team/mi-api","entity":"observation","entity_key":"obs_123","op":"upsert","payload":"{...}"}
    ]
  }'
```

### GET /sync/mutations/pull — Pull manual

Trae mutaciones del servidor desde un punto específico.

```bash
curl "http://192.168.0.178:7437/sync/mutations/pull?since_seq=0&project=team/mi-api&limit=100"
```

---

## 🔧 CLI Reference

### engram sync status

```bash
engram sync status

# Output:
# Sync status:          healthy
# Target:               cloud
# Last sync:            2026-05-20T16:00:00Z
# Cursor: last_pushed=142, last_pulled=89
# Health: failures=0, backoff=none
```

```bash
engram sync status --json | jq
```

### engram doctor

```bash
engram doctor --server http://192.168.0.178:7437

# Diagnostic Report
# ========================
# ✓ database      (healthy) 12ms
# ✓ http_server   (healthy) 45ms
# ✓ mcp_server    (healthy) 2ms
```

---

## 🧪 Códigos de Estado

| Código | Significado | Solución |
|--------|-------------|----------|
| 200 | ✅ OK | — |
| 400 | ❌ Bad Request | Revisá los campos requeridos |
| 404 | ❌ Not Found | El recurso no existe |
| 409 | ⚠️ Conflict | Ya existe (enroll) o sync pausado |
| 500 | ❌ Server Error | Revisá logs del servidor |
| 501 | ❌ Not Implemented | El backend no soporta esta feature |

---

## 🌐 Variables de Entorno

| Variable | Default | Descripción |
|----------|---------|-------------|
| `ENGRAM_SERVER_URL` | — | URL del servidor remoto |
| `ENGRAM_USER` | — | Identidad del usuario |
| `ENGRAM_DATA_DIR` | `~/.engram` | Directorio local de datos |
| `ENGRAM_DB_TYPE` | `sqlite` | Tipo de base de datos (`sqlite`, `postgres`) |
| `ENGRAM_PG_CONNECTION` | — | Connection string PostgreSQL |
| `ENGRAM_SYNC_ENABLED` | `false` | Habilita sync automático |
| `ENGRAM_SYNC_TARGET_KEY` | `cloud` | Target de sync |
| `ENGRAM_SYNC_POLL_INTERVAL` | `30s` | Intervalo de poll |
| `ENGRAM_SYNC_DEBOUNCE_DURATION` | `5s` | Debounce antes de push |
