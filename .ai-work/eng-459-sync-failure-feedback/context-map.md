# Context Map — ENG-459: Sync Failure Feedback

> **Feature**: Sync failure feedback — sin notificación visible cuando sync falla repetidamente  
> **Prioridad**: P0 | **Esfuerzo**: M (2-6h)  
> **Origen**: ← sesión sync 2026-07-16  
> **Fase**: Discovery (forge-discovery Phase 0)

---

## 1. Árbol de decisiones — problema

```
Usuario escribe memorias
  └─→ SqliteStore.EnqueueSyncMutation() ──→ sync_mutations table
        └─→ SyncManager.CycleAsync() (BackgroundService)
              ├─→ PushAsync() (envía a servidor)
              │     ├─→ Error: non-enrolled-pending → MarkSyncBlockedAsync() [solo logs]
              │     ├─→ Error: 409 sync-paused     → MarkSyncBlockedAsync() [solo logs]
              │     └─→ Exception (500/timeout/401)→ MarkSyncFailureAsync() [solo logs]
              └─→ PullAsync() (recibe del servidor)
                    └─→ Exception → MarkSyncFailureAsync() [solo logs]

⚠️  El usuario NO recibe notificación visible en ningún canal.
    Cree que el sync funciona → pérdida de datos silenciosa.
```

---

## 2. Dónde se detectan fallos de sync

### 2.1 SyncManager.cs — Motor del ciclo

| Lugar | Condición | Acción actual | Canal actual |
|-------|-----------|---------------|--------------|
| `CycleAsync()` L206-215 | Exception en push o pull | `_consecutiveFailures++`, `CalculateBackoff()`, `MarkSyncFailureAsync()` | `ILogger.LogError` (EventId 2002) |
| `CycleAsync()` L128-131 | `_consecutiveFailures >= MaxConsecutiveFailures` (default: 10) | SetPhase(Disabled), sale del loop | `ILogger.LogError` (EventId 2002) |
| `CycleAsync()` L134-138 | `_backoffUntil > now` | SetPhase(Backoff), espera | Ninguno (silencioso) |
| `PushAsync()` L229-233 | Pending mutations tienen proyectos no enrolados | `MarkSyncBlockedAsync("non-enrolled-pending")` | `ILogger.LogWarning` |
| `PushAsync()` L245-249 | Transporte responde con `PauseError` | `MarkSyncBlockedAsync("sync-paused")` | `ILogger.LogWarning` |
| `PushAsync()` L255-260 | TransportException con 409 | `MarkSyncBlockedAsync("sync-paused")` | `ILogger.LogWarning` |

### 2.2 SqliteStore.cs — Persistencia de fallos

| Método | Efecto en DB |
|--------|-------------|
| `MarkSyncFailureAsync()` L2645 | UPDATE `sync_state SET lifecycle='failed', consecutive_failures+1, backoff_until, last_error` |
| `MarkSyncBlockedAsync()` L2663 | UPDATE `sync_state SET lifecycle='blocked', last_error` |
| `MarkSyncHealthyAsync()` L2678 | UPDATE `sync_state SET lifecycle='healthy', consecutive_failures=0, backoff_until=NULL, last_error=NULL` |

### 2.3 Escenarios de fallo silencioso (de BACKLOG.md)

| # | Escenario | Dónde se detecta | Estado actual |
|---|-----------|------------------|---------------|
| 1 | `ENGRAM_SERVER_URL` no configurado → SyncManager se deshabilita | `EngramServer.cs` L69-76 (IsSyncSelfLoop) | Solo warning por stderr |
| 2 | Proyectos no enrolados → push bloqueado | `PushAsync()` L229-233 | Log Warning + MarkSyncBlockedAsync |
| 3 | Servidor remoto sin fixes → push 500 | `CycleAsync()` catch L206 | Log Error + MarkSyncFailureAsync |
| 4 | Red caída → timeout | `CycleAsync()` catch L206 | Log Error + MarkSyncFailureAsync |
| 5 | Credenciales inválidas → 401/403 | `CycleAsync()` catch L206 | Log Error + MarkSyncFailureAsync |

---

## 3. Dónde se debería notificar al usuario

### 3.1 Puntos de inyección de notificación

| Punto | Archivo | Línea | Justificación |
|-------|---------|-------|---------------|
| **Después de N fallos consecutivos** | `SyncManager.cs` | L206-215 (catch block) | Umbral < MaxConsecutiveFailures (ej: 3) |
| **Al entrar en estado Disabled** | `SyncManager.cs` | L129 (return del loop) | Sync se detuvo completamente |
| **Al estar en estado Blocked** | `SyncManager.cs` | PushAsync L229-260 | Casos non-enrolled y sync-paused |
| **Al iniciar MCP tools con sync bloqueado** | `EngramTools.cs` | Constructor/init | El agente (LLM) puede advertir al usuario |
| **En consulta de estado** | `Program.cs` (CLI) | L377-395 | `engram sync status` output actual es escueto |

