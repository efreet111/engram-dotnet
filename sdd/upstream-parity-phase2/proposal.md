# Proposal: Upstream Parity — Phase 2 (API Parity)

## Intent

Agregar nuevos endpoints, herramientas MCP, y mejoras al CLI que completan la paridad con el upstream Go sin cambiar el contrato existente de los write tools.

## Problema

### Sin DELETE de sesiones/prompts
El upstream agregó `DELETE /sessions/{id}` y `DELETE /prompts/{id}`. Engram-dotnet no tiene forma de borrar sesiones o prompts individualmente.

### Sin herramienta para ver proyecto actual
El upstream agregó `mem_current_project` — un tool read-only que le dice al agente qué proyecto va a usar antes de escribir. Sin esto, el agente opera a ciegas.

### Errores sin estructura
Cuando la detección de proyecto falla, devolvemos texto plano. El upstream devuelve errores estructurados con `error_code`, `available_projects`, `hint`.

### Obsidian export sin watch mode
Nuestro export es one-shot. El upstream tiene `--watch` (daemon continuo) y `--since` (filtro por fecha).

### Export sin filtro por proyecto
Nuestro `/export` exporta TODO. El upstream permite `GET /export?project=X` para un solo proyecto.

## Alcance

### In scope
1. **DELETE /sessions/{id}** — Borrar sesión con FK guard
2. **DELETE /prompts/{id}** — Borrar prompt con soft-delete
3. **mem_current_project** — MCP tool read-only
4. **Structured error responses** — Errores MCP con metadata
5. **Obsidian-export --watch** — Daemon continuo con intervalo
6. **Obsidian-export --since** — Filtro por fecha
7. **ExportProject** — Exportar un solo proyecto

### Out scope
- Remover `project` de write tools (breaking — Fase 3)
- Project envelope en responses (Fase 3)
- Memory conflict surfacing (Fase 4)
- Tool profiles (Fase 5)

---

## 1. DELETE /sessions/{id}

### Qué es
Endpoint para borrar una sesión y sus datos asociados.

### Implementación

#### IStore
```csharp
Task<DeleteResult> DeleteSessionAsync(string id);
```

#### DeleteResult
```csharp
public record DeleteResult(
    bool Success,
    string? Error,        // "session_not_found", "blocked_by_observations", "blocked_by_sync"
    int DeletedObservations,
    int DeletedPrompts
);
```

#### Comportamiento
1. Verificar que la sesión existe → 404 si no
2. Verificar que no tiene observaciones activas → 409 si tiene (blocked)
3. Borrar prompts asociados (soft-delete: set `deleted_at`)
4. Borrar sesión
5. Retornar conteo de items borrados

#### Endpoint
```
DELETE /sessions/{id}
→ 200 { "deleted_sessions": 1, "deleted_prompts": 5 }
→ 404 { "error": "session_not_found" }
→ 409 { "error": "session_has_observations", "observation_count": 42 }
```

### Criterios de aceptación
- [ ] 404 si sesión no existe
- [ ] 409 si sesión tiene observaciones activas
- [ ] Soft-delete de prompts asociados
- [ ] Funciona en SQLite y PostgreSQL
- [ ] FK constraints respetadas

---

## 2. DELETE /prompts/{id}

### Qué es
Endpoint para borrar un prompt individual.

### Implementación

#### IStore
```csharp
Task<bool> DeletePromptAsync(string id);
```

#### Comportamiento
1. Verificar que el prompt existe → 404 si no
2. Soft-delete: set `deleted_at = NOW()` en `prompt_tombstones`
3. Retornar 200

#### Endpoint
```
DELETE /prompts/{id}
→ 200 { "deleted": true }
→ 404 { "error": "prompt_not_found" }
```

### Criterios de aceptación
- [ ] 404 si prompt no existe
- [ ] Soft-delete (no hard delete)
- [ ] Funciona en SQLite y PostgreSQL

---

