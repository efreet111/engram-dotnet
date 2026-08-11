# HU-013 — Multi-Project Sync Management

**Status**: ✅ Done
**Owner**: @owner
**Completed**: 2026-08-11
**Implementation**: commit `hu-013-implementation` (engram-dotnet)

**As**: Developer / User
**I want**: ability to manage which projects sync with which servers, with minimal friction
**To**: avoid manual enrollment per project and have fine-grained control over sync behavior per server

---

## Acceptance Criteria

- [x] **R1**: CLI discovers all projects almacenados en la base de datos SQLite local (tablas `observations`/`mutations`) y ofrece enrollment sin tipeo manual
- [x] **R2**: MCP/Agent puede SOLO sugerir enrollment en el proyecto actual — nunca auto-enroll
- [ ] **R3**: Pull behavior configurable via CLI en setup inicial (no via MCP) — deferred
- [x] **R4**: Migration: proyectos ya enrolados preguntan al usuario qué behavior asignar
- [x] **R5**: Unenroll: el config NO se borra automáticamente — se pregunta al usuario si desea mantener o eliminar
- [x] CLI selector interactivo (estilo fzf) para seleccionar proyectos a enroll/exclude
- [x] Flags existentes del CLI siguen funcionando (backward compatible)
- [x] Per-server denylist: un proyecto puede estar enrolado en Server A pero excluido de Server B localmente
- [x] `sync_behavior` por proyecto: `silent-skip` o `fail-loud`
- [x] Config file en `~/.engram/sync-projects.dotnet.yml` con soporte para `ENGRAM_CONFIG_DIR`
- [x] Estructura YAML soporta `default_behavior`, `projects[].behavior`, `projects[].excluded_servers`

---

## Tasks (Implementation)

- [x] Diseñar schema de tabla para `sync_config` en SQLite (project_name, behavior, excluded_servers JSON)
- [x] Implementar comando CLI `sync enroll` con auto-discovery desde DB
- [x] Implementar selector interactivo estilo fzf para `sync enroll --interactive`
- [x] Implementar flag `--behavior=silent-skip|fail-loud` en `sync enroll`
- [x] Implementar flag `--exclude-server=<server-id>` en `sync enroll`
- [x] Agregar `--sync-behavior` y `--exclude-servers` a `sync status`
- [x] Implementar `sync unenroll` con prompt de retención de config
- [x] Crear lógica de migración para proyectos ya enrolados (prompt interactivo)
- [ ] Implementar `--pull-behavior` en CLI setup inicial — deferred
- [x] Crear función `suggest_enrollment()` en MCP que solo loguea/warneá sin auto-enroll
- [x] Implementar lectura de `~/.engram/sync-projects.dotnet.yml` con override de `ENGRAM_CONFIG_DIR`
- [x] Integrar `silent-skip` y `fail-loud` en SyncManager — fail-loud debe bloquear sync si hay proyectos no enrolados con mutations pendientes
- [x] Tests unitarios para CLI commands y SyncManager behavior logic
- [ ] Tests de integración con PostgreSQL para sync behavior — skipped (Testcontainers, se ejecutan en CI)

---

## Notes

### Arquitectura de sync_behavior

| Behavior | Comportamiento |
|----------|----------------|
| `silent-skip` | Proyecto excluido o no enrolado → se skippea, sync continúa |
| `fail-loud` | Proyecto no enrolado con pending mutations → BLOQUEA todo el sync (default actual) |

### Flujo de enrollment

```
1. CLI detecta proyectos existentes en DB (queries distintas por backend)
2. Usuario selecciona cuáles enrolar (fzf o flags)
3. Para cada proyecto: behavior + excluded_servers
4. Se escribe sync_config a SQLite + config file YAML
```

### MCP Suggestion Protocol

Cuando `mem_save()` se llama en un proyecto nuevo, el MCP/Agent:
1. Verifica si el proyecto ya está enrolado
2. Si NO → solo loguea: `"[engram] Este proyecto no está enrolado para sync. Ejecutá `engram sync enroll` para activarlo."`
3. No modifica estado, no toca DB, no hace enroll automático

### Config File Schema

```yaml
# ~/.engram/sync-projects.dotnet.yml
default_behavior: silent-skip  # aplica a proyectos nuevos
projects:
  mi-proyecto:
    behavior: fail-loud
    excluded_servers:
      - server-2
  otro-proyecto:
    behavior: silent-skip
    excluded_servers: []
```

### Migration Flow

Al detectar proyectos ya enrolados en DB (sin sync_config):
1. Mostrar lista de proyectos detectados
2. Para cada uno: preguntar `silent-skip` o `fail-loud`
3. O opción "aplicar default a todos"
4. Persistir configuración resultante

### Unenroll Flow

```
$ engram sync unenroll mi-proyecto
Proyecto 'mi-proyecto' desenrolado.
¿Qué hago con la configuración guardada?
  [1] Mantener (para futuro re-enroll)
  [2] Eliminar
> 1
```

- Dependency: requiere ADR para la decisión de storing sync_config en YAML vs SQLite (temporalmente en ambos hasta estabilizar)
- `included_servers` y `excluded_servers` son complementarios — ambos se soportan en la misma HU.
- Rate limiting por proyecto: postergado para HU separada (ver ROADMAP.md → Future Ideas)
- Multi-server dedup: ver [RFC-006](docs/architecture/rfc/RFC-006-multi-server-pull-deduplication.md) para estrategia de deduplicación en pull de múltiples servidores. **Al implementar, generar ADR.**