### 3.2 Umbral de notificación propuesto (de BACKLOG.md)

```
ConsecutiveFailures >= 3  →  disparar notificación
```

Actualmente `MaxConsecutiveFailures = 10` en `SyncManagerConfig.cs` L28.  
El umbral de notificación debe ser **independiente** del umbral de deshabilitación.

---

## 4. Canales de notificación disponibles

| Canal | Estado actual | Viabilidad | Notas |
|-------|---------------|------------|-------|
| **ILogger** | ✅ Usado extensivamente | Ya existe | Solo visible en stderr del proceso servidor. No visible para el usuario final. |
| **DB (`sync_state`)** | ✅ Persiste fallos | Ya existe | Backend para otros canales. No visible directamente. |
| **`/sync/status` endpoint** | ✅ Implementado | ✅ Listo para extender | `GET /sync/status` devuelve `SyncStatusResponse` con health. **Falta campo `suggested_action`.** |
| **`engram sync status` CLI** | ✅ Implementado | ✅ Listo para extender | Muestra raw fields. **Falta formateo humano con error claro + acción sugerida.** |
| **MCP tools (mem_doctor)** | ✅ Implementado | ✅ Listo para extender | `DiagnosticService` actualmente no verifica sync health. Se puede añadir check. |
| **MCP server logs (stderr)** | ✅ Implementado | ✅ Canal directo | `EngramMcpServer.cs` redirige logs a stderr. Agente (LLM) los ve. |
| **Notification file** | ❌ No existe | ✅ Simple de implementar | Propuesta: `~/.engram/sync-notifications.log` (últimas 10). Diestro para diagnóstico off-session. |
| **MCP tool return value** | ❌ No existe | ⚠️ Complejidad media | Añadir campo `sync_health` en `mem_status` o tool response. No es estándar MCP. |

### 4.1 Matriz canal-escenario

| Escenario | ILogger | DB | `/sync/status` | CLI | MCP `mem_doctor` | Notification file | MCP init warning |
|-----------|---------|----|---------------|-----|------------------|-------------------|------------------|
| 3 fallos consecutivos | ✅ ya | ✅ ya | ✅ ya (leído) | ✅ ya | ⚠️ no checkea | ❌ ausente | ❌ ausente |
| Disabled | ✅ ya | ✅ ya | ✅ ya (status="disabled") | ✅ ya | ❌ ausente | ❌ ausente | ❌ ausente |
| Blocked (non-enrolled) | ✅ ya | ✅ ya | ✅ ya (last_error) | ✅ ya (raw) | ❌ ausente | ❌ ausente | ❌ ausente |
| Action suggestion | ❌ | ❌ | ❌ ausente | ❌ ausente | ❌ ausente | ❌ ausente | ❌ ausente |

---

## 5. Arquitectura actual (relevante para ENG-459)

### 5.1 Componentes involucrados

```
┌─────────────────────────────────────────────────────────────────┐
│                        engram server                            │
│                                                                 │
│  ┌───────────────────┐    ┌──────────────────┐                  │
│  │   SyncManager     │───▶│  ILogger         │ (stderr/stdio)   │
│  │   (BackgroundSvc) │───▶│  SqliteStore     │ (DB persistence) │
│  └────────┬──────────┘    └──────────────────┘                  │
│           │ ISyncStatusProvider                                 │
│           ▼                                                     │
│  ┌───────────────────┐    ┌──────────────────┐                  │
│  │  EngramServer      │    │ /sync/status     │                  │
│  │  (Minimal API)     │───▶│ endpoint         │──▶ CLI/HTTP      │
│  └───────────────────┘    └──────────────────┘                  │
│           │                                                     │
│  ┌────────▼──────────┐                                         │
│  │  EngramMcpServer   │───▶ MCP tools (stdout JSON-RPC)         │
│  │  (stdio transport) │───▶ mem_doctor → DiagnosticService      │
│  └───────────────────┘                                         │
└─────────────────────────────────────────────────────────────────┘
```

### 5.2 Interfaces clave

