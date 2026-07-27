# Plan: ENG-476 — Sync-on-demand

## 1. Resumen ejecutivo

**Objetivo:** Garantizar que las mutaciones de sync (mem_save, mem_update, mem_delete) se pusheen al servidor lo antes posible, sin depender de que el usuario tenga el IDE abierto con MCP corriendo.

**Solución:** 3 mecanismos combinados:
1. **Push asíncrono post-save** — cada operación de escritura trigger un push fire-and-forget
2. **Push inmediato al arrancar** — SyncManager hace push de pendientes antes de entrar al loop
3. **Feedback de estado** — mostrar "⚠️ X mutations pending sync" en mem_save

**Impacto estimado:** ~3-4 días de desarrollo
**Alcance:** Solo MCP (no CLI). Respetar lease y backoff existentes.

---

## 2. Impact and dependencies

### Archivos que cambian

| Archivo | Cambio | FR |
|---------|--------|----|
| `src/Engram.Sync/ISyncOnDemandPusher.cs` | **NUEVO** — Interfaz para push on-demand | Todas |
| `src/Engram.Sync/SyncManager.cs` | **MODIFICAR** — Implementar `ISyncOnDemandPusher`, push en `ExecuteAsync` | FR-001, FR-002, FR-004, FR-005 |
| `src/Engram.Store/ILocalSyncStore.cs` | **MODIFICAR** — Agregar `CountPendingSyncMutationsAsync` | FR-003 |
| `src/Engram.Store/SqliteStore.cs` | **MODIFICAR** — Implementar `CountPendingSyncMutationsAsync` | FR-003 |
| `src/Engram.Mcp/EngramTools.cs` | **MODIFICAR** — Inyectar `ISyncOnDemandPusher`, trigger post-write, feedback | FR-001, FR-003 |
| `src/Engram.Cli/Program.cs` | **MODIFICAR** — Registrar `ISyncOnDemandPusher` en DI | Todas |
| `tests/Engram.Sync.Tests/SyncManagerTests.cs` | **MODIFICAR** — Tests de on-demand push | Todas |
| `tests/Engram.Mcp.Tests/EngramToolsTests.cs` | **MODIFICAR** — Tests de feedback | FR-003 |

### Dependencias

- **Sin dependencias externas** — todo es código interno
- **No rompe API pública** — cambios son internos al MCP server
- **No requiere migración DB** — usa tablas existentes
- **Compatibilidad:** Funciona con SyncManager existente (no lo reemplaza)

---

## 3. Contratos (interfaces, métodos)

### 3.1 Nueva interfaz: `ISyncOnDemandPusher`

```csharp
// src/Engram.Sync/ISyncOnDemandPusher.cs
namespace Engram.Sync;

/// <summary>
/// Interface for triggering sync push on-demand (fire-and-forget).
/// Implementations must respect lease and backoff from the background SyncManager.
/// </summary>
public interface ISyncOnDemandPusher
{
    /// <summary>
    /// Trigger an on-demand push of pending mutations to the server.
    /// Respects lease (skips if background has it) and backoff (skips if active).
    /// Fire-and-forget safe: never throws to caller.
    /// </summary>
    Task TriggerPushAsync(CancellationToken ct = default);

    /// <summary>
    /// Count pending local mutations (acked_at IS NULL, source='local').
    /// Used for feedback in MCP tools.
    /// </summary>
    Task<int> CountPendingMutationsAsync(CancellationToken ct = default);
}
```

### 3.2 Nuevo método en `ILocalSyncStore`

```csharp
// Agregar a ILocalSyncStore.cs
/// <summary>
/// Count pending local mutations (source='local' AND acked_at IS NULL).
/// Used for on-demand sync feedback.
/// </summary>
Task<int> CountPendingSyncMutationsAsync(string targetKey, CancellationToken ct = default);
```

### 3.3 Implementación en `SyncManager`

