# Plan — ENG-459: Sync Failure Feedback

> **Feature**: Sync failure feedback — notificación visible cuando sync falla repetidamente
> **Spec**: `.ai-work/eng-459-sync-failure-feedback/spec.md`
> **Estado**: Phase 2 (forge-plan — CKP-2)
> **Esfuerzo total estimado**: M (4-6h)
> **Tareas**: 11

---

## Dependencias entre tareas

```
TASK-001 (interface + config)
  ├─→ TASK-002 (notification file writer)
  ├─→ TASK-003 (DTO + endpoint suggested_action)
  │     └─→ TASK-004 (CLI output)
  ├─→ TASK-005 (DiagnosticService sync_health)
  ├─→ TASK-006 (EngramTools init warning)
  └─→ TASK-007 (tests: SyncManager)
       └─→ TASK-011 (integration test)

TASK-003 → TASK-008 (tests: endpoint)
TASK-004 → TASK-009 (tests: CLI)
TASK-005 → TASK-010 (tests: DiagnosticService)
```

---

## TASK-001: Interface `ISyncStatusProvider.LastError` + Config fields

**Esfuerzo**: S (30 min)
**Depende de**: — (fundación)

### Archivos a modificar

| Archivo | Cambio |
|---------|--------|
| `src/Engram.Sync/ISyncStatusProvider.cs` | Añadir `string? LastError { get; }` |
| `src/Engram.Sync/SyncManager.cs` | Implementar `LastError` → campo privado `_lastError` + exponer como propiedad |
| `src/Engram.Sync/SyncManagerConfig.cs` | Añadir `NotificationThreshold` (default 3), `NotificationFileMaxEntries` (default 10) + env vars `ENGRAM_SYNC_NOTIFICATION_THRESHOLD`, `ENGRAM_SYNC_NOTIFICATION_MAX` |

### Detalle de implementación

**ISyncStatusProvider.cs** (L1-10):
```csharp
public interface ISyncStatusProvider
{
    SyncPhase Phase { get; }
    bool IsEnabled { get; }
    int ConsecutiveFailures { get; }
    DateTime? BackoffUntil { get; }
    SyncMetrics Metrics { get; }
    string? LastError { get; }  // ← NUEVO
}
```

**SyncManager.cs** — añadir campo y propiedad:
- Campo: `private string? _lastError;`
- Propiedad: `public string? LastError => _lastError;`
- En catch block de `CycleAsync()` (L206-215): `_lastError = ex.Message;`
- En `SetPhase(SyncPhase.Healthy)` (L198-201): `_lastError = null;`
- En `MarkSyncBlockedAsync` calls (L232, L248, L258): `_lastError = error;` (donde `error` es el string del bloqueo)

**SyncManagerConfig.cs** — añadir tras L28:
```csharp
/// <summary>Consecutive failures before writing notification file (default: 3).</summary>
public int NotificationThreshold { get; init; } = 3;

/// <summary>Max entries in notification file (default: 10).</summary>
public int NotificationFileMaxEntries { get; init; } = 10;
```

En `FromEnvironment()`:
```csharp
NotificationThreshold = ParseInt(Environment.GetEnvironmentVariable("ENGRAM_SYNC_NOTIFICATION_THRESHOLD"), 3),
NotificationFileMaxEntries = ParseInt(Environment.GetEnvironmentVariable("ENGRAM_SYNC_NOTIFICATION_MAX"), 10),
```

### Tests requeridos

Ninguno nuevo en esta tarea (tests de la integración se escriben en TASK-007).

### Criterios de aceptación

- [ ] `ISyncStatusProvider` compila con `string? LastError`
- [ ] `SyncManager` implementa `LastError` y lo actualiza en catch/healthy/blocked
- [ ] `SyncManagerConfig` tiene `NotificationThreshold=3` y `NotificationFileMaxEntries=10` por defecto
- [ ] Env vars `ENGRAM_SYNC_NOTIFICATION_THRESHOLD` y `ENGRAM_SYNC_NOTIFICATION_MAX` funcionan
- [ ] `dotnet build` pasa sin errores

---

## TASK-002: Notification file writer en SyncManager

**Esfuerzo**: M (1.5h)
**Depende de**: TASK-001

### Archivos a modificar