## 3. mem_current_project MCP Tool

### Qué es
Tool read-only que retorna qué proyecto se usaría para el cwd actual. **Nunca falla** — siempre retorna éxito incluso cuando el proyecto es ambiguo.

### Implementación

#### Tool definition
```csharp
[McpServerTool(Name = "mem_current_project")]
public string GetCurrentProject(string? workingDir = null)
{
    var result = _projectDetector.DetectProjectFull(workingDir);
    return JsonSerializer.Serialize(new
    {
        project = result.Project,
        project_source = result.Source,
        project_path = result.ProjectPath,
        cwd = workingDir ?? Directory.GetCurrentDirectory(),
        available_projects = result.AvailableProjects,
        warning = result.Warning
    });
}
```

#### Comportamiento
- Nunca lanza excepción
- Si es ambiguo: retorna el primer proyecto encontrado + `available_projects` + `warning`
- Si no hay git repo: retorna basename del directorio
- Recomendado como primera llamada al iniciar sesión

### Criterios de aceptación
- [ ] Nunca retorna error
- [ ] Incluye `project`, `project_source`, `project_path`, `cwd`
- [ ] Incluye `available_projects` cuando hay múltiples repos
- [ ] Incluye `warning` cuando hay ambigüedad

---

## 4. Structured Error Responses

### Qué es
Cuando un MCP tool falla, retornar JSON estructurado en vez de texto plano.

### Formato
```json
{
  "error": true,
  "error_code": "ambiguous_project",
  "message": "Multiple git repositories found in working directory",
  "available_projects": ["auth-service", "frontend", "shared"],
  "hint": "Navigate to one of the project directories before writing"
}
```

### Error codes
| Código | HTTP | Descripción |
|--------|------|-------------|
| `ambiguous_project` | 409 | Múltiples repos en cwd |
| `project_not_found` | 404 | Proyecto no existe en store |
| `session_not_found` | 404 | Sesión no existe |
| `prompt_not_found` | 404 | Prompt no existe |
| `blocked_by_observations` | 409 | Sesión tiene observaciones |
| `validation_error` | 400 | Parámetro inválido |

### Implementación
```csharp
public static class McpErrors
{
    public static string Structured(string code, string message, Dictionary<string, object>? meta = null);
}
```

### Criterios de aceptación
- [ ] Todos los errores MCP usan formato estructurado
- [ ] `error_code` es machine-readable (snake_case)
- `message` es human-readable
- [ ] `meta` opcional con contexto adicional

---

## 5. Obsidian Export --watch

### Qué es
Modo daemon que exporta observaciones continuamente cada N segundos.

### CLI
```bash
engram obsidian-export --watch                    # Cada 60s (default)
engram obsidian-export --watch --interval 30s     # Cada 30s
engram obsidian-export --watch --interval 5m      # Cada 5 minutos
```

### Implementación
```csharp
if (watch)
{
    var interval = ParseInterval(intervalStr);
    using var timer = new PeriodicTimer(interval);
    while (await timer.WaitForNextTickAsync(cancellationToken))
    {
        var since = lastExport ?? GetLastExportTime();
        await exporter.ExportAsync(outputPath, since: since);
        lastExport = DateTime.UtcNow;
    }
}
```

### Comportamiento
1. Ejecuta export inicial inmediatamente
2. Espera el intervalo
3. Exporta solo observaciones nuevas (desde último export)
4. Repite hasta Ctrl+C
5. Log en stderr: `[watch] exported 3 observations at 14:32:01`

### Criterios de aceptación
- [ ] Export inicial inmediato
- [ ] Interval configurable (--interval)
- [ ] Solo exporta observaciones nuevas desde último export
- [ ] Graceful shutdown con Ctrl+C
- [ ] Log de cada ciclo en stderr

---

## 6. Obsidian Export --since

### Qué es
Filtrar observaciones por fecha de creación.