| Interfaz/Clase | Rol | ENG-459 toca |
|----------------|-----|--------------|
| `ISyncStatusProvider` | Expone Phase, ConsecutiveFailures, BackoffUntil, Metrics | ✅ Leer estado |
| `ILocalSyncStore` | Persiste sync state (MarkSyncFailure, MarkSyncBlocked, MarkSyncHealthy) | ✅ Leer/escribir |
| `SyncManager` | Implementa ISyncStatusProvider, BackgroundService | ✅ Inyectar notificación |
| `SyncMetrics` | In-memory counters (TotalPushed, TotalPulled, TotalFailures, LastError) | ✅ Ya captura errores |
| `DiagnosticService` | Health checks (DB, HTTP, MCP, Project ID) | ⚠️ NO checkea sync aún |
| `EngramTools` (MCP) | 28 tools; constructor recibe dependencias vía DI | ✅ Inyectar ISyncStatusProvider |
| `Program.cs` (CLI) | `engram sync status` command | ✅ Mejorar output |

### 5.3 SyncStatusResponse actual (en MutationDtos.cs)

```csharp
SyncStatusResponse(
    bool SyncEnabled,
    string Phase,
    string Target,
    StatusCursorBody Cursor,
    StatusHealthBody Health,      // Status, ConsecutiveFailures, BackoffUntil, LastError, LastSyncAt
    StatusCountsBody Counts,      // PendingPush, TotalPushed, TotalPulled, DeferredPending
    IReadOnlyList<string> EnrolledProjects,
    IReadOnlyList<string> PausedProjects
);
```

**Carencia**: No tiene campo `suggested_action`. El health status mapea a "healthy/degraded/disabled" pero no ofrece guía al usuario.

### 5.4 sync_state table schema (SQLite)

```sql
CREATE TABLE sync_state (
    target_key           TEXT PRIMARY KEY,
    lifecycle            TEXT NOT NULL DEFAULT 'idle',  -- idle|healthy|failed|blocked
    last_enqueued_seq    INTEGER NOT NULL DEFAULT 0,
    last_acked_seq       INTEGER NOT NULL DEFAULT 0,
    last_pulled_seq      INTEGER NOT NULL DEFAULT 0,
    consecutive_failures INTEGER NOT NULL DEFAULT 0,
    backoff_until        TEXT,
    lease_owner          TEXT,
    lease_until          TEXT,
    last_error           TEXT,
    updated_at           TEXT NOT NULL DEFAULT (datetime('now'))
);
```

---

## 6. Dependencias con otros ENGs

### 6.1 Dependencias que ENG-459 tiene

| ENG | Relación | Estado | Impacto |
|-----|----------|--------|---------|
| **ENG-458** | ⚠️ **Bloqueante**: mutaciones vacías bloquean sync → notificación avisa de "non-enrolled" pero el verdadero problema es otro | `Ready` | ENG-459 debe asumir que ENG-458 está fixeado, o incluir detección de falsos positivos |
| **ENG-453** | Relacionado: FlowForge installer guarda `ENGRAM_SERVER_URL` | PR Open | Si ENG-453 no guarda la URL, escenario #1 (self-loop) se activa |
| **ENG-455** | Complementario: `flowforge sync connect` con auto-enroll | Hielo | ENG-459 puede sugerir comando `flowforge sync connect` en `suggested_action` |
| **ENG-451** | Histórico: orphaned mutation recovery (sync reliability infra) | Done | Infrastructure de sync estable; ENG-459 se basa en que sync recovery existe |

### 6.2 ENG-458 vs ENG-459 — separación de concerns

| ENG-458 | ENG-459 |
|---------|---------|
| Bug: `project=""` bloquea sync | Feature: notificar al usuario cuando sync falla |
| Fix: filtrar `project=""` en `CountPendingNonEnrolledAsync` | Fix: múltiples canales de notificación |
| Sin acción de usuario requerida | Requiere acción del usuario (enroll/connect/check) |
| Scope: SqliteStore.cs + tests | Scope: SyncManager.cs, Program.cs, CloudSyncEndpoints.cs, EngramTools.cs |

**Ambos son independientes**. ENG-459 no necesita esperar a ENG-458, pero la UX del `suggested_action` puede diferir según el estado de ENG-458.

---

## 7. Información faltante (no blockers)

Estos puntos no están claros en la spec pero NO bloquean el diseño:

| Ítem | Pregunta | Dónde resolver |
|------|----------|----------------|
| **Ubicación del notification file** | `~/.engram/sync-notifications.log` es propuesta. ¿Se usa `~/.engram/` consistentemente en otros contextos? | Confirmar con `EngramSync.cs` y `SyncConfig.SyncDir` |
| **Formato del notification file** | ¿JSON Lines (cada línea = una notificación JSON) o texto plano con timestamp? | Decidir en diseño (forge-arch) |
| **Máximo de notificaciones** | Propuesta: últimas 10. ¿Rotación circular o append + truncate? | Decidir en diseño |
| **¿Notificar solo al superar umbral o en cada fallo?** | La spec dice "después de N fallos consecutivos" (N=3). ¿Notificar también al primer fallo si es grave (401, self-loop)? | Decidir en spec |
| **MCP tool init warning** | ¿Solo loggear warning, o devolver en respuesta de herramienta? El MCP protocol no tiene notificaciones push al cliente. | Decidir en diseño |
| **Integración con DiagnosticService** | ¿`mem_doctor` debe incluir check de sync health? Sería natural pero no está en la spec actual. | Decidir en spec (bajo effort) |