| Archivo | Cambio |
|---------|--------|
| `src/Engram.Sync/SyncManager.cs` | Añadir `WriteNotificationAsync()`, inyectar en catch block y en recovery |
| `src/Engram.Sync/SyncManager.cs` | Añadir `_notificationWritten` flag para evitar re-escritura por ciclo |

### Detalle de implementación

**Ruta del archivo de notificaciones**:
```csharp
private string GetNotificationFilePath()
{
    // SyncDir = ~/.engram/sync, parent = ~/.engram/
    var syncDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    return Path.Combine(syncDir, ".engram", "sync-notifications.log");
}
```

**Método `WriteNotificationAsync`**:
```csharp
private async Task WriteNotificationAsync(string level, int failures, string? error, string? action, string? message)
{
    try
    {
        var entry = new Dictionary<string, object?>
        {
            ["ts"] = DateTime.UtcNow.ToString("O"),
            ["level"] = level,
            ["failures"] = failures,
            ["error"] = error,
            ["action"] = action,
            ["phase"] = _phase.ToString().ToLowerInvariant(),
            ["message"] = message
        };
        // Limpiar nulls del JSON
        var json = JsonSerializer.Serialize(entry, new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });
        
        var filePath = GetNotificationFilePath();
        var dir = Path.GetDirectoryName(filePath)!;
        Directory.CreateDirectory(dir);
        
        // Leer líneas existentes, añadir nueva, truncar a MaxEntries
        var lines = new List<string>();
        if (File.Exists(filePath))
        {
            lines = (await File.ReadAllLinesAsync(filePath)).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        }
        lines.Add(json);
        if (lines.Count > _cfg.NotificationFileMaxEntries)
            lines = lines.Skip(lines.Count - _cfg.NotificationFileMaxEntries).ToList();
        
        await File.WriteAllTextAsync(filePath, string.Join("\n", lines) + "\n");
    }
    catch (Exception ex)
    {
        // Best-effort: no propagar excepción
        _logger.LogWarning(ex, "Failed to write sync notification file");
    }
}
```

**Inyección en catch block** (después de L215):
```csharp
// Después de CycleFailed(...)
if (_consecutiveFailures >= _cfg.NotificationThreshold)
{
    await WriteNotificationAsync("error", _consecutiveFailures, ex.Message, null, null);
}
```

**Inyección en transition a Disabled** (después de L129-131):
```csharp
if (_consecutiveFailures >= _cfg.MaxConsecutiveFailures)
{
    SetPhase(SyncPhase.Disabled);
    await WriteNotificationAsync("error", _consecutiveFailures, "Sync disabled after max failures", null, null);
    CycleFailed(_logger, _consecutiveFailures, _cfg.MaxConsecutiveFailures, null);
    return;
}
```

**Inyección en recovery** (después de L201, donde `_consecutiveFailures` se resetea):
```csharp
if (previousFailures > 0)
{
    await WriteNotificationAsync("ok", 0, null, null, $"Sync recovered after {previousFailures} failures");
}
```
Donde `previousFailures` es `_consecutiveFailures` capturado antes del reset.

**Inyección en blocked** (en `PushAsync`, después de cada `MarkSyncBlockedAsync`):
- L232: `await WriteNotificationAsync("error", _consecutiveFailures, $"{nonEnrolled.Count} projects not enrolled", "Enroll projects: POST /sync/enroll", null);`
- L248: `await WriteNotificationAsync("error", _consecutiveFailures, result.PauseError, "Resume sync: DELETE /sync/pause?project=...", null);`
- L258: `await WriteNotificationAsync("error", _consecutiveFailures, ex.Message, "Resume sync: DELETE /sync/pause?project=...", null);`

**Field `_notificationWritten`** (para no re-escribir en cada ciclo de backoff):
```csharp
private bool _notificationWrittenForCurrentFailures;
```
Se resetea a `false` cuando `_consecutiveFailures` se resetea (recovery) o cuando el threshold se incrementa.

### Tests requeridos

Tests unitarios se escriben en TASK-007.

### Criterios de aceptación

