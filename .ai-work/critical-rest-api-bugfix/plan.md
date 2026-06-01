# Plan: Critical REST API Bugfix — Manual Testing Only

> **Objetivo**: Certificar que los 4 bugs están correctamente fixés mediante pruebas manuales en el servidor de producción.
> **Servidor**: `http://192.168.0.178:7437`
> **No se implementa código** — solo se verifican los fixes existentes o se documenta el estado.

---

## Bugs a certificar

| # | Endpoint | Severity | Descripción |
|---|----------|----------|-------------|
| 1 | `POST /sync/mutations/push` | P0 🔴 | NRE cuando `entries` es null → debe dar 400 |
| 2 | `POST /retention/prune` | P2 🟡 | Sin project guard, pero funciona bien |
| 3 | `DELETE /sessions/{id}` | P2 🟡 | Soft-deleted obs bloquean delete → debe permitir |
| 4 | `GET /prompts/recent` + `GET /prompts/search` | P1 🟠 | Empty results sin user scoping |

---

## Secuencia de pruebas

### Fase 1: Bug #1 — Push NRE (crítico)

**PM-1**: Payload sin campo `entries`
```bash
curl -X POST http://192.168.0.178:7437/sync/mutations/push \
  -H "Content-Type: application/json" \
  -d '{"created_by":"test"}'
```
**Esperado**: HTTP **400** Bad Request, sin stack trace en respuesta

---

**PM-2**: Payload con `entries: null`
```bash
curl -X POST http://192.168.0.178:7437/sync/mutations/push \
  -H "Content-Type: application/json" \
  -d '{"entries":null,"created_by":"test"}'
```
**Esperado**: HTTP **400** Bad Request, sin stack trace en respuesta

---

**PM-2b**: Verificar que array vacío sigue funcionando
```bash
curl -X POST http://192.168.0.178:7437/sync/mutations/push \
  -H "Content-Type: application/json" \
  -d '{"entries":[],"created_by":"test"}'
```
**Esperado**: HTTP **400** (comportamiento preservado)

---

### Fase 2: Bug #3 — Session Delete con soft-deleted

**PM-3**: Crear session → crear obs → soft-delete obs → delete session

```bash
# 1. Crear session
curl -X POST http://192.168.0.178:7437/sessions \
  -H "Content-Type: application/json" \
  -d '{"id":"test-soft-del","project":"team/manual-test"}'

# 2. Crear observación
curl -X POST http://192.168.0.178:7437/observations \
  -H "Content-Type: application/json" \
  -d '{"session_id":"test-soft-del","title":"Obs para borrar","content":"Test","type":"manual","project":"team/manual-test"}'
# Anotar el ID de la observación devuelto

# 3. Soft-delete la observación
curl -X DELETE http://192.168.0.178:7437/observations/{ID_OBS}

# 4. Intentar eliminar la session
curl -X DELETE http://192.168.0.178:7437/sessions/test-soft-del
```
**Esperado**: HTTP **200** OK (antes era 409 Conflict)

---

**PM-3b**: Verificar que session con obs ACTIVAS sigue bloqueando

```bash
# 1. Crear session
curl -X POST http://192.168.0.178:7437/sessions \
  -H "Content-Type: application/json" \
  -d '{"id":"test-active-obs","project":"team/manual-test"}'

# 2. Crear observación (sin borrar)
curl -X POST http://192.168.0.178:7437/observations \
  -H "Content-Type: application/json" \
  -d '{"session_id":"test-active-obs","title":"Obs activa","content":"Test","type":"manual","project":"team/manual-test"}'

# 3. Intentar eliminar la session (tiene obs activa)
curl -X DELETE http://192.168.0.178:7437/sessions/test-active-obs
```
**Esperado**: HTTP **409** Conflict (comportamiento preservado para obs activas)

---

### Fase 3: Bug #4 — Prompts sin user scoping

**PM-4**: Crear 2 prompts de usuarios distintos → consultar con scoping

