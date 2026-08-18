# HU-018 — Sync Interactive Batch & TUI Navigator

**As**: Developer con múltiples proyectos locales
**I want**: Gestionar enrollment y sync de múltiples proyectos de forma masiva e interactiva
**To**: Tener visibilidad total del estado de sync y poder sincronizar todos mis proyectos sin depender de comandos individuales por proyecto

---

## Acceptance Criteria

### Batch Enrollment

- [ ] `engram sync enroll --all` enrola de un golpe todos los proyectos que:
  - (a) tienen mutaciones pendientes en la BD local
  - (b) NO están enrolados localmente todavía
- [ ] Si un proyecto ya está enrolado, el comando igual hace push de sus mutaciones pendientes
- [ ] El comando reporta cuántas mutaciones se pushearon por proyecto
- [ ] Si no hay proyectos que cumplan las condiciones, el comando lo indica claramente

### Vista de Enrollment (comando nuevo)

- [ ] `engram sync status --local` lista TODOS los proyectos con:
  - Nombre del proyecto
  - ¿Está enrolado? (✓/—)
  - Behavior actual (silent-skip / fail-loud)
  - Cantidad de mutaciones pendientes
- [ ] `engram sync status --local --json` output machine-readable
- [ ] `engram sync status` (sin flag) sigue funcionando como antes (vista server-centric)

### Columna Enrolled en projects list

- [ ] `engram projects list` muestra columna adicional "Enrolled" (✓/—)
- [ ] Los proyectos enrolados muestran ✓, los no enrolados muestran —
- [ ] Los proyectos enrolados también muestran su behavior entre paréntesis

### Modo Interactivo (TUI)

- [ ] `engram interactive` inicia un menú jerárquico TUI
- [ ] El menú permite navegar por las siguientes secciones:
  - **Status y Vista**: sync status --local, projects list
  - **Sync**: enroll --all, enroll --interactive, push --all, sync status
  - **Proyectos**: list, consolidate, prune
  - **Salir**
- [ ] Navegación por números (1, 2, 3...) y opción para volver atrás
- [ ] El TUI funciona en cualquier terminal sin dependencias externas (sin fzf, sin ncurses obligatorio)
- [ ] Ctrl+C sale del TUI de forma limpia

---

## Tasks (Implementation)

### Batch Enrollment

- [ ] Agregar `--all` flag a `syncEnrollCmd` en `Program.cs`
- [ ] Implementar lógica de enrollment batch:
  - Obtener proyectos con mutaciones pendientes via `ListDistinctProjectsWithPendingMutationsAsync`
  - Filtrar los ya enrolados localmente via `GetEnrolledProjectsLocalAsync`
  - Enrolar los no enrolados + hacer push de todos
- [ ] Reportar counts de mutaciones push eadas por proyecto
- [ ] Agregar tests para `sync enroll --all`

### Sync Status Local

- [ ] Crear `syncStatusLocalCmd` con flag `--local` y `--json`
- [ ] Implementar query que traiga todos los proyectos con enrollment info
- [ ] Mantener backward compatibility con `sync status` existente (server-centric)
- [ ] Agregar tests para `sync status --local`

### Projects List con Enrolled

- [ ] Modificar `projectsListCmd` handler para hacer join con enrollment data
- [ ] Mostrar output con formato de línea por proyecto: `nombre  obs  sessions  prompts  Enrolled  Behavior`
- [ ] Agregar `--json` flag a `projects list` para output machine-readable
- [ ] Verificar que no rompa tests existentes de `projects list`

### TUI Interactive Mode

- [ ] Crear clase/módulo `InteractiveMenu.cs` con:
  - Función `RunInteractiveLoop()` con ciclo while
  - Menú raíz con 4 secciones (Status, Sync, Proyectos, Salir)
  - Submenús para cada sección
  - Función `RenderMenu(title, options)` que imprime y lee input
- [ ] Crear comando `interactiveCmd` en root
- [ ] Implementar submenús interactivos:
  - Status → sync status --local, projects list
  - Sync → enroll --all, enroll --interactive, push --all, sync status
  - Proyectos → list, consolidate, prune
- [ ] Manejar Ctrl+C gracefully (no stack trace)
- [ ] Agregar tests de render del menú (opcional,取决于 esfuerzo)

---

## Notes

- **Dependencia**: Esta HU extiende HU-013 (multi-project sync) y HU-014 (smart sync triggers). No debería necesitar nuevos ADR.
- **TUI sin dependencias externas**: Se implementa con `Console.WriteLine` + `Console.ReadLine` puro. Sin `fzf`, sin `terminal.gui`, sin `ncurses`. Esto mantiene el CLI self-contained.
- **Scope de TUI**: Cubre sync y projects. No incluye `serve`, `mcp`, ni comandos de administración de memoria (`save`, `search`, `promote`). Se puede extender en future HUs.
- **Postgres compatibility**: `sync enroll --all` y `sync status --local` usan SQLite local store; verificar que no rompan cuando `ENGRAM_DB_TYPE=postgres`.
