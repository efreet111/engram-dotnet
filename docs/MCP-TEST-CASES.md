# MCP Tools — Casos de Prueba

> **Propósito**: Verificar que las 24 herramientas MCP funcionan correctamente.  
> **Server**: `http://192.168.0.178:7437` (PostgreSQL)  
> **Requiere**: `engram mcp` corriendo localmente O curl directo a la API REST.

---

## 📋 Pre-requisitos

```bash
# 1. Server debe estar corriendo
curl http://192.168.0.178:7437/health
# → {"status":"ok","backend":"postgres"}

# 2. Tener un proyecto enrolled
curl -X POST http://192.168.0.178:7437/sync/enroll \
  -H "X-Engram-User: victor.silgado" \
  -d '{"project":"team/mcp-test"}'

# 3. Crear sesión de prueba
curl -X POST http://192.168.0.178:7437/sessions \
  -H "Content-Type: application/json" \
  -d '{"id":"mcp-test-session","project":"team/mcp-test","directory":"/tmp"}'
```

---

## 🧪 CASO 1: `mem_save` — Guardar memoria

**Endpoint**: POST /observations

```bash
curl -X POST http://192.168.0.178:7437/observations \
  -H "Content-Type: application/json" \
  -H "X-Engram-User: victor.silgado" \
  -d '{
    "session_id":"mcp-test-session",
    "title":"Prueba MCP - Decisión técnica",
    "content":"**What**: Decisión de prueba\n**Why**: Verificar mem_save\n**Where**: tests/\n**Learned**: Funciona",
    "type":"decision",
    "project":"team/mcp-test",
    "topic_key":"testing/mcp-save"
  }'
```

**Expected**: 200 + `{"id":<number>,"status":"created"}`

**Criterio**: ✅ Memoria creada y visible vía search

---

## 🧪 CASO 2: `mem_search` — Buscar memorias

**Endpoint**: GET /search

```bash
# Búsqueda simple
curl "http://192.168.0.178:7437/search?q=Prueba+MCP&limit=5" \
  -H "X-Engram-User: victor.silgado" | jq '.[] | {id: .observation.id, title: .observation.title, rank: .rank}'

# Búsqueda por proyecto
curl "http://192.168.0.178:7437/search?q=Prueba+MCP&project=team/mcp-test" | jq '. | length'

# Búsqueda por tipo
curl "http://192.168.0.178:7437/search?q=decisión&type=decision" | jq '. | length'
```

**Expected**: Resultados con `observation.id`, `observation.title`, `rank` > 0

---

## 🧪 CASO 3: `mem_get_observation` — Ver observación

**Endpoint**: GET /observations/{id}

```bash
# Usar el ID del CASO 1
curl http://192.168.0.178:7437/observations/ID_DEL_CASO1 | jq
```

**Expected**: JSON completo con `title`, `content`, `type`, `project`, etc.

---

## 🧪 CASO 4: `mem_update` — Actualizar memoria

**Endpoint**: PATCH /observations/{id}

```bash
curl -X PATCH http://192.168.0.178:7437/observations/ID_DEL_CASO1 \
  -H "Content-Type: application/json" \
  -d '{"title":"Prueba MCP - ACTUALIZADO","content":"**What**: Versión actualizada"}'
```

**Expected**: 200 + `{"status":"updated"}`

**Verificar**:
```bash
curl http://192.168.0.178:7437/observations/ID_DEL_CASO1 | jq '.title'
# → "Prueba MCP - ACTUALIZADO"
```

---

## 🧪 CASO 5: `mem_delete` — Eliminar memoria

**Endpoint**: DELETE /observations/{id}

```bash
# Crear memoria temporal
curl -X POST http://192.168.0.178:7437/observations \
  -H "Content-Type: application/json" \
  -d '{"session_id":"mcp-test-session","title":"Temp","content":"Para borrar","type":"manual","project":"team/mcp-test"}'

# Eliminar (soft-delete)
curl -X DELETE http://192.168.0.178:7437/observations/ID_A_BORRAR
```

**Expected**: 200 + `{"status":"deleted"}`

**Verificar**:
```bash
curl http://192.168.0.178:7437/observations/ID_A_BORRAR | jq '.deleted_at'
# → Fecha (no null)
```

---

## 🧪 CASO 6: `mem_context` — Contexto de sesión

**Endpoint**: GET /context

```bash
curl "http://192.168.0.178:7437/context?session_id=mcp-test-session&limit=5" | jq
```

**Expected**: Observaciones de la sesión ordenadas por fecha

---

## 🧪 CASO 7: `mem_session_start` / `mem_session_end` — Sesiones

**Endpoint**: POST /sessions + POST /sessions/{id}/end

```bash
# Iniciar sesión
curl -X POST http://192.168.0.178:7437/sessions \
  -H "Content-Type: application/json" \
  -d '{"id":"mcp-flow-test","project":"team/mcp-test","directory":"/tmp"}'

# Finalizar sesión
curl -X POST http://192.168.0.178:7437/sessions/mcp-flow-test/end \
  -H "Content-Type: application/json" \
  -d '{"summary":"Sesión de prueba MCP completada exitosamente"}'

# Ver sesión finalizada
curl http://192.168.0.178:7437/sessions/mcp-flow-test | jq '{id, summary, started_at, ended_at}'
```

**Expected**: Sesión con `ended_at` y `summary` seteados

---

## 🧪 CASO 8: `mem_session_summary` — Resumen de sesión

Ver CASO 7 — el summary se setea al finalizar la sesión.

---

## 🧪 CASO 9: `mem_stats` — Estadísticas

**Endpoint**: GET /stats

```bash
curl http://192.168.0.178:7437/stats | jq
```