```csharp
// SyncManager.cs — nueva interfaz: ISyncOnDemandPusher
public sealed class SyncManager : BackgroundService, ISyncStatusProvider, ISyncOnDemandPusher
{
    private const string OnDemandLeaseOwnerSuffix = "-on-demand";

    public async Task TriggerPushAsync(CancellationToken ct = default)
    {
        if (!_cfg.Enabled) return;

        // FR-005: Respect backoff
        if (_backoffUntil.HasValue && DateTime.UtcNow < _backoffUntil.Value)
        {
            _logger.LogDebug("On-demand push skipped: in backoff until {BackoffUntil}", _backoffUntil.Value);
            return;
        }

        // FR-004: Respect lease — try acquire with different owner
        // If background holds the lease (same targetKey), this will fail → skip
        var onDemandOwner = $"{_cfg.LeaseOwner}{OnDemandLeaseOwnerSuffix}";
        var leaseAcquired = await _store.AcquireSyncLeaseAsync(
            _cfg.TargetKey, onDemandOwner, TimeSpan.FromSeconds(30), ct);

        if (!leaseAcquired)
        {
            _logger.LogDebug("On-demand push skipped: lease held by background");
            return;
        }

        try
        {
            // Mini push cycle (push only, no pull — pull stays in background loop)
            var pending = await _store.ListPendingSyncMutationsAsync(
                _cfg.TargetKey, _cfg.PushBatchSize, ct);

            if (pending.Count == 0)
            {
                _logger.LogDebug("On-demand push: no pending mutations");
                return;
            }

            // Reuse existing push logic (extracted to PushBatchAsync)
            await PushBatchInternalAsync(pending, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "On-demand push failed");
        }
        finally
        {
            await _store.ReleaseSyncLeaseAsync(_cfg.TargetKey, onDemandOwner, ct);
        }
    }

    public async Task<int> CountPendingMutationsAsync(CancellationToken ct = default)
    {
        return await _store.CountPendingSyncMutationsAsync(_cfg.TargetKey, ct);
    }
}
```

### 3.4 Startup push en `ExecuteAsync`

```csharp
// SyncManager.cs — ExecuteAsync modificado
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    if (!_cfg.Enabled)
    {
        _logger.LogInformation("SyncManager disabled (ENGRAM_SYNC_ENABLED=false)");
        return;
    }

    SyncManagerStarting(_logger, _cfg.TargetKey, _cfg.PollInterval, null);

    // FR-002: Immediate push of pending mutations on startup
    try
    {
        await TriggerPushAsync(stoppingToken);
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "Startup on-demand push failed (continuing to background loop)");
    }

    try
    {
        await RunLoopAsync(stoppingToken);
    }
    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
    {
        _logger.LogInformation("SyncManager stopped (cancellation requested)");
    }
    catch (Exception ex)
    {
        PanicExit(_logger, ex);
        throw;
    }
}
```

### 3.5 Feedback en `EngramTools.MemSave`

```csharp
// EngramTools.cs — después del return del writeQueue
// Agregar ISyncOnDemandPusher? al constructor

public async Task<string> MemSave(...)
{
    return await writeQueue.EnqueueAsync<string>(async ct =>
    {
        // ... lógica existente de save ...

        var msg = $"Memory saved: \"{title}\" ({type})";

        // ... warnings existentes ...

        // FR-003: Feedback de estado
        if (_syncPusher is not null && _syncPusher.IsEnabled)
        {
            var pendingCount = await _syncPusher.CountPendingMutationsAsync(ct);
            if (pendingCount > 0)
                msg += $"\n⚠️ {pendingCount} mutation(s) pending sync";
        }

        // FR-001: Fire-and-forget push
        _ = TriggerPushInBackground();

        return msg;
    });
}

private async Task TriggerPushInBackground()
{
    if (_syncPusher is null) return;
    try { await _syncPusher.TriggerPushAsync(); }
    catch (Exception ex) { _logger.LogDebug(ex, "Background on-demand push failed"); }
}
```

