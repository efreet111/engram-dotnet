# Proposal: Upstream Parity — Phase 1 (Foundation)

## Intent

Portear cambios del upstream Go (v1.12 → v1.14.8) que son **aditivos** (sin breaking changes) y mejoran la base del sistema: detección de proyectos, schema, write queue, y activity tracker.

## Problema

### Project detection incompleto
Nuestro `ProjectDetector` tiene 3 casos (git_remote, git_root, dir_basename). El upstream tiene 5: agrega **git_child** (auto-promover repo hijo único) y **ambiguous** (error cuando hay múltiples repos hijos). Esto causa falsos positivos en monorepos.

### Schema desactualizado
El upstream agregó columnas a `observations`: `review_after`, `expires_at`, `embedding`, `embedding_model`, `embedding_created_at`. Sin estas columnas, no podemos soportar features futuras (decay, vector search, expiración).

### Writes concurrentes en SQLite
El upstream implementó una **write queue** (buffer 32) para serializar escrituras MCP y evitar race conditions en SQLite. Engram-dotnet no tiene esta protección — writes concurrentes pueden corromper la DB.

### Sin feedback de actividad
El upstream tiene un **session activity tracker** que mide tool calls vs saves y hace "nudge" al agente si no guardó nada en N minutos. Esto mejora la calidad de la memoria.

## Alcance

### In scope
1. **Project detection 5-case** — Agregar git_child + ambiguous, nuevo tipo `DetectionResult`
2. **Schema columns** — review_after, expires_at, embedding (nullable, sin lógica)
3. **Write queue** — Serializar writes MCP con Channel<T>
4. **Session activity tracker** — Contar tool calls/saves, nudge en mem_session_end

### Out scope
- Remover `project` de write tools (breaking — Fase 3)
- Project envelope en responses (Fase 3)
- Memory conflict surfacing (Fase 4)
- Cloud features (no aplica)

---

## 1. Project Detection 5-Case

### Qué cambia
Actualizar `ProjectDetector.cs` para matchear el algoritmo de 5 casos del upstream.

### Algoritmo actual (3 casos)
```
1. git_remote → nombre del remote origin
2. git_root → basename del repo root
3. dir_basename → fallback
```

### Algoritmo nuevo (5 casos)
```
1. git_remote → nombre del remote origin (igual)
2. git_root → basename del repo root (igual)
3. git_child → auto-promover si hay UN solo repo hijo
4. ambiguous → error si hay MÚLTIPLES repos hijos (con lista de disponibles)
5. dir_basename → fallback (igual)
```

### Nuevo tipo
```csharp
public record DetectionResult(
    string Project,
    string Source,          // "git_remote", "git_root", "git_child", "ambiguous", "dir_basename"
    string ProjectPath,
    string? Warning,        // "Multiple git repos found, using 'X'"
    string? Error,          // "Ambiguous project"
    List<string> AvailableProjects  // Populated when ambiguous
);
```

### API
```csharp
// Nuevo método
DetectionResult DetectProjectFull(string? workingDir = null);

// Compatibilidad — wrapper sobre DetectProjectFull
string DetectProject(string? workingDir = null);
```

### Criterios de aceptación
- [ ] `DetectProjectFull` retorna `DetectionResult` con los 5 casos
- [ ] `DetectProject` sigue funcionando (wrapper)
- [ ] git_child: escanea directorios hijos, si hay exactamente 1 repo, lo usa
- [ ] ambiguous: si hay >1 repo hijo, retorna error con `AvailableProjects`
- [ ] Timeout de 200ms en scanChildren (como upstream)
- [ ] Límite de 20 entradas en scanChildren

---

## 2. Schema Columns

### Qué cambia
Agregar columnas nuevas a la tabla `observations` en SQLite y PostgreSQL.

### Columnas nuevas
| Columna | Tipo | Default | Descripción |
|---------|------|---------|-------------|
| `review_after` | TEXT | NULL | Fecha sugerida para revisar (decay) |
| `expires_at` | TEXT | NULL | Fecha de expiración |
| `embedding` | BLOB | NULL | Vector embedding (reservado) |
| `embedding_model` | TEXT | NULL | Modelo que generó el embedding |
| `embedding_created_at` | TEXT | NULL | Timestamp del embedding |

### Model
```csharp
public record Observation(
    // ... existentes ...
    string? ReviewAfter = null,
    string? ExpiresAt = null,
    byte[]? Embedding = null,
    string? EmbeddingModel = null,
    string? EmbeddingCreatedAt = null
);
```

### Migraciones
- **SQLite**: `ALTER TABLE observations ADD COLUMN review_after TEXT` (x5)
- **PostgreSQL**: `ALTER TABLE observations ADD COLUMN review_after TEXT` (x5)

### Criterios de aceptación
- [ ] Columnas agregadas en SQLite (migración on-startup)
- [ ] Columnas agregadas en PostgreSQL (migración on-startup)
- [ ] No crash con DBs existentes (columnas NULL)
- [ ] No crash con DBs nuevas (columnas existen desde CREATE TABLE)

---

## 3. Write Queue

### Qué cambia
Todas las escrituras MCP pasan por una cola serializada para evitar race conditions en SQLite.

