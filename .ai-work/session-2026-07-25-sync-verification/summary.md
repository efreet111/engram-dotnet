# Session Summary: Sync Verification + ENG-475 Fix

**Fecha:** 2026-07-25
**Duración:** ~2 horas
**Participantes:** Humano + FlowForge Orchestrator

---

## Contexto

Sesión de verificación de sync entre engram-dotnet local y servidor PostgreSQL en `192.168.0.178:7437`. El objetivo era habilitar `ENGRAM_SYNC_ENABLED=true` y verificar que el sync funcionara correctamente.

---

## Descubrimientos

### 1. Sync habilitado pero con problemas

- Config MCP actualizada: `ENGRAM_SYNC_ENABLED=true`, `ENGRAM_SERVER_URL=http://192.168.0.178:7437`
- Servidor respondía correctamente (v1.1.0, Postgres)
- **Problema:** 48 mutaciones pendientes de push, sync bloqueado

### 2. ENG-475: PostgreSQL idx_obs_dedupe overflow

**Root cause:** El índice `idx_obs_dedupe` incluía `title` (TEXT sin límite) que podía exceder 2704 bytes (límite B-tree de PostgreSQL).

**Error:**
```
PostgresException 54000: index row size 2800 exceeds btree version 4 maximum 2704 for index "idx_obs_dedupe"
```

**Impacto:** 13 observaciones con contenido >2000 bytes no podían sincronizarse. Data loss silenciosa.

### 3. Fix aplicado (PR #22)

- Removido `title` de `idx_obs_dedupe` en PostgresStore.cs y SqliteStore.cs
- Agregada migración idempotente `MigrateDedupeIndex()` para DBs existentes
- Tests regresión: 2/2 SQLite, 2/2 PostgreSQL (Testcontainers)
- CI pasó: SQLite 54s, PostgreSQL 1m19s

### 4. Sync verificado funcionando

Después del fix:
- 35 mutaciones pushed exitosamente
- 70 mutaciones pulled del servidor
- Sin errores, phase=healthy

### 5. Problema de diseño identificado (ENG-476)

**Insight:** El `SyncManager` solo corre dentro de `engram mcp` o `engram serve`. No hay daemon independiente. Si el usuario cierra el IDE, el sync se detiene y las memorias se desincronizan sin que el usuario lo sepa.

**Propuesta:** ENG-476 — Sync-on-demand: trigger sync cycle cuando el usuario hace búsqueda vía MCP/CLI. No requiere daemon.

---

## Archivos modificados

| Archivo | Cambio |
|---------|--------|
| `src/Engram.Store/PostgresStore.cs` | Fix índice + migración `MigrateDedupeIndex()` |
| `src/Engram.Store/SqliteStore.cs` | Fix índice + migración |
| `tests/Engram.Store.Tests/SqliteStoreTests.cs` | 2 tests regresión |
| `tests/Engram.Postgres.Tests/PostgresStoreTests.cs` | 2 tests regresión |
| `docs/BACKLOG.md` | ENG-475 Done, ENG-476 agregado |
| `.ai-work/eng-475-postgres-dedupe-index-overflow/ticket.md` | Ticket completo |
| `.ai-work/obsidian-memory-graph/context-map.md` | Actualizado con datos reales |

---

## Commits

| Commit | Descripción |
|--------|-------------|
| `68e33dc` | fix: remove title from idx_obs_dedupe to prevent B-tree overflow (ENG-475) |
| `62eca98` | Merge pull request #22 |
| `dd95d2e` | docs: mark ENG-475 as Done in BACKLOG |

---

## Estado actual

| Componente | Estado |
|------------|--------|
| Sync | ✅ Funcionando (push/pull) |
| ENG-475 | ✅ Done (PR #22 mergeado) |
| ENG-476 | 📋 Idea (en backlog) |
| ENG-474 (Obsidian Memory Graph) | 📋 Idea (context-map actualizado) |

---

## Próximos pasos

1. **ENG-476 (Sync-on-demand):** Diseñar e implementar trigger de sync en MCP tools (`mem_search`, `mem_context`, `mem_get`)
2. **ENG-474 (Obsidian Memory Graph):** Continuar con spec.md cuando haya prioridad
3. **Migración en servidor:** Ejecutar SQL de migración en `192.168.0.178:7437` para fix permanente del índice

---

## Memory Signal

**Key learnings:**
- PostgreSQL B-tree tiene límite de 2704 bytes por fila de índice
- Índices compuestos con campos TEXT sin límite pueden exceder este límite
- El sync de engram-dotnet requiere que el proceso MCP o server esté corriendo
- No hay daemon independiente para sync continuo

**Patterns to remember:**
- Para índices de deduplicación, usar campos de tamaño fijo (hashes, IDs)
- Si se incluyen campos variables, usar funciones hash (md5, sha256)
- El workaround para desbloquear sync es marcar mutaciones como `acked_at`

**Cross-repo tickets:**
- FlowForge NS-10: PostgreSQL idx_obs_dedupe overflow (creado en esta sesión)
