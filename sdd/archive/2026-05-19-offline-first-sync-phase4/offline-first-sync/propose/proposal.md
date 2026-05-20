# Proposal: Phase 4 — Observability for Offline-First Sync

## Intent

Agregar observabilidad al sistema de sync mutation-based (Phases 1-3): endpoint `/sync/status` con health + métricas, tracking de metrics en SyncManager vía `ILogger`, comando CLI `engram sync status` que consulte el endpoint, y documentación de setup.

## Scope

### In scope
1. **Endpoint `/sync/status`** — cursor position, last sync time, health status, push/pull counts, enrolled projects, pause state
2. **SyncManager metrics via `ILogger`** — phase transitions, error counts, sync cycle counts (push/pull), deferred replay stats
3. **CLI `engram sync status`** — consume endpoint y mostrar output formateado (y `--json`)
4. **Documentación de setup** — `docs/SYNC-SETUP.md` con vars de entorno, prerequisitos, PostgreSQL en TrueNAS, enrollment

### Out scope
- Dashboard UI / visual (solo CLI + JSON)
- Prometheus/OpenTelemetry export (fase posterior)
- `/metrics` endpoint genérico (otra feature en ROADMAP)
- Alarms/alerting (depende de infraestructura externa)
- Phase 5: git pull/push (ya está planificado separadamente)

---

## Approach

### 1. Endpoint `/sync/status`

**Ruta**: `GET /sync/status` (ya registrada en `EngramServer.cs:90` — reemplazar stub)

**Ubicación**: mover a `CloudSyncEndpoints.cs` (consistente con demás endpoints de sync). El stub actual en `EngramServer.cs:464` devuelve `{ enabled: false, message: "..." }`.

**Datos a devolver**:
```json
{
  "sync_enabled": true,
  "phase": "healthy",
  "target": "cloud",
  "cursor": {
    "last_pushed_seq": 142,
    "last_pulled_seq": 89,
    "last_enqueued_seq": 145
  },
  "health": {
    "status": "healthy",
    "consecutive_failures": 0,
    "backoff_until": null,
    "last_error": null,
    "last_sync_at": "2026-05-19T10:30:00Z"
  },
  "counts": {
    "pending_push": 3,
    "total_pushed": 142,
    "total_pulled": 89,
    "deferred_pending": 0
  },
  "enrolled_projects": ["team/mi-proyecto"],
  "paused_projects": []
}
```

**Implementación**:
- Server-side: consultar `ICloudMutationStore` para enrolled/paused projects, latest seqs
- Client-side: consultar `ILocalSyncStore.GetSyncStateAsync()` para cursor local, pending mutations count
- SyncManager expone un `ISyncStatusProvider` (o se inyecta directamente) para phase + health

**Decisiones**:
- El endpoint debe funcionar **sin SyncManager activo** (devuelve lo que pueda)
- Si el server no tiene `ICloudMutationStore` (ej: SqliteStore local), devuelve solo datos locales
- Los contadores "total_pushed" y "total_pulled" se calculan del `sync_state`

### 2. SyncManager Metrics via ILogger

**Eventos estructurados** (usar `LoggerMessage` source-gen para performance):

| Evento | Level | Data |
|--------|-------|------|
| CycleStart | Debug | phase, cycle_id |
| CycleComplete | Information | phase, duration_ms, pushed, pulled |
| CycleFailed | Error | phase, failures, max_failures, error |
| PushBatch | Debug | project, count |
| PullBatch | Debug | count, since_seq, latest_seq |
| DeferredReplay | Information | replayed, dead |
| PanicExit | Critical | error |
| PhaseTransition | Debug | from, to |

```csharp
// Ejemplo de estructura
private static readonly Action<ILogger, SyncPhase, int, int, Exception?> CycleComplete =
    LoggerMessage.Define<SyncPhase, int, int>(
        LogLevel.Information,
        new EventId(2001, "SyncCycleComplete"),
        "Sync cycle completed: phase={Phase}, pushed={Pushed}, pulled={Pulled}");
```

**Counters en memoria** (para consultar via `/sync/status`):
```csharp
public sealed class SyncMetrics
{
    private long _totalPushed;
    private long _totalPulled;
    private int _totalFailures;
    private DateTime _lastSyncAt;
    // ...
}
```

SyncManager recibe `SyncMetrics` por DI y lo updatea en cada ciclo. El endpoint lo consulta.