---

## 8. Archivos a modificar (mapeados)

| Archivo | Líneas relevantes | Qué cambiar |
|---------|-------------------|-------------|
| `src/Engram.Sync/SyncManager.cs` | L76-93, L127-131, L206-215 | Inyectar notificación tras N fallos; exponer LastError en ISyncStatusProvider (ya existe en vía Metrics) |
| `src/Engram.Server/CloudSyncEndpoints.cs` | L429-462, L445 | Añadir campo `suggested_action` a `HandleSyncStatusAsync` |
| `src/Engram.Server/Dtos/MutationDtos.cs` | L169-174 | Añadir `suggested_action` a `StatusHealthBody` |
| `src/Engram.Cli/Program.cs` | L377-395 | Mejorar output human-readable con sugerencia |
| `src/Engram.Mcp/EngramTools.cs` | ~L52 (constructor) | Inyectar ISyncStatusProvider; loggear warning si sync bloqueado |
| `tests/Engram.Sync.Tests/SyncManagerTests.cs` | — | Tests: notificación tras 3 fallos |
| `tests/Engram.Cli.Tests/SyncStatusCliTests.cs` | — | Tests: output con error + sugerencia |
| `tests/Engram.Server.Tests/SyncStatusEndpointTests.cs` | — | Tests: `suggested_action` en respuesta |

### Archivos a consultar (no modificar)

| Archivo | Propósito |
|---------|-----------|
| `src/Engram.Sync/ISyncStatusProvider.cs` | Interfaz de estado que SyncManager expone |
| `src/Engram.Store/ILocalSyncStore.cs` | Interfaz de persistencia de sync_state |
| `src/Engram.Store/SqliteStore.cs` | Implementación de persistencia (lectura de `last_error`) |
| `src/Engram.Diagnostics/DiagnosticService.cs` | Referencia para posible sync health check |
| `src/Engram.Sync/SyncManagerConfig.cs` | Valores por defecto (MaxConsecutiveFailures=10) |
| `src/Engram.Server/EngramServer.cs` | Registro de SyncManager en DI |

---

## 9. Riesgos y consideraciones

### 9.1 Riesgos técnicos

| Riesgo | Probabilidad | Mitigación |
|--------|-------------|------------|
| **Falsos positivos**: notificar cuando sync está saludable pero momentáneamente degradado (backoff transitorio) | Baja | Usar umbral `ConsecutiveFailures >= 3` + verificar `Phase != Backoff` reciente |
| **Race condition**: SyncManager escribe notificación mientras otro ciclo se completa exitosamente | Baja | Verificar estado actual antes de escribir notificación; usar `_consecutiveFailures` y `_phase` atómicamente |
| **Notification file sin límite**: crece indefinidamente | Media | Rotación circular: mantener solo últimas N (10) entradas |

### 9.2 Consideraciones de diseño

- **ISyncStatusProvider** no expone `LastError` directamente (solo via `Metrics.LastError`). SyncManager ya escribe error en Metrics. La fuente DB (`sync_state.last_error`) tiene la versión persistente.
- El notification file propuesto (`~/.engram/sync-notifications.log`) sería escrito por SyncManager (el proceso `engram serve`), que corre como background service. El path debe ser configurable o determinista.
- **PostgreSQL backend** (cloud relay mode) no tiene SyncManager local. El `/sync/status` endpoint ya maneja este caso (`isCloudRelay = true`). No aplica feedback ya que no hay BackgroundService local.

---

## 10. Conclusión para forge-arch (CKP-1)

ENG-459 es **factible** con cambios localizados en 5 archivos de código + 3 de tests. No hay blockers arquitectónicos. Las dependencias con ENG-458 son de UX (el tipo de sugerencia) no de infraestructura. La feature puede implementarse en orden sugerido:

1. **SyncManager**: umbral de notificación + notification file + `LastError` en ISyncStatusProvider
2. **DTOs + Endpoint**: `suggested_action` en `/sync/status`
3. **CLI**: output mejorado de `engram sync status`
4. **MCP**: warning en inicialización si sync bloqueado
5. **Tests**: cobertura de todos los escenarios

---

*Generado por forge-discovery (Phase 0) para FlowForge. Input de forge-arch (CKP-1).*
