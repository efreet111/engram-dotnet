# Technical Debt Backlog

> Hallazgos de revisión de código — 2026-04-24
> Priorizados por severidad. Cada ítem es independiente y puede tacklearse en su propia rama.

---

## 🔴 Alta Prioridad

### TD-001 — PostgresStore: God Class (1221 líneas)

**Problema**: `PostgresStore.cs` tiene 1221 líneas con 22+ métodos, migrations, helpers, queries, todo mezclado. SRP violado.

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

### TD-002 — SqliteStore: God Class (1703 líneas)

**Problema**: Misma situación que PostgresStore pero peor — 1703 líneas. Es la clase más grande del proyecto.

**Propuesta**: Misma estrategia de partial classes que TD-001.

**Impacto**: Mayor que TD-001 por el tamaño.

---

## 🟡 Media Prioridad

### TD-003 — PostgresStore: Métodos "Async" son sincrónicos

**Problema**: ~15 métodos tienen nombre `*Async()` pero usan `ExecuteNonQuery()`, `ExecuteReader()`, `ExecuteScalar()` sincrónicos. Solo envuelven el resultado en `Task.CompletedTask` o `Task.FromResult()`.

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

### TD-009 — EngramTools.cs: Separar por categoría (638 líneas)

**Problema**: Las 15 herramientas MCP están en una sola clase.

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
