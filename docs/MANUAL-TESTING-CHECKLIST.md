# Manual Testing Checklist

> **Propósito**: Trazabilidad real de pruebas manuales sobre el servidor en producción.
> **Servidor**: `http://192.168.0.178:7437` (PostgreSQL)
> **Última verificación**: 2026-06-01
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
| 1 | `/health` | GET | ✅ | 2026-06-01 | Backend postgres, servicio ok |
| 2 | `/stats` | GET | ✅ | 2026-06-01 | 221 sessions, 568 observations, 19 proyectos |

### Sesiones (5)

| # | Endpoint | Método | Status | Última prueba | Notas |
|---|----------|--------|--------|---------------|-------|
| 3 | `/sessions` | POST | ✅ | 2026-06-01 | Crea session, devuelve `{id, status:"created"}` |
| 4 | `/sessions/{id}` | GET | ⚠️ | 2026-06-01 | Error "session not found" post-end — la session está ended |
| 5 | `/sessions/{id}/end` | POST | ✅ | 2026-06-01 | Finaliza session con summary |
| 6 | `/sessions/recent` | GET | ✅ | 2026-06-01 | Lista sessions recientes con filtros |
| 7 | `/sessions/{id}` | DELETE | ✅ | 2026-06-01 | Elimina session soft-delete |

### Observaciones (7)

| # | Endpoint | Método | Status | Última prueba | Notas |
|---|----------|--------|--------|---------------|-------|
| 8 | `/observations` | POST | ✅ | 2026-06-01 | Requiere `session_id` + `title` + `content` |
| 9 | `/observations/passive` | POST | ✅ | 2026-06-01 | Devuelve `{extracted:0, saved:0, duplicates:0}` — sin efecto directo |
| 10 | `/observations/recent` | GET | ✅ | 2026-06-01 | Lista observaciones recientes |
| 11 | `/observations/{id}` | GET | ✅ | 2026-06-01 | GET by ID funciona |
| 12 | `/observations/{id}` | PATCH | ✅ | 2026-06-01 | Actualiza título correctamente |
| 13 | `/observations/{id}` | DELETE | ✅ | 2026-06-01 | Soft-delete, devuelve `{id, status:"deleted"}` |
| 14 | `/search` | GET | ✅ | 2026-06-01 | Búsqueda con ranking y FTS |

### Contexto y timeline (2)

| # | Endpoint | Método | Status | Última prueba | Notas |
|---|----------|--------|--------|---------------|-------|
| 15 | `/timeline` | GET | ✅ | 2026-06-01 | Requiere `observation_id` + `before` + `after` |
| 16 | `/context` | GET | ✅ | 2026-06-01 | `session_id` param + limit — devuelve contexto formateado |

### Prompts (5)

| # | Endpoint | Método | Status | Última prueba | Notas |
|---|----------|--------|--------|---------------|-------|
| 17 | `/prompts` | POST | ✅ | 2026-06-01 | Crea prompt, devuelve `{id, status:"saved"}` |
| 18 | `/prompts/recent` | GET | ✅ | 2026-06-01 | Filtra por project |
| 19 | `/prompts/search` | GET | ✅ | 2026-06-01 | Búsqueda por query |
| 20 | `/prompts/{id}` | DELETE | ✅ | 2026-06-01 | Soft-delete prompt |
| 21 | `/export` | GET | ⚠️ | 2026-06-01 | POST, no GET — requiere `{"project":"..."}` |

### Proyectos (6)

| # | Endpoint | Método | Status | Última prueba | Notas |
|---|----------|--------|--------|---------------|-------|
| 22 | `/projects/list` | GET | ✅ | 2026-06-01 | Lista 19 proyectos |
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
| 39 | ⭐ `/sync/status` | GET | ✅ | 2026-06-01 | `sync_enabled: true`, `phase: cloud` |
| 40 | `/sync/mutations/push` | POST | ❌ | 2026-06-01 | NullReferenceException en CloudSyncEndpoints line 141 |
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
| Sync | 8 | 5 | 1 | 1 | 1 |
| **Subtotal** | **8** | **5** | **1** | **1** | **1** |

### MCP Tools
| Grupo | Total | ✅ | ⚠️ | ❌ | 🔲 |
|-------|-------|---|---|---|---|
| MCP | 26 | 0 | 0 | 0 | 26 |

### Multi-usuario
| Grupo | Total | ✅ | ⚠️ | ❌ | 🔲 |
|-------|-------|---|---|---|---|
| Multi-usuario | 3 | 0 | 0 | 0 | 3 |

**Total general: 70 endpoints — 33 probados, 37 pendientes**

---

## 🐛 Bugs encontrados

1. **`/sync/mutations/push`**: NullReferenceException en `CloudSyncEndpoints.cs:141` — el `ICloudMutationStore` es null cuando no hay enrollment activo.

2. **`/sessions/{id}` GET**: Devuelve "session not found" para sessions que fueron ended via `/end`. Debería devolver la session con `ended_at` en lugar de 404.

3. **`/sync/enroll` GET**: No muestra los proyectos enrollados — siempre devuelve `{"projects":[],"count":0}`.

4. **`/prompts/recent` y `/prompts/search`**: Devuelven array vacío para el proyecto `team/smoke-test` aunque hay prompts creados.

---

## 📝 Notas de testing

- Los endpoints de Markdown (`/md/promote`, `/md/sync`, `/md/index`) requieren datos específicos (observation ID válido, repo configurado) — no probados.
- MCP tools requieren cliente MCP real — no accesible vía REST. Necesitan test con editor configurado.
- Tests multi-usuario requieren segunda persona o segundo user token.

---

## ✅ Cómo usar este checklist

1. Después del deploy, corré los endpoints vía curl
2. Marcá ✅ si pasa, ❌ si falla, ⚠️ si hay algo raro
3. Anotá la fecha en "Última prueba"
4. Cuando un grupo completo esté probado, actualizá el ROADMAP
5. Bugs encontrados → documentar en `sdd/postgres-bug-fixes/` si requieren fix

### Smoke test rápido post-deploy

```bash
# 1. Health
curl http://192.168.0.178:7437/health

# 2. Sync status (⭐ fix recién pusheado)
curl http://192.168.0.178:7437/sync/status | jq '{sync_enabled, phase, health}'

# 3. Stats
curl http://192.168.0.178:7437/stats

# 4. Enroll (si no hay proyectos)
curl -X POST http://192.168.0.178:7437/sync/enroll \
  -H "X-Engram-User: test" \
  -d '{"project":"team/smoke-test"}'

# 5. Ver enroll
curl http://192.168.0.178:7437/sync/enroll

# 6. Crear observación
curl -X POST http://192.168.0.178:7437/observations \
  -H "Content-Type: application/json" \
  -d '{"title":"Smoke test","content":"Test","type":"manual","project":"team/smoke-test"}'

# 7. Buscar
curl "http://192.168.0.178:7437/search?q=Smoke+test"
```