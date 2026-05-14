# Proposal: Memory Health & Continuity

## Intent

Implementar un sistema de 3 capas para mantener la memoria de Engram útil a largo plazo:
1. **Métricas de retención** — visibilidad del estado de la memoria
2. **TTL configurable** — expiración automática de observaciones viejas
3. **Interconexión de proyectos** — redirect hints para proyectos renombrados/consolidados

## Problema

### Acumulación sin control
Las observaciones se acumulan indefinidamente. Después de 1 año de desarrollo, un proyecto tiene miles de observaciones mezclando **señal** (decisiones de arquitectura, bugfixes) con **ruido** (debugging temporal, comandos efímeros). No hay mecanismo para distinguir ni limpiar.

### Contexto desactualizado
Cuando un proyecto se renombra (ej: `login` → `auth-service`), las observaciones viejas siguen apuntando al nombre antiguo. El agente busca "login" y encuentra info desactualizada en vez del proyecto actual.

### Sin visibilidad
No hay forma rápida de responder: "¿Cuántas observaciones viejas tengo?", "¿Qué proyectos están inactivos?", "¿Vale la pena limpiar?"

## Alcance

### In scope (100% engram-dotnet)
- Capa 1: Métricas de retención (endpoint, CLI, MCP tool)
- Capa 2: TTL configurable (store method, CLI, config)
- Capa 3: Redirect hints en search results (store + server)

### Out scope (depende del agente)
- El agente siguiendo redirects automáticamente (el agente decide si usa el campo `redirect`)
- UI visual para métricas (solo CLI + JSON por ahora)
- Archive/export automático de observaciones expiradas (fase posterior)

---

## Capa 1: Métricas de Retención

### Qué es
Endpoint + CLI + MCP tool que devuelve un reporte de la salud de la memoria: distribución por edad, proyectos inactivos, observaciones sin topic_key.

### Por qué
Sin métricas, no se puede tomar decisiones informadas sobre limpieza. El humano necesita una foto clara en 10 segundos.

### Cómo

#### Nuevo endpoint
```
GET /retention/stats
→ 200 RetentionStats
```

#### Modelo
```csharp
public class RetentionStats
{
    [JsonPropertyName("total_observations")] public int TotalObservations { get; set; }
    [JsonPropertyName("age_buckets")] public List<AgeBucket> AgeBuckets { get; set; } = [];
    [JsonPropertyName("inactive_projects")] public List<InactiveProject> InactiveProjects { get; set; } = [];
    [JsonPropertyName("without_topic_key_90d")] public int WithoutTopicKey90d { get; set; }
    [JsonPropertyName("recommendation")] public string Recommendation { get; set; } = "";
}

public class AgeBucket
{
    [JsonPropertyName("label")] public string Label { get; set; } = "";  // "< 30 days", "30-90 days", etc.
    [JsonPropertyName("count")] public int Count { get; set; }
    [JsonPropertyName("without_topic_key")] public int WithoutTopicKey { get; set; }
}

public class InactiveProject
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("last_activity")] public string LastActivity { get; set; } = "";
    [JsonPropertyName("observation_count")] public int ObservationCount { get; set; }
}
```

#### Age buckets
| Bucket | Rango |
|--------|-------|
| `< 30 days` | `created_at > NOW() - 30 days` |
| `30-90 days` | `created_at > NOW() - 90 days AND <= 30 days` |
| `90-180 days` | `created_at > NOW() - 180 days AND <= 90 days` |
| `180-365 days` | `created_at > NOW() - 365 days AND <= 180 days` |
| `> 365 days` | `created_at <= NOW() - 365 days` |

#### CLI
```bash
engram retention check          # Muestra reporte formateado
engram retention check --json   # Muestra JSON crudo
```

#### MCP tool
```
mem_retention_stats → string (reporte formateado)
```

### Criterios de aceptación
- [ ] `GET /retention/stats` devuelve datos correctos para SQLite y PostgreSQL
- [ ] `engram retention check` muestra reporte legible en terminal
- [ ] `mem_retention_stats` tool disponible en MCP
- [ ] Funciona con 0 observaciones (no crash)

