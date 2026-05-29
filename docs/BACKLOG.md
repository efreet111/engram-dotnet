# Backlog — engram-dotnet

> **Fuente de verdad para el orden de trabajo.**  
> El [ROADMAP](ROADMAP.md) describe fases y visión; **este archivo define qué hacer ahora y en qué orden.**

**Última actualización:** 2026-05-28  
**Meta release:** finales de junio 2026 (uso por terceros + instalador)

---

## Cómo usamos este backlog

| Regla | Descripción |
|-------|-------------|
| **Una cola** | La tabla [Cola de ejecución](#cola-de-ejecución) es el orden. Se trabaja de arriba hacia abajo salvo bloqueo explícito. |
| **Feature = entregable** | Cada fila es una **Feature** (épica pequeña) con criterio de “hecho” claro. Las tareas grandes se descomponen en **Stories** dentro de la feature. |
| **Estados** | `Done` · `Ready` (sin spec o spec listo) · `In Progress` · `Blocked` · `Icebox` |
| **Tipos** | `Feature` · `Bug` · `Doc` · `Chore` · `Test` |
| **Specs** | Features `Ready` con spec en `sdd/` o `docs/rfcs/` — enlazado en la columna Spec. |
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

| # | ID | P | Tipo | Feature | Estado | Effort | Spec / notas |
|---|-----|---|------|---------|--------|--------|--------------|
| — | **Hecho recientemente (2026-05-27/28)** |
| ✓ | ENG-101 | — | Bug | Sincronización doc/código (versión CLI, conteo MCP, CHANGELOG) | Done | S | `69e83d7` |
| ✓ | ENG-102 | — | Bug | `mem_current_project` implementado en código | Done | S | `69e83d7` |
| ✓ | ENG-103 | — | Bug | Tests Obsidian CRLF (LF en markdown) | Done | S | `69e83d7` |
| ✓ | ENG-104 | — | Bug | Docker `/app/docs` permisos MCP sync | Done | S | `bf01f18` |
| ✓ | ENG-105 | — | Bug | MCP crash con `ENGRAM_SYNC_ENABLED` (`ILocalSyncStore`) | Done | S | `a7f45eb` |
| ✓ | ENG-106 | — | Feature | Hub MCP multi-editor (`config/mcp/`, `scripts/setup.*`) | Done | M | PR ENG-201 |
| ✓ | ENG-201 | — | Chore | MCP/setup/docs + reglas Cursor + GIT-WORKFLOW + backlog | Done | S | PR ENG-201 |
| — | **Siguiente (cerrar base pre-release)** |
| 1 | ENG-202 | P0 | Doc | OSS essentials: `CONTRIBUTING.md` (enlaza `GIT-WORKFLOW.md`), `CODE_OF_CONDUCT`, `SECURITY` | In Progress | S | [GIT-WORKFLOW.md](GIT-WORKFLOW.md) ya creado |
| 2 | ENG-203 | P0 | Doc | Plantillas GitHub: issue + PR template | In Progress | S | — |
| 3 | ENG-204 | P0 | Chore | Pinear `ModelContextProtocol` (no `*-*`) | Ready | S | Reproducible builds |
| 4 | ENG-205 | P0 | Doc | Auditoría README vs código (tools, endpoints, versiones) | Ready | M | Post sesión pre-release |
| 5 | ENG-206 | P0 | Test | PostgreSQL: arreglar 3 tests skipped | Ready | M | [sdd/postgres-bug-fixes/](../sdd/postgres-bug-fixes/) |
| 6 | ENG-207 | P0 | Feature | Logging infrastructure | Ready | M | [sdd/logging-infrastructure/](../sdd/logging-infrastructure/specs/logging-infrastructure.md) |
| 7 | ENG-208 | P1 | Feature | Completar Upstream Phase 2 API parity | Ready | M | Ver [ROADMAP](ROADMAP.md); `mem_current_project` ya Done |
| 8 | ENG-209 | P1 | Test | Manual: pull entre 2 clientes (sync) | Ready | S | [ROADMAP § Manual Testing](ROADMAP.md#-manual-testing-backlog) |
| 9 | ENG-210 | P1 | Test | Manual: offline + reconexión | Ready | S | Idem |
| — | **Meta junio — instalador y DX** |
| 10 | ENG-301 | P1 | Feature | Instalador Windows (MSI o script) + `engram` en PATH | Ready | L | Evolución de `scripts/setup.ps1` |
| 11 | ENG-302 | P1 | Feature | Wizard gráfico: modo local vs offline-first sync | Ready | L | Depende ENG-301 |
| 12 | ENG-303 | P1 | Doc | Guía “instalación desde git” unificada (enlaza `config/mcp/INSTALL.md`) | Ready | S | — |
| 13 | ENG-304 | P1 | Chore | `global.json` + `Directory.Build.props` (versiones centralizadas) | Ready | S | — |
| 14 | ENG-305 | P1 | Chore | Badge CI en README | Ready | S | — |
| — | **Icebox (no sacar hasta vaciar P0/P1)** |
| — | ENG-401 | P2 | Feature | Backend config file `~/.engram/config.json` | Icebox | M | [sdd/backend-config-switch/](../sdd/backend-config-switch/proposal.md) |
| — | ENG-402 | P2 | Chore | Giant class refactor (Sqlite/Postgres partial) | Icebox | L | [TECHNICAL-DEBT](TECHNICAL-DEBT.md) TD-001/002 |
| — | ENG-403 | P2 | Feature | Phase 3 — breaking (quitar `project` de writes) | Icebox | L | Requiere guía migración |
| — | ENG-404 | P2 | Feature | Phase 4 — memory relations | Icebox | XL | — |
| — | ENG-405 | P2 | Feature | Authentication & access control | Icebox | L | Sin proposal aún |
| — | ENG-406 | P2 | Feature | Tool deferral MCP | Icebox | — | Blocked: SDK .NET |

---

## Features detalladas (próximas en cola)

### ENG-202 — OSS essentials (P0)

**Stories:**
- [ ] `CONTRIBUTING.md` (build, test, PR flow)
- [ ] `CODE_OF_CONDUCT.md` (Contributor Covenant)
- [ ] `SECURITY.md` (cómo reportar vulnerabilidades)

**Hecho cuando:** un contribuidor externo puede clonar, testear y abrir PR sin preguntar por chat.

---

### ENG-206 — PostgreSQL tests (P0)

**Stories:**
- [ ] FTS ranking: alinear expectativa test vs Postgres
- [ ] FK rollback: SAVEPOINT o assert compatible
- [ ] Merge projects: visibilidad post-transacción

**Hecho cuando:** `Engram.Postgres.Tests` sin skips en CI.

---

### ENG-301 — Instalador (P1, junio)

**Stories:**
- [ ] Publicar `engram.exe` estable (win-x64 / linux-x64) en release GitHub
- [ ] Script/MSI que instale en PATH
- [ ] Ejecutar wizard post-install (local vs sync)
- [ ] Escribir configs en `config/mcp/generated/` + opción copiar a Cursor

**Hecho cuando:** usuario sin .NET SDK puede instalar y conectar MCP en &lt;10 min.

---

### ENG-106 — Hub MCP multi-editor (Done)

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

## Changelog del backlog

| Fecha | Cambio |
|-------|--------|
| 2026-05-28 | ENG-106/ENG-201 Done; ENG-202/ENG-203 In Progress (OSS essentials + templates) |
| 2026-05-28 | Creación del backlog ordenado; ítems de sesión pre-release y meta junio |
| 2026-05-27 | Fixes doc/MCP/Docker/Obsidian (commits en main) |
