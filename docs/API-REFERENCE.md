# Engram API Reference

> **Purpose**: Complete REST API reference for humans.  
> **Server**: `http://192.168.0.178:7437`  
> **Format**: JSON with `snake_case`  
> **Auth**: Header `X-Engram-User: {identity}` (required for sync endpoints)

---

## 📋 General

### `GET /health`

Returns server status and active backend.

```bash
curl http://192.168.0.178:7437/health
```

```json
{"status":"ok","service":"engram","version":"1.1.0","backend":"postgres"}
```

### `GET /stats`

Returns usage statistics: total observations, sessions, prompts.

```bash
curl http://192.168.0.178:7437/stats
```

---

## 📋 Sessions

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/sessions` | Create a session |
| GET | `/sessions/{id}` | Get session details |
| POST | `/sessions/{id}/end` | End a session (with summary) |
| GET | `/sessions/recent` | List recent sessions |
| DELETE | `/sessions/{id}` | Delete a session |

### POST /sessions

```bash
curl -X POST http://192.168.0.178:7437/sessions \
  -H "Content-Type: application/json" \
  -d '{"id":"session-1","project":"team/mi-api","directory":"/tmp"}'
```

```json
{"id":"session-1","status":"created"}
```

### DELETE /sessions/{id}

```bash
curl -X DELETE http://192.168.0.178:7437/sessions/session-1
```

| Status | Response |
|--------|----------|
| 200 | `{"id": "session-1", "status": "deleted"}` |
| 404 | `{"error": true, "error_code": "session_not_found", "message": "session not found: session-1"}` |
| 409 | `{"error": true, "error_code": "blocked_by_observations", "message": "session has N active observations, cannot delete"}` |

---

## 📋 Observations

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/observations` | Create an observation |
| GET | `/observations/{id}` | Get observation details |
| GET | `/observations/recent` | List recent observations |
| PATCH | `/observations/{id}` | Update an observation |
| DELETE | `/observations/{id}` | Soft-delete an observation |
| POST | `/observations/passive` | Automatic passive capture |

### POST /observations

```bash
curl -X POST http://192.168.0.178:7437/observations \
  -H "Content-Type: application/json" \
  -d '{
    "session_id":"session-1",
    "title":"My decision",
    "content":"**What**: ... **Why**: ... **Where**: ... **Learned**: ...",
    "type":"decision",
    "project":"team/mi-api",
    "topic_key":"architecture/auth-model"
  }'
```

Key parameters:
- `type`: `decision | architecture | bugfix | pattern | learning | discovery | config | manual`
- `scope`: `team` (shared) or `personal` (private)
- `topic_key`: for upserts — if it exists, updates the observation
- `project`: project namespace

---

## 📋 Search

| Method | Endpoint | Parameters |
|--------|----------|------------|
| GET | `/search` | `q`, `project`, `type`, `limit` |

```bash
curl "http://192.168.0.178:7437/search?q=architecture+sync&project=team/mi-api&limit=10"
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

## 📋 Context & Timeline

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/context` | Get session context (observations in a session) |
| GET | `/timeline` | Get timeline around an observation |

### `GET /context`

```bash
curl "http://192.168.0.178:7437/context?session_id=session-1&limit=5"
```

### `GET /timeline`

```bash
curl "http://192.168.0.178:7437/timeline?observation_id=123&window=5"
```

---

## 📋 Export / Import

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/export` | Export all data (optionally filter by project) |
| POST | `/import` | Import previously exported data |

### `GET /export`

Export all data, optionally filtered by project.

```bash
curl "http://192.168.0.178:7437/export?project=team/mi-api"
```

### `GET /export/since`

Export mutations after a specific sequence cursor (for incremental sync).

```bash
curl "http://192.168.0.178:7437/export/since?project=team/mi-api&after_seq=0&limit=100"
```

```json
{
  "observations": [...],
  "prompts": [...],
  "sessions": [...],
  "next_seq": 1234,
  "has_more": true
}
```

### `POST /import`

```bash
curl -X POST http://192.168.0.178:7437/import \
  -H "Content-Type: application/json" \
  -d '{"observations":[],"sessions":[],"prompts":[]}'
```

---

## 📋 Prompts

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/prompts` | Save a user prompt |
| GET | `/prompts/recent` | List recent prompts |
| GET | `/prompts/search` | Search prompts |
| DELETE | `/prompts/{id}` | Delete a prompt |

### DELETE /prompts/{id}

```bash
curl -X DELETE http://192.168.0.178:7437/prompts/42
```

| Status | Response |
|--------|----------|
| 200 | `{"id": 42, "status": "deleted"}` |
| 400 | `{"error": true, "error_code": "validation_error", "message": "invalid prompt id"}` |
| 404 | `{"error": true, "error_code": "prompt_not_found", "message": "prompt not found: 999"}` |

---

## 📋 Projects

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/projects/list` | List all projects |
| GET | `/projects/stats` | Project statistics |
| GET | `/projects/migrations` | List pending migrations |
| POST | `/projects/migrate` | Merge projects |
| POST | `/projects/prune` | Delete a project and its data |

---

## 🔁 Offline-First Sync

### POST /sync/enroll — Enroll a project

Registers a project for synchronization. Without this, the project won't sync.

```bash
curl -X POST http://192.168.0.178:7437/sync/enroll \
  -H "Content-Type: application/json" \
  -H "X-Engram-User: victor.silgado" \
  -d '{"project":"team/mi-api"}'