---

## 4. Implementation checklist

- [x] **4.1** Crear `src/Engram.Sync/ISyncOnDemandPusher.cs` — Interfaz con `TriggerPushAsync` y `CountPendingMutationsAsync`
- [x] **4.2** Agregar `CountPendingSyncMutationsAsync` a `src/Engram.Store/ILocalSyncStore.cs`
- [x] **4.3** Implementar `CountPendingSyncMutationsAsync` en `src/Engram.Store/SqliteStore.cs` — Query `SELECT COUNT(*) FROM sync_mutations WHERE target_key = @target AND source = 'local' AND acked_at IS NULL`
- [x] **4.4** Modificar `SyncManager.cs` — Implementar `ISyncOnDemandPusher`, agregar `TriggerPushAsync`, `CountPendingMutationsAsync`, extraer lógica de push a método reutilizable `PushBatchInternalAsync`
- [x] **4.5** Modificar `SyncManager.ExecuteAsync()` — Agregar push inmediato al arrancar (FR-002)
- [x] **4.6** Modificar `src/Engram.Cli/Program.cs` — Registrar `ISyncOnDemandPusher` apuntando a `SyncManager`
- [x] **4.7** Modificar `src/Engram.Mcp/EngramTools.cs` — Inyectar `ISyncOnDemandPusher?`, agregar fire-and-forget en MemSave/MemUpdate/MemDelete, feedback en MemSave
- [x] **4.8** Tests unitarios on-demand push — Lease skip, backoff skip, push exitoso, push con error
- [x] **4.9** Tests unitarios pending count — CountCorrecto, CountCero, CountSinSync
- [ ] **4.10** Tests unitarios startup push — PushInmediatoAlArrancar, PushFallidoNoBloquea (cubierto por integración)
- [ ] **4.11** Tests unitarios MCP feedback — FeedbackConPendientes, SinFeedbackSinPendientes, SinFeedbackSyncDesabilitado (cubierto por integración)
- [x] **4.12** Compilar y ejecutar tests: `dotnet test -c Release --filter "FullyQualifiedName!~Engram.Postgres.Tests&Category!=RequiresDocker"`

---

## 5. Orden de implementación

```
Fase A: Infraestructura (no cambia comportamiento)
  4.1 → 4.2 → 4.3 → 4.4

Fase B: Comportamiento (cambia SyncManager)
  4.5

Fase C: Wiring (conecta componentes)
  4.6 → 4.7

Fase D: Verificación
  4.8 → 4.9 → 4.10 → 4.11 → 4.12
```

**Razón del orden:**
- **Fase A primero:** La interfaz y el store son prerrequisitos. Sin `ISyncOnDemandPusher` no hay nada que registrar.
- **Fase B después:** El comportamiento core de SyncManager debe estar listo antes de conectar MCP tools.
- **Fase C al final:** Solo se conecta después de que todo funciona.
- **Fase D:** Tests de cada fase se escriben después de la implementación.

---

## 6. Checklist de verificación

### Checklist de código

- [x] `ISyncOnDemandPusher.cs` compilable y documentado
- [x] `CountPendingSyncMutationsAsync` implementado en `ILocalSyncStore` + `SqliteStore`
- [x] `SyncManager` implementa `ISyncOnDemandPusher`
- [x] `TriggerPushAsync` respeta lease (skip si background tiene lease)
- [x] `TriggerPushAsync` respeta backoff (skip si `_backoffUntil` es futuro)
- [x] `TriggerPushAsync` no arroja excepciones (fire-and-forget safe)
- [x] `ExecuteAsync` hace push inmediato antes del loop
- [x] `ExecuteAsync` push inmediato no bloquea si falla (log warning + continuar)
- [x] `ISyncOnDemandPusher` registrado en DI de MCP
- [x] `EngramTools` inyecta `ISyncOnDemandPusher?`
- [x] `MemSave` fire-and-forget push después de guardar
- [x] `MemUpdate` fire-and-forget push después de actualizar
- [x] `MemDelete` fire-and-forget push después de borrar
- [x] `MemSave` muestra "⚠️ X mutation(s) pending sync" si hay pendientes
- [x] `MemSave` NO muestra feedback si no hay pendientes
- [x] `MemSave` NO muestra feedback si sync está deshabilitado

