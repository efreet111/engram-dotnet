# Technical Debt Backlog

> Code review findings — 2026-04-24 (updated audit 2026-06-05)  
> Prioritized by severity. Each item is independent and can be tackled in its own branch.

---

## 🔴 Alta Prioridad

### TD-001 — PostgresStore: God Class (2136 líneas)

**Problema**: `PostgresStore.cs` tiene **2136 líneas** (audit 2026-06; era 1221 en review 2026-04) con migrations, helpers, queries mezclados. SRP violado.

**Propuesta**: Separar con `partial classes`:
```
PostgresStore.cs          → constructor, Dispose, Migrate
PostgresStore.Sessions.cs
PostgresStore.Observations.cs
PostgresStore.Search.cs
PostgresStore.Prompts.cs
PostgresStore.Projects.cs
PostgresStore.Export.cs
PostgresStore.Sync.cs
PostgresStore.Helpers.cs  → Read*, Query*, mappers
```

**Impacto**: Mejora mantenibilidad, facilita code review, reduce merge conflicts.

---

### TD-002 — SqliteStore: God Class (2397 líneas)

**Problema**: Misma situación que PostgresStore pero peor — **2397 líneas** (audit 2026-06; era 1703 en review 2026-04). Es la clase más grande del proyecto.

**Propuesta**: Misma estrategia de partial classes que TD-001.

**Impacto**: Mayor que TD-001 por el tamaño.

---

## 🟡 Media Prioridad

### TD-003 — PostgresStore: Métodos "Async" son sincrónicos

**Problema**: ~15 métodos tienen nombre `*Async()` pero usan APIs sincrónicas. Audit 2026-06: **6 ocurrencias** actuales de `Task.CompletedTask` en PostgresStore (L341, 353, 473, 863, 1085, 1616); SqliteStore tiene 14 adicionales (ver TD-014).

**Archivos**: `PostgresStore.cs` (líneas 205, 219, 231, etc.)

**Propuesta**: Cambiar a `ExecuteNonQueryAsync()`, `ExecuteReaderAsync()`, `ExecuteScalarAsync()` con `async/await` real.

**Impacto**: Mejor uso de thread pool bajo carga concurrente.

---

### TD-004 — PostgresStore: AddObservationAsync — 3 paths anidados

**Problema**: Líneas 279-372 (~90 líneas). Tiene 3 caminos de ejecución (topic_key upsert, hash dedup, fresh insert) anidados con `using` statements. Difícil de seguir y testear.

**Propuesta**: Extraer cada path a un método privado:
- `TryTopicKeyUpsertAsync()` → `long?`
- `TryHashDedupAsync()` → `long?`
- `FreshInsertAsync()` → `long`

**Impacto**: Cada path testeable individualmente, código más legible.

---

### TD-005 — PostgresStore: StatsAsync — 3 queries separadas

**Problema**: Líneas 667-695. Hace 3 queries independientes para 3 COUNTs. 3 round-trips a la DB.

**Propuesta**: Una sola query con subselects:
```sql
SELECT
  (SELECT COUNT(*) FROM sessions) as sessions,
  (SELECT COUNT(*) FROM observations WHERE deleted_at IS NULL) as observations,
  (SELECT COUNT(*) FROM user_prompts) as prompts;
```

**Impacto**: Menos latencia, menos conexiones.

---

### TD-006 — PostgresStore: ImportAsync — N+1 inserts

**Problema**: Líneas 727-822. Cada observación/session/prompt se inserta individualmente en un loop. Con 1000 observaciones son 1000 round-trips.

**Propuesta**: Usar `NpgsqlBinaryImporter` (COPY) o al menos batching con `INSERT INTO ... VALUES (...), (...), (...)`.

**Impacto**: Import 10-50x más rápido para datasets grandes.

---

### TD-007 — PostgresStore: ReadSessionFromOps nombre confuso

**Problema**: Línea 705. `ReadSessionFromObs` toma un `Observation` y lo convierte en `Session`. El nombre está mal — está leyendo sessions, no observations.

**Propuesta**: Renombrar a `SessionFromRow` o `MapSession`.

**Impacto**: Claridad de código.

---

## 🟢 Baja Prioridad

### TD-008 — PostgresStore: AddWithValue warnings CS8604

**Problema**: Líneas 305, 340, 369. `AddWithValue` con valores nullables causa warnings de null safety.

**Propuesta**: Usar patrón explícito:
```csharp
cmd.Parameters.Add(new NpgsqlParameter("@param", NpgsqlDbType.Text) { Value = (object?)value ?? DBNull.Value });
```

**Impacto**: Elimina warnings, más type-safe.

---

### TD-009 — EngramTools.cs: Separar por categoría (1034 líneas, 26 tools)

**Problema**: Las **26** herramientas MCP están en una sola clase (1034 LOC; audit 2026-06).

**Propuesta**: Partial classes por categoría: `EngramTools.Save.cs`, `EngramTools.Search.cs`, `EngramTools.Session.cs`, etc.

**Impacto**: Cosmético, mejora navegación.

---

### TD-010 — StoreReaderAdapter: Null check redundante

**Problema**: Línea 15 de `StoreReaderAdapter.cs`. Con `<Nullable>enable` y tipo `IStore` (no nullable), el check manual es redundante.

**Propuesta**: Sacar el `?? throw`.

**Impacto**: Menos código, confianza en el type system.

---

### TD-011 — GraphConfig: No cachea embedded resource

**Problema**: `LoadEmbeddedGraphJson()` lee del assembly cada vez que se llama `WriteGraphConfig`.

