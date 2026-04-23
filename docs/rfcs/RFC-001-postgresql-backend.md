[← Volver al README](../../README.md) | [← Volver a docs](../ROADMAP.md)

# RFC-001 — PostgreSQL Backend para engram-dotnet

| Campo       | Valor |
|-------------|-------|
| **RFC**     | 001 |
| **Título**  | PostgreSQL Backend (`PostgresStore`) |
| **Estado**  | Draft |
| **Autor**   | Equipo engram-dotnet |
| **Creado**  | 2026-04-20 |
| **Actualizado** | 2026-04-20 |

---

## Índice

- [Motivación](#motivación)
- [Contexto técnico actual](#contexto-técnico-actual)
- [Alcance](#alcance)
- [Propuesta de diseño](#propuesta-de-diseño)
- [Decisiones clave](#decisiones-clave)
- [Impacto en componentes existentes](#impacto-en-componentes-existentes)
- [Migración de datos](#migración-de-datos)
- [Riesgos y mitigaciones](#riesgos-y-mitigaciones)
- [Alternativas descartadas](#alternativas-descartadas)
- [Criterios de éxito](#criterios-de-éxito)
- [Trabajo pendiente antes de spec](#trabajo-pendiente-antes-de-spec)

---

## Motivación

`engram-dotnet` está pensado para correr como **servidor compartido para equipos de desarrollo**. En ese escenario, SQLite presenta limitaciones estructurales que se vuelven evidentes a escala:

### Limitaciones de SQLite en modo servidor

| Limitación | Impacto concreto |
|------------|-----------------|
| **Concurrencia por escritura** | SQLite solo permite una escritura simultánea (lock a nivel de archivo). Con 5+ devs guardando memorias en paralelo, aparecen `SQLITE_BUSY` y reintentos. El WAL mode mitiga pero no resuelve. |
| **Sin conexiones remotas nativas** | SQLite es un archivo local. En modo equipo, `HttpStore` hace HTTP → el servidor tiene un solo `SqliteStore`. Ese singleton se convierte en cuello de botella. |
| **Sin pool de conexiones real** | Aunque `Microsoft.Data.Sqlite` soporta pooling básico, no hay un pool comparable al de Npgsql (multi-thread, multi-connection). |
| **FTS5 vs `pg_trgm` / `tsvector`** | FTS5 es excelente para casos de uso de texto completo, pero `tsvector` de PostgreSQL ofrece ranking más sofisticado, indexado por idioma, y mejor performance en tablas grandes. |
| **Backup y HA** | SQLite requiere estrategias artesanales (dump, cp). PostgreSQL tiene streaming replication, pg_dump, point-in-time recovery, y se integra con cualquier proveedor managed (Neon, Supabase, RDS, Azure DB). |
| **Observability** | No hay métricas nativas de query performance en SQLite. PostgreSQL tiene `pg_stat_statements`, `EXPLAIN ANALYZE`, y se integra con Prometheus via `postgres_exporter`. |

### Cuándo importa

Con **menos de 10 devs** en modo servidor compartido, SQLite funciona bien. El problema aparece cuando:
- El equipo crece a 15-30+ devs activos
- Hay múltiples proyectos guardando memorias simultáneamente
- Se quiere alta disponibilidad (failover, réplica)
- Se requiere backup automático con RPO bajo

### Por qué no migrar todo a PostgreSQL

SQLite sigue siendo la opción correcta para:
- **Modo local** (una instancia por dev, sin servidor)
- **Entornos sin infraestructura** (dev personal, CI, pruebas)
- **Simplicidad de onboarding** (binario único, zero config)

La propuesta mantiene **ambos backends** — PostgreSQL como opción explícita para deployments de servidor de equipo.

---

## Contexto técnico actual

### Strategy Pattern existente

El codebase ya usa **Strategy Pattern** para el backend de persistencia:

```
IStore (interfaz — 22 métodos)
  ├── SqliteStore   (implementación local — Microsoft.Data.Sqlite)
  └── HttpStore     (proxy HTTP — team mode, delega a servidor remoto)
```

El switch ocurre en `Program.cs`:
```csharp
IStore store = config.IsRemote
    ? new HttpStore(config)
    : new SqliteStore(config);
```

Agregar `PostgresStore` es **agregar una tercera implementación de `IStore`** sin tocar la interfaz, los handlers, ni el protocolo MCP.

### Superficie de IStore

`IStore` expone 22 métodos agrupados en:

| Grupo | Métodos | Complejidad de port |
|-------|---------|---------------------|
| Sessions | `Create`, `End`, `Get`, `Recent` | Baja — SQL directo |
| Observations | `Add`, `Get`, `Recent`, `Update`, `Delete` | **Alta** — deduplicación + FTS |
| Search | `Search` (2 overloads), `Timeline` | **Alta** — FTS5 → `tsvector` |
| Prompts | `Add`, `Recent`, `Search` | Media — FTS en prompts |
| Context & Stats | `FormatContext` (2 overloads), `Stats` | Media — queries de agregación |
| Export / Import | `Export`, `Import` | Baja — bulk INSERT/SELECT |
| Projects | `MergeProjects` | Baja — UPDATE masivo |
| Sync | `GetSyncedChunks`, `RecordSyncedChunk` | Baja — tabla simple |

### El punto crítico: FTS5 → PostgreSQL

El mayor trabajo técnico es traducir las **consultas FTS5 de SQLite a `tsvector` de PostgreSQL**.

SQLite FTS5 actual:
```sql
SELECT o.* FROM observations_fts fts
JOIN observations o ON o.id = fts.rowid
WHERE observations_fts MATCH @fts
ORDER BY fts.rank
```

PostgreSQL equivalente:
```sql
SELECT o.*, ts_rank(search_vector, query) as rank
FROM observations o,
     plainto_tsquery('simple', @fts) query
WHERE search_vector @@ query
  AND deleted_at IS NULL
ORDER BY rank DESC
```

La columna `search_vector` sería un `tsvector` generado automáticamente con un trigger o columna generada:
```sql
ALTER TABLE observations ADD COLUMN search_vector tsvector
    GENERATED ALWAYS AS (
        to_tsvector('simple',
            coalesce(title, '') || ' ' ||
            coalesce(content, '') || ' ' ||
            coalesce(tool_name, '') || ' ' ||
            coalesce(type, '') || ' ' ||
            coalesce(project, '') || ' ' ||
            coalesce(topic_key, '')
        )
    ) STORED;

CREATE INDEX idx_obs_fts ON observations USING GIN(search_vector);
```

### Deduplicación — ventana de tiempo

La lógica de dedup usa `datetime('now', '-15 minutes')` de SQLite. En PostgreSQL:
```sql
-- SQLite
AND datetime(created_at) >= datetime('now', @window)

-- PostgreSQL
AND created_at >= NOW() - INTERVAL '15 minutes'
```

Además, el tipo de `created_at` cambiaría de `TEXT` (ISO-8601 string) a `TIMESTAMPTZ` nativo — lo que mejora performance y correctness.

---

## Alcance

### Dentro del alcance

- [ ] Nuevo package `Engram.Store.Postgres` (o clase `PostgresStore` en `Engram.Store`)
- [ ] `PostgresStore : IStore` con los 22 métodos
- [ ] Schema SQL equivalente para PostgreSQL (migrations via código, no Flyway/EF Migrations)
- [ ] `StoreConfig` extendido con `ENGRAM_DB_TYPE` y `ENGRAM_PG_CONNECTION`
- [ ] Switch en `Program.cs` para `serve` y `mcp`
- [ ] Tests de paridad: `PostgresStore` pasa los mismos tests que `SqliteStore` (parametrizados)
- [ ] Migración `export → import` de SQLite a PostgreSQL (via JSON export existente)
- [ ] Documentación: `docs/POSTGRES-SETUP.md` (guía para IT)
- [ ] Actualización de `docs/ARCHITECTURE.md`
- [ ] Actualización de `docker/` config para incluir PostgreSQL como servicio companion

### Fuera del alcance (explícito)

- **EF Core / ORM**: se mantiene SQL directo (ver [ADR-001](../adr/ADR-001-no-orm.md))
- **pgvector / embeddings semánticos**: separado como mejora futura (Python port)
- **Multi-tenant a nivel de schema PG**: overkill para este caso de uso
- **Réplica / HA automática**: fuera del scope de la librería (responsabilidad del operador)
- **Sync git-friendly con PostgreSQL**: el mecanismo de sync actual (chunks gzip) no cambia

---

## Propuesta de diseño

### Estructura de packages

```
src/
├── Engram.Store/
│   ├── IStore.cs              ← Sin cambios
│   ├── SqliteStore.cs         ← Sin cambios
│   ├── HttpStore.cs           ← Sin cambios
│   ├── PostgresStore.cs       ← NUEVO
│   ├── StoreConfig.cs         ← Extender con DbType y PgConnection
│   ├── Models.cs              ← Sin cambios (excepto fechas — ver decisión)
│   ├── Normalizers.cs         ← Sin cambios
│   └── PassiveCapture.cs      ← Sin cambios
```

> **Opción alternativa**: `src/Engram.Store.Postgres/` como proyecto separado.
> Tradeoff: proyecto separado = más limpio pero requiere referencia extra. Proyecto mismo = simpler.
> **Decisión pendiente** — ver [Decisiones clave](#decisiones-clave).

### Variables de entorno nuevas

| Variable | Default | Descripción |
|----------|---------|-------------|
| `ENGRAM_DB_TYPE` | `sqlite` | Backend: `sqlite` \| `postgres` |
| `ENGRAM_PG_CONNECTION` | — | Connection string de PostgreSQL. Ej: `Host=localhost;Port=5432;Database=engram;Username=engram;Password=secret` |

### Switch en Program.cs

```csharp
IStore store = (config.DbType, config.IsRemote) switch
{
    (_, true)              => new HttpStore(config),
    ("postgres", false)    => new PostgresStore(config),
    _                      => new SqliteStore(config),
};
```

### Schema PostgreSQL

```sql
-- sessions
CREATE TABLE IF NOT EXISTS sessions (
    id          TEXT PRIMARY KEY,
    project     TEXT NOT NULL,
    directory   TEXT NOT NULL,
    started_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    ended_at    TIMESTAMPTZ,
    summary     TEXT
);

-- observations
CREATE TABLE IF NOT EXISTS observations (
    id               BIGSERIAL PRIMARY KEY,
    sync_id          TEXT UNIQUE,
    session_id       TEXT NOT NULL REFERENCES sessions(id),
    type             TEXT NOT NULL,
    title            TEXT NOT NULL,
    content          TEXT NOT NULL,
    tool_name        TEXT,
    project          TEXT,
    scope            TEXT NOT NULL DEFAULT 'project',
    topic_key        TEXT,
    normalized_hash  TEXT,
    revision_count   INTEGER NOT NULL DEFAULT 1,
    duplicate_count  INTEGER NOT NULL DEFAULT 1,
    last_seen_at     TIMESTAMPTZ,
    created_at       TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at       TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    deleted_at       TIMESTAMPTZ,
    -- FTS column generada automáticamente
    search_vector    tsvector GENERATED ALWAYS AS (
        to_tsvector('simple',
            coalesce(title, '') || ' ' ||
            coalesce(content, '') || ' ' ||
            coalesce(tool_name, '') || ' ' ||
            coalesce(type, '') || ' ' ||
            coalesce(project, '') || ' ' ||
            coalesce(topic_key, '')
        )
    ) STORED
);

CREATE INDEX IF NOT EXISTS idx_obs_session   ON observations(session_id);
CREATE INDEX IF NOT EXISTS idx_obs_type      ON observations(type);
CREATE INDEX IF NOT EXISTS idx_obs_project   ON observations(project);
CREATE INDEX IF NOT EXISTS idx_obs_created   ON observations(created_at DESC);
CREATE INDEX IF NOT EXISTS idx_obs_scope     ON observations(scope);
CREATE INDEX IF NOT EXISTS idx_obs_topic     ON observations(topic_key, project, scope, updated_at DESC);
CREATE INDEX IF NOT EXISTS idx_obs_dedupe    ON observations(normalized_hash, project, scope, type, title, created_at DESC);
CREATE INDEX IF NOT EXISTS idx_obs_deleted   ON observations(deleted_at);
CREATE INDEX IF NOT EXISTS idx_obs_fts       ON observations USING GIN(search_vector);
```

### Manejo de fechas: TEXT vs TIMESTAMPTZ

SQLite guarda fechas como `TEXT` (ISO-8601). Los modelos C# las tienen como `string` para round-trip perfecto.

PostgreSQL tiene `TIMESTAMPTZ` nativo. Opciones:

**Opción A — Mantener `string` en modelos, convertir en queries**
- Pro: zero cambios en `Models.cs`, `HttpStore`, serialización JSON
- Contra: overhead de parsing en queries; menos idiomático en PG

**Opción B — Cambiar modelos a `DateTimeOffset?`**  
- Pro: idiomático, mejor performance, correcto timezone handling
- Contra: cambio de interfaz pública, breaking change en `ExportData`, serialización JSON diferente

> **Recomendación**: Opción A para v1 (velocidad, sin breaking changes). Opción B como mejora futura planificada.

---

## Decisiones clave

Las siguientes decisiones deben resolverse antes de escribir specs formales:

### D1 — ¿`PostgresStore` en `Engram.Store` o en `Engram.Store.Postgres`?

| Criterio | Mismo proyecto | Proyecto separado |
|----------|---------------|-------------------|
| Referencia en CLI | Simple | Requiere `<ProjectReference>` extra |
| Tamaño del binario | Npgsql siempre incluido | Solo si se referencia |
| Claridad de separación | Mezclado con SQLite | Clean |
| Costo | Bajo | Bajo |

**Recomendación**: mismo proyecto por ahora, separar si el binario crece demasiado.

### D2 — ¿Migrations con código propio o Flyway/EF Migrations?

- EF Migrations: overhead de setup, introduce EF Core
- Flyway: herramienta externa, complica el binario self-contained
- **Código propio (como SqliteStore)**: `IF NOT EXISTS` + `ALTER TABLE ADD COLUMN IF NOT EXISTS` — idempotente, sin dependencia externa

**Recomendación**: código propio, igual que SQLite.

### D3 — ¿`TIMESTAMPTZ` o `TEXT` para fechas?

Ver sección anterior. **Recomendación**: `TEXT` en v1 para no romper interfaz.

### D4 — ¿Npgsql o Dapper?

- Raw Npgsql (`NpgsqlCommand`, `NpgsqlDataReader`): idéntico al patrón actual de SQLite, zero overhead
- Dapper: micro-ORM, simplifica mapeo, añade dependencia
- EF Core: descartado (ver ADR-001)

**Recomendación**: Npgsql directo, mismo patrón que `SqliteStore`. Consistencia > comodidad.

### D5 — ¿Cómo manejar `normalized_hash` en el dedupe window?

SQLite usa `datetime('now', '-15 minutes')` como string. PostgreSQL usa `NOW() - INTERVAL '15 minutes'`. Con fechas como `TEXT`, hay que hacer `created_at::timestamptz >= NOW() - INTERVAL '15 minutes'` o comparar strings ISO-8601.

**Recomendación**: cast explícito `created_at::timestamptz` en PostgreSQL — funciona con strings ISO-8601 válidos.

---

## Impacto en componentes existentes

### `Engram.Store` — alto impacto
- `StoreConfig.cs`: agregar `DbType`, `PgConnectionString`
- `PostgresStore.cs`: nueva clase, ~700-900 líneas estimadas
- `SqliteStore.cs`: **sin cambios**
- `IStore.cs`: **sin cambios**
- `Models.cs`: **sin cambios** (decisión D3)
- `Normalizers.cs`: posible adición de helpers PG-específicos (dedupe window)

### `Engram.Cli` — impacto bajo
- `Program.cs`: extender el switch de store selection (3 líneas)
- Nuevo flag opcional en `serve`: `--db-type postgres` (documentación, no funcional)

### `Engram.Server` — **sin cambios**
Recibe `IStore` por inyección. No sabe qué implementación está corriendo.

### `Engram.Mcp` — **sin cambios**
Mismo que Server — Strategy Pattern hace su trabajo.

### `Engram.Sync` — impacto bajo
El sync trabaja con `IStore.Export/Import` y `IStore.GetSyncedChunks/RecordSyncedChunk`. Si `PostgresStore` implementa esos métodos correctamente, sync funciona sin cambios.

### `tests/` — impacto medio-alto
- Tests parametrizados: misma suite corriendo contra `SqliteStore` y `PostgresStore`
- Nuevo fixture para PostgreSQL (Testcontainers.PostgreSQL o PostgreSQL embebido)
- **No** se elimina la suite de SQLite — sigue siendo el backend primario

### `config/cursor/` y `config/vscode/` — **sin cambios**
Los agentes y herramientas MCP no cambian. El protocolo MCP es agnóstico al backend.

### `docker/` — impacto medio
- Agregar `docker-compose` con `postgres` como servicio companion
- Actualizar `docker/README.md` con opción PG
- Actualizar `docs/DEPLOYMENT.md` con setup PG

### `docs/ARCHITECTURE.md` — impacto bajo
- Actualizar diagrama de dependencias
- Agregar sección de configuración de backends

---

## Migración de datos

La ruta de migración **no requiere código nuevo** — usa el mecanismo de Export/Import ya existente:

```
1. Parar el servidor engram
2. engram export --output backup.json          # usa SqliteStore
3. ENGRAM_DB_TYPE=postgres ENGRAM_PG_CONNECTION=... engram import backup.json
4. Verificar con: engram stats
5. Apuntar ENGRAM_DB_TYPE=postgres en el servidor
6. Arrancar el servidor
```

Esta es la ruta más simple. No requiere migración online ni downtime complejo.

Para rollback:
```
1. ENGRAM_DB_TYPE=sqlite engram import backup.json   # volver a SQLite si falla
```

---

## Riesgos y mitigaciones

| Riesgo | Probabilidad | Impacto | Mitigación |
|--------|-------------|---------|------------|
| Comportamiento FTS diferente entre SQLite FTS5 y PG tsvector | Media | Alto | Tests de paridad exhaustivos; documentar diferencias conocidas |
| Ranking de resultados diferente | Alta | Medio | Los tests de paridad verifican contenido, no posición exacta de ranking |
| Fechas ISO-8601 string → cast PG falla para fechas legacy | Baja | Alto | Validación de datos antes de migración; normalización en `ImportAsync` |
| Npgsql connection pool agotado bajo carga | Baja | Alto | Configurar pool size en connection string; `MaxPoolSize=50` |
| `PostgresStore` desincronizado de `SqliteStore` al agregar features | Media | Medio | Suite de tests parametrizados obligatoria; ambos stores deben pasar |

---

## Alternativas descartadas

### A1 — Usar EF Core para ambos backends
- **Razón de descarte**: EF Core introduce abstracción sobre schema que dificulta mantener paridad con el Go original. Además, el equipo ya tiene SQLite funcionando con SQL directo y es eficiente.

### A2 — Reemplazar SQLite completamente por PostgreSQL
- **Razón de descarte**: rompe el caso de uso de modo local (una instancia por dev, zero deps). El binario self-contained pierde su ventaja principal si requiere un servidor PG corriendo.

### A3 — SQLite con Litestream (replicación a S3)
- **Razón de descarte**: mitiga el problema de backup pero no resuelve la concurrencia de escritura con equipos grandes. Es una mejora operacional, no arquitectural.

### A4 — Usar `pg_trgm` en vez de `tsvector` para FTS
- **Razón de descarte**: `pg_trgm` es búsqueda por similitud de texto (trigramas), no full-text search. Para el caso de uso de engram (buscar por palabras clave en observaciones), `tsvector` es más adecuado y performante.

---

## Criterios de éxito

1. ✅ `PostgresStore` implementa los 22 métodos de `IStore`
2. ✅ Suite de tests parametrizados: 100% de los tests de `SqliteStore` pasan también con `PostgresStore`
3. ✅ Migración SQLite → PostgreSQL funciona con el flujo export/import sin pérdida de datos
4. ✅ Configuración via `ENGRAM_DB_TYPE=postgres` + `ENGRAM_PG_CONNECTION` sin cambios en código de agentes o MCP
5. ✅ Docker Compose con PostgreSQL companion disponible para equipos
6. ✅ `docs/POSTGRES-SETUP.md` completa para IT

---

## Trabajo pendiente antes de spec

Los siguientes puntos necesitan decisión explícita **antes** de escribir los specs formales:

- [ ] **D1** — Confirmar si `PostgresStore` va en `Engram.Store` o en `Engram.Store.Postgres`
- [ ] **D3** — Confirmar si se cambian fechas a `TIMESTAMPTZ` o se mantienen como `TEXT` en v1
- [ ] **Testcontainers**: evaluar si usar `Testcontainers.PostgreSQL` para tests de integración automáticos en CI, o un servidor PG manual
- [ ] **Versión PG mínima**: definir versión mínima soportada (recomendación: PG 15 por `GENERATED ALWAYS AS` en columnas `tsvector`)
- [ ] **Sync con PostgreSQL**: el sync actual exporta mutations. Con PG hay opción de usar logical replication. ¿Mantener el sync actual o aprovechar PG?

---

*Este RFC describe la intención y el panorama técnico. Los specs formales y el task breakdown se escriben en una fase posterior.*
