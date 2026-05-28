---
name: engram-docs-on-done
description: >-
  Checklist de documentación al cerrar una feature, bug o ítem del BACKLOG/ROADMAP
  en engram-dotnet. Usar al marcar ENG-xxx como Done, antes del commit/PR, o al
  ejecutar sdd-archive. Evita desfases README vs código, MCP, API y versiones.
---

# Documentación al cerrar trabajo — engram-dotnet

## Git (ramas, commits, PR)

Antes de commit/PR en este repo, seguir también [`docs/GIT-WORKFLOW.md`](../../../docs/GIT-WORKFLOW.md) y la regla `config/cursor/rules/engram-git-workflow.mdc`.

---

## Cuándo usar este skill

- Completaste un ítem de [docs/BACKLOG.md](../../docs/BACKLOG.md) o [docs/ROADMAP.md](../../docs/ROADMAP.md)
- Vas a hacer commit/PR de una feature, bugfix o cambio de MCP/API/sync
- Ejecutás fase **sdd-archive** (cierre SDD)
- El usuario dice "está listo", "cerramos la feature", "sacamos del backlog"

**No omitas documentación** aunque el código compile y los tests pasen.

---

## Paso 0 — Identificar el alcance

Anotá:

- **ID backlog** (ej. `ENG-206`) si aplica
- **Tipo**: Feature | Bug | Doc | Chore | MCP | REST | Sync | Docker | CLI
- **¿Breaking?** Si sí → nota de migración obligatoria

---

## Paso 1 — Buscar en el repo (obligatorio)

Ejecutá búsquedas según el tipo de cambio:

| Si cambiaste… | Buscar en el repo |
|---------------|-------------------|
| MCP tools | `EngramTools.cs`, `McpServerTool`, `docs/MCP-CONFIG.md`, `docs/MCP-TEST-CASES.md`, `config/mcp/`, `README*.md` "tools" |
| REST / Server | `EngramServer.cs`, `CloudSyncEndpoints.cs`, `docs/API-REFERENCE.md` |
| CLI | `Program.cs`, `docs/01-QUICK-START.md`, `docs/DEVELOPMENT.md` |
| Sync | `SyncManager`, `docs/OFFLINE-FIRST-SYNC.md`, `docs/SYNC-SETUP.md`, `docs/OFFLINE-FIRST-SYNC-TEST-CASES.md` |
| Store / DB | `IStore.cs`, `docs/ARCHITECTURE.md`, `docs/POSTGRES-SETUP.md`, `docs/MIGRATION.md` |
| Env vars nuevas | `StoreConfig.cs`, `SyncManagerConfig`, `docs/MCP-CONFIG.md`, `config/mcp/*.json` |
| Versión | `Program.cs` `Version`, `CHANGELOG.md`, tags, `GET /health` version field en docs |
| Obsidian | `Engram.Obsidian`, tests Obsidian, `docs/` obsidian mentions |
| Docker | `Dockerfile`, `docker/`, `docs/ROADMAP` docker, `sdd/docker-*` |

Comandos útiles:

```bash
# Contar tools MCP reales
rg "McpServerTool\(Name" src/Engram.Mcp/EngramTools.cs | wc -l

# Endpoints nuevos
rg "Map(Get|Post|Put|Delete|Patch)" src/Engram.Server/

# Variables de entorno
rg "ENGRAM_" src/ docs/ config/
```

---

## Paso 2 — Checklist por archivo (marcar todos los que apliquen)

### Siempre (cada cierre con impacto de producto)

- [ ] **`CHANGELOG.md`** — entrada en `[Unreleased]` (Added / Changed / Fixed / Tests)
- [ ] **`docs/BACKLOG.md`** — fila `Done`, fecha en changelog del backlog
- [ ] **`docs/ROADMAP.md`** — mover a ✅ Completed; quitar o actualizar sección en 📋 Backlog; corregir "Suggested Work Order" si aplica
- [ ] **`README.md`** y **`README.es.md`** — tabla de features, conteos (MCP tools, endpoints), enlaces a docs nuevos
- [ ] **Versión coherente** — `Program.cs` `Version` = última tag o política acordada; no dejar `1.1.0` en docs si CHANGELOG dice `0.3.0`