---

## Capa 2: TTL Configurable

### Qué es
Permitir configurar expiración automática por tipo de observación. Las observaciones que superan su TTL son marcadas como `deleted_at` (soft-delete).

### Por qué
Contexto viejo contamina búsquedas y reduce la calidad de las respuestas del agente. Las observaciones de tipo `tool_use`, `command`, `file_change` tienen vida útil corta (semanas). Las de tipo `decision`, `architecture` son valiosas indefinidamente.

### Cómo

#### Configuración
```bash
# Variables de entorno
ENGRAM_TTL_TOOL_USE=30d
ENGRAM_TTL_FILE_CHANGE=30d
ENGRAM_TTL_COMMAND=30d
ENGRAM_TTL_BUGFIX=90d
ENGRAM_TTL_PATTERN=90d
ENGRAM_TTL_LEARNING=60d
ENGRAM_TTL_DISCOVERY=60d
# decision, architecture, session_summary → nunca expiran (default)
```

O via config file (futuro):
```json
{
  "ttl": {
    "tool_use": "30d",
    "file_change": "30d",
    "command": "30d",
    "bugfix": "90d",
    "pattern": "90d",
    "learning": "60d",
    "discovery": "60d"
  }
}
```

#### Store method
```csharp
Task<PruneResult> PruneOldObservationsAsync(DateTime cutoff, string? type = null, bool dryRun = false);
```

- `cutoff`: fecha límite — observaciones anteriores se marcan como deleted
- `type`: filtro por tipo (opcional — si null, aplica a todos los tipos con TTL configurado)
- `dryRun`: si true, retorna cuántas se borrarían sin ejecutar

#### CLI
```bash
engram retention prune --dry-run              # Muestra qué se borraría
engram retention prune --older-than 90d       # Borra obs > 90 días sin TTL especial
engram retention prune --type tool_use --older-than 30d
engram retention prune --apply                # Ejecuta el prune real
```

#### MCP tool
```
mem_retention_prune(older_than: "90d", dry_run: true) → string
mem_retention_prune(older_than: "90d", dry_run: false) → string (resultado)
```

#### Comportamiento
1. Solo aplica a observaciones **activas** (`deleted_at IS NULL`)
2. Respeta `topic_key` — observaciones con topic_key **nunca expiran** (son conocimiento estructurado)
3. Soft-delete — setea `deleted_at = NOW()`, no borra físicamente
4. Log en stderr: `Pruned 42 observations (tool_use: 30, command: 12)`

### Criterios de aceptación
- [ ] `PruneOldObservationsAsync` marca correctamente observaciones como deleted
- [ ] Observaciones con `topic_key` NO se borran (preservación de conocimiento)
- [ ] `--dry-run` muestra conteo sin modificar datos
- [ ] TTL configurable por tipo via env vars
- [ ] Funciona en SQLite y PostgreSQL
- [ ] MCP tool `mem_retention_prune` disponible

---

## Capa 3: Interconexión de Proyectos

### Qué es
Cuando un proyecto se renombra o consolida, el servidor devuelve **redirect hints** en los resultados de búsqueda para que el agente sepa dónde encontrar la info actualizada.

### Por qué
El proyecto `login` fue renombrado a `auth-service`. El agente busca "login" y encuentra info de hace 6 meses. Con redirect hints, el agente sabe que `login → auth-service` y puede buscar también en el proyecto nuevo.

### Cómo

#### Store de migraciones
Tabla `project_migrations` (SQLite) o equivalente en PostgreSQL:

| Column | Type | Descripción |
|--------|------|-------------|
| `from_project` | TEXT | Nombre original |
| `to_project` | TEXT | Nombre nuevo |
| `migrated_at` | TEXT | Fecha de migración |
| `notes` | TEXT | Opcional — razón de la migración |

#### API
```
POST /projects/migrate  (ya existe — extendemos para guardar redirect)
→ Guarda la migración en project_migrations

GET /projects/redirects
→ Lista todas las migraciones activas
```

