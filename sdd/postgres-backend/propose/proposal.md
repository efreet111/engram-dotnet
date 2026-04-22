# Proposal: PostgreSQL Backend for engram-dotnet

| Campo       | Valor |
|-------------|-------|
| **Change**  | `postgres-backend` |
| **Status**  | Proposed |
| **RFC**     | [RFC-001](../../../docs/rfcs/RFC-001-postgresql-backend.md) |
| **PRD**     | [PRD-001](../../../docs/rfcs/PRD-001-postgresql-backend.md) |
| **Exploration** | [exploration.md](../explore/exploration.md) |

---

## Intent

Add `PostgresStore` as a third `IStore` implementation, enabling `engram serve` to use PostgreSQL as a persistence backend instead of SQLite. This addresses the concurrency limitations of SQLite when 10+ developers write to the shared server simultaneously and enables enterprise-grade backup, HA, and observability.

## Scope

### In Scope

- `PostgresStore.cs` implementing all 22 `IStore` methods
- `StoreConfig` extended with `DbType` and `PgConnectionString`
- Switch in `Program.cs` to select backend via `ENGRAM_DB_TYPE` / `ENGRAM_PG_CONNECTION`
- PostgreSQL schema (idempotent migrations in code)
- FTS via `tsvector` stored generated column + GIN index
- Parity test suite (same tests run against SqliteStore and PostgresStore)
- Docker Compose with PostgreSQL companion
- Documentation: `docs/POSTGRES-SETUP.md`, updated `ARCHITECTURE.md`

### Out of Scope

- pgvector / semantic search (future change)
- Multi-tenant at PG schema level (overkill)
- Replication / HA orchestration (operator responsibility)
- EF Core or any ORM (ADR-001)
- Removing SQLite (it remains the default)

---

## Approach

### D1 — PostgresStore location: Same project (`Engram.Store`)

`PostgresStore.cs` lives in `src/Engram.Store/` alongside `SqliteStore.cs` and `HttpStore.cs`.

**Rationale**: `HttpStore` already follows this pattern. No extra project reference needed. `Normalizers.cs` shared directly. Binary size increase (~2MB for Npgsql) is acceptable.

### D2 — Migrations: Code-first with `IF NOT EXISTS`

Same approach as `SqliteStore.Migrate()`: each `CREATE TABLE`, `CREATE INDEX`, and `ALTER TABLE` uses `IF NOT EXISTS` / `IF NOT EXISTS` equivalent. No Flyway, no EF Migrations.

PostgreSQL equivalent: `CREATE TABLE IF NOT EXISTS`, `DO $$ BEGIN ... EXCEPTION WHEN duplicate_column ... END $$`.

### D3 — Date storage: `TEXT` in v1

Keep dates as `TEXT` (ISO-8601 strings) in v1 — same as SQLite. This:
- Prevents breaking changes to `Models.cs`, `ExportData`, and JSON serialization
- Allows direct migration via export/import without data transformation
- Uses explicit casts in PG queries: `created_at::timestamptz >= NOW() - INTERVAL '15 minutes'`

Future v2 can migrate to `TIMESTAMPTZ` with a data migration script.

### D4 — Data access: Raw Npgsql (no Dapper, no EF Core)

`NpgsqlConnection`, `NpgsqlCommand`, `NpgsqlDataReader` — identical pattern to `SqliteStore` with `SqliteConnection/Command/Reader`. Consistent, auditable, no magic.

### D5 — Connection management: Npgsql connection pool (default)

Use Npgsql's built-in connection pooling. Default `MaxPoolSize=50` is sufficient for team use. Document tuning in `POSTGRES-SETUP.md`.

### D6 — FTS: `tsvector GENERATED ALWAYS AS STORED` + GIN index

```sql
-- Column added to observations table
ALTER TABLE observations ADD COLUMN search_vector tsvector
  GENERATED ALWAYS AS (
    to_tsvector('simple',
      coalesce(title,'') || ' ' || coalesce(content,'') || ' ' ||
      coalesce(tool_name,'') || ' ' || coalesce(type,'') || ' ' ||
      coalesce(project,'') || ' ' || coalesce(topic_key,'')
    )
  ) STORED;

CREATE INDEX idx_obs_fts ON observations USING GIN(search_vector);
```