**Propuesta**: Cachear en `static readonly byte[]`:
```csharp
private static readonly byte[] _defaultGraphJson = LoadEmbeddedGraphJson();
```

**Impacto**: Evita reflection + stream copy repetido.

---

### TD-012 — StateFile: Write no atómico

**Problema**: `WriteState` usa `File.WriteAllText`. Si el proceso muere a mitad, el archivo queda corrupto.

**Propuesta**: Write-to-temp + rename:
```csharp
var tmp = path + ".tmp";
File.WriteAllText(tmp, json, Encoding.UTF8);
File.Move(tmp, path, overwrite: true);
```

**Impacto**: Previene corrupción de estado. No crítico para Fase A (--force re-exporta todo).

---

## 🔴 Alta Prioridad (audit 2026-06-05)

### TD-013 — SqliteStore: ApplyPulledMutationAsync stub

**Problema**: `SqliteStore.cs:1910-1916` — `ApplyPulledMutationAsync` retorna `Task.CompletedTask` sin aplicar mutaciones. `SyncManager.PullAsync` (L271) invoca este método; el cursor avanza pero los datos pulled no se persisten localmente.

**Propuesta**: Implementar upsert session/observation/prompt desde `SyncMutation.Payload` con FK deferral. Añadir test en `CloudSyncIntegrationTests`.

**Impacto**: P0 funcional para usuarios con `ENGRAM_SYNC_ENABLED` y SQLite local.

**Origen**: AUD-013 — [`.ai-work/code-audit/`](../../.ai-work/code-audit/)

---

### TD-014 — SqliteStore: Métodos Async sincrónicos (14 ocurrencias)

**Problema**: Misma deuda que TD-003 pero en SqliteStore — 14× `return Task.CompletedTask` incluyendo métodos `ILocalSyncStore`.

**Propuesta**: Priorizar después de TD-003 (Postgres prod).

**Impacto**: P2 mantenimiento / thread pool bajo carga.

---

### TD-015 — Debug residual en Server (resuelto parcialmente)

**Problema**: 7× `Console.Error` en `/sync/enroll` y endpoint `/debug-test` sin tests.

**Estado**: Console.Error eliminados y `/debug-test` removido (2026-06-05). Ver ENG-419.

**Impacto**: P2 limpieza operacional.

---

### TD-016 — Exporter: full scan en vez de incremental (AUD-062)

**Problema**: `Exporter.cs:71` siempre llama `_store.ExportAsync()` (dump completo) y filtra en memoria por project/since. No usa los nuevos métodos `ExportProjectAsync` / `ExportSinceAsync` que existen en IStore, REST y `IObsidianStoreReader`.

**Descubrimiento**: ENG-208 (AUD-062). El export incremental existe en la capa Store y Server pero el Exporter de Obsidian no lo aprovecha.

**Impacto**: P2. Con datasets pequeños (la realidad actual) no duele. Con 10k+ observaciones, el watch mode va a escanear toda la DB cada ciclo aunque solo haya 2 obs nuevas.

**Propuesta**: Unificar flujo: `ObsidianExport` → usar `ExportProjectAsync(project)` cuando hay `--project`, y `ExportSinceAsync(project, lastSeq, limit)` cuando el state tiene `last_seq`.

---

### TD-017 — WatchLoop: prefetch ExportSince sin alimentar Exporter (AUD-063)

**Problema**: `WatchLoop.cs:72-106` llama `ExportSinceAsync` para obtener `NextSeq`/calcular fallback timestamp, pero el export real sigue siendo full scan via Exporter. El prefetch no reduce I/O.

**Relación**: Mismo origen que TD-016. La solución es integral.

**Impacto**: P2. Rendimiento en watch mode con datasets grandes.

---

### TD-018 — MemCurrentProject no expone hint de ambigüedad (PM-7 gap)

**Problema**: `EngramTools.cs:963-1034` (`mem_current_project`) recibe `DetectionResult` del detector pero solo expone `warning` cuando es `null`. El campo `DetectionResult.Error` (p. ej. "Ambiguous project: multiple git repositories found") no se mapea a `warning` ni a ningún campo del JSON de respuesta.

**Descubrimiento**: ENG-208 auditoría PM-7. El detector sí setea `Error` correctamente, pero el wrapper `MemCurrentProject` no lo pasa al JSON de salida.

**Impacto**: P3. El agente MCP recibe `available_projects` correctamente pero sin hint textual. En modo ambiguo, el agente tiene que inferir la ambigüedad desde `project=""` + `available_projects[]`.

---

### TD-019 — Comentarios XML redundantes en parsers CLI (AUD-068)

**Problema**: `SinceArgumentParser.cs`, `WatchIntervalParser.cs`, `WatchLoop.cs`/`WatchConfig` tienen bloques `///` (`<param>`, `<returns>`, `<exception>`) que parafrasean la firma sin aportar información. ~45% de los archivos .cs en src/ tienen al menos un `///` redundante.

**Descubrimiento**: ENG-208 auditoría (AUD-068). Concentrado en parsers nuevos, no es patrón inventado por este feature.

**Impacto**: P3. Ruido cognitivo — lector pierde tiempo filtrando lo obvio. No afecta compilación ni runtime.

**Propuesta**: PR post-merge de ~30-50 líneas menos en parsers afectados. Dejar class-level summary donde aporte (formatos válidos de --since/--interval), podar `<param>`/`<returns>`/`<exception>` espejo de firma. Documentar política en `docs/DEVELOPMENT.md` (tabla cuándo sí/no).

---