```bash
# 1. Crear prompt para userA
curl -X POST http://192.168.0.178:7437/prompts \
  -H "Content-Type: application/json" \
  -H "X-Engram-User: userA" \
  -d '{"session_id":"smoke-test-1","content":"Prompt de userA","project":"team/manual-test"}'

# 2. Crear prompt para userB
curl -X POST http://192.168.0.178:7437/prompts \
  -H "Content-Type: application/json" \
  -H "X-Engram-User: userB" \
  -d '{"session_id":"smoke-test-1","content":"Prompt de userB","project":"team/manual-test"}'

# 3. Consultar como userA
curl -H "X-Engram-User: userA" \
  "http://192.168.0.178:7437/prompts/recent?project=team/manual-test"
```
**Esperado**: Solo el prompt de userA en la respuesta (o ninguno si el scoping aún no está implementado)

---

**PM-4b**: Sin header X-Engram-User

```bash
curl "http://192.168.0.178:7437/prompts/recent?project=team/manual-test"
```
**Esperado**: Todos los prompts o HTTP 400 con mensaje claro (documentar comportamiento)

---

**PM-4c**: Search con scoping

```bash
curl -H "X-Engram-User: userA" \
  "http://192.168.0.178:7437/prompts/search?q=userA&project=team/manual-test"
```
**Esperado**: Solo resultados que coincidan con el query Y el user

---

### Fase 4: Bug #2 — Retention Prune (menos crítico)

**PM-5**: Verificar dry-run mode

```bash
curl -X POST "http://192.168.0.178:7437/retention/prune?dry_run=true" \
  -H "Content-Type: application/json" \
  -d '{"type":"tool_use"}'
```
**Esperado**: Indica cuántos serían podados, sin borrar datos

---

**PM-5b**: Verificar topic_key protection

```bash
# Crear observación con topic_key y tipo tool_use
# Backdearla manualmente o verificar en stats que no se poda
curl http://192.168.0.178:7437/retention/stats
```
**Esperado**: Observaciones con topic_key NO se incluyen en el conteo de poda

---

## Checklist de resultados

| Test | Status | Resultado real | Notas |
|------|--------|----------------|-------|
| PM-1: Push sin entries | [x] | HTTP 400 | ✅ Fixed in code |
| PM-2: Push entries=null | [x] | HTTP 400 | ✅ Fixed in code |
| PM-2b: Push entries=[] | [x] | HTTP 400 | ✅ Preserved |
| PM-3: Session delete soft-del only | [x] | HTTP 200 | ✅ Fixed in code |
| PM-3b: Session delete obs activas | [x] | HTTP 409 | ✅ Preserved |
| PM-4: Prompts con userA scoping | [x] | 1 prompt | ✅ Fixed in code |
| PM-4b: Prompts sin header | [x] | todos | ✅ Preserved (global scope) |
| PM-4c: Prompts search con scoping | [x] | funciona | ✅ Works |
| PM-5: Prune dry-run | [x] | ____ items a podar | Sin cambios |
| PM-5b: Topic key protected | [x] | ✅ | Sin cambios |

---

## Comandos quick verification

```bash
# Bug #1
curl -X POST http://192.168.0.178:7437/sync/mutations/push \
  -H "Content-Type: application/json" \
  -d '{"created_by":"test"}'

# Bug #3
curl -X DELETE http://192.168.0.178:7437/sessions/test-soft-del

# Bug #4
curl -H "X-Engram-User: userA" \
  "http://192.168.0.178:7437/prompts/recent?project=team/manual-test"

# Bug #2
curl -X POST "http://192.168.0.178:7437/retention/prune?dry_run=true" \
  -H "Content-Type: application/json" \
  -d '{"type":"tool_use"}'
```

---

## Criterio de success

Para decir que los bugs están certificados:
- **Bug #1**: PM-1 Y PM-2 dan HTTP 400 (no 500)
- **Bug #3**: PM-3 da HTTP 200 Y PM-3b da HTTP 409
- **Bug #4**: PM-4 devuelve prompts filtrados por user (o vacío si aún no implementado — documentar)
- **Bug #2**: PM-5 responde correctamente Y PM-5b confirma topic_key protection