Search query replaces FTS5 `MATCH` with `plainto_tsquery('simple', @query)`.

### D7 — Dedupe window: `created_at::timestamptz >= NOW() - INTERVAL`

SQLite uses `datetime('now', '-15 minutes')`. PostgreSQL uses `NOW() - INTERVAL '15 minutes'`. The `Normalizers.DedupeWindowExpression` method will be updated to return the interval string, and each store uses its own dialect.

### D8 — Testcontainers for CI

Use `Testcontainers.PostgreSQL` NuGet package for integration tests. Each test run spins up an ephemeral PG container, runs tests, and tears it down. This gives true parity testing without requiring a persistent PG server.

### D9 — Minimum PG version: 15

Required for `GENERATED ALWAYS AS` stored columns. PG 15 is widely available in managed services (Neon, Supabase, RDS, Azure DB).

### D10 — Sync: Keep existing mechanism, no changes

The current `EngramSync` works via `IStore.Export/Import`. Since `PostgresStore` implements `IStore`, sync works automatically. No need for PG logical replication at this stage.

---

## Key Changes by File

| File | Change Type | Description |
|------|------------|-------------|
| `src/Engram.Store/PostgresStore.cs` | **NEW** | ~800-900 lines. All 22 IStore methods, PG schema, migrations. |
| `src/Engram.Store/StoreConfig.cs` | Extend | Add `DbType` enum, `PgConnectionString` property |
| `src/Engram.Store/Normalizers.cs` | Extend | Add `DedupeWindowPg()` helper |
| `src/Engram.Cli/Program.cs` | Extend | 3-line switch: `DbType.Postgres → new PostgresStore(config)` |
| `src/Engram.Store/Engram.Store.csproj` | Extend | Add `Npgsql` NuGet dependency |
| `tests/Engram.Store.Tests/` | Extend | Parametrize tests for both stores |
| `tests/Engram.Postgres.Tests/` | **NEW** | PostgreSQL-specific test project with Testcontainers |
| `docker/docker-compose.yml` | **NEW/Extend** | Add `postgres` service companion |
| `docs/POSTGRES-SETUP.md` | **NEW** | Setup guide for IT |
| `docs/ARCHITECTURE.md` | Update | Add PostgresStore to diagrams and dependency graph |
| `README.md` | Update | Already has `ENGRAM_DB_TYPE` and `ENGRAM_PG_CONNECTION` |

**Unchanged**: `IStore.cs`, `Models.cs`, `HttpStore.cs`, `PassiveCapture.cs`, `EngramMcpServer.cs`, `EngramServer.cs`, `EngramSync.cs`, all MCP tool configs.

---

## Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| FTS behavior differs between FTS5 and tsvector | Medium | High | Parity test suite covering search queries |
| Date string casting fails on malformed ISO dates | Low | High | Validate dates in ImportAsync; reject bad data |
| Npgsql pool exhaustion under load | Low | High | Document pool sizing; set sensible defaults |
| Schema drift between SqliteStore and PostgresStore | Medium | Medium | Shared test suite running against both backends |
| Testcontainers flaky in CI | Low | Medium | PG service in GitHub Actions as fallback |

---

## Open Questions (Resolved)

All open questions from RFC-001 exploration have been resolved with recommendations:

| Question | Decision |
|----------|----------|
| D1: PostgresStore location | Same project (`Engram.Store`) |
| D2: Migrations strategy | Code-first, `IF NOT EXISTS` |
| D3: Date storage format | `TEXT` (ISO-8601) in v1 |
| D4: Data access approach | Raw `Npgsql` |
| D5: Connection management | Npgsql built-in pool |
| D6: FTS implementation | `tsvector GENERATED ALWAYS AS STORED` + GIN |
| D7: Dedupe window expression | `created_at::timestamptz >= NOW() - INTERVAL` |
| D8: Testing in CI | `Testcontainers.PostgreSQL` |
| D9: Minimum PG version | 15 |
| D10: Sync strategy | Keep existing, no changes |

---

## Next Step

Proceed to **spec writing** (`sdd-spec`) to produce formal specifications with requirements, scenarios, and acceptance criteria. Then task breakdown (`sdd-tasks`) before implementation.