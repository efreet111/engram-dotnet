# Spec — ENG-459: Sync Failure Feedback

> **Feature**: Sync failure feedback — notificación visible cuando sync falla repetidamente
> **Prioridad**: P0 | **Esfuerzo**: M (2-6h)
> **Origen**: ← sesión sync 2026-07-16 (pérdida de datos silenciosa)
> **Estado**: Phase 1 (forge-arch — CKP-1)
> **Context Map**: `.ai-work/eng-459-sync-failure-feedback/context-map.md`

---

## 1. Resumen ejecutivo

Cuando `SyncManager` (BackgroundService) falla repetidamente, el usuario no recibe **ninguna notificación visible**. Los errores se loggean a `ILogger` (stderr del proceso) y se persisten en `sync_state.last_error` (DB), pero estos canales son invisibles para el usuario final. El usuario cree que sus memorias se sincronizan, pero nunca llegan al servidor remoto. Esto causa **pérdida de datos silenciosa** — un escenario de usuario real donde 38 mutaciones quedaron pendientes de push durante días sin que nadie lo supiera.

Esta feature introduce 4 canales de feedback que cubren el espectro completo de interacción con el sistema:

| Canal | Quién lo ve | Cuándo |
|-------|------------|--------|
| **Notification file** (`~/.engram/sync-notifications.log`) | Usuario (diagnóstico off-session) | Tras umbral de fallos |
| **`/sync/status` endpoint** (`suggested_action` field) | CLI, scripts, herramientas | Cada consulta de estado |
| **`engram sync status` CLI** (output mejorado) | Usuario directamente | Cada ejecución del comando |
| **MCP `mem_doctor` tool** (sync health check) | Agente LLM → usuario | Cada diagnóstico MCP |

---

## 2. Functional Requirements (FR)

### FR-1: Notification file on failure threshold

**Descripción**: SyncManager escribe una entrada en un archivo de notificaciones cuando `ConsecutiveFailures` alcanza un umbral configurable.

| Campo | Valor |
|-------|-------|
| **Umbral de notificación** | `NotificationThreshold = 3` (configurable via `ENGRAM_SYNC_NOTIFICATION_THRESHOLD`) |
| **Umbral de deshabilitación** | `MaxConsecutiveFailures = 10` (ya existe, inmutable) |
| **Separación** | El umbral de notificación es **independiente** del de deshabilitación |
| **Archivo** | `~/.engram/sync-notifications.log` (path determinista, en `EngramSync.SyncDir` parent) |
| **Formato** | JSON Lines — cada línea = un JSON con timestamp ISO-8601 |
| **Rotación** | Mantener últimas 10 entradas (reescritura circular en archivo) |
| **Trigger** | Se escribe cuando `ConsecutiveFailures` cruza el umbral (≥3) O cuando cambia a `Disabled` |

**Formato de línea**:
```json
{"ts":"2026-07-16T14:30:00Z","level":"error","failures":5,"error":"non-enrolled-pending: 3 projects not enrolled","action":"Enroll projects: POST /sync/enroll","phase":"blocked"}
```

**Caso de éxito (recover)**: Cuando sync pasa a `Healthy` (ciclo exitoso), se escribe una línea de recover:
```json
{"ts":"2026-07-16T15:00:00Z","level":"ok","message":"Sync recovered after 5 failures","failures":0}
```

### FR-2: `suggested_action` field in `/sync/status`

**Descripción**: El endpoint `GET /sync/status` devuelve un campo `suggested_action` en el objeto `health` con una instrucción de recuperación legible para humanos.

**Ubicación actual**: `StatusHealthBody` en `MutationDtos.cs` L169-174.
**Cambio**: Añadir `[property: JsonPropertyName("suggested_action")] string? SuggestedAction`.

**Valores de `suggested_action` según estado**:

| Estado | `suggested_action` |
|--------|--------------------|
| `healthy` | `null` (no acción sugerida) |
| `degraded` (failures ≥3, phase ≠ disabled) | `"Sync degraded. Check server connectivity and run 'engram sync status' for details."` |
| `disabled` (failures ≥ MaxConsecutiveFailures) | `"Sync disabled after {N} failures. Restart engram server or check ENGRAM_SERVER_URL configuration."` |
| `blocked` (non-enrolled-pending) | `"Sync blocked: {count} project(s) not enrolled. Enroll with: curl -X POST {server_url}/sync/enroll -H 'Content-Type: application/json' -d '{\"project\": \"<name>\"}'"` |
| `blocked` (sync-paused) | `"Sync paused for project {project}. Resume with: curl -X DELETE {server_url}/sync/pause?project={project}"` |

