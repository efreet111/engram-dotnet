# Session Report: REST API Bugfixes — 2026-05-31 / 2026-06-01

## Objetivo

Fix 3 bugs críticos en la API REST de Engram descubiertos durante testing manual, desplegar en TrueNAS y verificar en producción.

**Esfuerzo aproximado**: ~3 h (FlowForge + fixes + CI + migración PostgreSQL + deploy + verificación manual).

---

## Bugs Objetivo

| # | Endpoint | Descripción | Severity |
|---|----------|-------------|----------|
| 1 | `POST /sync/mutations/push` | NRE cuando `entries` es null → debe dar 400 | P0 |
| 2 | `DELETE /sessions/{id}` | Soft-deleted obs bloquean delete → debe permitir | P2 |
| 3 | `GET /prompts/recent` | Ignora `X-Engram-User` header → debe filtrar | P1 |

---

## Estado final (2026-06-01)

| Item | Status |
|------|--------|
| Código fix (3 bugs) | ✅ `a0ff6ee` — CI pass |
| Migración PostgreSQL `created_by` | ✅ `e1a9cf9` |
| Deploy TrueNAS (`git pull` → `e1a9cf9`) | ✅ |
| Health + `/projects/*` | ✅ |
| Testing manual post-deploy (3 bugs) | ✅ Ver sección [Verificación post-deploy](#verificación-post-deploy) |

**Commit desplegado en producción**: `e1a9cf9` (main)

---

## Trabajo Realizado

### 1. FlowForge Discovery → Spec → Plan

- `.ai-work/critical-rest-api-bugfix/context-map.md`
- `.ai-work/critical-rest-api-bugfix/spec.md`
- `.ai-work/critical-rest-api-bugfix/plan.md`
- `.ai-work/critical-rest-api-bugfix/rework_ticket.md`
- `.ai-work/critical-rest-api-bugfix/verify-report.md`

### 2. Testing Manual — antes del fix (servidor your-server)

| Test | Resultado |
|------|-----------|
| PM-1: Push sin entries | ❌ HTTP 500 (debería 400) |
| PM-2: Push entries=null | ❌ HTTP 500 (debería 400) |
| PM-3: Session con soft-deleted | ❌ HTTP 409 (debería 200) |
| PM-4: Prompts con user scoping | ⚠️ `/prompts/recent` ignoraba header |

### 3. Código — Fixes Implementados

**Bug #1** (`CloudSyncEndpoints.cs`):

```csharp
// Before:
if (body is null || body.Entries.Count == 0)
// After:
if (body is null || body.Entries is null || body.Entries.Count == 0)
```

**Bug #2** (`PostgresStore.cs`, `SqliteStore.cs`):

```sql
-- After:
SELECT COUNT(*) FROM observations WHERE session_id = @id AND deleted_at IS NULL
```

**Bug #3** (`EngramServer.cs`, stores):

- `GetUserId(ctx)` en `HandleRecentPrompts`
- Columna `created_by` en `user_prompts` + `RecentPromptsAsync(project, userId, limit)`

### 4. Tests automatizados

| Archivo | Cambio |
|---------|--------|
| `CloudSyncEndpointsTests.cs` | `Push_NullEntries_Returns400`, `Push_EntriesFieldNull_Returns400` |
| `SqliteStoreTests.cs`, `PostgresStoreTests.cs`, `HttpStoreTests.cs` | Firma `RecentPromptsAsync` actualizada |

### 5. CI

- `a0ff6ee`: ✅ CI passed (12 files, 260 insertions)
- Fixes de compilación en tests Postgres/HttpStore en el mismo ciclo

---

## Problema: Migration `created_by` (bloqueó deploy ~1 h)

### Error

```
column "created_by" does not exist
SqlState: 42703
```

### Causa raíz

En bases **existentes**, `CREATE TABLE IF NOT EXISTS user_prompts` no añade columnas nuevas, pero los `CREATE INDEX` del mismo bloque SQL **sí se ejecutan**. En `a0ff6ee` el índice `idx_prompts_created_by` corría antes de `ALTER TABLE ... ADD COLUMN created_by`.

### Intentos de fix

| Commit | Qué hizo | Resultado en TrueNAS |
|--------|----------|----------------------|
| `f8ba8f6` | Añadió índice condicional al final | ❌ Sigue fallando — **no quitó** el índice del bloque inicial |
| `e1a9cf9` | Quitó índice del bloque inicial; índice solo tras `ColumnExists` | ✅ Correcto en código |

### Confusión de deploy

TrueNAS seguía en `5ae578d` (sin ningún fix). Los commits `f8ba8f6` / `e1a9cf9` no estaban desplegados; el informe inicial marcaba `e1a9cf9` como error por pruebas contra código viejo o solo `f8ba8f6`.

**Solución operativa**:

```bash
cd /mnt/Pool_8TB/engram_data
git fetch origin && git pull origin main   # debe quedar en e1a9cf9+
cd docker
sudo docker compose up -d --build --force-recreate
```

---

## Configuración del Servidor (TrueNAS)

### Rutas

```
/mnt/Pool_8TB/engram_data/          # clone git
/mnt/Pool_8TB/engram_data/docker/   # docker-compose.yml
```

### Servicios

- `docker-postgres-1` — PostgreSQL :5432
- `engram` — API :7437

### Variables relevantes

```yaml
ENGRAM_DB_TYPE: postgres
ENGRAM_PG_CONNECTION: "Host=host.docker.internal;Port=5432;Database=engram_cloud;..."
```

- Database: **`engram_cloud`** (no `engram` del compose por defecto)
- `host.docker.internal` + `extra_hosts: host-gateway` para PostgreSQL en el host
- Build: `dockerfile: Dockerfile` en la **raíz del repo** (compila fuente), no `docker/Dockerfile` (binario de GitHub Releases)

```bash
cd /mnt/Pool_8TB/engram_data/docker
sudo docker compose up -d --build --force-recreate
```

---

## Verificación post-deploy

Ejecutado contra `http://your-server:7437` el **2026-06-01** (commit `e1a9cf9`).

### Health

```json
{"status":"ok","service":"engram","version":"1.1.0","backend":"postgres"}
```

### Bug #1 — `POST /sync/mutations/push`

| Caso | HTTP | Body |
|------|------|------|
| Sin `entries` | **400** | `error_code: empty-batch` |
| `entries: null` | **400** | `error_code: empty-batch` |
| `entries: []` | **400** | `empty-batch` (preservado) |

Ya no hay HTTP 500 / NRE.

### Bug #2 — `DELETE /sessions/{id}`

| Caso | HTTP |
|------|------|
| Sesión con obs soft-deleted únicamente | **200** `{"status":"deleted"}` |
| Sesión con obs activa (control) | **409** `active observations` |

### Bug #3 — `GET /prompts/recent` + `X-Engram-User`

| Usuario | Resultado |
|---------|-----------|
| `X-Engram-User: userA` | Solo prompts de userA |
| `X-Engram-User: userB` | Solo prompts de userB |

**Nota menor**: el campo `created_by` en el JSON de respuesta puede aparecer vacío (`""`) aunque el filtrado por header funciona; revisar serialización del modelo `Prompt` si hace falta trazabilidad en la API.

### Proyectos (smoke)

- `GET /projects/list` — ✅ lista de proyectos
- `GET /projects/stats` — ✅ conteos por proyecto

---

## Commit History

| Hash | Descripción | Estado |
|------|-------------|--------|
| `5ae578d` | ENG-206 PostgreSQL tests | Base previa en TrueNAS (atrasada) |
| `a0ff6ee` | 3 REST API bugs + user scoping | ✅ CI; migración PG rota en legacy |
| `f8ba8f6` | Índice condicional al final (incompleto) | ❌ Índice seguía en bloque inicial |
| `e1a9cf9` | Quita índice del CREATE inicial | ✅ **Deploy producción** |

---

## Lessons Learned (memoria del equipo)

1. **Orden de migración**: índices solo después de que la columna exista (`ColumnExists` + `ALTER TABLE` antes de `CREATE INDEX`).
2. **`CREATE TABLE IF NOT EXISTS` ≠ migración de columnas**: en PG legacy hay que usar `ALTER TABLE` idempotente por columna.
3. **Un commit incompleto puede parecer “el fix no funciona”**: `f8ba8f6` añadió el fix al final pero dejó el índice roto al inicio.
4. **Verificar `git log -1` en el servidor** antes de debuggear Docker: TrueNAS en `5ae578d` explicaba todos los síntomas.
5. **Dos Dockerfiles**: raíz = build fuente; `docker/Dockerfile` = release precompilado (no incluye fixes locales).
6. **DB name**: producción usa `engram_cloud`; el default del compose es `engram`.
7. **Post-deploy obligatorio**: correr regression tests de [MANUAL-TESTING-CHECKLIST.md](MANUAL-TESTING-CHECKLIST.md#regression-tests-bugfixes-críticos-2026-06-01).
8. **`host.docker.internal`**: necesario para PostgreSQL en el host desde el contenedor Engram.

---

## Referencias

- Checklist manual: [MANUAL-TESTING-CHECKLIST.md](MANUAL-TESTING-CHECKLIST.md)
- Artefactos FlowForge: `.ai-work/critical-rest-api-bugfix/`
- Servidor: TrueNAS `your-server:7437`
- Repo: `efreet111/engram-dotnet`

---

## Contacto

Usuario: Gantz