### API y arquitectura

- [ ] **`docs/API-REFERENCE.md`** — cada endpoint nuevo/cambiado con método, body, respuestas, headers (`X-Engram-User`)
- [ ] **`docs/ARCHITECTURE.md`** — diagramas, capas, lista MCP si cambió
- [ ] **`docs/AGENT-PROTOCOL.md`** — si afecta cómo el agente debe usar tools

### MCP y editores (agnóstico de Cursor)

- [ ] **`docs/MCP-CONFIG.md`** — modos local / remoto / sync; variables; troubleshooting
- [ ] **`config/mcp/INSTALL.md`** y plantillas en **`config/mcp/editors/`**
- [ ] **`config/cursor/mcp.json`** y **`config/vscode/mcp.json`** — ejemplos del repo (placeholders, no IPs personales fijas salvo ejemplo genérico)
- [ ] **`docs/MCP-TEST-CASES.md`** — casos para tools nuevos
- [ ] **`docs/SETUP-WIZARD.md`** — si cambia el flujo post-clone

### Operación y despliegue

- [ ] **`docs/01-QUICK-START.md`** — perfiles solo / team / admin afectados
- [ ] **`docs/SYNC-SETUP.md`** / **`docs/OFFLINE-FIRST-SYNC.md`** — sync/enroll/status
- [ ] **`docs/POSTGRES-SETUP.md`** / **`docker/README.md`** — si backend o Docker cambió
- [ ] **`docs/DEVELOPMENT.md`** — build, test filters, `dotnet` commands

### Deuda y calidad

- [ ] **`docs/TECHNICAL-DEBT.md`** — ítems nuevos o resueltos (TD-xxx)
- [ ] **Tests** — si hay `RequiresDocker` o skips, documentar en BACKLOG o TECHNICAL-DEBT

### SDD / specs (si hubo carpeta `sdd/`)

- [ ] **Archive report** en `sdd/archive/.../archive-report.md` (patrón del repo)
- [ ] Listar **archivos de docs tocados** en el archive report (como en Phase 4 observability)
- [ ] **`mem_save`** con `topic_key` del archive-report (convención SDD)

### Lo que NO debe quedar

- [ ] Features documentadas que **no existen** en código (ej. `mem_generate_index`, conteo "27 tools")
- [ ] `ENGRAM_URL` documentado para sync cuando el modo correcto es `ENGRAM_SERVER_URL`
- [ ] IPs o rutas personales hardcodeadas sin placeholder en docs **del repo** (ok en `~/.cursor/mcp.json` local)

---

## Paso 3 — Verificación rápida

Antes de commit:

1. Los números del README coinciden con `rg` en código (tools, endpoints aprox.)
2. Un dev nuevo puede seguir `config/mcp/INSTALL.md` o `SETUP-WIZARD.md` sin preguntar en chat
3. `CHANGELOG` + `BACKLOG` + `ROADMAP` dicen lo mismo sobre el estado del ítem

---

## Paso 4 — Commit

Mensaje sugiere ID backlog si existe:

```
docs: close ENG-206 — postgres test fixes (changelog, backlog, API)
```

Incluí archivos de **docs** en el mismo commit que el código cuando sea el mismo PR.

---

## Referencias

- Cola de trabajo: [docs/BACKLOG.md](../../docs/BACKLOG.md)
- Visión: [docs/ROADMAP.md](../../docs/ROADMAP.md)
- Ejemplo archive con docs: `sdd/archive/2026-05-19-offline-first-sync-phase4/offline-first-sync/archive-report.md`