**Generación**: La lógica de `suggested_action` vive en `CloudSyncEndpoints.HandleSyncStatusAsync()` (L403-465), no en SyncManager. Esto separa la lógica de observabilidad (endpoint) de la de ejecución (background service).

**Server URL**: El endpoint conoce su propia URL vía `HttpContext.Request` (`Request.GetDisplayUrl()` o construir desde host/port). Alternativamente, se usa `ENGRAM_SERVER_URL` del environment.

### FR-3: CLI `engram sync status` output mejorado

**Descripción**: El comando `engram sync status` muestra error claro con acción sugerida en formato legible para humanos.

**Ubicación actual**: `Program.cs` L377-395.
**Cambio**: Leer `suggested_action` del JSON de respuesta y mostrarlo si no es null.

**Output esperado** (ejemplo degradado):
```
Sync status (mutation-based):
  Enabled:              true
  Phase:                pushfailed
  Health:               degraded
  Consecutive failures: 5
  Backoff until:        2026-07-16T14:35:00Z
  Last sync:            2026-07-16T12:00:00Z
  Last error:           non-enrolled-pending: 3 projects not enrolled
  Pending push:         38
  Total pushed:         142
  Total pulled:         89
  Last pushed seq:      142
  Last pulled seq:      89

  💡 Suggested action:
     Sync degraded. Check server connectivity and run 'engram sync status' for details.
```

**Output esperado** (ejemplo bloqueado):
```
Sync status (mutation-based):
  Enabled:              true
  Phase:                pushfailed
  Health:               blocked
  ...

  ⚠️  WARNING: Sync blocked — data is NOT being synchronized!
  Pending mutations: 38

  💡 Suggested action:
     Sync blocked: 3 project(s) not enrolled. Enroll with:
     curl -X POST http://server/sync/enroll -H 'Content-Type: application/json' -d '{"project": "team/engram-dotnet"}'
```

**Regla de formato**:
- `health.status == "healthy"` → sin emoji de advertencia
- `health.status == "degraded"` → ⚠️ + "Suggested action:" en itálica
- `health.status == "disabled"` o `health.status == "blocked"` → ⚠️ + WARNING en mayúsculas + "Suggested action:" con el comando

### FR-4: MCP `mem_doctor` includes sync health check

**Descripción**: El tool MCP `mem_doctor` (via `DiagnosticService`) incluye un check de sync health como componente más.

**Ubicación actual**: `DiagnosticService.cs` ejecuta 4 checks: database, http_server, mcp_server, project_identity.
**Cambio**: Añadir `CheckSyncHealthAsync()` como 5to check.

**Requisito de inyección**: `DiagnosticService` necesita acceso a `ISyncStatusProvider` (nullable — puede no existir en cloud relay mode).

**ComponentHealth result**:

| Sync Phase | `IsHealthy` | `Message` |
|-----------|-------------|-----------|
| `Healthy` | `true` | `"Sync healthy (X mutations pending)"` |
| `Backoff` / `PushFailed` / `PullFailed` | `false` | `"Sync degraded: {failures} consecutive failures. Last error: {error}"` |
| `Disabled` | `false` | `"Sync disabled: {failures} failures reached ceiling. Last error: {error}"` |
| `null` (no provider) | `true` | `"Sync not applicable (cloud relay mode)"` |

**DI change**: `DiagnosticService` constructor recibe `ISyncStatusProvider?` (nullable). `Program.cs`/DI registration pasa el provider si existe.

### FR-5: MCP stderr warning on init with blocked sync

**Descripción**: `EngramTools` emite un `LogWarning` al construirse si sync está bloqueado, visible en stderr del MCP server.

**Ubicación actual**: `EngramTools.cs` constructor (primary constructor parameters, L52).
**Cambio**: Añadir `ISyncStatusProvider?` como parámetro del primary constructor. En el body del constructor (o init block), loggear warning si `Phase` es `Disabled` o `ConsecutiveFailures >= 3`.

**Mensaje de warning**:
```
⚠️ Sync is {phase}: {consecutiveFailures} consecutive failures. Last error: {lastError}. Run 'engram sync status' for details.
```

