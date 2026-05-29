# Manual Testing Checklist

> **Propósito**: Trazabilidad real de pruebas manuales sobre el servidor en producción.  
> **Servidor**: `http://192.168.0.178:7437` (PostgreSQL)  
> **Última verificación**: 2026-05-28  
> **Conteo verificado desde código**: 33 REST core + 8 REST sync + 26 MCP tools = **41 REST endpoints**

---

## 📋 Convenciones

| Símbolo | Significado |
|---------|-------------|
| 🔲 | No probado |
| ✅ | Probado y pasa |
| ❌ | Probado y falla |
| ⚠️ | Probado con advertencias |
| N/A | No aplica (backend SQLite, etc.) |

---

## 🖥️ Core REST API (33 endpoints)

### General (2)

| # | Endpoint | Método | Status | Última prueba | Notas |
|---|----------|--------|--------|---------------|-------|
| 1 | `/health` | GET | ✅ | — | Backend postgres |
| 2 | `/stats` | GET | 🔲 | — | |

### Sesiones (5)

| # | Endpoint | Método | Status | Última prueba | Notas |
|---|----------|--------|--------|---------------|-------|
| 3 | `/sessions` | POST | 🔲 | — | |
| 4 | `/sessions/{id}` | GET | 🔲 | — | |
| 5 | `/sessions/{id}/end` | POST | 🔲 | — | |
| 6 | `/sessions/recent` | GET | 🔲 | — | |
| 7 | `/sessions/{id}` | DELETE | 🔲 | — | |

### Observaciones (7)

| # | Endpoint | Método | Status | Última prueba | Notas |
|---|----------|--------|--------|---------------|-------|
| 8 | `/observations` | POST | 🔲 | — | |
| 9 | `/observations/passive` | POST | 🔲 | — | |
| 10 | `/observations/recent` | GET | 🔲 | — | |
| 11 | `/observations/{id}` | GET | 🔲 | — | |
| 12 | `/observations/{id}` | PATCH | 🔲 | — | |
| 13 | `/observations/{id}` | DELETE | 🔲 | — | |
| 14 | `/search` | GET | 🔲 | — | |

### Contexto y timeline (2)

| # | Endpoint | Método | Status | Última prueba | Notas |
|---|----------|--------|--------|---------------|-------|
| 15 | `/timeline` | GET | 🔲 | — | |
| 16 | `/context` | GET | 🔲 | — | |

### Prompts (5)

| # | Endpoint | Método | Status | Última prueba | Notas |
|---|----------|--------|--------|---------------|-------|
| 17 | `/prompts` | POST | 🔲 | — | |
| 18 | `/prompts/recent` | GET | 🔲 | — | |
| 19 | `/prompts/search` | GET | 🔲 | — | |
| 20 | `/prompts/{id}` | DELETE | 🔲 | — | |
| 21 | `/export` | GET | 🔲 | — | |

### Proyectos (6)

| # | Endpoint | Método | Status | Última prueba | Notas |
|---|----------|--------|--------|---------------|-------|
| 22 | `/projects/list` | GET | 🔲 | — | |
| 23 | `/projects/stats` | GET | 🔲 | — | |
| 24 | `/projects/migrate` | POST | 🔲 | — | Merge projects |
| 25 | `/projects/prune` | POST | 🔲 | — | Delete project |
| 26 | `/projects/migrations` | GET | 🔲 | — | |
| 27 | `/import` | POST | 🔲 | — | |

### Markdown (3)

| # | Endpoint | Método | Status | Última prueba | Notas |
|---|----------|--------|--------|---------------|-------|
| 28 | `/md/promote/{id}` | POST | 🔲 | — | |
| 29 | `/md/sync` | POST | 🔲 | — | |
| 30 | `/md/index` | POST | 🔲 | — | |

### Retention (2)

| # | Endpoint | Método | Status | Última prueba | Notas |
|---|----------|--------|--------|---------------|-------|
| 31 | `/retention/stats` | GET | 🔲 | — | |
| 32 | `/retention/prune` | POST | 🔲 | — | |

### Debug (1)

| # | Endpoint | Método | Status | Última prueba | Notas |
|---|----------|--------|--------|---------------|-------|
| 33 | `/debug-test` | POST | 🔲 | — | |

---

## 🔁 Sync REST API (8 endpoints)

| # | Endpoint | Método | Status | Última prueba | Notas |
|---|----------|--------|--------|---------------|-------|
| 34 | `/sync/enroll` | POST | 🔲 | — | ⭐ Probar primero |
| 35 | `/sync/enroll` | GET | 🔲 | — | |
| 36 | `/sync/enroll` | DELETE | 🔲 | — | |
| 37 | `/sync/pause` | POST | 🔲 | — | |
| 38 | `/sync/pause` | DELETE | 🔲 | — | |
| 39 | ⭐ `/sync/status` | GET | 🔲 | — | **Fix del último push** — debe devolver `phase: cloud`, `sync_enabled: true` |
| 40 | `/sync/mutations/push` | POST | 🔲 | — | |
| 41 | `/sync/mutations/pull` | GET | 🔲 | — | |

---

## 🛠️ MCP Tools (26 tools)

Ver `docs/MCP-TEST-CASES.md` para casos detallados vía curl.

| # | Tool | Status | Notas |
|---|------|--------|-------|
| 1 | `mem_save` | 🔲 | |
| 2 | `mem_search` | 🔲 | |
| 3 | `mem_get_observation` | 🔲 | |
| 4 | `mem_update` | 🔲 | |
| 5 | `mem_delete` | 🔲 | |
| 6 | `mem_context` | 🔲 | |
| 7 | `mem_session_start` / `mem_session_end` | 🔲 | |
| 8 | `mem_stats` | 🔲 | |
| 9 | `mem_timeline` | 🔲 | |
| 10 | `mem_save_prompt` | 🔲 | |
| 11 | `mem_doctor` | 🔲 | |
| 12 | `mem_suggest_topic_key` | 🔲 | |
| 13 | `mem_capture_passive` | 🔲 | |
| 14 | `mem_retention_stats` | 🔲 | |
| 15 | `mem_retention_prune` | 🔲 | |
| 16 | `mem_merge_projects` | 🔲 | |
| 17 | `mem_verify_artifact` | 🔲 | |
| 18 | `mem_traceability` | 🔲 | |
| 19 | `mem_promote_to_md` | 🔲 | |
| 20 | `mem_sync_md_to_repo` | 🔲 | |
| 21 | `mem_trace_source` | 🔲 | |
| 22 | `mem_lineage` | 🔲 | |
| 23 | `mem_project_redirects` | 🔲 | |
| 24 | `mem_current_project` | 🔲 | |
| 25 | `mem_session_summary` | 🔲 | |
| 26 | `mem_session_start` (duplicate?) | 🔲 | Verificar |

---

## 👥 Tests multi-usuario (requiere 2 developers)

| # | Test | Status | Notas |
|---|------|--------|-------|
| T1 | Pull entre 2 clientes | 🔲 | Dev1 push → Dev2 pull |
| T2 | Offline + reconexión | 🔲 | Crear offline, reconectar, verificar sync |
| T3 | Aislamiento multi-usuario | 🔲 | `personal:{user}` scope |

---

## ✅ Cómo usar este checklist

1. Después del deploy, corré los endpoints vía curl
2. Marcá ✅ si pasa, ❌ si falla, ⚠️ si hay algo raro
3. Anotá la fecha en "Última prueba"
4. Cuando un grupo completo esté probado, actualizá el ROADMAP

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
