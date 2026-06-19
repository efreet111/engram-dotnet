# Backlog — engram-dotnet

> **Fuente de verdad para el orden de trabajo.**  
> El [ROADMAP](ROADMAP.md) describe fases y visión; **este archivo define qué hacer ahora y en qué orden.**

**Última actualización:** 2026-06-18  
**Meta release:** finales de junio 2026 (uso por terceros + instalador)

---

## Cómo usamos este backlog

| Regla | Descripción |
|-------|-------------|
| **Una cola** | La tabla [Cola de ejecución](#cola-de-ejecución) es el orden. Se trabaja de arriba hacia abajo salvo bloqueo explícito. |
| **Feature = entregable** | Cada fila es una **Feature** (épica pequeña) con criterio de "hecho" claro. Las tareas grandes se descomponen en **Stories** dentro de la feature. |
| **Estados** | `Done` · `Ready` (sin spec o spec listo) · `In Progress` · `Blocked` · `Icebox` |
| **Tipos** | `Feature` · `Bug` · `Doc` · `Chore` · `Test` |
| **Specs** | Features `Ready` con spec en `sdd/` o `docs/rfcs/` — enlazado en la columna Spec. |
| **Trazabilidad** | `Origen` indica de dónde nace (HU, ENG padre, bug report). `←` = nace de, `→` = depende de. |
| **No duplicar** | Deuda técnica detallada vive en [TECHNICAL-DEBT.md](TECHNICAL-DEBT.md); aquí solo el **orden** y el **por qué**. |
| **Docs al cerrar** | Al marcar `Done`, seguir [`.cursor/skills/engram-docs-on-done/SKILL.md`](../.cursor/skills/engram-docs-on-done/SKILL.md) y regla `config/cursor/rules/engram-docs-on-done.mdc`. |
| **Git** | [GIT-WORKFLOW.md](GIT-WORKFLOW.md) + regla proyecto `engram-git-workflow` (`config/cursor/rules/` → sync a `.cursor/rules/`). |

### Definition of Done (documentación)

Un ítem **no está Done** hasta que:

- [ ] Código + tests mergeables
- [ ] Checklist de documentación del skill completado (solo ítems que apliquen al cambio)
- [ ] `BACKLOG` + `ROADMAP` + `CHANGELOG` actualizados

### Plantilla de Feature (para issues / SDD)

```markdown
## [ENG-XXX] Título corto

**Tipo:** Feature | Bug | Doc  
**Prioridad:** P0 | P1 | P2  
**Esfuerzo:** S (<2h) | M (2-6h) | L (1-2d) | XL (>2d)
**Origen:** ← HU-XXX / ← ENG-XXX / bug report / —  
**Depende de:** → ENG-XXX / —

### Problema / valor
Un párrafo.

### Criterios de aceptación
- [ ] …
- [ ] …

### Fuera de alcance
- …

### Spec / enlaces
- sdd/… o docs/…
```

---

## Cola de ejecución

Trabajar en este orden. **P0** = antes de publicitar; **P1** = junio; **P2** = después de release.

| # | ID | P | Tipo | Feature | Estado | Effort | Origen | Spec / notas |
|---|-----|---|------|---------|--------|--------|--------|--------------|
| — | **Hecho recientemente (2026-05-27/28 y 2026-06-18)** |
| ✓ | ENG-101 | — | Bug | Sincronización doc/código (versión CLI, conteo MCP, CHANGELOG) | Done | S | testing | `69e83d7` |
| ✓ | ENG-102 | — | Bug | `mem_current_project` implementado en código | Done | S | testing | `69e83d7` |
| ✓ | ENG-103 | — | Bug | Tests Obsidian CRLF (LF en markdown) | Done | S | testing | `69e83d7` |
| ✓ | ENG-104 | — | Bug | Docker `/app/docs` permisos MCP sync | Done | S | testing | `bf01f18` |
| ✓ | ENG-105 | — | Bug | MCP crash con `ENGRAM_SYNC_ENABLED` (`ILocalSyncStore`) | Done | S | testing | `a7f45eb` |
| ✓ | ENG-106 | — | Feature | Hub MCP multi-editor (`config/mcp/`, `scripts/setup.*`) | Done | M | ← ENG-201 | PR ENG-201 |
| ✓ | ENG-201 | — | Chore | MCP/setup/docs + reglas Cursor + GIT-WORKFLOW + backlog | Done | S | — | PR ENG-201 |
| ✓ | ENG-202 | — | Doc | OSS essentials: `CONTRIBUTING.md`, `CODE_OF_CONDUCT`, `SECURITY` | Done | S | backlog | Creados |
| ✓ | ENG-203 | — | Doc | Plantillas GitHub: issue + PR template | Done | S | backlog | Creados |
| ✓ | ENG-204 | — | Chore | Pinear `ModelContextProtocol` (no `*-*`) | Done | S | backlog | `1.3.0` pineada |
| ✓ | ENG-205 | — | Doc | Auditoría README vs código (tools, endpoints, versiones) | Done | M | sesión pre-release | Versiones corregidas + 7 endpoints |
| ✓ | ENG-206 | — | Test | PostgreSQL: arreglar 3 tests skipped | Done | M | testing | 2 fixeados, 1 eliminado |
| ✓ | ENG-305 | — | Chore | Badge CI en README | Done | S | sesión pre-release | — |
| ✓ | ENG-304 | P1 | Chore | `global.json` + `Directory.Build.props` (versiones centralizadas) | Done | S | backlog | `781e9fe` |
| ✓ | ENG-306 | P1 | Chore | Mejorar trazabilidad en backlog (columna Origen + template HU) | Done | S | ← ENG-205 | Columna Origen agregada |
| ✓ | ENG-307 | P1 | Test | Test infrastructure: `regression-test.sh` (31 checks) + `dev-test.sh` (T3 gate) | Done | S | sesión 2026-06-05 | `781e9fe` |
| ✓ | ENG-308 | P1 | Doc | Dev workflow docs: `AGENTS.md` + `docs/DEVELOPMENT.md` | Done | S | sesión 2026-06-05 | `781e9fe` |
| ✓ | ENG-207 | P0 | Feature | Logging infrastructure | Done | M | roadmap | [sdd/logging-infrastructure/](../sdd/logging-infrastructure/specs/logging-infrastructure.md) |
| ✓ | ENG-208 | P1 | Feature | Completar Upstream Phase 2 API parity (structured errors, server-side incremental, --watch, --since) | Done | M | ← upstream engram | `e7e5736` |
| ✓ | ENG-209 | P1 | Test | Pull entre 2 clientes (sync) | Done | S | roadmap | `bash scripts/test-2client-pull.sh` — verificado end-to-end 2026-06-15 |
| ✓ | ENG-210 | P1 | Test | Offline + reconexión | Done | S | roadmap | `bash scripts/test-offline-reconnect.sh` — verificado end-to-end 2026-06-16 |
| ✓ | ENG-211 | P1 | Bug | SyncManager: ReplayDeferredAsync falla con "no such column: id" en SQLite con schema viejo | Done | S | descubierto en sesión logging 2026-06-05 | `de63304` |
| ✓ | ENG-428 | P1 | Bug | Mutation push: observation payload sin session_id — PostgresException 23502 en server | Done | S | descubierto en sesión ENG-209/210 2026-06-15 | `628be52`. Fix: SnakeCaseLower en JsonPullOpts |
| ✓ | ENG-427 | P1 | Bug | ListMutationsSinceAsync: SQL syntax error con project filter (ANY array) | Done | S | descubierto en sesión ENG-426 | Fix en PostgresStore.cs:1814 (post-commit 781e9fe) |
| — | **Siguiente** |
| 5 | ENG-301 | P1 | Feature | Instalador Windows (MSI o script) + `engram` en PATH | Ready | L | roadmap | Evolución de `scripts/setup.ps1` |
| 6 | ENG-302 | P1 | Feature | Wizard gráfico: modo local vs offline-first sync | Ready | L | → ENG-301 | — |
| 7 | ENG-303 | P1 | Doc | Guía "instalación desde git" unificada (enlaza `config/mcp/INSTALL.md`) | Ready | S | → ENG-301 | — |
| — | **Estabilidad inmediata (v1.0.0)** |
| 10 | ENG-410 | P1 | Feature | Project identity fingerprint (.engram-id UUID v5 determinista) | Done | M | ← PRD memoria semántica | `00e340cd` generado. RFC-001. |
| 11 | ENG-411 | P1 | Chore | SQLite WAL mode + Polly retry para SQLITE_BUSY | Done | S | ← PRD memoria semántica punto #5 | WAL ya existía (ApplyPragmas). +Polly 8.7 retry pipeline (3 retries, exp backoff) en `86db473` |
| 12 | ENG-429 | P1 | Feature | Exponer `project_id` en `mem_current_project` MCP tool | Ready | S | ← ENG-410 | [spec](../.ai-work/eng-429-project-id-mcp/spec.md) |
| 13 | ENG-430 | P0 | Doc | Documentar `.engram-id` en `.gitignore` + check de instalación | Done | S | ← ENG-410 | `680dd1a` — doctor check + .gitignore docs + CONTRIBUTING |
| 14 | ENG-431 | P2 | Feature | Validación de consistencia del GUID | Ready | S | ← ENG-410 | [spec](../.ai-work/eng-431-guid-validation/spec.md) |
| 15 | ENG-432 | P2 | Feature | CLI: `engram project id` — mostrar/regenerar project_id | Ready | S | ← ENG-410 | [spec](../.ai-work/eng-432-cli-project-id/spec.md) |
| 16 | ENG-433 | P2 | Feature | Auto-generación de `.engram-id` en startup | Ready | S | ← ENG-410 | [spec](../.ai-work/eng-433-434-auto-migrate/spec.md#eng-433) |
| 17 | ENG-434 | P1 | Feature | Migración `project` string → GUID canónico (v1.1) | Ready | L | ← ENG-410 | [spec](../.ai-work/eng-433-434-auto-migrate/spec.md#eng-434) |
| 18 | ENG-435 | P1 | Feature | Legacy Identity Migration Toolkit: asignar GUID custom + migrar memorias | Done | M | ← ENG-410 + ENG-432 | [spec](../.ai-work/eng-435-legacy-migration/spec.md) |
| — | **Meta v1.1 — memoria semántica avanzada** |
| — | ENG-412 | P2 | Feature | Memory taxonomy & lifecycle (Decision, Insight, Transient, consolidation) | Ready | L | ← PRD memoria semántica puntos #3, #10 | Ver [RFC-002](../docs/architecture/rfc/RFC-002-memory-taxonomy.md) (pendiente) |
| — | ENG-413 | P2 | Feature | Smart token budget packer para queries | Ready | M | ← PRD memoria semántica punto #4 | — |
| — | ENG-414 | P2 | Feature | Contradicción temporal y supersedencia de memorias | Ready | L | ← PRD memoria semántica punto #2 | Depende de ENG-412 |
| — | ENG-415 | P2 | Feature | Resolución de conflictos sync multi-dispositivo | Ready | L | ← PRD memoria semántica punto #6 | — |
| — | ENG-416 | P2 | Chore | Schema evolution con migraciones versionadas | Ready | M | ← PRD memoria semántica punto #7 | — |
| — | ENG-417 | P2 | Feature | SQLite encryption (SQLCipher) | Ready | M | ← PRD memoria semántica punto #8 | — |
| — | ENG-418 | P2 | Feature | Búsqueda híbrida (vector + FTS5 + metadata) | Ready | XL | ← PRD memoria semántica punto #9 | Requiere embeddings |
| — | **Icebox (no sacar hasta vaciar P0/P1)** |
| — | ENG-419 | P2 | Bug | Eliminar debug enroll + endpoint /debug-test | Done | S | ← audit AUD-021/022 | Resuelto 2026-06-05 |
| — | ENG-420 | P1 | Test | CloudSyncIntegrationTests en CI PR | Done | S | ← audit AUD-031 | Step en ci.yml job postgres |
| ✓ | ENG-421 | P0 | Bug | ApplyPulledMutationAsync (sync pull) — resuelto como parte de ENG-425 (server-side apply, ADR-002) | Done | L | ← audit AUD-013 | Implementado en SqliteStore.cs:2082 + 11 tests |
| ✓ | ENG-425 | P0 | Feature | Server-side mutation apply: servidor aplica mutations a PostgresStore | Done | L | ← ADR-002 decisión | [ADR-002](../docs/architecture/adr/ADR-002-sync-mutation-application.md) |
| ✓ | ENG-426 | P0 | Architecture | ID mapping strategy: sync_id como canonical (sin mapping a server ID) | Done | M | ← ADR-002 decisión | Verificado V1-V6. Fix bug SQL en ListMutationsSinceAsync (L1703). |
| — | ENG-422 | P1 | Test | REST endpoints sin cobertura (13 rutas) | Ready | M | ← audit AUD-023 | /md/*, retention, import, timeline |
| — | ENG-423 | P1 | Test | RetentionPostgresTests → Testcontainers | Ready | S | ← audit AUD-016 | 5 tests skipped |
| — | ENG-424 | P2 | Test | Unit tests 11 MCP tools sin cobertura | Ready | M | ← audit AUD-036 | mem_timeline, mem_doctor, etc. |
| ✓ | ENG-404 | P1 | Feature | Phase 4 — memory relations (grafo de observaciones) | Done | M | ← ENG-410 + spike 55bdbf8 | [spike learnings](../.ai-work/eng-404-spike/learnings.md) + MCP tools + CLI |
| — | ENG-401 | P2 | Feature | Backend config file `~/.engram/config.json` | Icebox | M | [sdd/backend-config-switch/](../sdd/backend-config-switch/proposal.md) |
| — | ENG-402 | P2 | Chore | Giant class refactor (Sqlite/Postgres partial) | Icebox | L | [TECHNICAL-DEBT](TECHNICAL-DEBT.md) TD-001/002 |
| — | ENG-403 | P2 | Feature | Phase 3 — breaking (quitar `project` de writes) | Icebox | L | Requiere guía migración |
| — | ENG-405 | P2 | Feature | Authentication & access control | Icebox | L | Sin proposal aún |
| — | ENG-406 | P2 | Feature | Tool deferral MCP | Icebox | — | Blocked: SDK .NET |

---

## Features detalladas (próximas en cola)

Cada ítem incluye **por qué nace**, **para qué sirve** y **cómo probarlo** para que cualquiera que lo tome entienda el contexto.

---

### ENG-207 — Logging infrastructure (P0)

**Problema:** Hoy no hay logs estructurados. Si algo falla en producción, no hay forma de saber qué pasó sin conectarse al servidor y adivinar. Los warnings de null reference en `PostgresStore.cs` y errores de sync se pierden.

**Para qué sirve:** Que cualquier persona pueda correr `journalctl -u engram` o revisar `docker logs` y entender qué está pasando sin tener que leer código.

**Stories:**
- [ ] Logger estructurado (JSON o formato legible) con niveles (info, warn, error)
- [ ] Request logging middleware (método, endpoint, status code, duración)
- [ ] SyncManager events loggeados (cycle, push/pull, errores, backoff)
- [ ] Configurable por env var (`ENGRAM_LOG_LEVEL`)

**Cómo probar:**
```bash
curl http://localhost:7437/health
# → ver el log con el request
# Forzar error: curl http://localhost:7437/no-existe
# → ver 404 en log
```

**Hecho cuando:** un error de servidor se puede diagnosticar solo con los logs, sin preguntar por chat.

---

### ENG-208 — Upstream Phase 2 API parity (P1) ✅ DONE

**Entregado:**
- [x] `DELETE /sessions/{id}` — 200/404/409 con respuestas estructuradas
- [x] `DELETE /prompts/{id}` — 200/404/400 con respuestas estructuradas
- [x] `mem_current_project` MCP tool con tests
- [x] `McpErrors.cs` helper con 9 códigos de error
- [x] 18 migraciones de error en `EngramTools.cs` a formato estructurado
- [x] `ExportProjectAsync` + `ExportSinceAsync` en store
- [x] `GET /export?project=X` server endpoint
- [x] `GET /export/since?project=X&after_seq=N` endpoint con cursor
- [x] `--since` filter CLI (ISO 8601 y relativo)
- [x] `--watch` mode daemon
- [x] `--interval` flag
- [x] Per-project state files (`state-{project}.json`)

**Commit:** `e7e5736`

---

### ENG-209 — Pull entre 2 clientes (P1, manual)

**Problema:** El sync está implementado pero nunca se probó con 2 personas reales. Puede haber problemas de visibilidad, conflictos o aislamiento que no aparecen en tests unitarios.

**Para qué sirve:** Garantizar que el flujo core del producto (sync multi-cliente) funciona en condiciones reales antes del release.

**Cómo probar:**
```bash
# Dev1 (Victor)
export ENGRAM_USER=victor.silgado
engram mem_save "Mi decision" --project team/mi-api

# Dev2 (Juan)
export ENGRAM_USER=juan.perez
# Esperar sync cycle
engram search "decision" --project team/mi-api
# → Debe ver la memoria de Victor si es scope team
```

**Requisitos:** 2 developers, servidor PostgreSQL, sync habilitado en ambos MCP.

**Hecho cuando:** Dev1 crea memoria → Dev2 hace pull y la ve.

---

### ENG-210 — Offline + reconexión (P1, manual)

**Problema:** El offline-first sync promete que podés trabajar sin conexión y los cambios se sincronizan al reconectar. Nunca se probó end-to-end.

**Para qué sirve:** Validar que la promesa principal del producto se cumple: trabajar sin conexión sin perder datos.

**Cómo probar:**
```bash
# 1. Cortar conectividad al servidor
# 2. Crear 3 memorias locales
engram mem_save "Offline 1" --project team/mi-api
engram mem_save "Offline 2" --project team/mi-api
engram mem_save "Offline 3" --project team/mi-api
# 3. Ver pending_push = 3
engram sync status --json | jq '.counts.pending_push'
# 4. Restaurar conexión, esperar sync
# 5. Ver pending_push = 0 y memorias en servidor
curl http://server:7437/search?q=Offline
```

**Requisitos:** Servidor PostgreSQL, capacidad de cortar/restaurar red.

**Hecho cuando:** 3 memorias offline → reconexión → aparecen en servidor.

---

### ENG-211 — SyncManager: ReplayDeferredAsync falla en SQLite con schema viejo (P1, bug)

**Descubierto durante:** Sesión logging 2026-06-05 — logs JSON mostraron errores en `SyncManager.CycleAsync` que antes eran invisibles.

**Problema:** `ReplayDeferredAsync` en `SqliteStore.cs:1938` falla con `SQLite Error 1: 'no such column: id'` al hacer SELECT/UPDATE/DELETE sobre `sync_apply_deferred`. También puede fallar al INSERTAR `project` en `sync_mutations` si la columna no existe.

**Causa:** La DB SQLite local fue creada por una versión anterior del código donde la tabla `sync_apply_deferred` tenía un schema diferente o la columna `project` en `sync_mutations` no existía. `CREATE TABLE IF NOT EXISTS` no reconstruye la tabla si ya existe.

**Síntoma visible:**
```json
{"LogLevel":"Error","Message":"Sync cycle failed (failure 1/10)",
 "Exception":"SqliteException: SQLite Error 1: 'no such column: id'"}
```

**¿A quién afecta?** Cualquier usuario con una DB SQLite creada en una versión anterior que ejecute `engram mcp` o `engram serve` con sync habilitado. El sync se bloquea hasta el conteo de reintentos (10), causando errores cíclicos.

**Stories:**
- [ ] Diagnosticar qué columnas faltan (`id` en `sync_apply_deferred`, `project` en `sync_mutations`, posiblemente otras)
- [ ] Agregar `AddColumnIfNotExists` para columnas faltantes en ambas tablas de sync
- [ ] Verificar que el fix no rompe el sync con DBs nuevas (limpias)
- [ ] Documentar: `rm -rf ~/.engram/data/` como workaround

**Hecho cuando:** un usuario existente con schema viejo y uno nuevo con schema limpio pueden hacer sync sin errores.

---

### ✅ ENG-428 — Mutation push: observation sin session_id (P1, bug) — DONE

**Descubierto durante:** Sesión ENG-209/210 (2026-06-15) — test dockerizado de sync multi-cliente.

**Problema:** Cuando `engram save` crea una memoria vía CLI, el `SyncManager` genera una mutation y la envía al servidor. La mutation se serializa con claves snake_case (`session_id`) pero el servidor deserializa esperando camelCase (`sessionId`). `PropertyNameCaseInsensitive = true` no alcanza porque la diferencia es un underscore, no case.

**Fix:** Agregar `PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower` a `JsonPullOpts` en `SqliteStore.cs` y `PostgresStore.cs`. Esto mapea `SessionId` → `"session_id"` correctamente.

**Resultado:** `scripts/test-2client-pull.sh` pasa end-to-end. Servidor recibe la mutation, aplica la observación, y Client-B puede verla via pull.

---

---

### ENG-429 — Exponer `project_id` en `mem_current_project` (P1, S)

**Problema:** `DetectionResult.ProjectId` ya existe en código. Pero `mem_current_project` no lo incluye en su JSON. El campo está en el modelo pero el wrapper MCP lo ignora.

**Valor:** Agente FlowForge conoce el UUID estable del proyecto, no solo el nombre. Referencias cross-machine. `.engram-id` pasa de ser invisible a estar disponible para el ecosistema.

**Criterios:**
- [ ] `mem_current_project` incluye `project_id` en snake_case
- [ ] `null` cuando no hay identidad
- [ ] Test unitario

---

### ENG-430 — `.engram-id` en `.gitignore` + check (P0, S)

**Problema:** Si `.engram-id` está en `.gitignore`, el archivo nunca se comparte. El equipo tendría GUIDs diferentes → memorias duplicadas. Es un bloqueo silencioso.

**Valor:** Garantiza que el equipo comparte la misma identidad. Sin esto, project identity no sirve en equipos.

**Criterios:**
- [ ] `.engram-id` NO en `.gitignore` del template
- [ ] `engram doctor` check que verifica el archivo no está ignorado
- [ ] Documentar en CONTRIBUTING.md que debe commitearse

---

### ENG-431 — Validación de consistencia del GUID (P2, S)

**Problema:** Si alguien edita `.engram-id` manualmente, el GUID no coincide con `UUIDv5(remote, first_commit)`. El sistema usa el GUID editado sin warning. Otros miembros tendrían identidad diferente.

**Valor:** Detección temprana de corrupción. Evita divergencia de identidad silenciosa.

**Criterios:**
- [ ] Al leer `.engram-id`, validar contra cálculo determinista
- [ ] Si no coincide → log warning
- [ ] El archivo manda (no bloquea)
- [ ] `ENGRAM_STRICT_PROJECT_ID=true` para CI (fatal)

---

### ENG-432 — CLI `engram project id` (P2, S)

**Problema:** No hay forma de ver el `project_id` desde terminal. Debug requiere abrir `.engram-id` a mano.

**Valor:** Comando análogo a `git remote -v`. Utilidad para desarrollo.

**Criterios:**
- [ ] `engram project id` → imprime GUID o `null`
- [ ] `--json` → output estructurado con `source` (file|computed|none)
- [ ] `--regenerate` → recalcula y sobreescribe (con confirmación)

---

### ENG-433 — Auto-generación de `.engram-id` en startup (P2, S)

**Problema:** `.engram-id` debe generarse manualmente hoy. Si un nuevo miembro clona antes de que exista, queda sin identidad.

**Valor:** Elimina fricción de setup. Primer contacto con el proyecto = identidad generada automáticamente.

**Criterios:**
- [ ] Flag `ENGRAM_AUTO_ENROLL=true` habilita auto-generación
- [ ] Por defecto OFF (no generar archivos sin consentimiento)
- [ ] Detecta git repo sin `.engram-id` → calcula y guarda
- [ ] Log: "Generated project identity: ..."

---

### ENG-434 — Migración `project` string → GUID canónico (P1, L, v1.1)

**Problema:** `project` string (nombre de carpeta) es la key del store hoy. Renombrar carpeta = perder memorias. El GUID existe pero no se usa en storage.

**Valor:** Identidad inmune a renames, clones y moves. Fundación para sync multi-dispositivo confiable.

**Depende de:** ENG-404 (memory relations).

**Criterios:**
- [ ] Store acepta `project_id` GUID como parámetro
- [ ] Search/save/context con GUID canónico
- [ ] Migración automática de memorias existentes
- [ ] Guía de migración para equipos
- [ ] Deprecation period para `project` string

---

### ⚠️ Nota sobre salud del sync (2026-06-16)

**Actualización:** Los issues originales detectados en T3 (2026-06-05) están **resueltos**:
- ✅ **ENG-211**: `SQLite Error 1: 'no such column: id'` en `ReplayDeferredAsync` — fix `AddColumnIfNotExists` aplicado (`de63304`)
- ✅ **ENG-428**: `null session_id` en mutation push — fix `SnakeCaseLower` en `JsonPullOpts` (`628be52`)
- ✅ **ENG-427**: SQL syntax error con project filter (ANY array) — fix en `PostgresStore.cs:1814`
- ✅ **ENG-209/210**: validación end-to-end con Docker (2-client pull + offline + reconexión) — PASS

**Estado actual del sync (verificado 2026-06-16):**
- Pull multi-cliente funciona (3/3 memorias transferidas)
- Offline + reconexión funciona (3/3 memorias recuperadas y visibles en servidor)
- `pending_push` se reconcilia más rápido de lo esperado (0 después de reconectar, no 3)

**Recomendación para release testing:**
- Mantener `bash scripts/test-2client-pull.sh` y `bash scripts/test-offline-reconnect.sh` en CI como smoke tests de regresión
- Monitorear `docker logs engram | grep SyncManager` durante pruebas de aceptación
- Verificar `GET /sync/status` devuelve `consecutive_failures: 0` después de 5+ minutos de uptime

---

### ENG-306 — Trazabilidad en backlog (P1, chore)

**Problema:** Hoy no hay forma de saber de dónde nace cada desarrollo, qué problema resuelve ni cómo probarlo. Si alguien nuevo toma un ENG, no entiende el contexto.

**Para qué sirve:** Que cualquier persona (contribuidor, mantenedor, yo dentro de 3 meses) entienda qué hay que hacer, por qué y cómo verificarlo sin tener que preguntar.

**Stories:**
- [ ] Template de feature con "Problema", "Para qué sirve", "Cómo probar"
- [ ] Columna Origen en la tabla
- [ ] ENG-207→ENG-306 descripciones completas (este documento)

**Hecho cuando:** cualquier ENG pendiente se puede entender sin contexto adicional.

---

### ENG-301 — Instalador (P1, junio)

**Problema:** Hoy para probar Engram necesitás tener .NET SDK, clonar el repo, compilar, y configurar MCP a mano. Eso es una barrera enorme para cualquiera que quiera probarlo.

**Para qué sirve:** Que un usuario sin conocimientos de .NET pueda instalar Engram en 5 minutos y tenerlo funcionando con su editor.

**Stories:**
- [ ] Publicar `engram.exe` estable (win-x64 / linux-x64) en release GitHub
- [ ] Script/MSI que instale en PATH
- [ ] Ejecutar wizard post-install (local vs sync)
- [ ] Escribir configs en `config/mcp/generated/` + opción copiar a Cursor

**Cómo probar:**
```bash
# En una máquina SIN .NET SDK:
# 1. Descargar release de GitHub
# 2. Ejecutar instalador
# 3. Correr wizard
# 4. Verificar que `engram --version` funciona
# 5. Verificar que el MCP se conecta al editor
```

**Hecho cuando:** usuario sin .NET SDK puede instalar y conectar MCP en &lt;10 min.

---

### ENG-302 — Wizard gráfico (P1, junio) → Depende de ENG-301

**Problema:** Configurar Engram (modo local vs offline-first sync, PostgreSQL, etc.) requiere editar JSON a mano o leer documentación. El wizard actual es solo CLI.

**Para qué sirve:** Que cualquier usuario pueda elegir modo local o sync, ingresar sus datos y tener el MCP configurado sin leer docs.

**Stories:**
- [ ] UI modo local vs offline-first
- [ ] Input de variables (server URL, user, etc.)
- [ ] Generar mcp.json automáticamente
- [ ] Opción "copiar al portapapeles" o "guardar en editor"

**Cómo probar:** Ejecutar wizard, elegir modo sync, ingresar URL falsa, verificar que genera un mcp.json válido.

**Hecho cuando:** un usuario no técnico configura Engram sin abrir la terminal.

---

### ENG-303 — Guía instalación unificada (P1, junio) → Depende de ENG-301

**Problema:** Hoy la documentación de instalación está dispersa entre QUICK-START, SETUP-WIZARD, MCP-CONFIG y INSTALL.md. Un usuario nuevo no sabe por dónde empezar.

**Para qué sirve:** Una sola página que lleve al usuario desde "nunca usé Engram" hasta "tengo el MCP funcionando".

**Cómo probar:** Darle la guía a alguien que no conoce el proyecto y pedirle que instale. Medir tiempo y frustración.

**Hecho cuando:** un usuario nuevo sigue la guía y tiene Engram funcionando en &lt;15 min sin preguntar.

---

### ENG-304 — Versiones centralizadas (P1, chore)

**Problema:** Hoy las versiones de dependencias y del proyecto están dispersas: `Directory.Build.props` no existe, `global.json` no está, el versionado es manual en cada `.csproj`.

**Para qué sirve:** Que el build sea reproducible y las dependencias estén centralizadas en un solo lugar.

**Stories:**
- [ ] Crear `Directory.Build.props` con versiones comunes
- [ ] Crear `global.json` con SDK de .NET 10
- [ ] Centralizar `ModelContextProtocol` y otras packages

**Cómo probar:**
```bash
dotnet build -c Release
# → debe compilar sin cambios de versión manual
```

**Hecho cuando:** `dotnet build` funciona y todas las versiones están en un solo lugar.

---

### ENG-425 — Server-side mutation apply (P0)

**Problema:** El servidor (PostgresStore) solo almacena mutations en `cloud_mutations` pero NO las aplica a su store. Los agentes en centralized mode ven datos vacíos.

**Para qué sirve:** Que el servidor sea la memoria compartida del equipo. Cualquier agente puede consultar y ver todas las memorias sync-eadas.

**Contexto:** ADR-002 decidió que para equipos, el servidor debe aplicar mutations a su PostgresStore. Sin esto, FlowForge y otros agentes trabajan a ciegas.

**Stories:**
- [ ] `InsertMutationBatchAsync` aplica cada mutation a PostgresStore además de `cloud_mutations`
- [ ] Observation upsert/delete con `sync_id` como canonical ID
- [ ] Session upsert/delete
- [ ] Prompt upsert/delete
- [ ] FK handling: observation/prompt referencing session via `sync_id`
- [ ] Tests: verificar que mutations se aplican a PostgresStore

**Cómo probar:**
```bash
# Cliente A crea memoria y hace push
docker run ... save "decisión arquitectura" --project team/mi-api
# SyncManager hace push

# En otra máquina, Cliente B consulta centralized mode
curl http://servidor:7437/observations/recent?project=team/mi-api
# → debe ver la memoria de A ✅
```

**Hecho cuando:** centralized mode muestra todas las memorias del equipo, no vacío.

---

### ENG-426 — ID mapping strategy para server-side apply (P0, depende de ENG-425)

**Problema:** Cuando el cliente hace push, usa `sync_id` local. El servidor necesita una estrategia para mapear/almacenar estos IDs.

**Decisión pendiente:** Usar `sync_id` como canonical ID o mapear a server ID secuencial.

**Stories:**
- [ ] Diseñar estrategia de ID mapping
- [ ] Implementar según estrategia elegida
- [ ] Verificar que queries por `sync_id` funcionan en PostgresStore
- [ ] Tests de roundtrip completo (push → server apply → pull → apply local)

---

### ✅ ENG-202 — OSS essentials (Done)

**Entregado:**
- [x] `CONTRIBUTING.md` — build, test, PR flow
- [x] `CODE_OF_CONDUCT.md` — Contributor Covenant
- [x] `SECURITY.md` — vulnerabilidades
- [x] `CONTRIBUTORS.md` — lista abierta
- [x] README.md / README.es.md actualizados

---

### ✅ ENG-206 — PostgreSQL tests (Done)

**Entregado:**
- [x] `MergeProjects_ReassignsObservations` — arreglado (ID dinámico)
- [x] `Search_TopicKeyShortcut_RanksFirst` — arreglado (rank 10000 para Postgres)
- [x] `DeleteSession_HasActiveObservations_Throws` — eliminado (incompatibilidad Npgsql)

---

### ✅ ENG-106 — Hub MCP multi-editor (Done)

**Entregado:**
- `config/mcp/INSTALL.md`, `editors/*`, `scripts/setup.ps1`, `setup.sh`
- Wizard genera todos los JSON en `generated/`

---

## Relación con otros documentos

| Documento | Rol |
|-----------|-----|
| [ROADMAP.md](ROADMAP.md) | Visión, fases, ideas futuras |
| **BACKLOG.md** (este) | **Orden de ejecución y estado** |
| [TECHNICAL-DEBT.md](TECHNICAL-DEBT.md) | Deuda de código (detalle técnico) |
| [SETUP-WIZARD.md](SETUP-WIZARD.md) | Guía usuario post-clone |
| `sdd/` | Specs por feature antes de implementar |

---

## Icebox (P2 — post v1.0.0)

Items en P2 / Icebox con descripción breve. No para release de junio; referencia rápida si alguien los toma en el futuro.

### ENG-401 — Backend config file (P2)

**Problema:** Hoy el backend (SQLite vs Postgres vs HttpStore) se elige por env vars. Para un usuario no técnico, eso es barrera.
**Para qué sirve:** Que `~/.engram/config.json` sea el switch entre backends, con detección automática + override.
**Spec:** [sdd/backend-config-switch/proposal.md](../sdd/backend-config-switch/proposal.md)

---

### ENG-402 — Giant class refactor (P2, chore)

**Problema:** `SqliteStore.cs` y `PostgresStore.cs` son enormes (1.5k–2k líneas cada uno). Mantenibilidad sufre.
**Para qué sirve:** Partir en partials por dominio (sessions, observations, prompts, sync, retention, projects). Sin cambiar API pública.
**Ref:** [TECHNICAL-DEBT.md TD-001/002](TECHNICAL-DEBT.md)

---

### ENG-403 — Phase 3 API breaking (P2)

**Problema:** Hoy `project` se puede pasar en cada write. Diseño actual permite inconsistencia (mismo obs con project diferente en cada write).
**Para qué sirve:** Quitar `project` de writes — usar siempre el proyecto detectado por contexto (env, git, manual). Más simple, menos ambigüedad.
**Riesgo:** Breaking change. Requiere guía de migración + deprecation period.

---

### ENG-404 — Memory relations (P1)

**Problema:** Las memorias son nodos aislados. No hay forma de decir "esta memoria corrige esta otra" o "depende de".
**Para qué sirve:** Grafo de memorias con relaciones tipadas (`depends_on`, `supersedes`, `conflicts_with`, `related_to`). Permite queries estructurales (lineage, cycle detection, contradiction surfacing).
**Evidence del spike (2026-06-18):** 291 líneas de código clonando el patrón de `Engram.Verification` (TraceRepository + LineageBuilder). 6/6 tests pasan en 216ms. Cero cambios de schema. Effort re-estimado: **XL → M**.
**Diseño decisions pendientes:** inverse traversal (follow-up M), MCP tool API shape, sync semantics, validation rules on insert, retention budget. Ver [spike learnings](../.ai-work/eng-404-spike/learnings.md).
**Código existente:** `src/Engram.Verification/MemoryRelation*.cs` (spike code, punto de partida para la feature real).

---

### ENG-405 — Authentication & access control (P2)

**Problema:** Hoy cualquiera con acceso a la API puede leer/escribir todo (excepto user scoping en `/prompts/recent` que se agregó en ENG-204).
**Para qué sirve:** JWT o API keys + roles (admin, read-write, read-only). Necesario para deployments públicos.
**Bloqueo:** No hay proposal aún. Spec primero.

---

### ENG-406 — Tool deferral MCP (P2)

**Problema:** El SDK .NET de MCP no soporta aún "tool deferral" (cargar tools bajo demanda en vez de upfront).
**Para qué sirve:** Reducir TTFB del MCP en proyectos con 26+ tools. Carga solo las relevantes al contexto.
**Bloqueo:** Esperar feature del SDK. Track upstream.

---

## Changelog del backlog

| Fecha | Cambio |
|-------|--------|
| 2026-06-16 | **ENG-210 Done**: Validado `scripts/test-offline-reconnect.sh` end-to-end (3/3 memorias offline recuperadas). Backlog consolidado: eliminadas 3 filas duplicadas (ENG-209/210/427), notación de salud de sync actualizada. |
| 2026-06-15 | **ENG-428 Done**: Fix JsonPullOpts SnakeCaseLower — session_id ≠ sessionId rompía push. Test 2-client pasa end-to-end. |
| 2026-06-15 | **ENG-211 Done**: AddColumnIfNotExists para sync_apply_deferred. |
| 2026-06-15 | **ENG-208 Done + push**: Upstream Phase 2 parity completo (structured errors, incremental, watch, since). 5 commits. |
| 2026-06-15 | **ENG-428 agregado**: Null session_id en mutation push bloquea sync pull. |
| 2026-06-15 | **ENG-421/427 marcados Done**: ApplyPulledMutationAsync (ENG-425), ListMutationsSince SQL (ENG-426). |
| 2026-06-15 | **ENG-209/210 dockerizados**: scripts/test-2client-pull.sh + test-offline-reconnect.sh. |
| 2026-06-09 | **ENG-425 Done**: Server-side mutation apply implementado y verificado (40/40 tests, PM-1 a PM-7 manuales). Servidor aplica mutations a PostgresStore además de cloud_mutations. sync_id como canonical ID. |
| 2026-06-09 | **ENG-426 Done**: ID mapping strategy verificado (V1-V6). sync_id como canonical — queries funcionan en todos los paths REST. |
| 2026-06-09 | **ENG-427 agregado**: Bug en ListMutationsSinceAsync — SQL syntax error con project filter (ANY array). Fix ya aplicado en PostgresStore.cs L1703. |
| 2026-06-07 | **DECISIÓN ADR-002**: Option B — Server apply mutations a PostgresStore. Prioridad P0. ENG-425 y ENG-426 agregados al backlog. |
| 2026-06-07 | ENG-425: Server-side mutation apply (P0). ADR-002 decisión: para equipos, el servidor debe ser memoria compartida. Centralized mode funciona correctamente. |
| 2026-06-07 | ENG-426: ID mapping strategy para server-side apply (P0, depende de ENG-425). |
| 2026-06-07 | ENG-425 agregado: Servidor como relay puro — mutations no se aplican a PostgresStore. ADR-002 creado con análisis completo. Contradicción: OFFLINE-FIRST-SYNC.md dice "server is source of truth" pero implementación no aplica mutations. |
| 2026-06-06 | ENG-304, ENG-306 Done: versions centralizadas + trazabilidad backlog. ENG-307 (test scripts: regression-test.sh 31 checks + dev-test.sh T3 gate) y ENG-308 (AGENTS.md + DEVELOPMENT.md) agregados al backlog. |
| 2026-06-05 | ENG-207 cerrado: Logging infrastructure (5 FRs, PM-*, ~3-4h, código no desplegado) |
| 2026-06-05 | Logging infrastructure implementado: JSON logging, client_ip, body preview, ENGRAM_LOG_LEVEL env var. |
| 2026-06-05 | ENG-306 cerrado: secciones detalladas para ENG-4xx (Icebox). |
| 2026-06-05 | ENG-211 fileado: SyncManager SQLite schema mismatch (bug descubierto gracias a logs JSON). |
| 2026-06-05 | PRD Memoria Semántica v1.1 documentado (10 puntos) + RFC-001 Project Identity + ENG-410..418 agregados al backlog. |
| 2026-06-04 | Verificación manual post-deploy: smoke test + 5 regression tests (R1-R5) contra `192.168.0.178:7437`, todos OK. Checklist actualizado. |
| 2026-05-28 | Sesión completa pre-release: ENG-202→206, ENG-305 Done. Columna Origen agregada. |
| 2026-05-28 | Creación del backlog ordenado; ítems de sesión pre-release y meta junio |
| 2026-05-27 | Fixes doc/MCP/Docker/Obsidian (commits en main) |