**Visibility**: Este warning aparece en stderr del proceso MCP server. El agente LLM (que lee stdout) no lo ve directamente, pero un usuario que ejecuta `engram doctor` o revisa logs del MCP server sí.

### FR-6: `ISyncStatusProvider` expone `LastError` directamente

**Descripción**: Añadir `string? LastError` a `ISyncStatusProvider` para que consumidores (endpoint, MCP, DiagnosticService) no dependan de `SyncMetrics.LastError` (que es in-memory y se pierde al reiniciar).

**Ubicación actual**: `ISyncStatusProvider.cs` (10 líneas, expone Phase, IsEnabled, ConsecutiveFailures, BackoffUntil, Metrics).
**Cambio**: Añadir `string? LastError { get; }`.

**Fuente de datos**: SyncManager lee de DB (`_store.GetSyncStateAsync`) o mantiene una copia en campo privado que se actualiza en `CycleAsync` catch block. La versión DB es la canónica (persiste reinicios); la versión in-memory es la de respaldo.

**Propagación**: `CloudSyncEndpoints.HandleSyncStatusAsync` ya lee `state?.LastError` (L449). Este cambio es complementario — provee la fuente in-memory para cuando no hay DB (ej: cloud relay no aplica, pero sí para testing).

---

## 3. Non-Functional Requirements (NFR)

### NFR-1: Performance

| Requisito | Medida |
|-----------|--------|
| **Notification file write** | < 5ms. Escritura append + truncate a archivo local. No bloquear el ciclo de sync. |
| **`suggested_action` generation** | < 1ms. String interpolation simple, sin I/O. |
| **`mem_doctor` sync check** | < 10ms. Lectura de `ISyncStatusProvider` propiedades en memoria. |
| **No impacto en sync cycle** | La escritura del notification file se ejecuta en el catch block del ciclo (ya falló, no hay presión de latencia). |

### NFR-2: Seguridad

| Requisito | Detalle |
|-----------|---------|
| **No exponer secrets en suggested_action** | `suggested_action` no contiene tokens, contraseñas ni credenciales. Solo comandos genéricos. |
| **No exponer IP interna** | Si `server_url` es una IP LAN, el `suggested_action` la muestra (ya es visible en otros campos). No es un nuevo vector. |
| **Notification file permissions** | Archivo creado con permisos 600 (solo owner). Contenido: solo errores de sync, no datos de memorias. |
| **No PII en MCP warning** | El warning en stderr no contiene contenido de memorias, solo metadatos de estado. |

### NFR-3: Fiabilidad

| Requisito | Detalle |
|-----------|---------|
| **Notification file es best-effort** | Si falla la escritura del archivo, no se propaga excepción al SyncManager. Se loggea a ILogger como fallback. |
| **Graceful degradation** | Si `ISyncStatusProvider` no está registrado (cloud relay), todos los canales de notificación se comportan como "healthy" (sin alarmas falsas). |
| **Idempotencia** | Escribir la misma notificación N veces no causa duplicados en el archivo (se basa en timestamp + failure count). |

### NFR-4: Compatibilidad

| Requisito | Detalle |
|-----------|---------|
| **Backward-compatible JSON** | `suggested_action` es un campo nullable. Clientes antiguos lo ignoran. |
| **CLI backward-compatible** | Si `suggested_action` es null, el output es idéntico al actual. |
| **MCP backward-compatible** | Si `ISyncStatusProvider` no está inyectado, `mem_doctor` funciona igual que antes. |

### NFR-5: Observabilidad

| Requisito | Detalle |
|-----------|---------|
| **Log structured** | Todas las notificaciones usan `ILogger` structured logging (no string interpolation). |
| **Event IDs nuevos** | `SyncNotificationWritten` (EventId 2010), `SyncRecovered` (EventId 2011). |

---

## 4. STRIDE Threat Analysis

### S — Spoofing (Suplantación)

| Amenaza | Riesgo | Mitigación |
|---------|--------|------------|
| Atacante modifica `sync-notifications.log` para ocultar fallos reales | Bajo — archivo local, solo owner tiene acceso | Permisos 600; archivo es informativo, no se usa para autorización |
| Atacante manipula `/sync/status` response | Bajo — endpoint es local, no autenticado actualmente | `suggested_action` es solo informativo; no ejecuta nada automáticamente |

### T — Tampering (Manipulación)

