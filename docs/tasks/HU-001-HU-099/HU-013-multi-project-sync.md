# HU-013 — Multi-Project Sync Management

**As**: Developer / User
**I want**: ability to manage which projects sync with which servers, with minimal friction
**To**: avoid manual enrollment per project and have fine-grained control over sync behavior per server

---

## Acceptance Criteria

- [ ] **R1**: CLI discovers all projects almacenados en la base de datos SQLite local (tablas `observations`/`mutations`) y ofrece enrollment sin tipeo manual
- [ ] **R2**: MCP/Agent puede SOLO sugerir enrollment en el proyecto actual — nunca auto-enroll
- [ ] **R3**: Pull behavior configurable via CLI en setup inicial (no via MCP)
- [ ] **R4**: Migration: proyectos ya enrolados preguntan al usuario qué behavior asignar
- [ ] **R5**: Unenroll: el config NO se borra automáticamente — se pregunta al usuario si desea mantener o eliminar
- [ ] CLI selector interactivo (estilo fzf) para seleccionar proyectos a enroll/exclude
- [ ] Flags existentes del CLI siguen funcionando (backward compatible)
- [ ] Per-server denylist: un proyecto puede estar enrolado en Server A pero excluido de Server B localmente
- [ ] `sync_behavior` por proyecto: `silent-skip` o `fail-loud`
- [ ] Config file en `~/.engram/sync-projects.dotnet.yml` con soporte para `ENGRAM_CONFIG_DIR`
- [ ] Estructura YAML soporta `default_behavior`, `projects[].behavior`, `projects[].excluded_servers`

---

## Tasks (Implementation)

- [ ] Diseñar schema de tabla para `sync_config` en SQLite (project_name, behavior, excluded_servers JSON)
- [ ] Implementar comando CLI `sync enroll` con auto-discovery desde DB
- [ ] Implementar selector interactivo estilo fzf para `sync enroll --interactive`
- [ ] Implementar flag `--behavior=silent-skip|fail-loud` en `sync enroll`
- [ ] Implementar flag `--exclude-server=<server-id>` en `sync enroll`
- [ ] Agregar `--sync-behavior` y `--exclude-servers` a `sync status`
- [ ] Implementar `sync unenroll` con prompt de retención de config
- [ ] Crear lógica de migración para proyectos ya enrolados (prompt interactivo)
- [ ] Implementar `--pull-behavior` en CLI setup inicial
- [ ] Crear función `suggest_enrollment()` en MCP que solo loguea/warneá sin auto-enroll
- [ ] Implementar lectura de `~/.engram/sync-projects.dotnet.yml` con override de `ENGRAM_CONFIG_DIR`
- [ ] Integrar `silent-skip` y `fail-loud` en SyncManager — fail-loud debe bloquear sync si hay proyectos no enrolados con mutations pendientes
- [ ] Tests unitarios para CLI commands y SyncManager behavior logic
- [ ] Tests de integración con PostgreSQL para sync behavior

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