### Implementación
```csharp
public sealed class WriteQueue : IDisposable
{
    private readonly Channel<WriteJob> _queue = Channel.CreateBounded<WriteJob>(32);
    private readonly Task _worker;
    private readonly CancellationTokenSource _cts = new();

    public WriteQueue(IStore store);

    // Enqueue a write operation, returns Task that completes when write is done
    public Task<T> EnqueueAsync<T>(Func<IStore, Task<T>> operation, CancellationToken ct);

    public void Dispose();
}
```

### Comportamiento
1. MCP write tool llama `writeQueue.EnqueueAsync(store => store.SaveObservationAsync(...), ct)`
2. El job se encola en el channel (buffer 32)
3. Un worker task procesa jobs de a uno (serializado)
4. El Task del caller completa cuando el write termina
5. Si el channel está lleno, el caller espera (backpressure)
6. En Dispose, se cancelan jobs pendientes gracefulmente

### Integración
- Inyectar `WriteQueue` en el MCP server via DI
- Write tools usan `writeQueue.EnqueueAsync()` en vez de llamar al store directo
- Read tools siguen llamando al store directo (sin cola)

### Criterios de aceptación
- [ ] Writes serializados — no hay race conditions en SQLite
- [ ] Backpressure — channel lleno espera, no pierde writes
- [ ] Graceful shutdown — jobs en progreso terminan, pendientes se cancelan
- [ ] Read tools no afectados (sin cola)
- [ ] Funciona con SqliteStore y PostgresStore

---

## 4. Session Activity Tracker

### Qué cambia
Trackear actividad por sesión para dar feedback al agente sobre su comportamiento de guardado.

### Modelo
```csharp
public sealed class SessionActivity
{
    private readonly ConcurrentDictionary<string, SessionStats> _sessions = new();

    public void RecordToolCall(string sessionId);
    public void RecordSave(string sessionId);
    public void ClearSession(string sessionId);

    // Returns nudge message if no saves for N minutes, null otherwise
    public string? NudgeIfNeeded(string sessionId, TimeSpan threshold);

    // Returns formatted activity score
    public string ActivityScore(string sessionId);
}

public sealed record SessionStats(
    int ToolCalls,
    int Saves,
    DateTime LastSave,
    DateTime LastToolCall
);
```

### Integración en MCP tools
- **Todos los tools**: `activity.RecordToolCall(sessionId)`
- **Write tools**: `activity.RecordSave(sessionId)`
- **mem_session_end**: incluir nudge message si aplica + activity score

### Respuesta de mem_session_end
```json
{
  "status": "session_ended",
  "session_id": "abc123",
  "activity": {
    "tool_calls": 45,
    "saves": 3,
    "save_ratio": "6.7%",
    "nudge": "Low save rate (3 saves in 45 tool calls). Consider saving key decisions with mem_save."
  }
}
```

### Threshold default
- **Nudge**: si `saves == 0` y `tool_calls > 10` y `last_save > 5 minutes ago`
- Configurable via env var: `ENGRAM_ACTIVITY_NUDGE_THRESHOLD=10`

### Criterios de aceptación
- [ ] `RecordToolCall` y `RecordSave` thread-safe
- [ ] Nudge message incluido en mem_session_end cuando aplica
- [ ] Activity score siempre incluido en mem_session_end
- [ ] No crash con session inexistente (create on first call)
- [ ] ClearSession en mem_session_end

---

## Impacto en el código

### Archivos a modificar

| Archivo | Cambio |
|---------|--------|
| `src/Engram.Project/ProjectDetector.cs` | Agregar git_child + ambiguous, `DetectProjectFull`, `DetectionResult` |
| `src/Engram.Store/Models.cs` | Agregar columnas a Observation |
| `src/Engram.Store/SqliteStore.cs` | Migración de columnas nuevas, CREATE TABLE actualizado |
| `src/Engram.Store/PostgresStore.cs` | Migración de columnas nuevas, CREATE TABLE actualizado |
| `src/Engram.Store/IStore.cs` | Sin cambios (columnas son opcionales) |
| `src/Engram.Mcp/WriteQueue.cs` | **Nuevo archivo** — cola serializada |
| `src/Engram.Mcp/SessionActivity.cs` | **Nuevo archivo** — activity tracker |
| `src/Engram.Mcp/EngramTools.cs` | Integrar WriteQueue en write tools, SessionActivity en todos los tools |
| `src/Engram.Mcp/McpServer.cs` | Inicializar WriteQueue y SessionActivity en DI |

### Breaking changes
**Ninguno.** Todos los cambios son aditivos.

---

## Esfuerzo estimado

| Componente | Esfuerzo | Complejidad |
|------------|----------|-------------|
| Project detection 5-case | 1-2h | Baja |
| Schema columns | 1h | Baja |
| Write queue | 2-3h | Media |
| Session activity tracker | 1-2h | Baja |
| **Total** | **5-8h** | |

---

## Tradeoffs

| Opción | Pros | Contras |
|--------|------|---------|
| Channel<T> vs SemaphoreSlim | Channel: backpressure natural, buffer configurable | SemaphoreSlim: más simple pero sin backpressure |
| Activity tracker in-memory vs persisted | In-memory: simple, sin I/O | Se pierde en restart (aceptable — es por sesión) |
| Columnas NULL vs con default | NULL: sin impacto en datos existentes | Requieren validación null-safe en todo el código |

---

## Dependencias con otras fases

- **Fase 2**: Usa `DetectionResult` de esta fase para `mem_current_project`
- **Fase 3**: Usa `DetectionResult` para project envelope
- **Fase 4**: Usa las columnas `review_after` y `expires_at` para decay