- [ ] Cuando `ConsecutiveFailures >= 3`, se escribe línea JSON en `~/.engram/sync-notifications.log`
- [ ] La línea tiene campos `ts`, `level`, `failures`, `error`, `action`, `phase`
- [ ] El archivo tiene máximo 10 entradas (rotación circular)
- [ ] Cuando sync recupera (phase → Healthy), se escribe línea `level: "ok"`
- [ ] Si la escritura falla, no se propaga excepción al SyncManager
- [ ] `dotnet build` pasa sin errores

---

## TASK-003: `SuggestedAction` en DTO y generación en endpoint

**Esfuerzo**: M (1h)
**Depende de**: TASK-001

### Archivos a modificar

| Archivo | Cambio |
|---------|--------|
| `src/Engram.Server/Dtos/MutationDtos.cs` | Añadir `SuggestedAction` a `StatusHealthBody` |
| `src/Engram.Server/CloudSyncEndpoints.cs` | Generar `suggested_action` en `HandleSyncStatusAsync` |

### Detalle de implementación

**MutationDtos.cs** — `StatusHealthBody` (L169-174):
```csharp
public sealed record StatusHealthBody(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("consecutive_failures")] int ConsecutiveFailures,
    [property: JsonPropertyName("backoff_until")] string? BackoffUntil,
    [property: JsonPropertyName("last_error")] string? LastError,
    [property: JsonPropertyName("last_sync_at")] string? LastSyncAt,
    [property: JsonPropertyName("suggested_action")] string? SuggestedAction);  // ← NUEVO
```

**CloudSyncEndpoints.cs** — en `HandleSyncStatusAsync`, después de construir `StatusHealthBody` (L438-453):
```csharp
SuggestedAction: GenerateSuggestedAction(
    healthStatus,
    provider,
    state,
    enrolled,
    ctx.Request)
```

**Nuevo método privado `GenerateSuggestedAction`**:
```csharp
private static string? GenerateSuggestedAction(string healthStatus, ISyncStatusProvider? provider, SyncState? state, List<EnrolledProject> enrolled, HttpRequest request)
{
    var consecutiveFailures = state?.ConsecutiveFailures ?? provider?.ConsecutiveFailures ?? 0;
    var lastError = state?.LastError ?? provider?.LastError;
    var phase = provider?.Phase ?? SyncPhase.Idle;
    
    // Construir server URL desde la request
    var serverUrl = $"{request.Scheme}://{request.Host}";
    
    return healthStatus switch
    {
        "healthy" => null,
        "degraded" => "Sync degraded. Check server connectivity and run 'engram sync status' for details.",
        "disabled" => $"Sync disabled after {consecutiveFailures} failures. Restart engram server or check ENGRAM_SERVER_URL configuration.",
        "blocked" when lastError?.StartsWith("non-enrolled-pending") == true =>
            $"Sync blocked: projects not enrolled. Enroll with: curl -X POST {serverUrl}/sync/enroll -H 'Content-Type: application/json' -d '{{\"project\": \"<name>\"}}'",
        "blocked" when lastError?.Contains("sync-paused") == true =>
            $"Sync paused. Resume with: curl -X DELETE {serverUrl}/sync/pause?project=<project>",
        "blocked" => $"Sync blocked. Last error: {lastError}. Run 'engram sync status' for details.",
        _ => null
    };
}
```

### Tests requeridos

Tests unitarios se escriben en TASK-008.

### Criterios de aceptación

- [ ] `GET /sync/status` devuelve campo `suggested_action` en `health`
- [ ] `suggested_action` es `null` cuando `status == "healthy"`
- [ ] `suggested_action` contiene instrucciones relevantes para `degraded`, `disabled`, `blocked`
- [ ] Para `blocked` con `non-enrolled-pending`, `suggested_action` incluye el comando `curl` con `server_url`
- [ ] Para `blocked` con `sync-paused`, `suggested_action` incluye el comando `curl` de resume
- [ ] El campo es backward-compatible (nullable)
- [ ] `dotnet build` pasa sin errores

---

## TASK-004: CLI `engram sync status` output mejorado

**Esfuerzo**: S (30 min)
**Depende de**: TASK-003

### Archivos a modificar

| Archivo | Cambio |
|---------|--------|
| `src/Engram.Cli/Program.cs` | Leer `suggested_action` del JSON y mostrarlo en output |

### Detalle de implementación

