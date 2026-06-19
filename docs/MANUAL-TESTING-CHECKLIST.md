# Manual Testing Checklist

> **Propósito**: Trazabilidad real de pruebas manuales sobre el servidor en producción.
> **Servidor**: `http://192.168.0.178:7437` (PostgreSQL)
> **Última verificación**: 2026-06-04 (smoke test + 5 regression tests, todos OK)
> **Verificación anterior**: 2026-06-01 (post-deploy `e1a9cf9`, regression bugs #1–#3)
> **Deploy commit**: `e1a9cf9` — ver [SESSION-REPORT-2026-05-31-REST-API-BUGFIX.md](SESSION-REPORT-2026-05-31-REST-API-BUGFIX.md)
> **Conteo verificado desde código**: 33 REST core + 8 REST sync + 26 MCP tools = **41 REST endpoints**

---

## 📋 Convenciones

| Símbolo | Significado |
|---------|-------------|
| ✅ | Probado y pasa |
| ❌ | Probado y falla |
| ⚠️ | Probado con advertencias |
| 🔲 | No probado |
| N/A | No aplica (backend SQLite, etc.) |

---

## 🖥️ Core REST API (33 endpoints)

### General (2)

| # | Endpoint | Método | Status | Última prueba | Notas |
|---|----------|--------|--------|---------------|-------|
| 1 | `/health` | GET | ✅ | 2026-06-04 | Backend postgres, servicio ok |
| 2 | `/stats` | GET | ✅ | 2026-06-04 | 226 sessions, 522 observations, 528 prompts, 18 proyectos |

### Sesiones (5)

| # | Endpoint | Método | Status | Última prueba | Notas |
|---|----------|--------|--------|---------------|-------|
| 3 | `/sessions` | POST | ✅ | 2026-06-04 | Crea session, devuelve `{id, status:"created"}` |
| 4 | `/sessions/{id}` | GET | ⚠️ | 2026-06-01 | Error "session not found" post-end — la session está ended |
| 5 | `/sessions/{id}/end` | POST | ✅ | 2026-06-01 | Finaliza session con summary |
| 6 | `/sessions/recent` | GET | ✅ | 2026-06-01 | Lista sessions recientes con filtros |
| 7 | `/sessions/{id}` | DELETE | ✅ | 2026-06-04 | Soft-deleted obs **no** bloquean delete (200); obs activas → 409 |

### Observaciones (7)

| # | Endpoint | Método | Status | Última prueba | Notas |
|---|----------|--------|--------|---------------|-------|
| 8 | `/observations` | POST | ✅ | 2026-06-04 | Requiere `session_id` + `title` + `content` |
| 9 | `/observations/passive` | POST | ✅ | 2026-06-01 | Devuelve `{extracted:0, saved:0, duplicates:0}` — sin efecto directo |
| 10 | `/observations/recent` | GET | ✅ | 2026-06-01 | Lista observaciones recientes |
| 11 | `/observations/{id}` | GET | ✅ | 2026-06-01 | GET by ID funciona |
| 12 | `/observations/{id}` | PATCH | ✅ | 2026-06-01 | Actualiza título correctamente |
| 13 | `/observations/{id}` | DELETE | ✅ | 2026-06-04 | Soft-delete, devuelve `{id, status:"deleted"}` |
| 14 | `/search` | GET | ✅ | 2026-06-01 | Búsqueda con ranking y FTS |

### Contexto y timeline (2)

| # | Endpoint | Método | Status | Última prueba | Notas |
|---|----------|--------|--------|---------------|-------|
| 15 | `/timeline` | GET | ✅ | 2026-06-01 | Requiere `observation_id` + `before` + `after` |
| 16 | `/context` | GET | ✅ | 2026-06-01 | `session_id` param + limit — devuelve contexto formateado |

### Prompts (5)

| # | Endpoint | Método | Status | Última prueba | Notas |
|---|----------|--------|--------|---------------|-------|
| 17 | `/prompts` | POST | ✅ | 2026-06-04 | Crea prompt, devuelve `{id, status:"saved"}` |
| 18 | `/prompts/recent` | GET | ✅ | 2026-06-04 | Filtra por `project` + **`X-Engram-User`** (user scoping verificado: userA solo ve userA) |
| 19 | `/prompts/search` | GET | ✅ | 2026-06-01 | Búsqueda por query |
| 20 | `/prompts/{id}` | DELETE | ✅ | 2026-06-01 | Soft-delete prompt |
| 21 | `/export` | GET | ⚠️ | 2026-06-01 | POST, no GET — requiere `{"project":"..."}` |

### Proyectos (6)

| # | Endpoint | Método | Status | Última prueba | Notas |
|---|----------|--------|--------|---------------|-------|
| 22 | `/projects/list` | GET | ✅ | 2026-06-04 | Lista 18 proyectos |
| 23 | `/projects/stats` | GET | ✅ | 2026-06-01 | Stats por proyecto con directorios |
| 24 | `/projects/migrate` | POST | ✅ | 2026-06-01 | Params: `old_project` + `new_project` (no `projects[]`) |
| 25 | `/projects/prune` | POST | ✅ | 2026-06-01 | Solo elimina si no hay observaciones |
| 26 | `/projects/migrations` | GET | ✅ | 2026-06-01 | Lista migraciones (vacío) |
| 27 | `/import` | POST | ✅ | 2026-06-01 | Import vacío sin datos |

### Markdown (3)

| # | Endpoint | Método | Status | Última prueba | Notas |
|---|----------|--------|--------|---------------|-------|
| 28 | `/md/promote/{id}` | POST | 🔲 | — | |
| 29 | `/md/sync` | POST | 🔲 | — | |
| 30 | `/md/index` | POST | 🔲 | — | |

### Retention (2)

| # | Endpoint | Método | Status | Última prueba | Notas |
|---|----------|--------|--------|---------------|-------|
| 31 | `/retention/stats` | GET | ✅ | 2026-06-01 | Age buckets: 4 <30d, 434 30-90d, 130 90-180d |
| 32 | `/retention/prune` | POST | ✅ | 2026-06-01 | Dry-run no soportado, hace prune real (47 items) |

### Debug (1)

| # | Endpoint | Método | Status | Última prueba | Notas |
|---|----------|--------|--------|---------------|-------|
| 33 | `/debug-test` | POST | ✅ | 2026-06-01 | Devuelve `{status:"ok"}` |

---

## 🔁 Sync REST API (8 endpoints)

| # | Endpoint | Método | Status | Última prueba | Notas |
|---|----------|--------|--------|---------------|-------|
| 34 | `/sync/enroll` | POST | ✅ | 2026-06-01 | Headers `X-Engram-User` + body `{"project":"..."}` |
| 35 | `/sync/enroll` | GET | ⚠️ | 2026-06-01 | Devuelve `{"projects":[],"count":0}` — no muestra enrolls activos |
| 36 | `/sync/enroll` | DELETE | ✅ | 2026-06-01 | Query param `?project=...` requerido |
| 37 | `/sync/pause` | POST | ✅ | 2026-06-01 | Query param `?project=...` + body `{"reason":"..."}` |
| 38 | `/sync/pause` | DELETE | ✅ | 2026-06-01 | Resume — query param `?project=...` |
| 39 | ⭐ `/sync/status` | GET | ✅ | 2026-06-04 | `sync_enabled: true`, `phase: cloud` |
| 40 | `/sync/mutations/push` | POST | ✅ | 2026-06-04 | `entries` null/ausente → **400** `empty-batch` (fix `a0ff6ee`) |
| 41 | `/sync/mutations/pull` | GET | ✅ | 2026-06-01 | Params `project` + `since` — devuelve `{mutations:[], has_more:false}` |

---

## 🛠️ MCP Tools (26 tools)

> **Nota**: MCP tools requieren cliente MCP (ModelContextProtocol). No accesible vía curl REST directo.
> Para probar, usar un cliente MCP como Cursor/VS Code/Antigravity con el servidor configurado.
> Ver `docs/MCP-TEST-CASES.md` para casos de prueba.

| # | Tool | Status | Notas |
|---|------|--------|-------|
| 1 | `mem_save` | 🔲 | Requiere cliente MCP |
| 2 | `mem_search` | 🔲 | Requiere cliente MCP |
| 3 | `mem_get_observation` | 🔲 | Requiere cliente MCP |
| 4 | `mem_update` | 🔲 | Requiere cliente MCP |
| 5 | `mem_delete` | 🔲 | Requiere cliente MCP |
| 6 | `mem_context` | 🔲 | Requiere cliente MCP |
| 7 | `mem_session_start` / `mem_session_end` | 🔲 | Requiere cliente MCP |
| 8 | `mem_stats` | 🔲 | Requiere cliente MCP |
| 9 | `mem_timeline` | 🔲 | Requiere cliente MCP |
| 10 | `mem_save_prompt` | 🔲 | Requiere cliente MCP |
| 11 | `mem_doctor` | 🔲 | Requiere cliente MCP |
| 12 | `mem_suggest_topic_key` | 🔲 | Requiere cliente MCP |
| 13 | `mem_capture_passive` | 🔲 | Requiere cliente MCP |
| 14 | `mem_retention_stats` | 🔲 | Requiere cliente MCP |
| 15 | `mem_retention_prune` | 🔲 | Requiere cliente MCP |
| 16 | `mem_merge_projects` | 🔲 | Requiere cliente MCP |
| 17 | `mem_verify_artifact` | 🔲 | Requiere cliente MCP |
| 18 | `mem_traceability` | 🔲 | Requiere cliente MCP |
| 19 | `mem_promote_to_md` | 🔲 | Requiere cliente MCP |
| 20 | `mem_sync_md_to_repo` | 🔲 | Requiere cliente MCP |
| 21 | `mem_trace_source` | 🔲 | Requiere cliente MCP |
| 22 | `mem_lineage` | 🔲 | Requiere cliente MCP |
| 23 | `mem_project_redirects` | 🔲 | Requiere cliente MCP |
| 24 | `mem_current_project` | 🔲 | Requiere cliente MCP |
| 25 | `mem_session_summary` | 🔲 | Requiere cliente MCP |
| 26 | `mem_session_start` (duplicate?) | 🔲 | Requiere cliente MCP — verificar duplicado |
| 27 | `mem_relations` | 🔲 | Implementado en ENG-404, pendiente test manual |
| 28 | `mem_lineage_obs` | 🔲 | Implementado en ENG-404, pendiente test manual |

---

## 👥 Tests multi-usuario (requiere 2 developers)

| # | Test | Status | Notas |
|---|------|--------|-------|
| T1 | Pull entre 2 clientes | 🔲 | Dev1 push → Dev2 pull |
| T2 | Offline + reconexión | 🔲 | Crear offline, reconectar, verificar sync |
| T3 | Aislamiento multi-usuario | 🔲 | `personal:{user}` scope |

---

## ✅ Resumen de resultados

### Core REST API
| Grupo | Total | ✅ | ⚠️ | ❌ | 🔲 |
|-------|-------|---|---|---|---|
| General | 2 | 2 | 0 | 0 | 0 |
| Sesiones | 5 | 4 | 1 | 0 | 0 |
| Observaciones | 7 | 7 | 0 | 0 | 0 |
| Contexto/Timeline | 2 | 2 | 0 | 0 | 0 |
| Prompts | 5 | 4 | 1 | 0 | 0 |
| Proyectos | 6 | 6 | 0 | 0 | 0 |
| Markdown | 3 | 0 | 0 | 0 | 3 |
| Retention | 2 | 2 | 0 | 0 | 0 |
| Debug | 1 | 1 | 0 | 0 | 0 |
| **Subtotal** | **33** | **28** | **2** | **0** | **3** |

### Sync REST API
| Grupo | Total | ✅ | ⚠️ | ❌ | 🔲 |
|-------|-------|---|---|---|---|
| Sync | 8 | 6 | 1 | 0 | 1 |
| **Subtotal** | **8** | **6** | **1** | **0** | **1** |

### MCP Tools
| Grupo | Total | ✅ | ⚠️ | ❌ | 🔲 |
|-------|-------|---|---|---|---|
| MCP | 26 | 0 | 0 | 0 | 26 |

### Multi-usuario
| Grupo | Total | ✅ | ⚠️ | ❌ | 🔲 |
|-------|-------|---|---|---|---|
| Multi-usuario | 3 | 0 | 0 | 0 | 3 |

**Total general: 70 endpoints — 34 probados, 36 pendientes**

---

## 🔄 Regression tests — bugfixes críticos (2026-06-01)

> Ejecutar **después de cada deploy** en TrueNAS. Requiere commit **`e1a9cf9`** o posterior.
> Servidor: `http://192.168.0.178:7437`

```bash
BASE="http://192.168.0.178:7437"
TS=$(date +%s)
PROJECT="team/manual-verify-$TS"
SESSION="sess-verify-$TS"

# ── Bug #1: push sin entries → 400 (no 500) ─────────────────────────────
curl -s -w "\nHTTP %{http_code}\n" -X POST "$BASE/sync/mutations/push" \
  -H "Content-Type: application/json" -d '{"created_by":"test"}'
# Esperado: HTTP 400, error_code "validation_error"

curl -s -w "\nHTTP %{http_code}\n" -X POST "$BASE/sync/mutations/push" \
  -H "Content-Type: application/json" -d '{"entries":null,"created_by":"test"}'
# Esperado: HTTP 400, error_code "validation_error"

# ── Bug #2: delete session con obs soft-deleted → 200 ───────────────────
curl -s -X POST "$BASE/sessions" -H "Content-Type: application/json" \
  -d "{\"id\":\"$SESSION\",\"project\":\"$PROJECT\",\"directory\":\"/tmp\"}"

OBS=$(curl -s -X POST "$BASE/observations" -H "Content-Type: application/json" \
  -d "{\"session_id\":\"$SESSION\",\"title\":\"test\",\"content\":\"x\",\"type\":\"manual\",\"project\":\"$PROJECT\"}")
OBS_ID=$(echo "$OBS" | grep -o '"id":[0-9]*' | head -1 | cut -d: -f2)

curl -s -X DELETE "$BASE/observations/$OBS_ID"   # soft-delete
curl -s -w "\nHTTP %{http_code}\n" -X DELETE "$BASE/sessions/$SESSION"
# Esperado: HTTP 200, {"status":"deleted"}

# Control: obs activa → 409
SESSION2="sess-active-$TS"
curl -s -X POST "$BASE/sessions" -H "Content-Type: application/json" \
  -d "{\"id\":\"$SESSION2\",\"project\":\"$PROJECT\",\"directory\":\"/tmp\"}"
curl -s -X POST "$BASE/observations" -H "Content-Type: application/json" \
  -d "{\"session_id\":\"$SESSION2\",\"title\":\"active\",\"content\":\"x\",\"type\":\"manual\",\"project\":\"$PROJECT\"}"
curl -s -w "\nHTTP %{http_code}\n" -X DELETE "$BASE/sessions/$SESSION2"
# Esperado: HTTP 409

# ── Bug #3: prompts/recent respeta X-Engram-User ─────────────────────────
PSESS="sess-prompts-$TS"
curl -s -X POST "$BASE/sessions" -H "Content-Type: application/json" \
  -d "{\"id\":\"$PSESS\",\"project\":\"$PROJECT\",\"directory\":\"/tmp\"}"
curl -s -X POST "$BASE/prompts" -H "Content-Type: application/json" -H "X-Engram-User: userA" \
  -d "{\"session_id\":\"$PSESS\",\"content\":\"Prompt userA $TS\",\"project\":\"$PROJECT\"}"
curl -s -X POST "$BASE/prompts" -H "Content-Type: application/json" -H "X-Engram-User: userB" \
  -d "{\"session_id\":\"$PSESS\",\"content\":\"Prompt userB $TS\",\"project\":\"$PROJECT\"}"

curl -s "$BASE/prompts/recent?project=$PROJECT&limit=50" -H "X-Engram-User: userA" | jq .
# Esperado: solo prompt de userA

curl -s "$BASE/prompts/recent?project=$PROJECT&limit=50" -H "X-Engram-User: userB" | jq .
# Esperado: solo prompt de userB
```

| # | Bug | Esperado | Verificado 2026-06-01 | Verificado 2026-06-04 |
|---|-----|----------|------------------------|------------------------|
| R1 | Push sin `entries` | HTTP 400 | ✅ | ✅ |
| R2 | Push `entries: null` | HTTP 400 | ✅ | ✅ |
| R3 | Delete session (solo soft-deleted) | HTTP 200 | ✅ | ✅ |
| R4 | Delete session (obs activa) | HTTP 409 | ✅ | ✅ |
| R5 | `/prompts/recent` + `X-Engram-User` | Solo prompts del usuario | ✅ | ✅ |

---

## ENG-208 — Phase 2 API Parity (2026-06-10)

> Feature: **ENG-208** — Upstream Phase 2 API parity
> Commit: `e7e5736` (pendiente de deploy)
> Servidor: `http://localhost:7437` (local dev)

| PM | Case | Steps | Expected | [x] |
|----|------|-------|----------|------|
| PM-1 | DELETE /sessions/empty | `curl -X DELETE http://localhost:7437/sessions/{empty-id}` | 200, `{ "id": "...", "status": "deleted" }` | [ ] |
| PM-2 | DELETE /sessions/with-obs | `curl -X DELETE http://localhost:7437/sessions/{id-with-obs}` | 409, observation count in error | [ ] |
| PM-3 | DELETE /prompts/existing | `curl -X DELETE http://localhost:7437/prompts/{existing-id}` | 200, `{ "id": N, "status": "deleted" }` | [ ] |
| PM-4 | DELETE /prompts/nonexistent | `curl -X DELETE http://localhost:7437/prompts/999999` | 404 | [ ] |
| PM-5 | All 19 MCP tool error paths | Call each tool in error conditions via MCP | All return structured JSON `{ "error": true, "error_code": "...", "message": "..." }` | [ ] |
| PM-6 | mem_current_project normal | Call `mem_current_project` from inside git repo | Returns project name, `project_source`, no error | [ ] |
| PM-7 | mem_current_project ambiguous | Call `mem_current_project` from dir with multiple git repos | Returns `IsError = false`, `project = ""`, `available_projects` populated | [ ] |
| PM-8 | Watch mode initial + tick | `engram obsidian-export --watch --interval 2s --vault /tmp/v` | Initial export runs immediately, second after ~2s | [ ] |
| PM-9 | --since 30d filter | `engram obsidian-export --since 30d --vault /tmp/v` | Only observations from last 30 days in vault | [ ] |
| PM-10 | --project X export | `engram obsidian-export --project X --vault /tmp/v` | Only project X's data in vault | [ ] |
| PM-11 | GET /export?project=X | `curl "http://localhost:7437/export?project=X"` | Only project X's data in JSON | [ ] |
| PM-12 | GET /export/since | `curl "http://localhost:7437/export/since?project=X&after_seq=0"` | JSON with `observations`, `prompts`, `sessions`, `next_seq`, `has_more` | [ ] |
| PM-13 | Watch continues after error | Kill store server while `--watch` is running | Cycle logs error, next cycle retries | [ ] |
| PM-14 | Watch graceful shutdown | Start `--watch`, press Ctrl+C | Exit code 0, state file persisted | [ ] |

### curl snippets (ENG-208)

```bash
BASE="http://localhost:7437"

# PM-1: Delete empty session
SESSION="sess-empty-$RANDOM"
curl -s -X POST "$BASE/sessions" -H "Content-Type: application/json" \
  -d "{\"id\":\"$SESSION\",\"project\":\"team/test\",\"directory\":\"/tmp\"}"
curl -s -w "\nHTTP %{http_code}\n" -X DELETE "$BASE/sessions/$SESSION"

# PM-2: Delete session with active observations
SESSION2="sess-with-obs-$RANDOM"
curl -s -X POST "$BASE/sessions" -H "Content-Type: application/json" \
  -d "{\"id\":\"$SESSION2\",\"project\":\"team/test\",\"directory\":\"/tmp\"}"
curl -s -X POST "$BASE/observations" -H "Content-Type: application/json" \
  -d "{\"session_id\":\"$SESSION2\",\"title\":\"test\",\"content\":\"x\",\"type\":\"manual\",\"project\":\"team/test\"}"
curl -s -w "\nHTTP %{http_code}\n" -X DELETE "$BASE/sessions/$SESSION2"

# PM-3: Delete existing prompt
curl -s -X DELETE "$BASE/prompts/1"

# PM-4: Delete nonexistent prompt
curl -s -w "\nHTTP %{http_code}\n" -X DELETE "$BASE/prompts/999999"

# PM-11: Export with project filter
curl -s "$BASE/export?project=team/test" | jq 'length'

# PM-12: Export since with cursor
curl -s "$BASE/export/since?project=team/test&after_seq=0&limit=10" | jq '{next_seq, has_more, observations}'
```

---

## 🪵 Logging regression (2026-06-05)

> Feature: **logging-infrastructure** (ENG-207) — structured JSON logging, POST body preview, `ENGRAM_LOG_LEVEL`, global exception handler.  
> Verificado por orquestador + agente forge-memory contra `http://localhost:7437` (local dev).  
> **Commit**: `99b3ca9` (no deployado — código local).

| PM | Case | Status | Notes |
|----|------|--------|-------|
| PM-1 | GET /health → JSON log with all fields | ✅ | JSON: `Timestamp`, `LogLevel`, `Method`, `Path`, `Status`, `Duration`, `ClientIp` |
| PM-2 | GET /foo (404) → log status 404 | ✅ | Warning level, status=404, no `error` object |
| PM-3 | POST malformed JSON → body preview | ✅ | `body preview:` field in log at Warning level |
| PM-4 | Endpoint throws 5xx → JSON error + stack trace | ⚠️ | **Deferred** — no endpoint always-throws available. Covered by unit test `Endpoint_Throwing_Returns500Json` |
| PM-5 | ENGRAM_LOG_LEVEL=warn suppresses info | ✅ | Info suppressed, Warning/Error logged |
| PM-6 | ClientIp field present | ✅ | `127.0.0.1` in `State.ClientIp` |
| PM-7 | CLI Console.WriteLine preserved | ✅ | 100 `Console.WriteLine` in CLI (untouched) |

### curl snippets (reproducible)

Ejecutar contra servidor local en `http://localhost:7437` con logs visibles en stdout:

```bash
# PM-1: Health check
curl -s http://localhost:7437/health
# Log esperado: JSON con @timestamp, level, method=GET, path=/health, status=200, duration_ms, client_ip

# PM-2: 404
curl -s http://localhost:7437/will-not-exist
# Log esperado: Warning level, status=404, no error object

# PM-3: Malformed JSON
curl -s -X POST http://localhost:7437/sessions \
  -H "Content-Type: application/json" \
  -d '{"id":"test-pm3",invalid'
# Esperado: HTTP 400. Log contiene "body preview: {"id":"test-pm3",invalid"

# PM-5: Log level suppression
ENGRAM_LOG_LEVEL=warn ./engram serve --port 7438 &
curl -s http://localhost:7438/health
# Esperado: 200 pero NO log line (info suppressed at warn level)
kill %1 2>/dev/null

# PM-6: Client IP
curl -s http://localhost:7437/health
# Log debe contener client_ip (127.0.0.1 para localhost)

# PM-7: CLI regression
grep -c 'Console.WriteLine' src/Engram.Cli/Program.cs
# Esperado: ~100 (user-facing output, no ILogger)
```

---

## 🐛 Bugs abiertos

1. **`/sessions/{id}` GET**: Devuelve "session not found" para sessions ended vía `/end`. Debería devolver la session con `ended_at` en lugar de 404.

2. **`/sync/enroll` GET**: No muestra proyectos enrollados — devuelve `{"projects":[],"count":0}`.

3. **`/prompts/recent` — campo `created_by` en JSON**: El filtrado por `X-Engram-User` funciona, pero la respuesta puede mostrar `"created_by": ""`. Revisar serialización del modelo `Prompt`.

---

## ✅ Bugs resueltos (2026-06-01)

| Bug | Endpoint | Fix | Commit |
|-----|----------|-----|--------|
| NRE en push null entries | `POST /sync/mutations/push` | Null-check en `body.Entries` | `a0ff6ee` |
| Soft-deleted obs bloquean delete | `DELETE /sessions/{id}` | `AND deleted_at IS NULL` en COUNT | `a0ff6ee` |
| User scoping en prompts | `GET /prompts/recent` | `GetUserId` + columna `created_by` | `a0ff6ee` + migración `e1a9cf9` |
| Migración PG `created_by` | Startup PostgresStore | Índice solo tras `ALTER TABLE` | `e1a9cf9` |

---

## 📝 Notas de testing

- Los endpoints de Markdown (`/md/promote`, `/md/sync`, `/md/index`) requieren datos específicos (observation ID válido, repo configurado) — no probados.
- MCP tools requieren cliente MCP real — no accesible vía REST. Necesitan test con editor configurado.
- Tests multi-usuario requieren segunda persona o segundo user token.
- **Deploy TrueNAS**: confirmar `git log -1` en el servidor antes de debuggear Docker; el clone en `/mnt/Pool_8TB/engram_data` estuvo atrasado en `5ae578d` mientras `main` ya tenía `e1a9cf9`.
- **Build**: `docker/docker-compose.yml` usa `Dockerfile` de la raíz (compila fuente). No usar `docker/Dockerfile` (binario de Releases) para probar fixes locales.
- **PostgreSQL producción**: database `engram_cloud` (ver `.env` en TrueNAS).

---

## ✅ Cómo usar este checklist

1. Después del deploy, corré los endpoints vía curl
2. Marcá ✅ si pasa, ❌ si falla, ⚠️ si hay algo raro
3. Anotá la fecha en "Última prueba"
4. Cuando un grupo completo esté probado, actualizá el ROADMAP
5. Bugs encontrados → documentar en `sdd/postgres-bug-fixes/` si requieren fix

### Smoke test rápido post-deploy

```bash
BASE="http://192.168.0.178:7437"

# 1. Health + backend
curl -s "$BASE/health" | jq .

# 2. Proyectos
curl -s "$BASE/projects/list" | jq 'length'
curl -s "$BASE/projects/stats" | jq '.[0]'

# 3. Sync status
curl -s "$BASE/sync/status" | jq '{sync_enabled, phase, health}'

# 4. Stats globales
curl -s "$BASE/stats" | jq .

# 5. Regression mínima — push inválido debe dar 400, no 500
curl -s -w "\nHTTP %{http_code}\n" -X POST "$BASE/sync/mutations/push" \
  -H "Content-Type: application/json" -d '{"created_by":"smoke"}'

# 6. (Opcional) Enroll + observación
curl -X POST "$BASE/sync/enroll" -H "X-Engram-User: test" \
  -H "Content-Type: application/json" -d '{"project":"team/smoke-test"}'
```

Después del smoke, correr la sección [Regression tests — bugfixes críticos](#-regression-tests--bugfixes-críticos-2026-06-01).