**Expected**: JSON con `total_observations`, `total_sessions`, `total_prompts`, etc.

---

## 🧪 CASO 10: `mem_timeline` — Timeline

**Endpoint**: GET /timeline

```bash
curl "http://192.168.0.178:7437/timeline?observation_id=ID_DEL_CASO1&window=5" | jq
```

**Expected**: Observaciones alrededor de la especificada

---

## 🧪 CASO 11: `mem_save_prompt` — Guardar prompt

**Endpoint**: POST /prompts

```bash
curl -X POST http://192.168.0.178:7437/prompts \
  -H "Content-Type: application/json" \
  -d '{
    "session_id":"mcp-test-session",
    "content":"¿Qué patrón de diseño usar para el módulo X?",
    "project":"team/mcp-test"
  }'
```

**Expected**: 200 + prompt creado

---

## 🧪 CASO 12: `mem_doctor` — Diagnóstico

**Endpoint**: CLI `engram doctor`

```bash
engram doctor --server http://192.168.0.178:7437
```

**Expected**: 
```
Engram Diagnostic Report
========================
✓ database      (healthy) 12ms
✓ http_server   (healthy) 45ms  
✓ mcp_server    (healthy) 2ms
```

---

## 🧪 CASO 13: `mem_suggest_topic_key` — Sugerir topic key

**Endpoint**: POST /observations (con topic_key, ya probado en CASO 1)

**Verificar upsert**:
```bash
# Guardar con mismo topic_key
curl -X POST http://192.168.0.178:7437/observations \
  -H "Content-Type: application/json" \
  -d '{
    "session_id":"mcp-test-session",
    "title":"Prueba upsert - V2",
    "content":"Versión actualizada del topic",
    "type":"decision",
    "project":"team/mcp-test",
    "topic_key":"testing/mcp-save"
  }'

# Verificar que se actualizó (mismo topic_key = upsert)
curl "http://192.168.0.178:7437/search?q=testing/mcp-save&project=team/mcp-test" | jq '.[0].observation.title'
# → "Prueba upsert - V2"
```

---

## 🧪 CASO 14: `mem_capture_passive` — Captura pasiva

**Endpoint**: POST /observations/passive

```bash
curl -X POST http://192.168.0.178:7437/observations/passive \
  -H "Content-Type: application/json" \
  -d '{
    "session_id":"mcp-test-session",
    "title":"Descubrimiento automático",
    "content":"**What**: Hallazgo durante debugging\n**Learned**: El error era por X",
    "type":"discovery",
    "project":"team/mcp-test"
  }'
```

---

## 🧪 CASO 15: `mem_retention_stats` — Stats de retención

**Endpoint**: GET /retention/stats

```bash
curl http://192.168.0.178:7437/retention/stats | jq
```

---

## 🧪 CASO 16: `mem_retention_prune` — Podar por TTL

**Endpoint**: POST /retention/prune

```bash
curl -X POST http://192.168.0.178:7437/retention/prune \
  -H "Content-Type: application/json" \
  -d '{"ttl_days":90}'
```

---

## 🧪 CASO 17: `mem_merge_projects` — Mergear proyectos

**Endpoint**: POST /projects/migrate

```bash
curl -X POST http://192.168.0.178:7437/projects/migrate \
  -H "Content-Type: application/json" \
  -d '{"source":["team/mcp-test-old"],"target":"team/mcp-test"}'
```

---

## 🧪 CASO 18: `mem_promote_to_md` — Promover a .md

**Endpoint**: POST /md/promote/{id}

```bash
curl -X POST http://192.168.0.178:7437/md/promote/ID_DEL_CASO1 \
  -H "Content-Type: application/json" \
  -d '{"md_dir":"docs/decisions"}'
```

---

## 🧪 CASO 19: `mem_sync_md_to_repo` — Sync .md al repo

**Endpoint**: POST /md/sync

```bash
curl -X POST http://192.168.0.178:7437/md/sync \
  -H "Content-Type: application/json" \
  -d '{}'
```

---

## 🧪 CASO 20: `mem_traceability` — Matriz de trazabilidad

Requiere spec.md como input. Es una herramienta MCP que se llama desde el agente.

---

## ✅ CHECKLIST DE VERIFICACIÓN

- [ ] CASO 1: `mem_save` — memoria creada
- [ ] CASO 2: `mem_search` — búsqueda funciona
- [ ] CASO 3: `mem_get_observation` — detalle visible
- [ ] CASO 4: `mem_update` — actualización funciona
- [ ] CASO 5: `mem_delete` — soft-delete funciona
- [ ] CASO 6: `mem_context` — contexto de sesión
- [ ] CASO 7: `mem_session_start/end` — ciclo de sesión
- [ ] CASO 9: `mem_stats` — estadísticas
- [ ] CASO 11: `mem_save_prompt` — prompt guardado
- [ ] CASO 12: `mem_doctor` — diagnóstico OK
- [ ] CASO 13: `mem_suggest_topic_key` — upsert funciona
- [ ] CASO 15: `mem_retention_stats` — stats de retención
- [ ] CASO 16: `mem_retention_prune` — prune funciona

---

## 🔍 VERIFICACIÓN EN POSTGRESQL

Además de los tests via API, verificá directamente en PostgreSQL:

```sql
-- Ver memorias creadas
SELECT id, title, type, project, created_at 
FROM observations 
WHERE project = 'team/mcp-test'
ORDER BY id DESC;

-- Ver sesiones
SELECT id, project, started_at, ended_at, summary 
FROM sessions 
WHERE project = 'team/mcp-test'
ORDER BY started_at DESC;

-- Ver prompts
SELECT id, session_id, content, project, created_at 
FROM user_prompts 
WHERE project = 'team/mcp-test';
```