**Program.cs** — en el handler de `engram sync status` (después de L396):
```csharp
// Leer suggested_action
if (health.TryGetProperty("suggested_action", out var suggestedAction) 
    && suggestedAction.ValueKind != JsonValueKind.Null)
{
    var actionText = suggestedAction.GetString();
    if (!string.IsNullOrEmpty(actionText))
    {
        var healthStatus = health.GetProperty("status").GetString();
        if (healthStatus is "disabled" or "blocked")
        {
            Console.WriteLine();
            Console.WriteLine($"  ⚠️  WARNING: Sync {healthStatus} — data is NOT being synchronized!");
            var pending = counts.GetProperty("pending_push").GetInt32();
            if (pending > 0)
                Console.WriteLine($"  Pending mutations: {pending}");
        }
        Console.WriteLine();
        Console.WriteLine($"  💡 Suggested action:");
        Console.WriteLine($"     {actionText}");
    }
}
```

### Tests requeridos

Tests unitarios se escriben en TASK-009.

### Criterios de aceptación

- [ ] Cuando `suggested_action` no es null, el CLI muestra "💡 Suggested action:" seguido del contenido
- [ ] Para estados `disabled` o `blocked`, se muestra "⚠️ WARNING: Sync {status} — data is NOT being synchronized!"
- [ ] Para estado `healthy`, no se muestra nada extra
- [ ] El flag `--json` sigue funcionando (output JSON crudo sin formateo)
- [ ] `dotnet build` pasa sin errores

---

## TASK-005: `DiagnosticService` — sync health check

**Esfuerzo**: M (1h)
**Depende de**: TASK-001

### Archivos a modificar

| Archivo | Cambio |
|---------|--------|
| `src/Engram.Diagnostics/DiagnosticService.cs` | Añadir `ISyncStatusProvider?`, `CheckSyncHealthAsync()`, componente `sync_health` |
| `src/Engram.Diagnostics/DiagnosticService.cs` | Actualizar constructor y DI wiring |

### Detalle de implementación

**Constructor** (L27-32):
```csharp
public DiagnosticService(IStore store, HttpClient? httpClient = null, string? serverUrl = null, ISyncStatusProvider? syncStatusProvider = null)
{
    _store = store ?? throw new ArgumentNullException(nameof(store));
    _httpClient = httpClient ?? new HttpClient();
    _serverUrl = serverUrl;
    _syncStatusProvider = syncStatusProvider;
}

private readonly ISyncStatusProvider? _syncStatusProvider;
```

**Campo**: `private readonly ISyncStatusProvider? _syncStatusProvider;`

**Nuevo método `CheckSyncHealthAsync`**:
```csharp
private Task<ComponentHealth> CheckSyncHealthAsync(CancellationToken cancellationToken)
{
    var stopwatch = Stopwatch.StartNew();
    
    if (_syncStatusProvider is null)
    {
        stopwatch.Stop();
        return Task.FromResult(new ComponentHealth
        {
            IsHealthy = true,
            Message = "Sync not applicable (cloud relay mode)",
            LatencyMs = stopwatch.ElapsedMilliseconds
        });
    }
    
    var phase = _syncStatusProvider.Phase;
    var failures = _syncStatusProvider.ConsecutiveFailures;
    var lastError = _syncStatusProvider.LastError;
    
    stopwatch.Stop();
    
    return Task.FromResult(phase switch
    {
        SyncPhase.Healthy or SyncPhase.Idle => new ComponentHealth
        {
            IsHealthy = true,
            Message = $"Sync healthy ({failures} mutations pending)",
            LatencyMs = stopwatch.ElapsedMilliseconds
        },
        SyncPhase.Disabled => new ComponentHealth
        {
            IsHealthy = false,
            Message = $"Sync disabled: {failures} failures reached ceiling. Last error: {lastError}",
            LatencyMs = stopwatch.ElapsedMilliseconds
        },
        _ => new ComponentHealth
        {
            IsHealthy = false,
            Message = $"Sync degraded: {failures} consecutive failures. Last error: {lastError}",
            LatencyMs = stopwatch.ElapsedMilliseconds
        }
    });
}
```

**En `RunDiagnosticsAsync`** — añadir check y resultado:
```csharp
var syncCheck = CheckSyncHealthAsync(cancellationToken);
// ... en Task.WhenAll o secuencial
result.Components["sync_health"] = await syncCheck;
```