### CLI
```bash
engram obsidian-export --since 2025-01-01
engram obsidian-export --since 30d     # Últimos 30 días
engram obsidian-export --since 7d      # Última semana
```

### Implementación
```csharp
public async Task ExportAsync(string outputPath, DateTime? since = null)
{
    var observations = await _store.ListObservationsAsync();
    if (since.HasValue)
    {
        observations = observations.Where(o => DateTime.Parse(o.CreatedAt) >= since.Value);
    }
    // ...
}
```

### Criterios de aceptación
- [ ] `--since` acepta formato ISO 8601 (`2025-01-01`)
- [ ] `--since` acepta formato relativo (`30d`, `7d`, `24h`)
- [ ] Filtra correctamente por `created_at`
- [ ] Compatible con `--watch` (watch usa since internamente)

---

## 7. ExportProject (Single Project Export)

### Qué es
Exportar observaciones de un solo proyecto en vez de todos.

### API
```
GET /export?project=my-project
→ Exporta solo observaciones del proyecto especificado
```

### CLI
```bash
engram obsidian-export --project my-project
```

### IStore
```csharp
// Ya existe ListObservationsAsync, agregar filtro por project
Task<List<Observation>> ListObservationsByProjectAsync(string project);
```

### Comportamiento
1. Verificar que el proyecto existe → 404 si no
2. Exportar solo observaciones de ese proyecto
3. Mantener formato Obsidian idéntico

### Criterios de aceptación
- [ ] 404 si proyecto no existe
- [ ] Exporta solo observaciones del proyecto
- [ ] Funciona en SQLite y PostgreSQL
- [ ] Compatible con `--watch` y `--since`

---

## Impacto en el código

### Archivos a modificar

| Archivo | Cambio |
|---------|--------|
| `src/Engram.Store/IStore.cs` | `DeleteSessionAsync`, `DeletePromptAsync`, `ListObservationsByProjectAsync` |
| `src/Engram.Store/SqliteStore.cs` | Implementación de nuevos métodos |
| `src/Engram.Store/PostgresStore.cs` | Implementación de nuevos métodos |
| `src/Engram.Store/Models.cs` | `DeleteResult` record |
| `src/Engram.Server/EngramServer.cs` | `DELETE /sessions/{id}`, `DELETE /prompts/{id}`, `GET /export?project=X` |
| `src/Engram.Mcp/EngramTools.cs` | `mem_current_project` tool, structured errors |
| `src/Engram.Mcp/McpErrors.cs` | **Nuevo archivo** — error helpers |
| `src/Engram.Cli/Program.cs` | `--watch`, `--since`, `--project` flags |
| `src/Engram.Obsidian/Exporter.cs` | Soporte para `since` y `project` filter |

### Breaking changes
**Ninguno.** Todos los cambios son aditivos.

---

## Esfuerzo estimado

| Componente | Esfuerzo | Complejidad |
|------------|----------|-------------|
| DELETE /sessions/{id} | 1-2h | Baja |
| DELETE /prompts/{id} | 1h | Baja |
| mem_current_project | 0.5h | Baja |
| Structured errors | 1h | Baja |
| Obsidian --watch | 1-2h | Baja |
| Obsidian --since | 0.5h | Baja |
| ExportProject | 1h | Baja |
| **Total** | **6-8h** | |

---

## Tradeoffs

| Opción | Pros | Contras |
|--------|------|---------|
| Soft-delete vs hard-delete (prompts) | Soft: recuperable, audit trail | Hard: menos storage |
| Watch con timer vs FileSystemWatcher | Timer: simple, portable | FSW: más reactivo pero platform-specific |
| --since relative parsing | UX mejorada | Más código de parsing |

---

## Dependencias con otras fases

- **Requiere Fase 1**: Usa `DetectProjectFull` de la Fase 1 para `mem_current_project`
- **Pre-requisito para Fase 3**: Los structured errors son la base para el project envelope
- **Independiente de Fase 4**: No depende de memory relations
