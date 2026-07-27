# Context Map: Sync-on-demand (ENG-476)

**Fecha:** 2026-07-25
**Estado:** Pre-spec (análisis profundo de contexto completado)
**Slug:** `eng-476-sync-on-demand`
**Origen:** Sesión de verificación de sync 2026-07-25 (ENG-475 fix)

---

## 1. Feature Summary

Trigger sync cycle on-demand cuando el usuario interactúa con Engram (búsqueda, guardado), sin requerir un daemon independiente. Resuelve dos problemas concretos de sincronización cuando el SyncManager (BackgroundService) no está corriendo o no ha corrido recientemente.

---

## 2. Problemas Identificados

### Problema A: Memorias creadas vía CLI no se sincronizan

**Causa raíz:** El CLI (`engram save`, `engram search`, etc.) NO ejecuta SyncManager. Abre un store, realiza la operación y cierra. Las mutaciones quedan pendientes en `sync_mutations` (acked_at = NULL) hasta que alguien arranque `engram mcp` o `engram serve`.

**Flujo actual:**
\`\`\`
Usuario: engram save "algo" (10:00)
  → SqliteStore.SaveObservationAsync()
  → EnqueueSyncMutation() → INSERT INTO sync_mutations (source='local')
  → Mutation queda pendiente (acked_at = NULL)
  → CLI termina (10:00)
  → (nadie corre SyncManager hasta las 15:00 cuando se abre IDE)
  → SyncManager hace push → mutation recién sincronizada (5 horas después)
\`\`\`

**Impacto:** Desincronización silenciosa. El usuario no sabe que sus memorias no están disponibles para otros miembros del equipo.

### Problema B: Búsqueda no ve memorias recientes (stale reads)

**Causa raíz:** El pull de mutaciones del servidor solo ocurre cada `PollInterval` (default: 30s) cuando SyncManager está corriendo. Si un usuario busca inmediatamente después de que otro guardó, el pull puede no haber ocurrido aún.

**Flujo actual:**
\`\`\`
Usuario A: mem_save "decisión X" (09:30)
  → Mutation pushed a servidor
Usuario B: mem_search "decisión" (09:31)
  → Pull no ocurrió desde 09:30
  → Usuario B NO ve la memoria "decisión X"
  → Pull ocurre en 09:31:30 (próximo ciclo)
  → Usuario B ya cerró su búsqueda
\`\`\`

**Impacto:** Resultados stale, experiencia de búsqueda inconsistente.

---

## 3. Arquitectura Actual (SyncManager)

### Componentes

| Componente | Archivo | Rol |
|---|---|---|
| `SyncManager` | `src/Engram.Sync/SyncManager.cs` | `BackgroundService` con loop push/pull cada `PollInterval` (30s) |
| `SyncManagerConfig` | `src/Engram.Sync/SyncManagerConfig.cs` | Config poll, batch size, backoff, etc. |
| `SyncPhase` | `src/Engram.Sync/SyncPhase.cs` | Estados: Idle, Pushing, Pulling, Backoff, Disabled, etc. |
| `ISyncStatusProvider` | `src/Engram.Sync/ISyncStatusProvider.cs` | Interfaz para exponer estado del sync |
| `IMutationTransport` | `src/Engram.Sync/Transport/IMutationTransport.cs` | Transporte HTTP para push/pull |
| `MutationTransport` | `src/Engram.Sync/Transport/MutationTransport.cs` | Implementación HTTP |
| `EngramSync` | `src/Engram.Sync/EngramSync.cs` | Sync legacy por chunks (git-based, NO mutation-based) |
| `ILocalSyncStore` | `src/Engram.Store/ILocalSyncStore.cs` | Interfaz para store con soporte sync |
| `SqliteStore.EnqueueSyncMutation()` | `src/Engram.Store/SqliteStore.cs:2922` | Crea mutaciones en cada write |

### Ciclo de SyncManager

\`\`\`
RunLoopAsync (SyncManager.cs:135)
  ↓
CycleAsync (cada PollInterval=30s)
  ↓
AcquireSyncLeaseAsync()  ← lease-based (solo 1 proceso a la vez)
  ↓
ReapplyPendingPulledMutationsAsync()
  ↓
PushAsync()  ← envía mutations locales al servidor
  ↓
ReplayDeferredAsync()
  ↓
PullAsync()  ← trae mutations del servidor
  ↓
ReleaseSyncLeaseAsync()
\`\`\`

### Dónde se registra SyncManager

Solo en dos comandos en `Program.cs`:

1. **`engram mcp`** (línea 169-193): Registra SyncManager como `IHostedService` + `ISyncStatusProvider`
2. **`engram serve`** (a través de `EngramServer.Build()`): Similar

**NO se registra en:** `engram save`, `engram search`, `engram context`, `engram stats`, etc.

### Mecanismo de lease

- `AcquireSyncLeaseAsync(targetKey, leaseOwner, duration)` — previene que dos procesos sincronicen simultáneamente
- Si el lease ya está tomado (ej: MCP ya corriendo), el segundo proceso no hace sync
- **Implicación para sync-on-demand:** Si MCP está corriendo con el lease, trigger manual no podría hacer push/pull sin competir

---

## 4. Opciones de Solución Exploradas

### Opción A: Sync-on-demand solo en MCP tools (propuesta original)

- **Scope:** Solo MCP, solo en `mem_search`, `mem_context`, `mem_get_observation`
- **Mecanismo:** Antes de ejecutar la búsqueda, trigger un ciclo de sync (fire-and-forget)
- **Pros:** Simple (~1-2 días), mínimo código
- **Contras:**
  - ❌ No resuelve Problema A (CLI)
  - ❌ No resuelve push inmediato tras `mem_save`
  - ❌ Lease conflict: si MCP ya tiene el lease, trigger manual compite
  - ❌ Latencia adicional en búsqueda (el usuario espera sync antes de resultados)
  - ❌ Solo funciona en MCP (IDE abierto) — no cubre CLI

### Opción B: Sync-on-demand en MCP + CLI manual (`engram sync push`)

- **Scope:** MCP tools + nuevo comando CLI `engram sync push`
- **Mecanismo:** 
  - MCP: trigger sync fire-and-forget antes de búsqueda
  - CLI: `engram sync push` invoca ciclo push (no pull) manualmente
- **Pros:** 
  - ✅ Resuelve Problema A (CLI tiene comando explícito)
  - ✅ Resuelve Problema B parcialmente (push inmediato tras save)
- **Contras:**
  - ⚠️ Lease conflict en MCP
  - ⚠️ Usuario debe saber que existe `engram sync push`
  - ⚠️ No resuelve pull inmediato antes de búsqueda (solo push)

### Opción C: Daemon independiente (systemd/launchd)

- **Scope:** Servicio background que corre siempre
- **Mecanismo:** Instalar servicio systemd que ejecuta SyncManager 24/7
- **Pros:** 
  - ✅ Resuelve ambos problemas completamente
  - ✅ Sync continuo siempre activo
- **Contras:**
  - ❌ No es offline-first puro
  - ❌ Requiere instalación/registro de servicio
  - ❌ Complejidad adicional de empaquetado
  - ❌ Consume recursos aunque no se use Engram

### Opción D: Sync en cada write (push inmediato síncrono)

- **Scope:** Cada `mem_save`, `mem_update`, `mem_delete` hace push inmediato
- **Mecanismo:** Después de `EnqueueSyncMutation()`, invocar `SyncManager.PushAsync()` directamente
- **Pros:** 
  - ✅ Memorias siempre sincronizadas inmediatamente
  - ✅ Sin lease conflict (es el mismo proceso)
- **Contras:**
  - ❌ Latencia en cada write (el usuario espera HTTP call)
  - ❌ Si servidor está caído, el write falla (hoy es offline-first)
  - ❌ Cambio arquitectónico significativo
  - ❌ No aplica a CLI (no tiene SyncManager)

### Opción E (Recomendación inicial): Trigger asíncrono vía dirty signal + CLI hook

- **Scope:** Signal de "dirty" + hook en CLI save
- **Mecanismo:**
  - `EnqueueSyncMutation()` emite evento/signal de "datos sucios"
  - SyncManager (si está corriendo) reacciona inmediatamente (sin esperar PollInterval)
  - CLI save: después de guardar, ejecuta `engram sync push` implícito
  - Búsqueda: trigger pull previo (si pasó cierto tiempo desde último pull)
- **Pros:**
  - ✅ Resuelve ambos problemas
  - ✅ Fire-and-forget (no bloquea al usuario)
  - ✅ Compatible con lease existente (si MCP no está, CLI hace su propio push)
- **Contras:**
  - ⚠️ Requiere diseño cuidadoso del signal/evento
  - ⚠️ Lease conflict necesita manejo explícito

---

## 5. Archivos Relevantes

### Código fuente

| Archivo | Líneas clave | Relevancia |
|---|---|---|
| `src/Engram.Sync/SyncManager.cs` | 1-433 (completo) | Core del background loop. `CycleAsync()` hace push + pull. Métodos `PushAsync()` (260-318) y `PullAsync()` (321-353) son reutilizables. |
| `src/Engram.Sync/SyncManagerConfig.cs` | 1-76 | Config: PollInterval=30s, PushBatchSize=100, PullBatchSize=100, etc. |
| `src/Engram.Sync/SyncPhase.cs` | 1-31 | Estados: Idle, Pushing, Pulling, PushFailed, etc. |
| `src/Engram.Sync/ISyncStatusProvider.cs` | 1-11 | Interfaz para exponer fase, failures, etc. |
| `src/Engram.Sync/Transport/IMutationTransport.cs` | 1-54 | Transport interface para push/pull HTTP |
| `src/Engram.Sync/Transport/MutationTransport.cs` | — | Implementación HTTP de push/pull |
| `src/Engram.Cli/Program.cs` | 169-193 (MCP DI) | Donde se registra SyncManager. Muestra que CLI no lo tiene. |
| `src/Engram.Mcp/EngramTools.cs` | 53-81 (constructor + EmitSyncWarning) | MCP tools. `MemSearch`, `MemSave`, etc. Inyecta `ISyncStatusProvider` para warning. |
| `src/Engram.Store/SqliteStore.cs` | 2922-2947 (EnqueueSyncMutation) | Crea mutaciones locales. Hook point para trigger sync. |
| `src/Engram.Store/SqliteStore.cs` | 620-702 (AddObservationAsync) | Save paths que llaman a EnqueueSyncMutation. |
| `src/Engram.Store/ILocalSyncStore.cs` | — | Interfaz del store local con soporte sync |
| `src/Engram.Server/CloudSyncEndpoints.cs` | 400-474 (HandleSyncStatusAsync) | Endpoint `/sync/status` en servidor |
| `src/Engram.Diagnostics/DiagnosticService.cs` | 72-113 (CheckSyncHealth) | Health check de sync |

### Documentación

| Archivo | Relevancia |
|---|---|
| `docs/architecture/rfc/RFC-003-offline-first-sync-architecture.md` | RFC original del diseño de sync. Describe fase 2 (SyncManager) y fases posteriores. |
| `docs/architecture/adr/ADR-007-sync-blocked-recovery.md` | ADR sobre recover de sync bloqueado |
| `docs/architecture/adr/ADR-008-sync-self-loop-detection.md` | ADR sobre detección de self-loop en sync |
| `docs/decisions/2026-07-16-estado-actual-sync-y-pr-ximos-pasos.md` | Estado del sync a julio 2026 (proveniente de memoria de Engram) |
| `docs/decisions/2026-07-16-eng-459-sin-feedback-cuando-sync-falla.md` | Problema previo: falta de feedback en fallos de sync |
| `docs/BACKLOG.md` (ENG-476, línea 977) | Entrada del feature en backlog |
| `.ai-work/session-2026-07-25-sync-verification/summary.md` | Resumen de la sesión donde se identificó ENG-476 |

---

## 6. Dependencias y Restricciones

### Técnicas

| Aspecto | Restricción |
|---|---|
| **Lease-based sync** | SyncManager usa `AcquireSyncLeaseAsync()` — solo 1 proceso puede sincronizar a la vez. Sync-on-demand debe respetar o liberar leases. |
| **PollInterval (30s)** | Pull y push normales ocurren cada 30s. Sync-on-demand puede acelerar esto. |
| **Backoff exponencial** | Si hay failures, SyncManager entra en backoff (hasta 5min). Sync-on-demand debe respetar o tener lógica separada. |
| **Failure ceiling** | Tras 10 fallos consecutivos, SyncManager se deshabilita permanentemente (hasta reinicio). |
| **Offline-first** | El diseño original asume que el cliente puede estar desconectado. Push síncrono rompe esta premisa. |
| **CLI no tiene SyncManager** | CLI abre store efímero — no hay BackgroundService. Sync-on-demand en CLI requiere o un comando explícito o un store con lógica de sync embebida. |
| **MCP tiene SyncManager** | Si MCP está corriendo, el lease está tomado. Sync-on-demand desde CLI compite con MCP. |
| **HttpStore remote mode** | Cuando se usa `ENGRAM_URL` (modo relay), el store es `HttpStore` — no implementa `ILocalSyncStore`. SyncManager no aplica. |

### De producto

| Aspecto | Restricción |
|---|---|
| **Usuario típico** | Usa MCP desde IDE (VS Code, Cursor). CLI es secundario. |
| **Sincronización eventual** vs **inmediata** | El diseño actual es eventual (30s de ventana). ¿Es aceptable? ¿Cuándo es crítica la inmediatez? |
| **Offline-first** | El cliente DEBE funcionar sin conexión. Si sync-on-demand requiere conexión, no debe bloquear la operación principal. |
| **Feedback al usuario** | Hoy no hay feedback de estado de sync en MCP. Solo un warning inicial en stderr si sync está deshabilitado. |

---

## 7. Riesgos y Descubrimientos

### Riesgos

1. **Lease race condition:** Si MCP y CLI intentan sync simultáneamente, el lease rechaza al segundo. Posible solución: implementar cola o retry corto.
2. **Performance impact en búsqueda:** Si sync-on-demand es síncrono antes de la búsqueda, el usuario experimenta latencia adicional (HTTP call al servidor).
3. **Ampliación de alcance sin querer:** Sync-on-demand podría escalar a "sync automático en cada write", lo cual cambia la arquitectura offline-first.
4. **Caso edge: servidor caído:** Si sync-on-demand falla, ¿cómo se comunica al usuario? ¿Se reintenta en el próximo ciclo normal?

### Descubrimientos durante análisis

1. **`PushAsync()` y `PullAsync()` en `SyncManager.cs` son métodos privados.** Si se quiere reutilizar para sync-on-demand, necesitan ser internal/public o extraerse a un servicio separado.
2. **`SyncManager.CycleAsync()` adquiere y libera lease.** Una versión sync-on-demand podría necesitar su propio manejo de lease o compartir el mismo.
3. **El CLI tiene comando `engram sync status` pero usa HTTP al servidor (`/sync/status`),** no consulta el SyncManager local. Esto es porque el CLI no tiene SyncManager.
4. **El comando `engram sync` tiene subcomandos `export`, `import`, `enroll`, `unenroll`.** Sync-on-demand podría agregar `push` y `pull` aquí.
5. **`MutationTransport` es singleton con HttpClient.** Se puede inyectar en CLI si es necesario para sync-on-demand.
6. **No hay mecanismo de "dirty signal"** — el SyncManager no sabe si hay nuevas mutaciones hasta que su timer lo despierta.
7. **`EnqueueSyncMutation()` se llama desde múltiples paths:** AddObservationAsync (líneas 638, 675, 702), AddPromptAsync (1003), CreateSessionAsync (424, 461), UpdateObservationAsync (781), DeleteObservationAsync (801). Cualquier hook en este método afectaría todas las operaciones.

---

## 8. Preguntas Abiertas para el Humano (CKP-0)

### Críticas (requieren respuesta antes de spec)

1. **¿Cuál es el problema prioritario?**
   - ¿Problema A (memorias CLI nunca sincronizadas)?
   - ¿Problema B (búsqueda stale incluso con MCP corriendo)?
   - ¿Ambos?

2. **¿Quién es el usuario target?**
   - ¿Usuario que solo usa MCP desde IDE (nunca CLI)?
   - ¿Usuario que usa CLI para scripts/automatización?
   - ¿Usuario multi-equipo (centralizado) que necesita sync inmediato?

3. **¿Sincronización eventual o inmediata?**
   - ¿Es aceptable que las memorias se sincronicen en <1 minuto (eventual, mejora sobre 30s)?
   - ¿O necesitamos garantía de que después de `mem_save` la memoria está disponible globalmente (inmediata)?

4. **¿Qué opción preferís explorar?**
   - **A:** Solo trigger en búsqueda MCP (mínimo, cubre Problema B parcialmente)
   - **B:** Trigger + CLI `engram sync push` (cubre ambos problemas)
   - **C:** Daemon independiente (máximo, pero más complejo)
   - **D:** Push en cada write (cambio arquitectónico)
   - **E:** Trigger asíncrono + dirty signal (balanceado)

### Secundarias (pueden responderse durante spec)

5. ¿Qué tolerancia hay a latencia adicional en `mem_search` si sync-on-demand es síncrono?
6. ¿Hay casos de uso donde push inmediato (Opción D) es crítico?
7. ¿El mecanismo de lease actual es adecuado o habría que repensarlo?
8. ¿Debería sync-on-demand reportar feedback al usuario (ej: "Sync: 5 mutations pushed")?

---

## 9. Next Steps Sugeridos

1. ✅ **CKP-0:** Context map completado — responder preguntas críticas arriba
2. ➡️ **forge-arch:** Producir spec.md con solución seleccionada
3. ➡️ **forge-plan:** Plan de implementación

---

## 10. Referencias

- [RFC-003: Offline-First Sync Architecture](docs/architecture/rfc/RFC-003-offline-first-sync-architecture.md)
- [ADR-007: Sync Blocked Recovery](docs/architecture/adr/ADR-007-sync-blocked-recovery.md)
- [ADR-008: Sync Self-Loop Detection](docs/architecture/adr/ADR-008-sync-self-loop-detection.md)
- [BACKLOG.md - ENG-476](docs/BACKLOG.md)
- [Session Summary - Sync Verification 2026-07-25](.ai-work/session-2026-07-25-sync-verification/summary.md)
- [Estado actual sync y próximos pasos](docs/decisions/2026-07-16-estado-actual-sync-y-pr-ximos-pasos.md)