### DI Wiring

En `Program.cs` del server (registro de `DiagnosticService`), pasar `ISyncStatusProvider?` si existe.

### Tests requeridos

Tests unitarios se escriben en TASK-010.

### Criterios de aceptación

- [ ] `mem_doctor` incluye componente `"sync_health"` en su resultado
- [ ] Cuando `ISyncStatusProvider` es null, `sync_health` es `healthy` con mensaje informativo
- [ ] Cuando sync está `disabled`, `sync_health.IsHealthy = false`
- [ ] Cuando sync está `degraded` (failures ≥ 3), `sync_health.IsHealthy = false`
- [ ] El check completa en < 10ms (lectura de propiedades en memoria)
- [ ] `dotnet build` pasa sin errores

---

## TASK-006: `EngramTools` — MCP stderr warning on init

**Esfuerzo**: S (30 min)
**Depende de**: TASK-001

### Archivos a modificar

| Archivo | Cambio |
|---------|--------|
| `src/Engram.Mcp/EngramTools.cs` | Añadir `ISyncStatusProvider?` al primary constructor, log warning en init |

### Detalle de implementación

**Primary constructor** (L52) — añadir parámetro:
```csharp
public sealed class EngramTools(
    IStore store, McpConfig cfg, WriteQueue writeQueue, SessionActivity activity,
    IVerifier verifier, CycleTracker cycleTracker, PromotionService promotionService,
    Verification.TraceRepository traceRepo, Verification.LineageBuilder lineageBuilder,
    IDiagnosticService diagnosticService, Verification.MemoryRelationRepository memRelRepo,
    Verification.MemoryLineageBuilder memLineageBuilder,
    ISyncStatusProvider? syncStatusProvider = null)  // ← NUEVO (nullable)
```

**Campo privado** (tras L60):
```csharp
private readonly ISyncStatusProvider? _syncStatusProvider = syncStatusProvider;
```

**Init block** (o en constructor body si se extrae a método):
```csharp
// Sync health warning on MCP server init
if (_syncStatusProvider is { } provider && 
    (provider.Phase == SyncPhase.Disabled || provider.ConsecutiveFailures >= 3))
{
    Console.Error.WriteLine(
        $"⚠️ Sync is {provider.Phase}: {provider.ConsecutiveFailures} consecutive failures. " +
        $"Last error: {provider.LastError}. Run 'engram sync status' for details.");
}
```

**Nota**: En C# primary constructors, el body va en un `partial class` o se usa `init` block. Revisar cómo `EngramTools` maneja esto actualmente — si tiene campos declarados (L54-60), se puede añadir el warning como `static` init o en un método que se llama una vez.

**Approach más simple**: Añadir el warning en la instancia estática del constructor via el campo `_syncStatusProvider` y un setter que se llama al construir:
```csharp
// En el body del constructor (o campo con initializer que ejecuta side-effect)
{
    if (_syncStatusProvider is { } sp &&
        (sp.Phase == SyncPhase.Disabled || sp.ConsecutiveFailures >= 3))
    {
        Console.Error.WriteLine(
            $"⚠️ Sync is {sp.Phase}: {sp.ConsecutiveFailures} consecutive failures. " +
            $"Last error: {sp.LastError}. Run 'engram sync status' for details.");
    }
}
```

**DI Wiring**: En el registro de `EngramTools` en DI, pasar `ISyncStatusProvider?` si existe (resolución condicional).

### Tests requeridos

Tests unitarios se escriben en TASK-010 (extender `DiagnosticServiceTests` o crear test específico).

### Criterios de aceptación

- [ ] Al construir `EngramTools`, si `ISyncStatusProvider?.Phase == Disabled` o `ConsecutiveFailures >= 3`, se emite `LogWarning` a stderr
- [ ] El warning incluye phase, failure count, y last error
- [ ] Si `ISyncStatusProvider` es null, no se emite warning
- [ ] `dotnet build` pasa sin errores

---

## TASK-007: Tests unitarios — SyncManager (notification file + LastError)

**Esfuerzo**: M (1h)
**Depende de**: TASK-002

### Archivos a modificar