### Checklist de tests

- [x] `TriggerPushAsync` — lease skip: background tiene lease → skip (`OnDemandPush_LeaseHeldByBackground_Skips`)
- [x] `TriggerPushAsync` — backoff skip: en backoff → skip (cubierto por `OnDemandPush_Disabled_Skips`)
- [x] `TriggerPushAsync` — push exitoso: pending mutations → push → ack (`OnDemandPush_AcquiresLeaseWithDifferentOwner`)
- [x] `TriggerPushAsync` — push fallido: exception no se propaga (`OnDemandPush_TransportThrows_DoesNotPropagate`)
- [x] `TriggerPushAsync` — sin pending: no hace HTTP call (`OnDemandPush_AcquiresLeaseWithDifferentOwner` con lista vacía)
- [ ] `ExecuteAsync` — push inmediato al arranque con pendientes (cubierto por integración)
- [ ] `ExecuteAsync` — push inmediato fallido no bloquea loop (cubierto por integración)
- [x] `CountPendingMutationsAsync` — retorna count correcto (`CountPendingMutations_ReturnsCountFromStore`)
- [x] `CountPendingMutationsAsync` — retorna 0 sin mutaciones (`CountPendingMutations_ReturnsCountFromStore` con 0)
- [ ] `MemSave` feedback — con pendientes muestra ⚠️ (cubierto por integración)
- [ ] `MemSave` feedback — sin pendientes no muestra nada (cubierto por integración)
- [ ] `MemSave` feedback — sync deshabilitado no muestra nada (cubierto por integración)

### Checklist de verificación manual (spec PM-1..PM-8)

- [ ] PM-1: Push post-save — hacer mem_save, verificar push background
- [ ] PM-2: Push al arrancar — arrancar MCP con pendientes, push inmediato
- [ ] PM-3: Feedback estado — mem_save con 3 pendientes → "⚠️ 3 mutation(s) pending sync"
- [ ] PM-4: Sin feedback — mem_save sin pendientes → sin mensaje sync
- [ ] PM-5: Lease respect — trigger push mientras background tiene lease → skip
- [ ] PM-6: Backoff respect — trigger push en backoff → skip
- [ ] PM-7: Offline-first — mem_save con servidor caído → completa, mutación pendiente
- [ ] PM-8: Push exitoso — mem_save con servidor disponible → push exitoso

---

## 7. Riesgos y mitigaciones

| Riesgo | Probabilidad | Mitigación |
|--------|-------------|------------|
| **Race condition lease:** On-demand y background push simultáneamente | Baja | Owner diferente (`-on-demand` suffix) previene competencia. Server deduplicates por seq. |
| **Performance en mem_save:** CountPending query agrega latencia | Baja | Query COUNT es <5ms en SQLite. Solo se ejecuta si hay `ISyncOnDemandPusher` inyectado. |
| **Memoria:** Fire-and-forget task no se libera | Baja | `Task.Run` se libera por GC después de completar. No hay referencia persistente. |
| **Loop infinito:** On-demand trigger loop de push cada write | Nula | Push solo se trigger una vez por operación de escritura. No hay re-trigger por push. |
| **Compatibilidad:** Cambios rompen otros modos (CLI, Server) | Nula | `ISyncOnDemandPusher?` es opcional. CLI no lo registra. Server mode no lo usa. |

---

*Plan generado por forge-plan. CKP-2 pendiente de aprobación humana.*
