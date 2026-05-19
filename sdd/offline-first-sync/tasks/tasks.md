# Tasks: Phase 4 — Observability for Offline-First Sync

## Phase 1: Infrastructure (1h)

- [x] 1.1 Create `src/Engram.Sync/SyncMetrics.cs` — sealed class con `Interlocked` counters: `_totalPushed`, `_totalPulled`, `_totalFailures`, `_lastSyncAt`. Métodos: `IncrementPushed`, `IncrementPulled`, `IncrementFailures`, `MarkSyncAt`, `RecordError`, snapshot via `GetSnapshot()` que devuelve `SyncMetricsSnapshot` record.
- [x] 1.2 Create `src/Engram.Sync/ISyncStatusProvider.cs` — interfaz con `Phase`, `IsEnabled`, `ConsecutiveFailures`, `BackoffUntil`, `Metrics` properties. SyncManager implementa la interfaz.
- [x] 1.3 Register `SyncMetrics` como singleton y `ISyncStatusProvider` con implementación en `SyncManager` en `EngramServer.Build()` DI (junto con `SyncManagerConfig`, `IMutationTransport`, `IHttpClientFactory`, `SyncManager` como hosted service).

## Phase 2: Endpoint (1h)

- [x] 2.1 Move `HandleSyncStatus` stub de `EngramServer.cs:464` a `CloudSyncEndpoints.cs` como `HandleSyncStatusAsync`. Route `GET /sync/status` registrada via `MapCloudSyncRoutes`.
- [x] 2.2 Implement `HandleSyncStatusAsync` — consulta `ISyncStatusProvider` (nullable via `GetService`), `ILocalSyncStore.GetSyncStateAsync` (cursors + pending), `ICloudMutationStore.GetEnrolledProjectsAsync` (enrolled). Retorna `SyncStatusResponse` JSON.
- [x] 2.3 Add `StatusCursorBody`, `StatusHealthBody`, `StatusCountsBody`, `SyncStatusResponse` records en `src/Engram.Server/Dtos/MutationDtos.cs` con `snake_case` JSON property names.
- [x] 2.4 Reemplazar stub en `EngramServer.cs` — removido `MapGet("/sync/status", ...)` inline y método `HandleSyncStatus`. Ruta delegada a `MapCloudSyncRoutes()`. Agregado registro de `SyncManager` + dependencias en `Build()`.

## Phase 3: Structured Logging (0.5h)

- [x] 3.1 Replace all `ILogger.Log*` calls en `SyncManager.cs` con `LoggerMessage` source-gen — definir `Action<ILogger, ...>` delegates estáticos para: `CycleStart`, `CycleComplete`, `CycleFailed`, `PushBatch`, `PullBatch`, `DeferredReplay`, `PhaseTransition`, `PanicExit`. Usar `EventId` con números 2000-2007.
- [x] 3.2 Inject `SyncMetrics` en constructor de `SyncManager` y actualizar counters: `IncrementPushed` en PushAsync, `IncrementPulled` en PullAsync, `IncrementFailures` en catch de CycleAsync, `MarkSyncAt` al completar ciclo.

## Phase 4: CLI (0.5h)

- [x] 4.1 En `src/Engram.Cli/Program.cs`, crear `sync status` subcommand que llame a `GET /sync/status` HTTP call. Deserializar a `JsonElement` y mostrar output formateado como tabla.
- [x] 4.2 Agregar `Option<bool>("--json")` a `sync status` command. Si `--json`, imprimir JSON sin formato (machine-readable). Si no hay server, mostrar error claro: "No se pudo conectar al servidor — ¿está engram server corriendo?"
- [x] 4.3 Test: `sync status` muestra output formateado correctamente y `--json` imprime JSON válido.

## Phase 5: Documentation (0.5h)

- [x] 5.1 Crear `docs/SYNC-SETUP.md` con: prerequisitos (PostgreSQL en TrueNAS), tabla de vars `ENGRAM_SYNC_*`, setup paso a paso (configurar PostgreSQL → env vars → correr server → enroll projects → `sync status`), troubleshooting (sync no arranca, 409 pause, failure ceiling).
- [x] 5.2 Actualizar `docs/OFFLINE-FIRST-SYNC.md` — cambiar Phase 3 & 4 status de "🔴 Pending" a "✅ Complete".

## Phase 6: Testing

- [ ] 6.1 Unit test: `SyncMetrics` — `RecordPush`/`RecordPull` incrementan counters, `RecordFailure`, `GetSnapshot` refleja valores, thread-safety con `Parallel.For`.
- [ ] 6.2 Integration test: `GET /sync/status` devuelve 200 + JSON con estructura correcta (cursor, health, counts, enrolled_projects). Testear con PostgresStore via Testcontainers.
- [ ] 6.3 Integration test: CLI `sync status` conecta a server real y parsea respuesta. Testear con `HttpClient` contra WebApplicationFactory.