#### Redirect hints en search results
```json
// GET /search?q=login
[
  {
    "observation": { "id": 42, "project": "login", ... },
    "rank": 0.8,
    "redirect": {
      "from": "login",
      "to": "auth-service",
      "migrated_at": "2025-10-15",
      "message": "Project 'login' was consolidated into 'auth-service'"
    }
  }
]
```

#### Implementación en SearchAsync
1. Ejecutar búsqueda normal
2. Para cada resultado, verificar si `observation.project` tiene una migración registrada
3. Si existe migración, agregar campo `redirect` al SearchResult
4. **Opcional**: también buscar en el proyecto destino y merge results

#### MCP tool (nuevo)
```
mem_project_redirects() → string (lista de proyectos renombrados)
```

### Criterios de aceptación
- [ ] `POST /projects/migrate` guarda redirect en `project_migrations`
- [ ] Search results incluyen campo `redirect` cuando aplica
- [ ] `GET /projects/redirects` lista migraciones
- [ ] Funciona en SQLite y PostgreSQL
- [ ] **No requiere cambios en el agente** — el redirect es informativo

---

## Impacto en el código

### Archivos a modificar

| Archivo | Capa | Cambio |
|---------|------|--------|
| `src/Engram.Store/IStore.cs` | 1, 2, 3 | `RetentionStatsAsync`, `PruneOldObservationsAsync`, `GetProjectRedirectsAsync` |
| `src/Engram.Store/SqliteStore.cs` | 1, 2, 3 | Implementación de los 3 métodos + tabla `project_migrations` |
| `src/Engram.Store/PostgresStore.cs` | 1, 2, 3 | Implementación de los 3 métodos + tabla `project_migrations` |
| `src/Engram.Store/HttpStore.cs` | 1, 2, 3 | Proxy a los nuevos endpoints |
| `src/Engram.Store/Models.cs` | 1, 3 | `RetentionStats`, `AgeBucket`, `InactiveProject`, `ProjectRedirect`, `SearchResult.Redirect` |
| `src/Engram.Server/EngramServer.cs` | 1, 3 | `GET /retention/stats`, `GET /projects/redirects`, redirect en search handler |
| `src/Engram.Cli/Program.cs` | 1, 2 | `engram retention check`, `engram retention prune` |
| `src/Engram.Mcp/EngramTools.cs` | 1, 2, 3 | `mem_retention_stats`, `mem_retention_prune`, `mem_project_redirects` |

### Nuevos archivos
| Archivo | Capa | Contenido |
|---------|------|-----------|
| `src/Engram.Store/RetentionConfig.cs` | 2 | Parsing de TTL desde env vars |

### Breaking changes
**Ninguno.** Todos los cambios son aditivos:
- Nuevos endpoints no interfieren con existentes
- Nuevo campo `redirect` en SearchResult es opcional (null si no hay migración)
- TTL es opt-in (sin config, no expira nada)

---

## Esfuerzo estimado

| Capa | Esfuerzo | Complejidad |
|------|----------|-------------|
| 1. Métricas | 2-3h | Baja — queries de agregación simples |
| 2. TTL | 3-4h | Media — parsing de config + prune logic |
| 3. Interconexión | 3-4h | Media — nueva tabla + redirect en search |
| **Total** | **8-11h** | |

---

## Tradeoffs

| Opción | Pros | Contras |
|--------|------|---------|
| TTL automático vs manual | Automático: no requiere acción humana | Puede borrar algo relevante si el TTL está mal configurado |
| Topic_key como preservación | Usa mecanismo existente, no requiere nuevo campo | Observaciones valiosas sin topic_key se pierden |
| Redirect hints vs auto-redirect | Hints: no cambia comportamiento del agente | Depende del agente seguir los hints |
| Soft-delete vs hard-delete | Soft: recuperable, audit trail | Hard: menos storage, más limpio |

---

## Recomendación

Implementar las 3 capas en orden:
1. **Capa 1 primero** — sin métricas no se puede tomar decisiones
2. **Capa 2 segundo** — con métricas, el TTL tiene sentido
3. **Capa 3 tercero** — complementa las otras dos, pero no depende de ellas

Cada capa es independiente y se puede mergear por separado.
