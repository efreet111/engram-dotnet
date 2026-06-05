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
| — | **Hecho recientemente (2026-05-27/28)** |
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
| — | **Siguiente (cerrar base pre-release)** |
| ✓ | ENG-207 | P0 | Feature | Logging infrastructure | Done | M | roadmap | [sdd/logging-infrastructure/](../sdd/logging-infrastructure/specs/logging-infrastructure.md) |
| 2 | ENG-208 | P1 | Feature | Completar Upstream Phase 2 API parity | Ready | M | ← upstream engram | `mem_current_project` ya Done |
| 3 | ENG-209 | P1 | Test | Manual: pull entre 2 clientes (sync) | Ready | S | roadmap | [ROADMAP § Manual Testing](ROADMAP.md#-manual-testing-backlog) |
| 4 | ENG-210 | P1 | Test | Manual: offline + reconexión | Ready | S | roadmap | Idem |
| 5 | ENG-306 | P1 | Chore | Mejorar trazabilidad en backlog (columna Origen + template HU) | Ready | S | ← ENG-205 | Columna Origen agregada |
| — | **Meta junio — instalador y DX** |
| 5 | ENG-301 | P1 | Feature | Instalador Windows (MSI o script) + `engram` en PATH | Ready | L | roadmap | Evolución de `scripts/setup.ps1` |
| 6 | ENG-302 | P1 | Feature | Wizard gráfico: modo local vs offline-first sync | Ready | L | → ENG-301 | — |
| 7 | ENG-303 | P1 | Doc | Guía “instalación desde git” unificada (enlaza `config/mcp/INSTALL.md`) | Ready | S | → ENG-301 | — |
| 8 | ENG-304 | P1 | Chore | `global.json` + `Directory.Build.props` (versiones centralizadas) | Ready | S | backlog | — |
| — | **Icebox (no sacar hasta vaciar P0/P1)** |
| — | ENG-401 | P2 | Feature | Backend config file `~/.engram/config.json` | Icebox | M | [sdd/backend-config-switch/](../sdd/backend-config-switch/proposal.md) |
| — | ENG-402 | P2 | Chore | Giant class refactor (Sqlite/Postgres partial) | Icebox | L | [TECHNICAL-DEBT](TECHNICAL-DEBT.md) TD-001/002 |
| — | ENG-403 | P2 | Feature | Phase 3 — breaking (quitar `project` de writes) | Icebox | L | Requiere guía migración |
| — | ENG-404 | P2 | Feature | Phase 4 — memory relations | Icebox | XL | — |
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

### ENG-208 — Upstream Phase 2 API parity (P1)

**Problema:** El upstream original de Engram tiene endpoints que aún no portamos (DELETE de sessions/prompts ya están, faltan algunos). Si alguien migra desde el upstream espera la misma API.

**Para qué sirve:** Compatibilidad total con clientes que usan la API original.

**Stories:**
- [ ] Revisar diff de endpoints entre upstream y este port
- [ ] Implementar endpoints faltantes
- [ ] Tests de paridad

**Cómo probar:**
```bash
# Comparar contra la API reference del upstream
curl http://localhost:7437/health
```

**Hecho cuando:** todos los endpoints del upstream existen acá con la misma firma.

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

## Changelog del backlog

| Fecha | Cambio |
|-------|--------|
| 2026-06-05 | ENG-207 cerrado: Logging infrastructure (5 FRs, PM-*, ~3-4h, código no desplegado) |
| 2026-06-05 | Logging infrastructure implementado: JSON logging, client_ip, body preview, ENGRAM_LOG_LEVEL env var. |
| 2026-06-04 | Verificación manual post-deploy: smoke test + 5 regression tests (R1-R5) contra `192.168.0.178:7437`, todos OK. Checklist actualizado. |
| 2026-05-28 | Sesión completa pre-release: ENG-202→206, ENG-305 Done. Columna Origen agregada. |
| 2026-05-28 | Creación del backlog ordenado; ítems de sesión pre-release y meta junio |
| 2026-05-27 | Fixes doc/MCP/Docker/Obsidian (commits en main) |
