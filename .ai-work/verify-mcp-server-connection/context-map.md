# Context Map — verify-mcp-server-connection

> **Feature**: Verificar (y desbloquear) la conexión MCP Engram → servidor remoto  
> **Slug**: `verify-mcp-server-connection`  
> **Prioridad**: P0 (MCP tools unavailable en Cursor)  
> **Origen**: sesión 2026-07-19 — Cursor `user-engram` en error + crash `engram mcp`/`doctor`  
> **Fase**: Discovery (forge-discovery Phase 0)  
> **CKP-0**: CLEAR — contexto suficiente para Phase 1

---

## 1. Veredicto de configuración (human intent)

**La config MCP de Cursor está bien formada para modo Local + sync.** No es un problema de shape/`mcp.json`.

| Variable / campo | Valor actual | Esperado (docs/MCP-CONFIG.md) | Estado |
|------------------|--------------|-------------------------------|--------|
| `type` | `stdio` | stdio | ✅ |
| `command` | `...\FlowForge\engram.exe` | binario instalado | ✅ existe |
| `args` | `["mcp"]` | `mcp` | ✅ |
| `ENGRAM_DATA_DIR` | `C:\Users\efree\.engram` | data dir absoluto | ✅ |
| `ENGRAM_USER` | `efree` | identidad | ✅ (config.json usa `efree@local.dev`) |
| `ENGRAM_SYNC_ENABLED` | `true` | `true` para sync | ✅ |
| `ENGRAM_SERVER_URL` | `http://192.168.0.178:7437` | URL remota (no `ENGRAM_URL`) | ✅ correcto modo |
| `ENGRAM_URL` | (ausente) | debe ausentarse en local+sync | ✅ |

Fuente canónica: `docs/MCP-CONFIG.md` § Tres modos — Local + sync usa `ENGRAM_SERVER_URL`, **no** `ENGRAM_URL` (ese activa HttpStore puro y desactiva journal SQLite).

`~/.engram/config.json` coincide: `sync.mode=sync`, `remote_url=http://192.168.0.178:7437`, componente `engram_dotnet` registered `v1.3.0`.

**Nota versión binario:** `engram.exe --version` reporta `1.0.0+72eb656...` mientras el installer registró `v1.3.0`. Posible mismatch de AssemblyInformationalVersion vs tag de release — no es el crash actual, pero merece verificación en Phase 1/ops.

---

## 2. Árbol de fallos (por qué Cursor ve `user-engram` en error)

```
Cursor live tool discovery
  └─→ spawns: engram.exe mcp (stdio)
        └─→ OpenStore(StoreConfig) → new SqliteStore(cfg)
              └─→ SqliteStore.Migrate()
                    └─→ CREATE UNIQUE INDEX idx_sync_mutations_pull_dedup
                          ON sync_mutations(target_key, entity_key) WHERE source='pull'
                    └─→ ❌ SqliteException 19 UNIQUE constraint failed
                          (target_key, entity_key)
        └─→ proceso muere → Cursor: "failed during live tool discovery"

Independiente (aunque Migrate se arregle):
  ENGRAM_SERVER_URL=http://192.168.0.178:7437
    └─→ Test-NetConnection / HTTP /health → TIMEOUT / TcpTestSucceeded=False
    └─→ SyncManager no puede push/pull (servidor caído o red)
```

**Conclusión:** hay **dos blockers independientes**:

| # | Blocker | Tipo | Impide |
|---|---------|------|--------|
| B1 | Migrate UNIQUE crash en `engram.db` local | Bug producto (gap ENG-457) | Arranque MCP / doctor / cualquier OpenStore SQLite |
| B2 | Host `192.168.0.178:7437` inalcanzable | Ops / infraestructura | Sync remoto (no el arranque local) |

`ENGRAM_SYNC_ENABLED=false` **no mitiga B1** — el crash ocurre en el constructor del store, antes del registro de SyncManager.

---

## 3. Evidencia local de B1 (DB)

Archivo: `C:\Users\efree\.engram\engram.db` (~479 KB, last write jun 2026).