### 3. CLI `engram sync status`

**Comportamiento actual**: `--status` en `sync` command llama a `EngramSync.GetStatusAsync()` que devuelve estado del git-sync engine (chunks, manifest).

**Comportamiento nuevo**: el `sync status` command debe consultar `GET /sync/status` y mostrar:
```bash
$ engram sync status
Sync status:          healthy
Target:               cloud
Last sync:            2026-05-19T10:30:00Z

Cursor:
  Last pushed:        142
  Last pulled:        89
  Pending push:       3

Health:
  Failures:           0
  Backoff:            none

Enrolled projects:    1
Paused projects:      0

$ engram sync status --json
{ ... JSON completo ... }
```

**Flag `--json`** para output machine-readable.

**¿Qué pasa si no hay server corriendo?** Mostrar error claro: "No se pudo conectar al servidor — ¿está engram server corriendo?"

### 4. Documentación de setup: `docs/SYNC-SETUP.md`

**Contenido**:
- Prerequisitos: PostgreSQL en TrueNAS, .NET 8 runtime
- Variables de entorno: tabla completa con todas las `ENGRAM_SYNC_*` vars
- Paso a paso: configurar PostgreSQL → configurar env vars → correr server → enroll projects → verificar con `sync status`
- Troubleshooting: sync no arranca, 409 pause, failure ceiling
- Arquitectura: diagrama de flujo (texto) mostrando local SQLite ↔ SyncManager ↔ MutationTransport ↔ Cloud Server ↔ PostgreSQL

---

## Affected Areas

| File | Change |
|------|--------|
| `src/Engram.Sync/SyncManager.cs` | Agregar `SyncMetrics` injection, logging estructurado con `LoggerMessage`, exponer `ISyncStatusProvider` |
| `src/Engram.Sync/SyncPhase.cs` | Sin cambios (ya cubre todos los estados) |
| `src/Engram.Sync/SyncMetrics.cs` | **Nuevo** — contadores en memoria |
| `src/Engram.Sync/ISyncStatusProvider.cs` | **Nuevo** — interfaz para consultar estado vivo del SyncManager |
| `src/Engram.Server/CloudSyncEndpoints.cs` | Agregar `GET /sync/status` handler (mover de EngramServer.cs) |
| `src/Engram.Server/EngramServer.cs` | Reemplazar stub `HandleSyncStatus` por delegación a CloudSyncEndpoints |
| `src/Engram.Cli/Program.cs` | Implementar `engram sync status` completo con `--json` |
| `docs/SYNC-SETUP.md` | **Nuevo** — documentación de setup |
| `docs/OFFLINE-FIRST-SYNC.md` | Actualizar Phase 4 status a "In Progress" |

---

## Estimated Effort

| Task | Effort | Dependencies |
|------|--------|-------------|
| SyncMetrics + ISyncStatusProvider | 1h | SyncManager exists |
| GET /sync/status endpoint | 1h | SyncMetrics, ICloudMutationStore |
| SyncManager ILogger metrics | 0.5h | SyncMetrics |
| CLI `engram sync status` | 0.5h | Endpoint |
| Documentación SYNC-SETUP.md | 0.5h | None |
| **Total** | **3.5h** | |

Ajustado del estimado ROADMAP (4-6h) a **3.5h** porque gran parte de la infraestructura ya existe (endpoint registrado, SyncPhase enum, ILocalSyncStore, CLI command structure).

---

## Tradeoffs

| Decisión | Pros | Contras |
|----------|------|---------|
| SyncMetrics en memoria vs persisted | Simple, sin IO overhead | Se pierde al reiniciar server (aceptable — los datos reales están en sync_state) |
| LoggerMessage vs ILogger directo | Performance, structured logging | Más boilerplate |
| ISyncStatusProvider interfaz vs reflection | Testeable, DI-friendly | Una interfaz más |
| Endpoint separado vs integrado en EngramServer.cs | Consistente con CloudSyncEndpoints (AD-3) | Inyección de dependencias: necesita acceder a SyncManager |

---

## Rollout

1. SyncMetrics + ISyncStatusProvider
2. GET /sync/status endpoint
3. SyncManager structured logging
4. CLI `engram sync status`
5. SYNC-SETUP.md
6. Tests (unit + integration)
7. Update OFFLINE-FIRST-SYNC.md