| Amenaza | Riesgo | Mitigación |
|---------|--------|------------|
| Modificar `suggested_action` para dirigir usuario a servidor malicioso | Bajo — `suggested_action` se genera server-side en `CloudSyncEndpoints`, no viene de input del usuario | Validar que `server_url` en `suggested_action` proviene de `HttpContext.Request`, no de parámetros |
| Modificar notification file para inyectar comandos | Bajo — archivo es solo logs, no se ejecuta | `engram sync status` solo **muestra** el contenido, no lo ejecuta |

### R — Repudiation (Repudiación)

| Amenaza | Riesgo | Mitigación |
|---------|--------|------------|
| Usuario niega que sync falló | Bajo — `sync_state` en DB persiste historial de fallos | `last_error`, `consecutive_failures`, `updated_at` en DB son la fuente de verdad. Notification file es complementario. |

### I — Information Disclosure (Divulgación de información)

| Amenaza | Riesgo | Mitigación |
|---------|--------|------------|
| `last_error` contiene información sensible del servidor | Medio — mensajes de error pueden incluir stack traces o paths del servidor | `last_error` ya se expone en `/sync/status`. ENG-459 no incrementa la superficie; solo formatea lo existente. **Consideración**: sanitizar `last_error` para remover paths absolutos en producción. |
| Notification file expone拓扑 de red | Bajo — solo contiene errores de sync, no datos de memorias ni contenido | Archivo no contiene payloads de mutationes, solo metadatos de estado |

### D — Denial of Service (Denegación de servicio)

| Amenaza | Riesgo | Mitigación |
|---------|--------|------------|
| Notification file crece indefinidamente | Medio — si rotación falla, archivo crece | Rotación circular: mantener últimas 10 entradas. Si falla la escritura, se ignora (best-effort). |
| `mem_doctor` sync check bloquea diagnóstico | Bajo — lectura de propiedades en memoria, < 10ms | Timeout implícito: `ISyncStatusProvider` es síncrono (propiedades, no métodos async) |

### E — Elevation of Privilege (Elevación de privilegios)

| Amenaza | Riesgo | Mitigación |
|---------|--------|------------|
| `suggested_action` contiene comandos que el usuario ejecuta como root | Bajo — comandos son genéricos (`curl`, `engram sync status`), no requieren privilegios especiales | No incluir `sudo` ni comandos de sistema en `suggested_action` |

---

## 5. API/Interface Contract

### 5.1 `GET /sync/status` — Response Changes

**Antes** (`StatusHealthBody`):
```json
{
  "status": "degraded",
  "consecutive_failures": 5,
  "backoff_until": "2026-07-16T14:35:00Z",
  "last_error": "non-enrolled-pending: 3 projects not enrolled",
  "last_sync_at": "2026-07-16T12:00:00Z"
}
```

**Después**:
```json
{
  "status": "degraded",
  "consecutive_failures": 5,
  "backoff_until": "2026-07-16T14:35:00Z",
  "last_error": "non-enrolled-pending: 3 projects not enrolled",
  "last_sync_at": "2026-07-16T12:00:00Z",
  "suggested_action": "Sync degraded. Check server connectivity and run 'engram sync status' for details."
}
```

**Campos nuevos**:

| Campo | Tipo | Nullable | Descripción |
|-------|------|----------|-------------|
| `suggested_action` | `string?` | Sí | Instrucción de recuperación legible para humanos. `null` cuando status es `healthy`. |

### 5.2 `ISyncStatusProvider` — Interface Change

**Antes**:
```csharp
public interface ISyncStatusProvider
{
    SyncPhase Phase { get; }
    bool IsEnabled { get; }
    int ConsecutiveFailures { get; }
    DateTime? BackoffUntil { get; }
    SyncMetrics Metrics { get; }
}
```

**Después**:
```csharp
public interface ISyncStatusProvider
{
    SyncPhase Phase { get; }
    bool IsEnabled { get; }
    int ConsecutiveFailures { get; }
    DateTime? BackoffUntil { get; }
    SyncMetrics Metrics { get; }
    string? LastError { get; }
}
```

### 5.3 `SyncManagerConfig` — New Config Fields

**Campos nuevos**:

| Campo | Tipo | Default | Env Variable | Descripción |
|-------|------|---------|-------------|-------------|
| `NotificationThreshold` | `int` | `3` | `ENGRAM_SYNC_NOTIFICATION_THRESHOLD` | Umbral de fallos consecutivos para escribir notificación |
| `NotificationFileMaxEntries` | `int` | `10` | `ENGRAM_SYNC_NOTIFICATION_MAX` | Máximo de entradas en notification file |