| Métrica | Valor |
|---------|-------|
| Filas `sync_mutations` | 609 (todas `source='pull'`) |
| Grupos duplicados `(target_key, entity_key)` | 6 |
| Peor caso | `obs-90b7a37268552d3b` × **421** |
| Keys pull únicas | 8 → cleanup dejaría ~8 filas |
| Índice `idx_sync_mutations_pull_dedup` | **no existe** (Migrate nunca completa) |
| `observations` | **0** |
| `sync_state` | `lifecycle=failed`, `last_pulled_seq=1124`, `last_error='The operation was canceled.'` |
| `sync_enrolled_projects` | vacío |

Reproducción confirmada (2026-07-19):

```text
engram doctor  → SqliteException UNIQUE constraint failed: sync_mutations.target_key, sync_mutations.entity_key
  at SqliteStore.Exec → Migrate → SqliteStore..ctor → OpenStore
```

---

## 4. Root cause producto (gap ENG-457)

ENG-457 (`docs/BACKLOG.md`, branch `fix/sync-mutations-dedup`) añadió:

1. Partial UNIQUE INDEX `idx_sync_mutations_pull_dedup` en `SqliteStore.Migrate()` (~L237-241)
2. `INSERT OR IGNORE` en `InsertPulledMutationAsync` (~L2212+)

**Gap:** `Migrate()` crea el UNIQUE INDEX **sin deduplicar filas pull preexistentes**. En DBs que ya acumularon duplicados (el síntoma exacto que ENG-457 resolvía — hasta 6.7M filas), el `CREATE UNIQUE INDEX` falla y **el proceso no arranca**.

El cleanup original de ENG-457 fue **manual** (sesión: 6,759,768 → 7 rows). No hay paso automático en Migrate:

```sql
-- ausente hoy, necesario antes del CREATE UNIQUE INDEX
DELETE FROM sync_mutations
WHERE source = 'pull' AND rowid NOT IN (
  SELECT MIN(rowid) FROM sync_mutations
  WHERE source = 'pull'
  GROUP BY target_key, entity_key
);
```

PostgresStore: **no** tiene índice equivalente — gap solo SQLite/cliente.

---

## 5. Associated epics / topic_keys

| Artefacto | Relación |
|-----------|----------|
| **ENG-457** | Causa directa B1 — índice sin pre-cleanup |
| **ENG-451** / ADR-007 | Pull orphans, `InsertPulledMutationAsync`, reapply — mismos paths |
| **ENG-459** | Feedback de sync failure — no ayuda si MCP ni arranca |
| **ENG-452** / ADR-008 | Self-loop `ENGRAM_SERVER_URL` — no aplica (URL apunta a remoto) |
| **ENG-453** | Installer + `ENGRAM_SERVER_URL` — config ya correcta aquí |
| **ENG-456** | MCP crash por `ANTHROPIC_API_KEY` — ya fixed; no es este crash |
| Docs | `MCP-CONFIG.md`, `SYNC-SETUP.md`, `DiagnosticService` `/health` |

Memoria Engram (Attempt A): **unavailable** — `user-engram` en error (mismo B1).  
Fallback local `.engram/local_memory/`: path no existe en este host.

---

## FlowDoc context

- PRD: `docs/PRD.md` — no existe (read: no)
- `.flowforge.json`: no existe en root
- HU referenced: none (intent ops/verify, no HU-NNN)
- HUs recientes listadas (filename desc): HU-009, HU-007, HU-006 — no aplicables a este fallo
- HU flowforge_slug: unset

---

## Reusable Patterns Found

