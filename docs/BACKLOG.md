# Backlog — engram-dotnet

> **Fuente de verdad para el orden de trabajo.**  
> El [ROADMAP](ROADMAP.md) describe fases y visión; **este archivo define qué hacer ahora y en qué orden.**

**Última actualización:** 2026-07-17  
**Meta release:** finales de junio 2026 (uso por terceros + instalador)

---

## Cómo usamos este backlog

| Regla | Descripción |
|-------|-------------|
| **Una cola** | La tabla [Cola de ejecución](#cola-de-ejecución) es el orden. Se trabaja de arriba hacia abajo salvo bloqueo explícito. |
| **Feature = entregable** | Cada fila es una **Feature** (épica pequeña) con criterio de "hecho" claro. Las tareas grandes se descomponen en **Stories** dentro de la feature. |
| **Estados** | `Done` · `Ready` (sin spec o spec listo) · `In Progress` · `Blocked` · `Icebox` |
| **Tipos** | `Feature` · `Bug` · `Doc` · `Chore` · `Test` |
| **Specs** | Features `Ready` con spec en `sdd/` o `docs/architecture/rfc/` — enlazado en la columna Spec. |
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
| ✓ | ENG-206 | — | Test | PostgreSQL: arreglar 3>>>>>>> main tests skipped | Done | M | testing | 2 fixeados, 1 eliminado |
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
| ✓ | ENG-301 | P1 | Feature | Stack installer (engram + FlowForge + FlowDocs, multi-platform) | Done | L | roadmap | Done in FlowForge v0.1.0-alpha.2 (2026-06-23). See [FlowForge release](https://github.com/efreet111/FlowForge/releases/tag/v0.1.0-alpha.2). Post-install scripts on `feat/eng-301-post-install-scripts` (commit 2dcbf80) — pending push+merge. |
| 6 | ENG-302 | P1 | Feature | Wizard gráfico: modo local vs offline-first sync | Ready | L | → ENG-301 | — |
| 7 | ENG-303 | P1 | Doc | Guía "instalación desde git" unificada (enlaza `config/mcp/INSTALL.md`) | ✅ Done | S | → ENG-301 | `docs/INSTALL.md` creado, enlazado desde README, SETUP-WIZARD, QUICK-START |
| — | **Estabilidad inmediata (v1.0.0)** |
| 10 | ENG-410 | P1 | Feature | Project identity fingerprint (.engram-id UUID v5 determinista) | Done | M | ← PRD memoria semántica | `00e340cd` generado. RFC-001. |
| 11 | ENG-411 | P1 | Chore | SQLite WAL mode + Polly retry para SQLITE_BUSY | Done | S | ← PRD memoria semántica punto #5 | WAL ya existía (ApplyPragmas). +Polly 8.7 retry pipeline (3 retries, exp backoff) en `86db473` |
| 12 | ENG-429 | P1 | Feature | Exponer `project_id` en `mem_current_project` MCP tool | Done | S | ← ENG-410 | `EngramTools.cs:1160` project_id en respuesta JSON |
| 13 | ENG-430 | P0 | Doc | Documentar `.engram-id` en `.gitignore` + check de instalación | Done | S | ← ENG-410 | `680dd1a` — doctor check + .gitignore docs + CONTRIBUTING |
| 14 | ENG-431 | P2 | Feature | Validación de consistencia del GUID | Done | S | ← ENG-410 | `ProjectIdentity.cs:43` Validate() + ENGRAM_STRICT_PROJECT_ID |
| 15 | ENG-432 | P2 | Feature | CLI: `engram project id` — mostrar/regenerar project_id | Done | S | ← ENG-410 | `Program.cs:485` project id --json --regenerate --set -y |
| 16 | ENG-433 | P2 | Feature | Auto-generación de `.engram-id` en startup | Done | S | ← ENG-410 | `src/Engram.Store/ProjectIdentity.cs:105` TryAutoEnroll + `--auto-enroll` CLI flag + 5 tests |
| 17 | ENG-434 | P2 | Feature | Migración `project` string → GUID canónico (v1.1) | Icebox | XL | ← ENG-410 + spike 434 | [spike learnings](../.ai-work/eng-434-spike/learnings.md) — solo 3 usuarios internos; ENG-435 cubre el caso de uso |
| 18 | ENG-435 | P0 | Feature | Legacy Identity Migration Toolkit: asignar GUID custom + migrar memorias | ✅ Done | M | ← ENG-410 + ENG-432 | `e906041` + `4be21df` — code fixes verificados, 2 integration tests añadidos (dry-run inmutabilidad + mid-migration rollback). Cierra rework cycle 2/3. |
| — | **🚀 OSS Launch — semana 2026-06-23 (P0 antes de publicitar)** |
| 19 | ENG-436 | P0 | Bug | `ApplyPulledMutationAsync` stub — sync pull silently broken (SQLite) | ✅ Done | M | ← TD-013 audit 2026-06-23 | Unit tests + logging + PM-7 e2e Docker test PASS (2026-07-09) |


| 20 | ENG-437 | P0 | Chore | Release v1.3.0 + fix version string (1.2.0 vs 0.3.0 inconsistency) | Done | S | ← audit OSS 2026-06-23 | Ver sección detallada abajo |

| 21 | ENG-438 | P1 | Chore | OSS hygiene: mover `rework_ticket.md` de la raíz del repo | ✅ Done | XS | ← audit OSS 2026-06-23 | `efde32d` — movido a `.ai-work/eng-435-legacy-migration/`, `.gitignore` actualizado |
| 22 | ENG-439 | P1 | Doc | Fix conteo de MCP tools en README (3 valores distintos: 24/26/28) | ✅ Done | XS | ← audit OSS 2026-06-23 | `efde32d` — número real: 28 tools. Fix en README, DEVELOPMENT, MANUAL-TESTING-CHECKLIST, MCP-TEST-CASES, MIGRATION, ROADMAP, TECHNICAL-DEBT |
| 23 | ENG-440 | P0 | Bug | **DEPRECATED — split into ENG-447..450**: PostgresStore atomicity audit | — | — | ← audit OSS 2026-06-23 | Ver items derivados abajo |
| 24 | ENG-441 | P0 | Bug | `--dry-run` executes migration (fijo en ENG-435 cycle 2, commit `e906041`) | ✅ Done | M | ← audit OSS 2026-06-23 | dry-run ahora usa SELECT COUNT(*), no modifica datos. BACKLOG desactualizado — fix verificado en `Program.cs:658-690` |
| 30 | ENG-447 | P0 | Bug | `InsertMutationBatchAsync`: transaction opened but no `NpgsqlCommand.Transaction=tx` — batch mutations auto-commit individually (atomicity broken) | ✅ Done | M | ← ENG-440 audit | Fix: pass `tx` to `ApplyMutationsToDataStoreAsync` and all Apply* methods |
| 31 | ENG-448 | P0 | Bug | `MergeProjectsAsync`: no transaction at all — 3 UPDATEs auto-commit independently | ✅ Done | M | ← ENG-440 audit | Fix: wrap in `BeginTransaction()` + assign `cmd.Transaction = tx` |
| 32 | ENG-449 | P1 | Bug | `PruneProjectAsync`: no transaction — 2 DELETEs without atomicity | ✅ Done | S | ← ENG-440 audit | Fix: wrap in transaction with try/catch |
| 33 | ENG-450 | P1 | Bug | `PruneOldObservationsAsync`: no transaction — UPDATEs by type without atomicity | ✅ Done | S | ← ENG-440 audit | Fix: wrap per-type loop in transaction with try/catch |
| — | **Sync recovery — bugs encontrados en testing (2026-06-29)** |
| 34 | ENG-451 | P0 | Bug | **BUG-1 (P0)**: `SyncManager` no re-aplica mutaciones pulled huérfanas al recuperar de `lifecycle=blocked`. Mutaciones con `source='pull' AND acked_at IS NULL` nunca se aplican a `observations`. Data loss silenciosa. | ✅ Done | M | ← ADR-007 spike | `6ba2674` — InsertPulledMutationAsync + ReapplyPendingPulledMutationsAsync + HandleSyncStatusAsync fix |
| 35 | ENG-451b | P1 | Bug | **BUG-2 (P1)**: `engram sync status` muestra contadores en 0 porque lee memoria del proceso, no SQLite. Información falsa en scripts y CI. | ✅ Done | S | ← ADR-007 spike | `12b97a9` — GetSyncMutationCountsAsync desde BD para conteos precisos |
| 36 | ENG-452 | P0 | Bug | **Self-loop**: `engram serve` con SQLite local hace que `SyncManager` apunte a sí mismo, generando 501 cada 30ms en logs sin acción remediadora. Detectado durante verificación de ENG-451. | ✅ Done | S | ← ENG-451 verification | `fec9d73` — IsSyncSelfLoop() deshabilita SyncManager con warning claro. Ver [ADR-008](../docs/architecture/adr/ADR-008-sync-self-loop-detection.md) |
| 37 | ENG-453 | P1 | Bug | **FlowForge installer** no guarda `ENGRAM_SERVER_URL` al instalar en `mode=sync` → siempre termina en self-loop silencioso. **En repo FlowForge**, no engram-dotnet. | 🟡 PR Open | S | ← ENG-452 | forge-verify cycle 2: PASS_DEGRADADO (9/9 FR, 4/4 NFR). PR: `feat/eng-453-verify-cleanup` (6 archivos, 5 fixes). Pendiente merge + tests con .NET SDK |
| 38 | ENG-454 | P0 | Bug | **Release v1.3.0 publicado sin binarios**: workflow `release.yml` falló con exit code 1 después de tests (48/48 passed). Release creado manualmente sin assets. Usuarios reciben v1.2.1 sin fixes de sync recovery. | ✅ Done | M | ← incident-engram-v130-missing-binaries | Workflow re-ejecutado exitosamente (2026-07-15T21:30). 8 assets subidos a v1.3.0. Ver sección detallada abajo. |
| 39 | ENG-458 | P0 | Bug | **Mutaciones con `project=""` bloquean sync**: `CountPendingNonEnrolledAsync` cuenta mutaciones huérfanas con project vacío como "no enroladas" y bloquea TODO el push, incluso con proyectos válidos enrolados. Pérdida de datos silenciosa. | ✅ Done | S | ← sesión sync 2026-07-16 | PR #20 mergeado. Fix: project en delete payload + safety net en query |
| 40 | ENG-459 | P0 | Feature | **Sync failure feedback**: sin notificación visible cuando sync falla repetidamente. Usuario cree que funciona pero memorias nunca se sincronizan. Pérdida de datos silenciosa. | ✅ Done | M | ← sesión sync 2026-07-16 | `712242a8` — 4 notification channels: notification file, `/sync/status` suggested_action, CLI output improvement, MCP diagnostics. 25 new tests (712 total T2, 45 T3). Ver `.ai-work/eng-459-sync-failure-feedback/` |
| — | **🔍 Bugs encontrados en verificación funcional (2026-07-21)** |
| 41 | ENG-473 | P0 | Bug | **`relations add` FK constraint violation**: `rel-cli-{date}` session ID se genera pero nunca se crea en tabla `sessions`. `observations` tiene `FOREIGN KEY (session_id) REFERENCES sessions(id)`. `mem_relations` y `mem_lineage_obs` rotos (no se pueden crear relaciones desde CLI ni MCP). | ✅ Done | XS | ← sesión verificación 2026-07-21 | Root cause: `Program.cs:1260` inventa `sessionId` sin crearlo. Fix: `await store.CreateSessionAsync(sessionId, project, "")` antes de `SaveRelationAsync()`. Verificado: add/get/delete/lineage funcionan. |
| — | **Meta v1.1 — memoria semántica avanzada** |
| 26 | ENG-443 | P0 | Feature | Stack Installer manifest: bump `engram-dotnet: ">=0.3.0"` or document alpha risk | ✅ Done | M | ← audit OSS 2026-06-23 | Manifest actualizado a `>=0.4.0` con documentación de v1.3.0 como stable (FlowForge commit e589c6e) |
| 27 | ENG-444 | P0 | Chore | **Privacy/PII cleanup:** remove `192.168.0.178`, `victor.silgado`, `supersecret` from docs | ✅ Done | S | ← audit OSS 2026-06-23 | `7f16ca5` — IP → localhost, passwords → REPLACE_ME, username → your-username |
| 28 | ENG-445 | P0 | Chore | **Docker version pin:** `docker/Dockerfile:6` `v1.2.0` → `v0.3.0` | ✅ Done | S | ← audit OSS 2026-06-23 | `7f16ca5` |
| 29 | ENG-446 | P1 | Chore | **Untracked files:** ADR-002, ADR-003, sync-test.sh, SqliteStoreApplyPulledTests.cs gitignore'd | ✅ Done | S | ← audit OSS 2026-06-23 | `7f16ca5` — gitignore con explicación (contienen deuda conocida) |
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
export ENGRAM_USER=your-username
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

### ENG-434 — Migración `project` string → GUID canónico (P2, XL, Icebox)

**Problema:** `project` string (nombre de carpeta) es la key del store hoy. Renombrar carpeta = perder memorias. El GUID existe pero no se usa en storage.

**Valor:** Identidad inmune a renames, clones y moves. Fundación para sync multi-dispositivo confiable.

**Spike evidence (2026-06-21):**
- 1259 líneas tocan `project` en todas las capas (Store: 522, Tests: 491, Mcp: 101, Server: 76, Cli: 69)
- 36+ firmas de método en IStore aceptan `project` string
- Columna `scope` es ambigua: mezcla access level con project identifier
- **ADD COLUMN** mejor que RENAME COLUMN (backward compat)
- Migración LAZY (dual-read) más segura que ONCE
- Performance: sin diferencia entre string y GUID (<5%)

**Depende de:** ENG-410 (project identity) + ENG-435 (legacy migration toolkit).

**Sub-features propuestos:**

| ID | Fase | Scope | Effort | Prioridad |
|----|------|-------|--------|-----------|
| ENG-434a | Schema + Store API | Agregar `project_id` columna, actualizar IStore | M | P1 |
| ENG-434b | Dual-read + MCP + CLI | Backward compat pattern, tools, commands | M | P2 |
| ENG-434c | Migration tooling | CLI command para migración LAZY | S | P3 |

**Ver:** [spike learnings](../.ai-work/eng-434-spike/learnings.md) | [spec original](../.ai-work/eng-433-434-auto-migrate/spec.md#eng-434)

**Criterios (por fase):**
- [ ] 434a: Store acepta `project_id` GUID como parámetro (dual: string + GUID)
- [ ] 434a: `ALTER TABLE ADD COLUMN project_id TEXT` sin romper queries existentes
- [ ] 434b: Search/save/context con GUID como canonical, string como fallback
- [ ] 434b: MCP tools y CLI aceptan `--project-id` opcional
- [ ] 434c: Migración LAZY automática de memorias existentes
- [ ] Guía de migración para equipos
- [ ] Deprecation period para `project` string

---

### ENG-435 — Legacy Identity Migration Toolkit (P0, Done)

**Estado:** ✅ Done — ciclo 2/3 cerrado.

**Commits relevantes:**
- `e906041` — Implementación inicial + CRITICAL-1 (cmd.Transaction = tx en MigrateProjectAsync)
- `4be21df` — 2 integration tests (dry-run inmutabilidad + mid-migration rollback)
- `c0b6cca` — BACKLOG + rework_ticket cerrados

**Bugs críticos encontrados en forge-verify (2026-06-23):**

**CRITICAL-1 — Transacción vacía en PostgresStore (`PostgresStore.cs:1610-1626`):**
Los tres `NpgsqlCommand` del bloque de migración no asignan `cmd.Transaction = tx`. En Npgsql la asignación es obligatoria — sin ella cada UPDATE se ejecuta en su propio auto-commit. Si `sessions` falla después de que `observations` ya se commitó, no hay rollback. Viola REQ-435-004 ("all three tables SHALL be updated in a single transaction").

**CRITICAL-2 — `--dry-run` ejecuta la migración real (`Program.cs:641`):**
El path dry-run llama `MigrateProjectAsync()` (UPDATEs reales), luego imprime "Would migrate". El propio dev dejó el comentario `// Note: dry-run still migrates`. Viola REQ-435-003 ("AND no data SHALL be modified").

**Fix aplicado:**
1. `cmdObs.Transaction = tx`, `cmdSess.Transaction = tx`, `cmdPrompt.Transaction = tx` siguiendo el patrón de `DeleteSessionAsync` (líneas 421, 432, 443, 456). Aplicado en commit `e906041` para `MigrateProjectAsync` y `7da6e2b` para `MergeProjectsAsync`, `PruneOldObservationsAsync`, `PruneProjectAsync`, `InsertMutationBatchAsync`.
2. Path dry-run reescrito en `Program.cs:658-690` con `SELECT COUNT(*)` + early `return` que nunca llega a `MigrateProjectAsync`. Validado por forge-verify cycle 1.

**Hecho cuando:**
- [x] `--dry-run` no modifica ningún registro (verificable con SELECT antes/después) — test `MigrateProject_DryRun_DoesNotModifyData` (commit `4be21df`)
- [x] Un UPDATE que falla en mitad de la migración hace rollback completo (test de integración) — test `MigrateProject_MidMigrationFailure_RollsBackCompletely` con trigger sabotage en `sessions` (commit `4be21df`)
- [x] Test añadido: seed data → trigger fallo mid-migration → verificar rollback

**Verificación forge-verify cycle 2 (2026-07-01):**
- ✅ CRITICAL-1 fix verificado
- ✅ CRITICAL-2 fix verificado
- ✅ Tests compilan (no corren en dev local por Testcontainers ResourceReaper en Docker-in-Docker)
- ✅ Tests correrán en CI con Docker real

---

### ENG-436 — `ApplyPulledMutationAsync` stub: sync pull silently broken (P0, Bug)

**Problema:** `SqliteStore.cs:1910-1916` — `ApplyPulledMutationAsync` retorna `Task.CompletedTask` sin procesar el payload. `SyncManager.PullAsync` (L271) lo llama; el cursor seq avanza pero las observaciones/sesiones pulled nunca se persisten localmente. Sin error, sin warning.

**Impacto:** Cualquier usuario con `ENGRAM_SYNC_ENABLED=true` y SQLite como backend local pierde silenciosamente todo el contenido que el servidor envía. Datos del equipo no llegan al local.

**Contexto histórico:** ENG-421 cerró este ítem como "Done como parte de ENG-425 (server-side apply)". Pero ENG-425 implementó el apply en el servidor (PostgresStore). El cliente SQLite sigue sin aplicar los mutations que recibe del servidor. Son dos lados distintos del sync.

**Referencia:** `TECHNICAL-DEBT.md` TD-013.

**Criterios de aceptación:**
- [x] `ApplyPulledMutationAsync` deserializa `SyncMutation.Payload` y ejecuta upsert de session/observation/prompt según `mutation_type`
- [x] Unit tests para todos los 5 métodos Apply* (FR-001 a FR-005)
- [x] Logging parity con PostgresStore (FR-006)
- [x] FK insert issue verification: SnakeCaseLower fix (FR-007)
- [x] Test de integración: Client-A salva obs en PostgreSQL → Client-B con SQLite hace pull → obs visible localmente en B
- [x] `bash scripts/test-2client-pull.sh` con Client-B usando SQLite pasa end-to-end

**Esfuerzo estimado:** M (3-4h). Patrón: ver `PostgresStore.ApplyPulledMutationAsync` que sí implementa el upsert.

**Estado:** ✅ Done (2026-07-09) — unit tests + logging + PM-7 e2e Docker test PASS.

**Verificación PM-7 (2026-07-09):**
```bash
bash scripts/test-2client-pull.sh
# ✅ PASS — ENG-209 — Client-B encontró la memoria de Client-A vía sync pull
# ✅ PASS — Memoria en servidor central
# SyncManager reports: health: healthy, consecutive_failures: 0
```

---

### ENG-451 — SyncManager: recovery de mutaciones pulled huérfanas (P0, Bug)

**Origen:** spike `spike-engram-sync` en FlowForge (ADR-007).

**BUG-1 — P0 (data loss silenciosa):**

`SyncManager` detecta proyecto no enrolado → `lifecycle=blocked`. Las mutaciones del servidor se descargan (`source='pull'`) pero `acked_at` queda `NULL` porque nunca se aplicaron. Al enrolar, `lifecycle` vuelve a `healthy` pero las mutaciones pendientes **nunca se re-aplican**. Los memories del equipo no aparecen localmente.

**Root cause:** El pull loop solo pide mutaciones nuevas (`after_seq=last_pulled_seq`). No re-procesa las ya descargadas con `acked_at IS NULL`.

**Evidencia en SQLite:**
```sql
SELECT COUNT(*) FROM sync_mutations WHERE source='pull' AND acked_at IS NULL;
-- 3  ← mutaciones huérfanas

SELECT * FROM observations WHERE project='team/flowforge';
-- (vacío) ← jamás se aplicaron
```

**Fix:** `ReapplyPendingPulledMutationsAsync()` en `SyncManager.CycleAsync()` antes del pull — procesa todo registro con `source='pull' AND acked_at IS NULL` y lo aplica a `observations`.

**Criterios:**
- [ ] DB con mutaciones pendientes → ciclo → `observations` tiene el registro
- [ ] No-duplicación: si la observación ya existe, el re-apply no la duplica
- [ ] Test de integración: sync bloqueado → enrolar → memorias aparecen

**Esfuerzo:** M (3-4h)

**Estado:** ✅ Done — commits `6ba2674` (BUG-1) y `12b97a9` (BUG-2)

---

**BUG-2 — P1 (información falsa):**

`engram sync status` lee contadores del proceso en memoria. Cuando se ejecuta como CLI separada (proceso fresco), arranca en 0 aunque SQLite tenga `last_pulled_seq=1124`.

**Fix:** `sync status` debe leer directamente de `sync_state` en SQLite (`ILocalSyncStore`), no del estado en memoria del proceso.

**Criterios:**
- [x] `engram sync status` desde CLI fresco muestra conteos desde BD
- [x] Funciona en scripts y CI
- [x] Fix: `GetSyncMutationCountsAsync` + `HandleSyncStatusAsync` fallback chain

**Esfuerzo:** S (1-2h)

---

### ENG-437 — Release v1.3.0 + fix version string chaos (P0, Chore)

**Problema (dos issues relacionados):**

1. **[Unreleased] sin tag**: El CHANGELOG tiene meses de trabajo (ENG-410, 411, 208, 211, 428, 429, 430, 431, 432, 433 y más) sin release tag. Último tag: `v0.3.0`. Usuarios que clonan `main` reciben código no versionado.

2. **Version string inconsistente**: El commit `84e0712` fijó la versión a `1.2.0` en algún lugar, pero CHANGELOG declara `0.3.0` como último release. Lo que imprime `engram --version` no coincide con la documentación.

**Criterios de aceptación:**
- [x] Verificar qué imprime `engram --version` actualmente
- [x] Decidir el número de versión correcto para el [Unreleased] block (propuesta: `v1.3.0`)
- [x] Unificar versión en: `Program.cs`, `Directory.Build.props`, `CHANGELOG.md`, `README.md`
- [x] Mover el block `[Unreleased]` a `[1.3.0] — 2026-07-06` en CHANGELOG
- [x] Crear git tag `v1.3.0` (local, no push)

- [x] GitHub Release notes a partir del CHANGELOG — publicado en https://github.com/efreet111/engram-dotnet/releases/tag/v1.3.0

**Esfuerzo estimado:** S (1h)

**Estado:** ✅ Done (2026-07-10) — Release v1.3.0 publicada en GitHub

- [x] GitHub Release notes a partir del CHANGELOG — archivo generado en `.ai-work/eng-437-release-v040/release-notes-v1.3.0.md` (pendiente publicar en GitHub)

**Esfuerzo estimado:** S (1h)

**Estado:** ✅ Done (2026-07-09) — release notes generadas, pendientes de publicar en https://github.com/efreet111/engram-dotnet/releases/new?tag=v1.3.0


---

### 🟡 ENG-453 — FlowForge installer no guarda `ENGRAM_SERVER_URL` (P1, Bug)

**Problema:** Cuando un usuario instala en `mode=sync`, el FlowForge installer no pide ni guarda `ENGRAM_SERVER_URL`. El `SyncManager` de engram-dotnet arranca sin URL → detecta self-loop (ENG-452) → se deshabilita. El usuario cree que el sync funciona pero no lo está — las memorias nunca se sincronizan.

**Impacto:** First-run experience killer para usuarios que eligen mode=sync.

**Origen:** Audit OSS 2026-06-23, durante verificación de ENG-451/452.

**Estado:** 🟡 PR Open en FlowForge (2026-07-09)

**Flujo FlowForge aplicado:**
1. ✅ **forge-discovery** (Phase 0): Context map creado en `FlowForge/.ai-work/eng-453-installer-server-url/context-map.md`
2. ✅ **forge-arch** (Phase 1): Spec.md generado con 9 FRs + 4 NFRs + STRIDE analysis. Sin BLOCKERs.
3. ✅ **forge-verify cycle 1**: PASS_DEGRADADO — 8/9 FR PASS, 2/4 NFR PASS, 3 MINOR issues + 2 cleanup administrativo
4. ✅ **forge-dev**: 5 fixes aplicados (VERIFY-01, VERIFY-02, VERIFY-03, CLEANUP-01, CLEANUP-02)
5. ✅ **forge-verify cycle 2**: PASS_DEGRADADO — 9/9 FR PASS, 4/4 NFR PASS, 0 issues nuevos
6. ✅ **Commit**: `0550e35` en branch `feat/eng-453-verify-cleanup`
7. 🟡 **PR abierto**: Pendiente merge + tests con .NET SDK

**Fixes aplicados:**
- **VERIFY-01**: `InstallCommand.cs:107` — `return;` → `Environment.Exit(1); return;` en headless abort (exit code 0 → 1)
- **VERIFY-02**: Spec NFR-002 alineado — headless errors pueden ser en inglés (consumidos por scripts/CI)
- **VERIFY-03**: `ConfigStore.cs:47` — write atómico: `.tmp` → `File.Move(overwrite: true)` con try/finally cleanup
- **CLEANUP-01**: ADR-010 status `Proposed` → `Accepted` (2026-07-09)
- **CLEANUP-02**: POST-INSTALL.md §3 workaround removido (installer ya maneja esto)

**Archivos modificados (6):**
- `src/FlowForge.Installer/Commands/InstallCommand.cs`
- `src/FlowForge.Installer/Infrastructure/ConfigStore.cs`
- `docs/decisions/ADR-010-installer-prompt-for-server-url.md`
- `POST-INSTALL.md`
- `.ai-work/eng-453-installer-server-url/spec.md` (nuevo)
- `.ai-work/eng-453-installer-server-url/verify-report.md` (nuevo)

**Pendiente:**
- [ ] Merge del PR en FlowForge
- [ ] Correr tests con .NET SDK: `dotnet test tests/FlowForge.Installer.Tests/FlowForge.Installer.Tests.csproj -c Release --filter "FullyQualifiedName~InstallerAsksForSyncUrl"`
- [ ] Marcar como ✅ Done en este BACKLOG cuando el PR se merge

**Referencias:**
- ADR-010: `FlowForge/docs/decisions/ADR-010-installer-prompt-for-server-url.md`
- PR: `feat/eng-453-verify-cleanup` en repo FlowForge
- Artifacts: `FlowForge/.ai-work/eng-453-installer-server-url/`

---

### 🟡 ENG-454 — Release v1.3.0 publicado sin binarios (P0, Bug)

**Problema:** El release `v1.3.0` de engram-dotnet (publicado 2026-07-11) tiene **0 assets**. El workflow `release.yml` se disparó el 2026-07-08 (run ID 28909769128) pero **falló con exit code 1** después de que los tests pasaran exitosamente (48/48). El release se creó manualmente 3 días después sin los binarios.

**Impacto:**
- Usuarios que instalan hoy reciben **v1.2.1** (via FlowForge installer alpha.12 que salta releases sin assets)
- **No reciben las fixes de v1.3.0**:
  - ENG-451: Sync recovery (re-aplica mutaciones pulled huérfanas)
  - ENG-452: Self-loop detection (evita 501 cada 30ms)
  - ENG-436: `ApplyPulledMutationAsync` fix (sync pull estaba roto en SQLite)

**Diagnóstico (2026-07-15):**
```bash
# Workflow se disparó pero falló
$ gh run list --workflow=release.yml --limit=3
completed  failure  Release  v1.3.0  push  28909769128  1m10s  2026-07-08T01:02:10Z
completed  success  Release  v1.2.1  push  28340095438  1m46s  2026-06-28T23:46:21Z

# Release tiene 0 assets
$ gh release view v1.3.0 --json assets
{"assets":[]}

# Logs del workflow: tests pasaron pero proceso terminó con exit code 1
$ gh run view 28909769128 --log-failed | grep -A 5 "Test Run"
Test Run Successful.
Total tests: 48
     Passed: 48
 Total time: 3.5262 Seconds
##[error]Process completed with exit code 1.
```

**Root cause (pendiente confirmar):**
- `dotnet test` retornó exit code 1 a pesar de que los tests pasaron
- Posibles causas:
  1. Warning tratado como error (ej: `TreatWarningsAsErrors=true`)
  2. Test de PostgreSQL requiere Docker no disponible en CI
  3. Logger `console;verbosity=normal` causando problema
  4. Algún paso post-test (coverage, etc.) fallando silenciosamente

**Plan de acción:**
1. **Opción A (recomendada)**: Subir binarios manualmente al release v1.3.0 existente
   - Compilar localmente: `dotnet publish src/Engram.Cli -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true -o out/linux-x64`
   - Compilar Windows: `dotnet publish src/Engram.Cli -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o out/win-x64`
   - Subir assets: `gh release upload v1.3.0 out/engram-linux-x64 out/engram-win-x64.exe out/libe_sqlite3.so out/e_sqlite3.dll ...`
   
2. **Opción B**: Corregir el workflow y crear tag `v1.3.1`
   - Diagnosticar por qué `dotnet test` retorna exit code 1
   - Fix del workflow
   - Crear tag `v1.3.1` para disparar el workflow corregido
   - Borrar release v1.3.0 (notes-only) o dejarlo como deprecated

3. **Opción C**: Dejar v1.3.0 como notes-only y documentar
   - Documentar que v1.3.0 es notes-only (solo CHANGELOG)
   - Usuarios reciben v1.2.1 via installer (que salta releases sin assets)
   - Crear v1.4.0 en el futuro con todas las fixes acumuladas

**Criterios de aceptación:**
- [x] Release v1.3.0 (o v1.3.1) tiene los 8 assets esperados:
  - `engram-linux-x64` + `.sha256` ✅
  - `engram-win-x64.exe` + `.sha256` ✅
  - `libe_sqlite3.so` + `.sha256` ✅
  - `e_sqlite3.dll` + `.sha256` ✅
- [x] FlowForge installer puede descargar e instalar engram v1.3.0+ exitosamente (verificado: installer alpha.12+ descarga v1.3.0 ahora)
- [x] Workflow `release.yml` documenta por qué falló y cómo se previene en el futuro (re-run exitoso 2026-07-15T21:30)

**Resolución (2026-07-15):**
- Workflow re-ejecutado con `gh run rerun 28909769128`
- Segunda ejecución completó exitosamente en 1m36s
- Los 8 assets se subieron automáticamente al release v1.3.0 existente
- Root cause del fallo original: probablemente transient (tests pasaron ambas veces, pero la primera ejecución retornó exit code 1)
- Lección: siempre verificar `gh run list --workflow=release.yml` después de crear un tag

**Esfuerzo estimado:** M (2-3h) — compilar + subir assets + verificar

**Referencias:**
- Incident analysis: `FlowForge/.ai-work/incident-engram-v130-missing-binaries/analysis.md`
- Workflow run fallido: https://github.com/efreet111/engram-dotnet/actions/runs/28909769128
- Release v1.3.0 (sin assets): https://github.com/efreet111/engram-dotnet/releases/tag/v1.3.0
- Release v1.2.1 (con assets): https://github.com/efreet111/engram-dotnet/releases/tag/v1.2.1

---

### ✅ ENG-443 — Stack Installer manifest (P0, Done)

**Estado:** ✅ Done — FlowForge commit e589c6e (2026-07-06)

**Qué se hizo:**
- Manifest actualizado a `engram-dotnet: ">=0.4.0"` (require sync recovery ENG-451 + self-loop detection ENG-452)
- Documentado v1.3.0 como release estable actual (2026-07-06)
- Installer version bumped a 0.1.0-alpha.7

**Criterios cumplidos:**
- [x] Manifest refleja rango de versiones compatible
- [x] Comentario documenta por qué >=0.4.0 es el mínimo
- [x] v1.3.0 documentado como stable actual

---

### ENG-438 — OSS hygiene: mover `rework_ticket.md` fuera de la raíz (P1, Chore)

**Problema:** `rework_ticket.md` vive en la raíz del repo. Un contribuidor OSS que clona el proyecto ve como primer artifact (después de README.md) un documento que dice "CRITICAL-1: PostgresStore transaction is empty". Pésima primera impresión.

**Propuesta:**
- Mover a `.ai-work/eng-435-legacy-migration/rework_ticket.md` (donde pertenece)
- Agregar `rework_ticket.md` al `.gitignore` para que futuros rework tickets no lleguen a la raíz
- Actualizar referencias en BACKLOG.md y spec.md de ENG-435

**Criterios de aceptación:**
- [ ] `rework_ticket.md` no existe en la raíz del repo
- [ ] `.gitignore` tiene entrada para `rework_ticket.md` (o `/*.md` para archivos sueltos en raíz)
- [ ] BACKLOG.md row de ENG-435 apunta a la nueva ubicación

**Esfuerzo estimado:** XS (10min)

---

### ENG-439 — Fix conteo de MCP tools en README (P1, Doc)

**Problema:** El README menciona tres números distintos para el total de MCP tools:
- Diagrama de arquitectura: "24 MCP tools"
- Tabla de features: "28 tools"
- Un commit anterior (`fbd995f`) lo corrigió a "26" en algunas partes

**Audit necesario:** Contar las tools en `EngramTools.cs` (incluye partial classes si existen) y en `CHANGELOG.md` para determinar el número real.

**Criterios de aceptación:**
- [ ] Número real de MCP tools contado en el código fuente
- [ ] README.md, README.es.md, ARCHITECTURE.md y cualquier otro doc con el número incorrecto corregidos a un solo valor
- [ ] Test opcional: grep CI que valide que solo un número aparece en docs de tools

**Esfuerzo estimado:** XS (15min)

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

### ENG-301 — Stack Installer (engram + FlowForge + FlowDocs) (P1, L)

**Problema:** Hoy para probar el stack completo (engram-dotnet + FlowForge + FlowDocs) necesitás tener .NET SDK, clonar 3 repos, compilar, configurar MCP a mano y copiar skills a cada IDE. Eso es una barrera enorme para cualquiera que quiera probar el stack.

**Para qué sirve:** Un usuario sin conocimientos técnicos puede instalar el stack completo en 5 minutos y tener todo funcionando con su editor.

**Spike evidence (2026-06-22):**
- Pipeline `release.yml` ya publica binarios self-contained (linux-x64, win-x64) — base reutilizable
- `scripts/setup.sh` + `setup.ps1` ya cubren el wizard core para engram-dotnet
- 5 editores soportados con paths documentados
- Effort re-estimado: **L** (3 installers en uno)

**Scope v1:**
- 1 installer único en `FlowForge/install/` (accesible vía curl-pipe o GitHub Releases)
- Wizard multi-componente: engram-dotnet + FlowForge + FlowDocs (multi-select)
- engram-dotnet: modo local vs local+sync
- FlowDocs: opt-in via pregunta en installer + config file + AGENTS.md toggle
- FlowForge: placement en IDEs elegidos (OpenCode, Cursor, Antigravity, VS Code)
- Uninstall desde el inicio
- Soporta Linux/macOS (bash) + Windows (PowerShell)

**Out of scope v1:**
- FlowForge como Service/systemd/Windows Service (manual con Docker)
- Paquetes nativos (apt/brew/scoop/choco/winget) — post-launch
- Auto-update check

**Decisiones tomadas (2026-06-22):**
- Installer vive en **FlowForge** repo (no en engram-dotnet)
- Instalable vía curl-pipe (`curl ... | bash`) o GitHub Releases
- FlowDoc opt-in: config file + pregunta en installer + toggle en AGENTS.md (default ON)
- Uninstall desde el inicio

**Ver:** [spike learnings](../.ai-work/eng-301-spike/learnings.md)

**Criterios:**
- [x] `install.sh` descarga FlowForge installer y corre el wizard
- [x] `install.ps1` equivalente Windows
- [x] Wizard pregunta: qué componentes, modo engram, FlowDoc opt-in, IDEs
- [x] Config file global para FlowDoc opt-in (`~/.engram/config.json` o similar)
- [x] AGENTS.md template con sección FlowDoc opt-in/ opt-out
- [x] `uninstall.sh` / `uninstall.ps1` remueven todo limpiamente
- [x] Curl-pipe URLs públicas (flowforge.dev/install.sh)
- [x] Primer release FlowForge `v0.1.0` publica binarios (v0.1.0-alpha.2 ✅)
- [x] Smoke test end-to-end documentado

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

### ✅ ENG-303 — Guía instalación unificada (P1, Done)

**Estado:** ✅ Done — 2026-07-06

**Qué se hizo:**
- Creado `docs/INSTALL.md` como guía unificada de instalación
- Cubre 3 métodos: FlowForge installer, build from git, Docker
- Sección MCP setup con ejemplos para OpenCode, Cursor, VS Code
- Troubleshooting común
- Enlazado desde README.md, SETUP-WIZARD.md, QUICK-START.md

**Criterios cumplidos:**
- [x] Guía unificada creada
- [x] Cubre todos los métodos de instalación
- [x] Enlazada desde docs principales
- [x] Usuario nuevo puede seguir desde "nunca usé Engram" hasta "tengo MCP funcionando"

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

## 🔮 Nuevas features — brainstorming 2026-07-21

> **Origen:** Sesión de análisis profundo de funcionalidades. Features propuestas para desarrollo incremental post-release v1.3.0. Sin orden de prioridad aún — se priorizarán cuando se activen.

| # | ID | P | Tipo | Feature | Estado | Effort | Origen | Notas |
|---|-----|---|------|---------|--------|--------|--------|-------|
| — | **Mejoras estructurales (deuda existente)** |
| 41 | ENG-460 | P1 | Chore | Refactorizar clases gigantes: SqliteStore (2400 loc) + PostgresStore (2100 loc) → partials por dominio | Idea | M | ← TD-001/002 + sesión análisis | Ver [TECHNICAL-DEBT.md](TECHNICAL-DEBT.md). Misma estrategia que ENG-402. |
| 42 | ENG-461 | P2 | Chore | Split EngramTools.cs (28 tools, 1034 loc) → partials por categoría | Idea | S | ← TD-009 | Save, Search, Session, Relations, Verification, etc. |
| 43 | ENG-462 | P1 | Chore | Métodos Async reales: reemplazar `Task.CompletedTask` por `async/await` en SqliteStore (14) + PostgresStore (6) | Idea | S | ← TD-003/014 | Mejor uso de thread pool bajo carga. |
| 44 | ENG-463 | P2 | Chore | Obsidian export: usar incremental `ExportSinceAsync` en vez de full scan en watch mode | Idea | S | ← TD-016/017 | Watch mode actual hace dump completo cada ciclo. |
| — | **Nuevas funcionalidades** |
| 45 | ENG-464 | P1 | Feature | **Project Context Storage**: guardar y recuperar contexto del proyecto en memoria (stack, convenciones, ADRs). MCP tool `mem_project_context` para carga automática al inicio de sesión. | Idea | M | ← sesión análisis 2026-07-21 | Integrado con `.engram-id`. Agentes no re-descubren el proyecto cada vez. |
| 46 | ENG-465 | P1 | Feature | **Obsidian export mejorado (sin IA)**: templates configurables, jerarquía `{project}/{type}/{date}-title.md`, frontmatter YAML, backlinks `[[wikilink]]` desde `mem_relations`, índice auto-generado por proyecto+tipo. | Idea | M | ← sesión análisis 2026-07-21 | Sin dependencia de Anthropic API. Puro export estructural. |
| 47 | ENG-466 | P2 | Feature | **Memory templates**: plantillas predefinidas para tipos comunes (decision, bugfix, architecture, PR review, daily standup). Agente rellena campos estructurados. | Idea | S | ← sesión análisis 2026-07-21 | Consistencia en formato de memorias. |
| 48 | ENG-467 | P2 | Feature | **Auto-summarization de sesiones**: `mem_session_end` genera resumen automático desde observaciones locales (sin IA externa). Agrupa por tipo, extrae títulos, detecta decisiones. | Idea | S | ← sesión análisis 2026-07-21 | Heurísticas, no LLM. |
| 49 | ENG-468 | P2 | Feature | **Tags/Labels**: campo `tags: [critical, auth, postgres]` en observaciones. Filtrado por tag en search, context y export. | Idea | S | ← sesión análisis 2026-07-21 | Más granular que `type`. |
| 50 | ENG-469 | P2 | Feature | **Memory consolidation**: fusionar N memorias relacionadas → 1 canónica con referencias. Útil cuando se acumulan observaciones sobre el mismo tema. | Idea | M | ← sesión análisis 2026-07-21 | Usa `mem_relations` graph para detectar candidatos. |
| 51 | ENG-470 | P2 | Feature | **Fuzzy search en CLI**: `engram search "algo parecido"` con tolerancia a typos. Mejora UX del search manual. | Idea | XS | ← sesión análisis 2026-07-21 | FTS5 ya soporta parcialmente. |
| — | **Quick wins (deuda pequeña)** |
| 52 | ENG-471 | P2 | Chore | State file atómico: `WriteAllText` → write-to-tmp + rename. Previene corrupción si el proceso muere. | Idea | XS | ← TD-012 | No crítico pero fácil de arreglar. |
| 53 | ENG-472 | P2 | Chore | `mem_current_project` expose ambiguity hint: mapear `DetectionResult.Error` al JSON de respuesta. | Idea | XS | ← TD-018 | El detector ya setea `Error`, solo falta pasarlo al output. |

### Criterios de activación

Un ítem pasa de **Idea** a **Ready** cuando:
- [ ] Se define prioridad real (P0/P1/P2) según momento
- [ ] Se escribe spec en `docs/tasks/HU-XXX/` o `docs/architecture/rfc/`
- [ ] Se asigna effort confirmado
- [ ] Se mueve a la [Cola de ejecución](#cola-de-ejecución) con número de orden

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

### ENG-404 — Memory relations (P1) — ✅ DONE 2026-06-18

**Problema:** Las memorias son nodos aislados. No hay forma de decir "esta memoria corrige esta otra" o "depende de".
**Para qué sirve:** Grafo de memorias con relaciones tipadas (`depends_on`, `supersedes`, `conflicts_with`, `related_to`). Permite queries estructurales (lineage, cycle detection, contradiction surfacing).
**Qué se implementó:**
- MCP tools: `mem_relations` (add/get/delete) y `mem_lineage_obs` (BFS lineage con cycle detection, max_hops default 5, max 10)
- CLI commands: `engram relations` y `engram lineage`
- 14 escenarios de spec cubiertos, 13 tests unitarios (291ms), suite completa 644/644 pass
- Rework resuelto (dead `max_hops` parameter — cycle 2/3)
- Zero schema changes — relations como observaciones `memrel/{project}/{observationId}`
**Ver:** [spec](../.ai-work/eng-404-memory-relations/spec.md) | [plan](../.ai-work/eng-404-memory-relations/plan.md) | [verify report](../.ai-work/eng-404-memory-relations/verify-report.md) | [summary](../.ai-work/eng-404-memory-relations/summary.md)
**Follow-up:** Inverse traversal (inbound edges) — estimado M, sin ENG asignado aún.

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
| 2026-07-21 | **ENG-473 Done**: Fix `relations add` FK constraint violation. `rel-cli-{date}` session wasn't created. Fix: `CreateSessionAsync()` before `SaveRelationAsync()` en `Program.cs:1260`. Verificado: add/get/delete/lineage todos funcionan. |
| 2026-07-14 | **ENG-456 Done**: NoOpVerifier factory pattern — MCP arranca sin `ANTHROPIC_API_KEY`, `mem_verify_artifact` retorna error `api_key_missing`. 8 tests nuevos, 682/683 passing. Commit `5764ce1`. |
| 2026-06-23 | **OSS Launch Audit**: ENG-435 → Rework (2 critical bugs: transacción vacía + dry-run ejecuta migración real). ENG-436 agregado (P0: `ApplyPulledMutationAsync` stub). ENG-437 agregado (P0: Release v1.3.0 + fix versión). ENG-438/439 agregados (P1: hygiene). Audit completo en FlowForge `.ai-work/oss-launch-audit/context-map.md`. |
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
| 2026-07-16 | **ENG-458 + ENG-459 agregados**: Bugs críticos de sync descubiertos en sesión de verificación manual. ENG-458: mutaciones con `project=""` bloquean sync (P0). ENG-459: sin feedback al usuario cuando sync falla (P0). Ambos causan pérdida de datos silenciosa. |
| 2026-06-05 | PRD Memoria Semántica v1.1 documentado (10 puntos) + RFC-001 Project Identity + ENG-410..418 agregados al backlog. |
| 2026-06-04 | Verificación manual post-deploy: smoke test + 5 regression tests (R1-R5) contra `192.168.0.178:7437`, todos OK. Checklist actualizado. |
| 2026-05-28 | Sesión completa pre-release: ENG-202→206, ENG-305 Done. Columna Origen agregada. |
| 2026-05-28 | Creación del backlog ordenado; ítems de sesión pre-release y meta junio |
| 2026-05-27 | Fixes doc/MCP/Docker/Obsidian (commits en main) |

### ENG-454 — Auto-update command (`flowforge update`) — Repo: FlowForge

**Tipo:** Chore | **P1** | **Effort:** M | **Origen:** ← ENG-452 (self-loop) + observación directa del usuario

**Problema:** Hoy día, cuando el usuario tiene `engram serve` (o MCP server) corriendo con un binario viejo, no hay forma de saber que hay updates disponibles. La única señal es el self-loop warning (ENG-452) que aparece en logs. El config.json dice `"auto_update": false`.

**Criterios de aceptación (para FlowForge):**
- [ ] `flowforge update` consulta el manifest remoto y muestra versiones disponibles vs instalada
- [ ] `flowforge update engram-dotnet` descarga el binario nuevo, lo reemplaza atómicamente, reinicia servicios MCP activos
- [ ] `--dry-run` muestra qué haría sin aplicar cambios
- [ ] `--check` (default) solo verifica y avisa si hay update
- [ ] Backward-compatible: si el usuario rechaza, no hace nada
- [ ] Rollback: si el nuevo binario no arranca en N segundos, vuelve al anterior

**Por qué importa:** Sin auto-update, los fixes (ENG-451, ENG-452, ENG-435, ENG-437, etc.) no llegan al usuario automáticamente. La sesión de hoy requirió 4 rebuilds manuales (cliente + tests + actualización binario en `~/.local/bin`).


### ENG-455 — `flowforge sync connect` command — Repo: FlowForge (cross-repo engram-dotnet)

**Tipo:** Feature | **P1** | **Effort:** M | **Origen:** ← ENG-453 + sesión usuario 2026-07-01 ("la idea es... que podamos hacer este paso transparente")

**Problema:** Para activar sync offline-first hoy día hay que:
1. Saber que `ENGRAM_SERVER_URL` existe
2. Setearlo en env
3. Saber que `ENGRAM_SYNC_ENABLED` debe ser `"true"`
4. Editar config MCP de cada IDE (cada uno con schema distinto)
5. Reiniciar procesos `engram mcp` activos
6. Verificar que sincroniza

Documentado en `FlowForge/POST-INSTALL.md` §3. Imposible de descubrir para un usuario nuevo.

**Propuesta:** Implementar `flowforge sync connect <url>` que automatiza los 6 pasos. Spec completo en `FlowForge/docs/decisions/ADR-009-flowforge-sync-connect.md`.

**Criterios de aceptación:**
- [ ] `flowforge sync connect <url>` valida server, persiste config, actualiza IDEs, reinicia MCPs
- [ ] Idempotente: misma URL 2 veces = no-op
- [ ] Detecta drift entre `~/.engram/config.json` y env vars del proceso (caso real del 2026-07-01)
- [ ] `flowforge sync disconnect` revierte sin perder URL persistida
- [ ] `flowforge sync status` muestra config + procesos activos + health del server
- [ ] Atomic write para `~/.engram/config.json` (write .tmp → rename)

**Depende de:** ENG-453 (FlowForge installer lee sync config existente)


### ✅ ENG-456 — `LlmVerifier` eager instantiation breaks MCP server without `ANTHROPIC_API_KEY`

**Estado:** ✅ Done (2026-07-14) — Commit `5764ce1`

**Tipo:** Bug | **P0** | **Effort:** XS | **Origen:** ← sesión 2026-07-01 (verificación final sync setup)

**Problema:** En `src/Engram.Cli/Program.cs:138-139`, `LlmVerifier` se registra con `_ => new LlmVerifier()`. El constructor (`src/Engram.Verification/ArtifactVerifier.cs:44-51`) tira `InvalidOperationException` si `ANTHROPIC_API_KEY` no está seteada. Como `EngramTools` recibe `IVerifier verifier` en su constructor, apenas se construye `EngramTools`, el DI instancia `LlmVerifier` → throw → **MCP server no arranca**.

Esto significa que `mem_save`, `mem_search`, y todos los demás tools MCP fallan con `"mem_save" threw an unhandled exception: ANTHROPIC_API_KEY is not set`.

**Workaround actual:** Setear `ANTHROPIC_API_KEY` con cualquier dummy en `~/.config/opencode/opencode.json`. El verifier solo se usa en `mem_verify_artifact` así que el key nunca se usa realmente.

**Criterios de aceptación:**
- [x] `engram mcp` arranca sin `ANTHROPIC_API_KEY` en env
- [x] `mem_save`, `mem_search`, `mem_get` funcionan sin la key
- [x] `mem_verify_artifact` reporta error claro si se llama sin la key (en vez de romper el server)
- [x] `LlmVerifier` se instancia solo cuando se necesita (lazy)

**Fix aplicado:** Factory delegate pattern (NoOpVerifier) en `Program.cs:138-144`. Si `ANTHROPIC_API_KEY` no está seteada, se usa `NoOpVerifier`; si está presente, se usa `LlmVerifier`. `mem_verify_artifact` retorna error estructurado `api_key_missing` cuando NoOpVerifier está activo.

**Tests:** 8 tests añadidos (T1-T6 + T4+ complement), todos pasando. 682/683 tests passing en suite completa (1 failure pre-existente en HttpStore no relacionado).

**Artifacts:** `.ai-work/eng-456-llm-verifier-lazy/` (context-map.md, spec.md, plan.md, verify-report.md)


### ENG-457 — Sync pull dedup: prevent millions of duplicate rows — Branch: `fix/sync-mutations-dedup`

**Tipo:** Bug | **P0** | **Effort:** S | **Origen:** ← sesión verificación 2026-07-05 (sync duplicaba 6.7M mutaciones)

**Problema:** El sync pull inserta la misma mutación múltiples veces en `sync_mutations` cuando el cursor `last_pulled_seq` retrocede (primera sync contra server con histórico, reset, etc). Un usuario acumuló **6,759,768 filas duplicadas** → BD de 2 GB → rendimiento degradado.

**Root cause:** Falta constraint UNIQUE en `(target_key, entity_key)` para `source='pull'`, y `InsertPulledMutationAsync` no usa `INSERT OR IGNORE`.

**Fix:**
1. Partial UNIQUE INDEX `idx_sync_mutations_pull_dedup` en `(target_key, entity_key) WHERE source='pull'`
2. `InsertPulledMutationAsync` ahora usa `INSERT OR IGNORE` y devuelve el `seq` existente si ya estaba

**Criterios de aceptación:**
- [x] UNIQUE INDEX impide duplicación futura
- [x] INSERT OR IGNORE funciona correctamente
- [x] Tests cubren todos los paths (7/7 passing)
- [x] Cero regresiones (220/220 store tests passing)
- [x] Cleanup local: 6,759,768 → 7 rows (1.97GB reclaim)

**PR:** https://github.com/efreet111/engram-dotnet/pull/new/fix/sync-mutations-dedup


---

### ENG-458 — Mutaciones con `project=""` bloquean sync (P0, Bug)

**Tipo:** Bug | **P0** | **Effort:** S | **Origen:** ← sesión sync 2026-07-16 (verificación manual de sync local → servidor)

**Problema:** Cuando se crean mutaciones sin `project` en el payload (ej: deletes de observaciones huérfanas), se guardan con `project=""` en `sync_mutations`. Luego `CountPendingNonEnrolledAsync` las cuenta como "no enroladas" y **bloquea TODO el push**, incluso si los proyectos válidos están correctamente enrolados.

**Impacto:** 3 mutaciones huérfanas con `project=""` bloquearon el sync de 38 mutaciones válidas (20 de `team/engram-dotnet` + 18 de `team/flowforge`). Pérdida de datos silenciosa: el usuario cree que el sync funciona pero las memorias nunca se sincronizan.

**Root cause:**
```csharp
// ExtractProjectFromPayload en SqliteStore.cs
private static string ExtractProjectFromPayload(object payload)
{
    if (payload is null) return "";  // ← Devuelve "" si no hay project
    ...
}

// CountPendingNonEnrolledAsync en SqliteStore.cs
SELECT sm.project, COUNT(*) as count
FROM sync_mutations sm
LEFT JOIN sync_enrolled_projects ep ON sm.project = ep.project
WHERE sm.target_key = @target AND sm.acked_at IS NULL AND ep.project IS NULL
GROUP BY sm.project
```

El query cuenta `project=""` como "no enrolado" porque no existe en `sync_enrolled_projects`.

**Escenario de reproducción:**
1. Usuario crea observaciones con proyecto válido
2. Usuario borra observaciones (se crean mutaciones `delete` con `project=""`)
3. Usuario configura sync y enrola proyectos válidos
4. SyncManager intenta push → bloqueado por "3 non-enrolled projects detected"
5. Mutaciones válidas nunca se sincronizan

**Solución propuesta:**

**Opción A (recomendada): Ignorar mutaciones con `project=""` en el conteo**
```csharp
// CountPendingNonEnrolledAsync en SqliteStore.cs
SELECT sm.project, COUNT(*) as count
FROM sync_mutations sm
LEFT JOIN sync_enrolled_projects ep ON sm.project = ep.project
WHERE sm.target_key = @target 
  AND sm.acked_at IS NULL 
  AND ep.project IS NULL
  AND sm.project != ''  // ← Ignorar mutaciones huérfanas
GROUP BY sm.project
```

**Opción B: Rechazar mutaciones con `project=""` al crearlas**
```csharp
// EnqueueSyncMutation en SqliteStore.cs
if (string.IsNullOrWhiteSpace(project))
{
    _logger?.LogWarning("Rejecting mutation with empty project: entity={Entity}, key={Key}", entity, entityKey);
    return;  // No encolar
}
```

**Opción C: Migrar mutaciones existentes con `project=""`**
```sql
-- Borrar mutaciones huérfanas antiguas
DELETE FROM sync_mutations WHERE project = '' AND acked_at IS NULL;
```

**Criterios de aceptación:**
- [ ] `CountPendingNonEnrolledAsync` ignora mutaciones con `project=""`
- [ ] Tests cubren escenario: mutaciones válidas + mutaciones huérfanas → push funciona
- [ ] Migración: limpiar mutaciones existentes con `project=""` en BDs existentes
- [ ] Documentar en ADR o comentario de código por qué se ignoran

**Tests:**
- Test unitario: `CountPendingNonEnrolledAsync` con mutaciones mixtas (válidas + huérfanas)
- Test de integración: push con mutaciones huérfanas no bloquea proyectos válidos

**Archivos a modificar:**
- `src/Engram.Store/SqliteStore.cs:2050` — `CountPendingNonEnrolledAsync`
- `tests/Engram.Store.Tests/SqliteStoreSyncTests.cs` — nuevos tests

---

### ENG-459 — Sync failure feedback (P0, Feature)

**Tipo:** Feature | **P0** | **Effort:** M | **Origen:** ← sesión sync 2026-07-16 (usuario no sabía que sync estaba roto)

**Problema:** El `SyncManager` corre como `BackgroundService` y los errores solo se loggean. No hay mecanismo para notificar al usuario cuando el sync falla repetidamente. El usuario cree que el sync funciona, pero las memorias nunca se sincronizan. **Pérdida de datos silenciosa.**

**Impacto:** Usuario trabajó durante días creando memorias pensando que se sincronizaban con el servidor. Al verificar manualmente, descubrió que el sync estaba bloqueado desde el primer día. 38 mutaciones pendientes de push.

**Escenarios de fallo silencioso:**
1. `ENGRAM_SERVER_URL` no configurado → SyncManager se deshabilita (self-loop detection)
2. Proyectos no enrolados → push bloqueado ("non-enrolled-pending")
3. Servidor remoto sin fixes (ej: ENG-428) → push falla con 500
4. Red caída → push/pull fallan con timeout
5. Credenciales inválidas → push falla con 401/403

**Solución propuesta:**

**1. Notificación visible cuando sync falla repetidamente**
```csharp
// SyncManager.cs — después de N fallos consecutivos
if (ConsecutiveFailures >= 3)
{
    // Escribir a archivo de notificación
    await WriteNotificationAsync(new SyncNotification
    {
        Level = "error",
        Message = $"Sync failed {ConsecutiveFailures} times: {LastError}",
        Action = "Run 'engram sync status' for details",
        Timestamp = DateTime.UtcNow
    });
}
```

**2. Comando `engram sync status` muestra error claro con acción sugerida**
```bash
$ engram sync status

❌ Sync Status: BLOCKED
   Phase: pending
   Target: cloud (http://192.168.0.178:7437)
   
   Error: 3 projects not enrolled
   Consecutive failures: 5
   Last sync: never
   
   Pending mutations: 38 (20 team/engram-dotnet, 18 team/flowforge)
   
💡 Suggested action:
   Enroll projects with:
   curl -X POST http://192.168.0.178:7437/sync/enroll \
     -H "Content-Type: application/json" \
     -d '{"project": "team/engram-dotnet"}'
   
   Or run: flowforge sync connect http://192.168.0.178:7437 --auto-enroll
```

**3. Alerta en MCP server cuando sync está bloqueado**
```csharp
// EngramTools.cs — al inicializar MCP tools
if (syncStatusProvider?.Phase == SyncPhase.Disabled || 
    syncStatusProvider?.ConsecutiveFailures >= 3)
{
    _logger.LogWarning("⚠️ Sync is blocked: {Error}. Run 'engram sync status' for details.", 
        syncStatusProvider?.LastError);
    
    // Opcional: añadir campo "sync_health" en mem_status tool
}
```

**4. Endpoint `/sync/status` incluye campo `suggested_action`**
```json
{
  "sync_enabled": true,
  "phase": "pending",
  "health": {
    "status": "blocked",
    "consecutive_failures": 5,
    "last_error": "non-enrolled-pending: 3 projects not enrolled",
    "suggested_action": "Enroll projects with: curl -X POST http://server/sync/enroll -d '{\"project\": \"name\"}'"
  }
}
```

**Criterios de aceptación:**
- [x] `engram sync status` muestra error claro con acción sugerida
- [x] Endpoint `/sync/status` incluye campo `suggested_action`
- [x] MCP server loggea warning cuando sync está bloqueado (al iniciar)
- [x] Archivo de notificación `~/.engram/sync-notifications.log` (últimas 10 notificaciones)
- [x] Tests cubren todos los escenarios de fallo

**Tests:**
- Test unitario: `engram sync status` con sync bloqueado → muestra error + acción
- Test unitario: `/sync/status` endpoint incluye `suggested_action`
- Test de integración: SyncManager con 3 fallos → escribe notificación

**Archivos a modificar:**
- `src/Engram.Sync/SyncManager.cs` — notificaciones después de N fallos
- `src/Engram.Cli/Program.cs` — mejorar output de `engram sync status`
- `src/Engram.Server/CloudSyncEndpoints.cs` — campo `suggested_action` en `/sync/status`
- `src/Engram.Mcp/EngramTools.cs` — warning al inicializar si sync bloqueado
- `tests/Engram.Sync.Tests/SyncManagerTests.cs` — nuevos tests
- `tests/Engram.Cli.Tests/SyncStatusCliTests.cs` — nuevos tests

**Dependencias:**
- ENG-453 (FlowForge installer guarda `ENGRAM_SERVER_URL`) — relacionado
- ENG-455 (`flowforge sync connect`) — complementa esta feature

---


### ✅ ENG-473 — `relations add` FK constraint violation (P0, Bug) — DONE 2026-07-21

**Tipo:** Bug | **P0** | **Effort:** XS | **Origen:** ← sesión verificación funcional 2026-07-21

**Problema:** `engram relations --action add` lanza `SQLite Error 19: 'FOREIGN KEY constraint failed'` porque el `sessionId` auto-generado `rel-cli-{date}` nunca se inserta en la tabla `sessions`. La tabla `observations` tiene `FOREIGN KEY (session_id) REFERENCES sessions(id)`.

**Root cause:** `Program.cs:1260` genera `sessionId = $"rel-cli-{DateTime.UtcNow:yyyyMMdd}"` pero nunca crea la sesión correspondiente. En cambio, `engram save` usa `manual-save-{project}` que se crea explícitamente con `store.CreateSessionAsync()` en `Program.cs:259`.

**Fix:** Agregar `await store.CreateSessionAsync(sessionId, project, "");` en `Program.cs:1261` antes de usar `SaveRelationAsync()`.

**Verificación (2026-07-21):**
- [x] `relations add` — ✅ `Relation depends_on:61 added to obs#60`
- [x] `relations get` — ✅ muestra `depends_on: 61`
- [x] `lineage` — ✅ muestra árbol `obs#60 → obs#61 → obs#63` con hops
- [x] `relations delete` — ✅ `Relation removed.`