### 5.4 Notification File Schema

**Ubicación**: `{ENGRAM_SYNC_DIR}/../sync-notifications.log` (parent of sync dir, which is `~/.engram/`)
**Ruta efectiva**: `~/.engram/sync-notifications.log`

**Formato** (JSON Lines):
```jsonl
{"ts":"2026-07-16T14:30:00Z","level":"error","failures":5,"error":"non-enrolled-pending: 3 projects not enrolled","action":"Enroll projects: POST /sync/enroll","phase":"blocked"}
{"ts":"2026-07-16T14:35:00Z","level":"error","failures":6,"error":"non-enrolled-pending: 3 projects not enrolled","action":"Enroll projects: POST /sync/enroll","phase":"blocked"}
{"ts":"2026-07-16T15:00:00Z","level":"ok","message":"Sync recovered after 6 failures","failures":0}
```

**Campos**:

| Campo | Tipo | Requerido | Descripción |
|-------|------|-----------|-------------|
| `ts` | `string` (ISO-8601) | Sí | Timestamp UTC de la notificación |
| `level` | `string` | Sí | `"error"` o `"ok"` |
| `failures` | `int` | Sí | `ConsecutiveFailures` al momento de la notificación |
| `error` | `string?` | No | `LastError` si aplica |
| `action` | `string?` | No | `suggested_action` si aplica |
| `phase` | `string` | Sí | Phase del SyncManager al momento de la notificación |
| `message` | `string?` | No | Mensaje descriptivo (para `level: "ok"`) |

### 5.5 `DiagnosticService` — Constructor Change

**Antes**:
```csharp
public DiagnosticService(IStore store, HttpClient? httpClient = null, string? serverUrl = null)
```

**Después**:
```csharp
public DiagnosticService(IStore store, HttpClient? httpClient = null, string? serverUrl = null, ISyncStatusProvider? syncStatusProvider = null)
```

**Componente nuevo**: `"sync_health"` en `DiagnosticResult.Components`.

---

## 6. Acceptance Criteria

### AC-1: Notification file written on failure threshold
- [ ] Cuando `ConsecutiveFailures >= 3`, SyncManager escribe una línea JSON en `~/.engram/sync-notifications.log`
- [ ] La línea contiene `ts`, `level: "error"`, `failures`, `error`, `action`, `phase`
- [ ] El archivo no tiene más de 10 entradas (rotación circular)
- [ ] Cuando sync recupera (`Phase = Healthy`), se escribe una línea `level: "ok"`
- [ ] Si la escritura del archivo falla, no se propaga excepción al SyncManager

### AC-2: `/sync/status` includes `suggested_action`
- [ ] `GET /sync/status` devuelve campo `suggested_action` en `health`
- [ ] `suggested_action` es `null` cuando `status == "healthy"`
- [ ] `suggested_action` contiene instrucciones relevantes para `degraded`, `disabled`, `blocked`
- [ ] Para `blocked` con `non-enrolled-pending`, `suggested_action` incluye el comando `curl` correcto con el `server_url` del endpoint
- [ ] Para `blocked` con `sync-paused`, `suggested_action` incluye el comando `curl` de resume
- [ ] El campo es backward-compatible (nullable, clientes antiguos lo ignoran)

### AC-3: CLI `engram sync status` shows actionable output
- [ ] Cuando `suggested_action` no es null, el CLI muestra "💡 Suggested action:" seguido del contenido
- [ ] Para estados `disabled` o `blocked`, se muestra "⚠️ WARNING: Sync {status} — data is NOT being synchronized!"
- [ ] Para estado `healthy`, no se muestra nada extra
- [ ] El flag `--json` sigue funcionando (output JSON crudo sin formateo)

### AC-4: MCP `mem_doctor` includes sync health
- [ ] `mem_doctor` incluye componente `"sync_health"` en su resultado
- [ ] Cuando `ISyncStatusProvider` es null (cloud relay), `sync_health` es `healthy` con mensaje informativo
- [ ] Cuando sync está `disabled`, `sync_health.IsHealthy = false`
- [ ] Cuando sync está `degraded` (failures ≥ 3), `sync_health.IsHealthy = false`
- [ ] El check completa en < 10ms (lectura de propiedades en memoria)