```

```json
{"project":"team/mi-api","enrolled_at":"2026-05-20 16:39:25.064291","enrolled_by":"victor.silgado"}
```

| Code | Meaning |
|------|---------|
| 200 | ✅ Enrolled successfully |
| 409 | ⚠️ Already enrolled |
| 400 | ❌ Missing `project` |

### GET /sync/enroll — List enrolled projects

```bash
curl -H "X-Engram-User: victor.silgado" http://192.168.0.178:7437/sync/enroll
```

```json
{"projects":[{"project":"team/mi-api","enrolled_at":"...","enrolled_by":"victor.silgado"}],"count":1}
```

### DELETE /sync/enroll — Unenroll a project

```bash
curl -X DELETE "http://192.168.0.178:7437/sync/enroll?project=team/mi-api" \
  -H "X-Engram-User: victor.silgado"
```

```json
{"project":"team/mi-api","unenrolled_at":"2026-05-20T13:55:22.811Z","status":"unenrolled"}
```

### POST /sync/pause — Pause sync (admin)

Temporarily stops sync for a project (useful for maintenance).

```bash
curl -X POST http://192.168.0.178:7437/sync/pause \
  -H "Content-Type: application/json" \
  -H "X-Engram-User: admin" \
  -d '{"project":"team/mi-api","reason":"Scheduled maintenance"}'
```

```json
{"project":"team/mi-api","paused":true,"paused_at":"2026-05-20T...","paused_by":"admin","reason":"Scheduled maintenance"}
```

### DELETE /sync/pause — Resume sync

```bash
curl -X DELETE "http://192.168.0.178:7437/sync/pause?project=team/mi-api" \
  -H "X-Engram-User: admin"
```

```json
{"project":"team/mi-api","paused":false,"resumed_at":"2026-05-20T...","resumed_by":"admin"}
```

### GET /sync/status — Full sync status

```bash
curl http://192.168.0.178:7437/sync/status
```

```json
{
  "sync_enabled": false,
  "phase": "idle",
  "target": "cloud",
  "cursor": {"last_pushed_seq": 142, "last_pulled_seq": 89, "last_enqueued_seq": 145},
  "health": {
    "status": "healthy",
    "consecutive_failures": 0,
    "backoff_until": null,
    "last_error": null,
    "last_sync_at": null
  },
  "counts": {
    "pending_push": 0, "total_pushed": 142, "total_pulled": 89, "deferred_pending": 0
  },
  "enrolled_projects": ["team/mi-api"],
  "paused_projects": []
}
```

### POST /sync/mutations/push — Manual push

Sends local mutations to the server.

```bash
curl -X POST http://192.168.0.178:7437/sync/mutations/push \
  -H "Content-Type: application/json" \
  -H "X-Engram-User: victor.silgado" \
  -d '{"entries":[{"project":"team/mi-api","entity":"observation","entity_key":"obs_123","op":"upsert","payload":"{}"}]}'
```

### GET /sync/mutations/pull — Manual pull

Fetches mutations from the server starting from a specific seq.

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

## 📊 MD Promotion & Index

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/md/promote/{id}` | Promote an observation to a .md file |
| POST | `/md/sync` | Sync all promoted .md files to the repo |
| POST | `/md/index` | Generate a markdown index file |

---

## 🧹 Retention (TTL)

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/retention/stats` | Get retention statistics by age bucket |
| POST | `/retention/prune` | Delete observations past their TTL |

---

## Error Responses

Errors return HTTP error status codes with a JSON body:

```json
{
  "error": true,
  "error_code": "snake_case_code",
  "message": "Human readable description",
  "hint": "Optional suggestion",
  "available_projects": ["..."]
}
```

Error codes:
- `ambiguous_project` — multiple git repos in cwd
- `unknown_project` — project override not found in store
- `project_not_found` — project doesn't exist
- `session_not_found` — session ID doesn't exist
- `prompt_not_found` — prompt ID doesn't exist
- `observation_not_found` — observation ID doesn't exist
- `validation_error` — invalid parameter
- `blocked_by_observations` — session can't be deleted (has active observations)
- `internal_error` — unexpected server error

---

## 🧪 HTTP Status Codes

| Code | Meaning | Solution |
|------|---------|----------|
| 200 | ✅ OK | — |
| 400 | ❌ Bad Request | Check required fields |
| 404 | ❌ Not Found | Resource doesn't exist |
| 409 | ⚠️ Conflict | Already enrolled or sync paused |
| 500 | ❌ Server Error | Check server logs |
| 501 | ❌ Not Implemented | Backend doesn't support this feature |

---

## 🌐 Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `ENGRAM_SERVER_URL` | — | Remote server URL |
| `ENGRAM_USER` | — | User identity |
| `ENGRAM_DATA_DIR` | `~/.engram` | Local data directory |
| `ENGRAM_DB_TYPE` | `sqlite` | Database type (`sqlite`, `postgres`) |
| `ENGRAM_PG_CONNECTION` | — | PostgreSQL connection string |
| `ENGRAM_SYNC_ENABLED` | `true` | Enable automatic sync |
| `ENGRAM_SYNC_TARGET` | `cloud` | Sync target key |
| `ENGRAM_SYNC_POLL_SECONDS` | `30` | Sync poll interval (seconds) |
| `ENGRAM_SYNC_DEBOUNCE_MS` | `500` | Debounce before sync (ms) |