- `src/Engram.Store/SqliteStore.cs` (~L237-241): `CREATE UNIQUE INDEX IF NOT EXISTS idx_sync_mutations_pull_dedup` — **clonar el patrón de “cleanup before constraint”** que falta; el índice en sí es correcto.
- `src/Engram.Store/SqliteStore.cs` (~L2212-2243): `InsertPulledMutationAsync` con `INSERT OR IGNORE` + SELECT seq existente — patrón de dedup en runtime; Migrate debe alinear el estado histórico al mismo invariante.
- `tests/Engram.Store.Tests/SqliteStorePullDedupTests.cs`: suite ENG-457 (7 tests) — **extender** con caso: DB pre-seeded con duplicados pull → `new SqliteStore(cfg)` no lanza y deja 1 fila por `(target_key, entity_key)`.
- `src/Engram.Cli/Program.cs` (~L112-122, ~L168+): selección HttpStore vs SqliteStore + registro SyncManager solo si `ILocalSyncStore` — config actual toma rama SQLite local+sync (correcto).
- `src/Engram.Diagnostics/DiagnosticService.cs` (~L172-237): check HTTP `/health` vía `ENGRAM_SERVER_URL` — reutilizable cuando doctor pueda abrir el store.
- `src/Engram.Store/StoreConfig.cs` (~L26-63): `ENGRAM_URL` → `IsRemote`/HttpStore vs sync journal — documentar en fix/docs para no confundir workarounds.
- Histórico `.ai-work/eng-451-sync-recovery/`, `.ai-work/eng-459-sync-failure-feedback/` — contexto de sync lifecycle; no clonar código, sí constraints de no-data-loss.

---

## 6. Constraints que Phase 1 / forge-arch DEBE respetar

1. **No proponer greenfield sync** — el gap es un paso de migración faltante en ENG-457.
2. **Cleanup debe ser idempotente** y correr **antes** del `CREATE UNIQUE INDEX`.
3. **Preservar 1 fila por `(target_key, entity_key)` con `source='pull'`** (preferir `MIN(seq)` / `MIN(rowid)` — alinear con `InsertPulledMutationAsync` que hace `ORDER BY seq ASC LIMIT 1`).
4. **No usar `ENGRAM_URL` como “fix” de sync** — cambiaría el modo a HttpStore y rompería offline-first (docs).
5. **B2 (servidor caído) es fuera de código** salvo mejorar mensajes en `doctor`/MCP stderr cuando `/health` falle — no inventar reconnect mágico.
6. Arquitectura lean: sin MediatR/CQRS; cambio mínimo en `SqliteStore.Migrate` + tests.
7. Comentarios `///` solo donde ADR-003 lo requiera; no docs markdown extra salvo que el humano pida.

---

## 7. Scope recomendado para Phase 1 (forge-arch)

### In scope (producto)

1. **Fix Migrate:** dedupe pull rows → luego `CREATE UNIQUE INDEX` (cierra gap ENG-457).
2. **Test de regresión:** store con duplicados preexistentes arranca sin excepción.
3. **(Opcional XS)** `engram doctor`: si OpenStore falla por este error, mensaje accionable (“dedupe sync_mutations / upgrade binary / backup DB”).

### Out of scope

- Levantar TrueNAS / VPN / firewall hacia `192.168.0.178` (humano/ops).
- Reescribir SyncManager, MCP tool surface, o cambiar modo a `ENGRAM_URL`.
- Migrar PostgresStore (sin índice equivalente hoy).

### Workaround ops inmediato (humano, no Phase 1)

Mientras no haya binario con el fix:

```sql
-- backup primero
-- luego dedupe; deja 1 fila por key
DELETE FROM sync_mutations
WHERE source = 'pull' AND rowid NOT IN (
  SELECT MIN(rowid) FROM sync_mutations WHERE source = 'pull'
  GROUP BY target_key, entity_key
);
```

O data dir fresco (aceptable aquí: **0 observations** locales). Luego reiniciar Cursor MCP. Sync seguirá fallando hasta que B2 se resuelva.

---

## 8. Preguntas al humano (no bloquean diseño del fix B1)

1. ¿El servidor en `192.168.0.178:7437` debería estar UP ahora (TrueNAS/VPN), o está apagado a propósito?
2. ¿Phase 1 debe limitarse al fix Migrate (B1), o también endurecer `doctor`/mensajes MCP para B2?
3. ¿OK dedupe in-place de `engram.db` (609→~8 filas pull, 0 obs), o preferís backup + wipe del data dir?

---

## 9. Prior observations (memory)

- Attempt A (`mem_search` / `mem_current_project`): **blocked** — MCP en error.
- Attempt B (`.engram/local_memory/`): directorio inexistente.
- Contexto recuperado de repo: BACKLOG ENG-457, `.ai-work/eng-451-*`, `.ai-work/eng-459-*`, docs MCP/SYNC.

---

**Última actualización discovery:** 2026-07-19  
**Handoff:** forge-arch lee este archivo para CKP-1 / `spec.md`