### AC-5: MCP stderr warning on init
- [ ] Al construir `EngramTools`, si `ISyncStatusProvider?.Phase == Disabled` o `ConsecutiveFailures >= 3`, se emite `LogWarning`
- [ ] El warning incluye phase, failure count, y last error
- [ ] Si `ISyncStatusProvider` es null, no se emite warning

### AC-6: `ISyncStatusProvider.LastError` exposed
- [ ] `ISyncStatusProvider` tiene propiedad `string? LastError`
- [ ] `SyncManager` implementa `LastError` leyendo de DB (`_store.GetSyncStateAsync`) o campo privado actualizado en catch block
- [ ] `CloudSyncEndpoints` puede usar `provider.LastError` como fallback de `state?.LastError`

### AC-7: Tests pass
- [ ] Test unitario: SyncManager con 3+ fallos → notification file escrito
- [ ] Test unitario: `/sync/status` response incluye `suggested_action` para cada estado
- [ ] Test unitario: CLI output incluye suggested action cuando status ≠ healthy
- [ ] Test unitario: `mem_doctor` incluye `sync_health` component
- [ ] Test de integración: Ciclo completo de fallo → notificación → recover → notificación de recover
- [ ] Todos los tests existentes siguen pasando (no regressions)

---

## 7. Testing Strategy

### 7.1 Unit Tests

| Test | Archivo | Qué verifica |
|------|---------|-------------|
| `NotificationFile WrittenOnThreshold` | `SyncManagerTests.cs` | Mock `ILocalSyncStore` + filesystem; verificar que archivo se escribe cuando failures ≥ 3 |
| `NotificationFile RotationMaxEntries` | `SyncManagerTests.cs` | Escribir 15 notificaciones; verificar que solo quedan 10 |
| `NotificationFile RecoverEntry` | `SyncManagerTests.cs` | Simular 5 fallos + 1 éxito; verificar línea `level: "ok"` |
| `SuggestedAction_NullWhenHealthy` | `SyncStatusEndpointTests.cs` | GET /sync/status con phase=healthy → suggested_action null |
| `SuggestedAction_BlockedNonEnrolled` | `SyncStatusEndpointTests.cs` | Mock non-enrolled → suggested_action contiene "Enroll" |
| `SuggestedAction_BlockedPaused` | `SyncStatusEndpointTests.cs` | Mock sync-paused → suggested_action contiene "Resume" |
| `SuggestedAction_Disabled` | `SyncStatusEndpointTests.cs` | Mock Disabled phase → suggested_action contiene "Restart" |
| `CliOutput_ShowsSuggestedAction` | `SyncStatusCliTests.cs` | Simular respuesta HTTP con suggested_action → verificar output contiene "💡" |
| `CliOutput_WarningOnBlocked` | `SyncStatusCliTests.cs` | Simular blocked → verificar output contiene "⚠️ WARNING" |
| `MemDoctor_SyncHealthComponent` | `DiagnosticServiceTests.cs` | Mock ISyncStatusProvider → verificar componente en resultado |
| `MemDoctor_SyncHealthyNullProvider` | `DiagnosticServiceTests.cs` | null provider → sync_health healthy |
| `LastError_Exposed` | `SyncManagerTests.cs` | SyncManager.LastError == last error message tras fallo |

### 7.2 Integration Tests

| Test | Archivo | Qué verifica |
|------|---------|-------------|
| `FullCycle_FailureThenRecover` | `SyncManagerTests.cs` | Ciclo real: 3 fallos → notificación escrita → 1 éxito → recover notificación |
| `NotificationFile_PersistsAcrossRestarts` | `SyncManagerTests.cs` | Escribir notificación, simular restart, verificar que archivo persiste |

### 7.3 Manual Testing

| Escenario | Comando | Esperado |
|-----------|---------|----------|
| Sync healthy | `engram sync status` | Output limpio, sin suggested action |
| Sync degradado | Desconectar red, esperar 3 ciclos, `engram sync status` | ⚠️ + suggested action |
| Sync bloqueado | Crear mutación sin enrolar, `engram sync status` | ⚠️ WARNING + curl command |
| Notification file | `cat ~/.engram/sync-notifications.log` | JSON Lines con timestamps |
| MCP diagnose | Ejecutar `mem_doctor` via MCP | Componente `sync_health` presente |

---

## 8. BLOCKERs

**No hay blockers para la implementación.** Todos los puntos resueltos:

| Ítem | Resolución |
|------|-----------|
| Ubicación del notification file | `~/.engram/sync-notifications.log` — consistente con `EngramSync.SyncDir` que usa `~/.engram/sync/` |
| Formato del notification file | JSON Lines — máquina-legible, parseable, consistente con el estilo del proyecto |
| Máximo de notificaciones | 10 entradas (rotación circular) — suficiente para diagnóstico, no crece indefinidamente |
| ¿Notificar en cada fallo o solo al umbral? | Solo al umbral (≥3) y en transición a Disabled — evita spam en logs |
| MCP tool init warning | LogWarning en stderr — MCP protocol no tiene push notifications, stderr es el canal canónico |
| Integración con DiagnosticService | Nuevo componente `sync_health` — natural, bajo esfuerzo |

---

## 9. Archivos a Modificar

| Archivo | Cambio | Esfuerzo |
|---------|--------|----------|
| `src/Engram.Sync/SyncManager.cs` | Añadir campo `_lastError`, exponer `LastError`, inyectar escritura de notificación en catch block | M |
| `src/Engram.Sync/ISyncStatusProvider.cs` | Añadir `string? LastError { get; }` | S |
| `src/Engram.Sync/SyncManagerConfig.cs` | Añadir `NotificationThreshold`, `NotificationFileMaxEntries` + env vars | S |
| `src/Engram.Server/Dtos/MutationDtos.cs` | Añadir `SuggestedAction` a `StatusHealthBody` | S |
| `src/Engram.Server/CloudSyncEndpoints.cs` | Generar `suggested_action` en `HandleSyncStatusAsync` | M |
| `src/Engram.Cli/Program.cs` | Leer y mostrar `suggested_action` en output | S |
| `src/Engram.Mcp/EngramTools.cs` | Añadir `ISyncStatusProvider?` al constructor, log warning | S |
| `src/Engram.Diagnostics/DiagnosticService.cs` | Añadir `ISyncStatusProvider?`, `CheckSyncHealthAsync()` | M |
| `tests/Engram.Sync.Tests/SyncManagerTests.cs` | Tests de notification file + LastError | M |
| `tests/Engram.Server.Tests/SyncStatusEndpointTests.cs` | Tests de suggested_action | M |
| `tests/Engram.Cli.Tests/SyncStatusCliTests.cs` | Tests de output mejorado | S |
| `tests/Engram.Diagnostics.Tests/DiagnosticServiceTests.cs` | Tests de sync_health component | S |

---

## 10. Memory Signal

```yaml
feature: eng-459-sync-failure-feedback
phase: spec (forge-arch)
status: ready
decisions:
  - id: DEC-459-01
    topic: notification file format
    decision: JSON Lines (cada línea = JSON con timestamp)
    rationale: máquina-legible, parseable, consistente con estilo del proyecto
  - id: DEC-459-02
    topic: notification file location
    decision: ~/.engram/sync-notifications.log (parent of SyncDir)
    rationale: consistente con convención ~/.engram/ del proyecto
  - id: DEC-459-03
    topic: notification threshold
    decision: ConsecutiveFailures >= 3 (independiente de MaxConsecutiveFailures=10)
    rationale: 3 es el mínimo para distinguir fallo transitorio de patrón; 10 es el techo de deshabilitación
  - id: DEC-459-04
    topic: suggested_action generation
    decision: CloudSyncEndpoints (server-side), no SyncManager
    rationale: separación de concerns; endpoint conoce HttpContext (server_url)
  - id: DEC-459-05
    topic: DiagnosticService sync check
    decision: Nuevo componente sync_health, ISyncStatusProvider? nullable
    rationale: graceful degradation en cloud relay mode (no SyncManager local)
  - id: DEC-459-06
    topic: notification file rotation
    decision: 10 entradas, reescritura circular
    rationale: suficiente para diagnóstico, no crece indefinidamente
  - id: DEC-459-07
    topic: notification write failure handling
    decision: best-effort (no propagar excepción, loggea a ILogger como fallback)
    rationale: notification file es complementario, no debe afectar ciclo de sync
scope:
  files_to_modify: 8 (code) + 4 (tests)
  effort: M (2-6h)
  blockers: none
dependencies:
  - eng-458 (independiente, pero suggested_action puede cambiar según estado)
  - eng-455 (complementario: suggested_action puede sugerir flowforge sync connect)
```