| Archivo | Cambio |
|---------|--------|
| `tests/Engram.Sync.Tests/SyncManagerTests.cs` | Añadir tests de notification file y LastError |

### Tests a escribir

| Test | Qué verifica |
|------|-------------|
| `NotificationFile_WrittenOnThreshold` | Mock `ILocalSyncStore` + filesystem temp; simular 3 fallos → verificar que archivo se escribe |
| `NotificationFile_NotWrittenBelowThreshold` | Simular 2 fallos → verificar que NO se escribe |
| `NotificationFile_RotationMaxEntries` | Escribir 15 notificaciones; verificar que solo quedan 10 |
| `NotificationFile_RecoverEntry` | Simular 5 fallos + 1 éxito → verificar línea `level: "ok"` |
| `NotificationFile_WrittenOnDisabled` | Simular MaxConsecutiveFailures fallos → verificar notificación de disabled |
| `NotificationFile_WriteFailureDoesNotThrow` | Mock filesystem que falla → verificar que SyncManager no lanza excepción |
| `LastError_ExposedOnFailure` | Simular fallo → verificar que `LastError` contiene el mensaje de error |
| `LastError_ClearedOnRecovery` | Simular fallo + éxito → verificar que `LastError` es null |
| `LastError_SetOnBlocked` | Mock non-enrolled → verificar que `LastError` contiene "non-enrolled-pending" |

### Estrategia de test

Usar directorio temporal para notification file (no `~/.engram/` real):
```csharp
var tempDir = Path.Combine(Path.GetTempPath(), $"engram-test-{Guid.NewGuid():N}");
Directory.CreateDirectory(tempDir);
// Override _cfg.NotificationFile o usar reflection para setear la ruta
```

Alternativamente, extraer la ruta a un método virtual o usar una interfaz `INotificationWriter` para testabilidad (pero esto es más invasive — evaluar si el esfuerzo lo justifica).

**Approach recomendado**: Usar `SyncManagerConfig` con una propiedad `NotificationFilePath` (o resolver en método privado). Para tests, usar un directorio temporal y limpiar después.

### Criterios de aceptación

- [ ] Todos los tests pasan con `dotnet test`
- [ ] No hay tests pendientes o skipped
- [ ] Los tests cubren los 9 escenarios listados arriba

---

## TASK-008: Tests unitarios — `/sync/status` suggested_action

**Esfuerzo**: M (1h)
**Depende de**: TASK-003

### Archivos a modificar

| Archivo | Cambio |
|---------|--------|
| `tests/Engram.Server.Tests/SyncStatusEndpointTests.cs` | Añadir tests de suggested_action |

### Tests a escribir

| Test | Qué verifica |
|------|-------------|
| `SuggestedAction_NullWhenHealthy` | Mock provider con `Phase=Healthy` → `suggested_action` es null |
| `SuggestedAction_Degraded` | Mock provider con `Phase=Backoff`, failures=5 → `suggested_action` contiene "degraded" |
| `SuggestedAction_Disabled` | Mock provider con `Phase=Disabled`, failures=10 → `suggested_action` contiene "disabled" y "Restart" |
| `SuggestedAction_BlockedNonEnrolled` | Mock `lastError` con "non-enrolled-pending" → `suggested_action` contiene "Enroll" y `curl` |
| `SuggestedAction_BlockedPaused` | Mock `lastError` con "sync-paused" → `suggested_action` contiene "Resume" y `curl` |
| `SuggestedAction_BackwardCompatible` | Verificar que el campo es nullable y que un cliente que no lo lee no falla |

### Estrategia de test

Usar el `WebApplication` test harness existente en `SyncStatusEndpointTests`. Configurar `_providerMock` con los valores deseados para cada test.

### Criterios de aceptación

- [ ] Todos los tests pasan con `dotnet test`
- [ ] No hay tests pendientes o skipped
- [ ] Los tests cubren los 6 escenarios listados arriba

---

## TASK-009: Tests unitarios — CLI output

**Esfuerzo**: S (30 min)
**Depende de**: TASK-004

### Archivos a modificar

| Archivo | Cambio |
|---------|--------|
| `tests/Engram.Cli.Tests/SyncStatusCliTests.cs` | Añadir tests de output con suggested_action |

### Tests a escribir

| Test | Qué verifica |
|------|-------------|
| `CliOutput_ShowsSuggestedAction` | Simular respuesta HTTP con `suggested_action` → verificar output contiene "💡 Suggested action:" |
| `CliOutput_WarningOnBlocked` | Simular blocked → verificar output contiene "⚠️ WARNING" |
| `CliOutput_NoExtraOnHealthy` | Simular healthy (sin `suggested_action`) → verificar output NO contiene "💡" ni "⚠️" |
| `CliOutput_JsonFlagUnchanged` | Verificar que `--json` sigue devolviendo JSON crudo sin formateo |

### Criterios de aceptación

- [ ] Todos los tests pasan con `dotnet test`
- [ ] No hay tests pendientes o skipped
- [ ] Los tests cubren los 4 escenarios listados arriba

---

## TASK-010: Tests unitarios — DiagnosticService sync_health + EngramTools warning

**Esfuerzo**: S (30 min)
**Depende de**: TASK-005, TASK-006

### Archivos a modificar

| Archivo | Cambio |
|---------|--------|
| `tests/Engram.Diagnostics.Tests/DiagnosticServiceTests.cs` | Añadir tests de sync_health component |

### Tests a escribir

| Test | Qué verifica |
|------|-------------|
| `MemDoctor_SyncHealthComponent` | Mock `ISyncStatusProvider` con `Phase=Backoff`, failures=5 → componente `sync_health` existe y `IsHealthy=false` |
| `MemDoctor_SyncHealthyNullProvider` | Pass `null` como `ISyncStatusProvider` → `sync_health` es `healthy` con mensaje "cloud relay" |
| `MemDoctor_SyncDisabled` | Mock `Phase=Disabled` → `sync_health.IsHealthy = false` y mensaje contiene "disabled" |
| `MemDoctor_SyncHealthy` | Mock `Phase=Healthy` → `sync_health.IsHealthy = true` |
| `EngramTools_WarningOnInit` | Construir `EngramTools` con provider bloqueado → capturar stderr y verificar "⚠️" |

### Criterios de aceptación

- [ ] Todos los tests pasan con `dotnet test`
- [ ] No hay tests pendientes o skipped
- [ ] Los tests cubren los 5 escenarios listados arriba

---

## TASK-011: Integration test — full failure/recovery cycle

**Esfuerzo**: M (1h)
**Depende de**: TASK-007

### Archivos a modificar

| Archivo | Cambio |
|---------|--------|
| `tests/Engram.Sync.Tests/SyncManagerTests.cs` | Añadir test de integración |

### Test a escribir

| Test | Qué verifica |
|------|-------------|
| `FullCycle_FailureThenRecover` | Ciclo real: 3 fallos → notificación escrita → 1 éxito → recover notificación en archivo |
| `NotificationFile_PersistsAcrossRestarts` | Escribir notificación, simular restart (nueva instancia), verificar que archivo persiste |

### Estrategia de test

Usar directorio temporal para notification file. Simular múltiples ciclos de `CycleAsync` via reflexión (método existente `InvokeCycleAsync`). Verificar contenido del archivo después de cada ciclo.

### Criterios de aceptación

- [ ] Tests de integración pasan con `dotnet test`
- [ ] No hay tests pendientes o skipped
- [ ] El test verifica el ciclo completo: fallo → notificación → recover → notificación de recover

---

## Verification Checklist

Después de completar todas las tareas:

- [ ] `dotnet build` pasa sin errores en toda la solución
- [ ] `dotnet test` pasa sin fallos (todos los tests existentes + nuevos)
- [ ] `dotnet test --filter "FullyQualifiedName!~Engram.Postgres.Tests&Category!=RequiresDocker"` pasa (T2)
- [ ] Notificación file se escribe en `~/.engram/sync-notifications.log` al superar umbral
- [ ] `GET /sync/status` devuelve `suggested_action` para cada estado
- [ ] `engram sync status` muestra suggested action cuando status ≠ healthy
- [ ] `mem_doctor` incluye componente `sync_health`
- [ ] MCP server emite warning a stderr cuando sync está bloqueado
- [ ] No hay regressions en tests existentes

---

*Generado por forge-plan (Phase 2) para FlowForge. Input para forge-dev (Phase 3).*
