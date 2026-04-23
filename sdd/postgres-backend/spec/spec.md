# Specification: PostgreSQL Backend (`postgres-backend`)

## Purpose

Define the behavioral requirements for `PostgresStore`, a third `IStore` implementation backed by PostgreSQL, enabling concurrent team-scale deployments of `engram serve`.

---

## ADDED Requirements

### Requirement: Backend Selection via Environment

The system MUST select the persistence backend based on `ENGRAM_DB_TYPE` and related environment variables.

| `ENGRAM_DB_TYPE` | `ENGRAM_URL` | Result |
|---|---|---|
| unset or `sqlite` | unset | `SqliteStore` |
| unset or `sqlite` | set | `HttpStore` |
| `postgres` | unset | `PostgresStore` |
| `postgres` | set | `HttpStore` (remote takes precedence) |

If `ENGRAM_DB_TYPE=postgres` and `ENGRAM_PG_CONNECTION` is unset or empty, the system MUST exit with an error message: `ENGRAM_PG_CONNECTION is required when ENGRAM_DB_TYPE=postgres`.

#### Scenario: SQLite default (no env vars)

- GIVEN no `ENGRAM_DB_TYPE` and no `ENGRAM_URL`
- WHEN `engram serve` starts
- THEN `SqliteStore` is used with `ENGRAM_DATA_DIR/engram.db`

#### Scenario: PostgreSQL with connection string

- GIVEN `ENGRAM_DB_TYPE=postgres` and `ENGRAM_PG_CONNECTION=Host=localhost;Database=engram;Username=engram;Password=secret`
- WHEN `engram serve` starts
- THEN `PostgresStore` connects to PostgreSQL using the connection string

#### Scenario: PostgreSQL without connection string

- GIVEN `ENGRAM_DB_TYPE=postgres` and `ENGRAM_PG_CONNECTION` unset
- WHEN `engram serve` starts
- THEN the process exits with a non-zero code and message `ENGRAM_PG_CONNECTION is required`

#### Scenario: Remote URL takes precedence over DB type

- GIVEN `ENGRAM_URL=http://server:7437` and `ENGRAM_DB_TYPE=postgres`
- WHEN `engram mcp` starts
- THEN `HttpStore` is used (remote mode overrides local backend selection)

### Requirement: PostgreSQL Schema Initialization

`PostgresStore` MUST create all required tables and indexes on startup using idempotent SQL (`CREATE TABLE IF NOT EXISTS`, `CREATE INDEX IF NOT EXISTS`, safe `ALTER TABLE ADD COLUMN`). The schema MUST be semantically equivalent to `SqliteStore` schema.

#### Scenario: Fresh database

- GIVEN an empty PostgreSQL database
- WHEN `PostgresStore` connects
- THEN all tables (`sessions`, `observations`, `user_prompts`, `sync_chunks`, `sync_state`, `sync_mutations`, `sync_enrolled_projects`) exist with correct columns, types, and constraints

#### Scenario: Existing database (idempotent migration)

- GIVEN a PostgreSQL database with a prior schema version
- WHEN `PostgresStore` connects
- THEN missing columns are added via `ALTER TABLE ADD COLUMN IF NOT EXISTS` equivalent; no data is lost

### Requirement: Full-Text Search via tsvector

`PostgresStore` MUST provide full-text search using PostgreSQL `tsvector` with `GIN` index, functionally equivalent to SQLite FTS5.

The `observations` table MUST have a `search_vector tsvector GENERATED ALWAYS AS STORED` column and a GIN index. Search queries MUST use `plainto_tsquery('simple', @query)` with `ts_rank` for ranking.

All project-scoped queries MUST use index-friendly `WHERE project = @proj` clauses. The schema MUST include a partial GIN index on `search_vector WHERE deleted_at IS NULL` to optimize FTS queries on active observations.

#### Scenario: Single-word search

- GIVEN observations with title "Fixed N+1 query in user list"
- WHEN `SearchAsync("query")` is called
- THEN the observation is returned with a non-zero rank

#### Scenario: Topic-key shortcut search

- GIVEN an observation with `topic_key = "architecture/auth-model"`
- WHEN `SearchAsync("architecture/auth-model")` is called
- THEN topic-key matches appear before FTS rank matches (rank = -1000)

#### Scenario: Empty search results

- GIVEN no observations matching "xyzabc123"
- WHEN `SearchAsync("xyzabc123")` is called
- THEN an empty list is returned (no error)

### Requirement: Deduplication Equivalence

`PostgresStore` MUST implement the same 3-path deduplication logic as `SqliteStore`:
1. topic_key upsert (increment `revision_count`)
2. hash dedup within 15-minute window (increment `duplicate_count`)
3. Fresh insert

The dedup window MUST use `created_at::timestamptz >= NOW() - INTERVAL '{window}'`.

#### Scenario: topic_key upsert

- GIVEN an existing observation with `topic_key = "architecture/auth-model"` in project "mi-api" scope "team"
- WHEN `AddObservationAsync` is called with the same `topic_key`, project, and scope
- THEN the existing observation is updated, `revision_count` is incremented, no new row is inserted

#### Scenario: Hash dedup within window

- GIVEN an observation created 5 minutes ago with `normalized_hash = X`
- WHEN `AddObservationAsync` is called with the same hash, project, scope, type, and title
- THEN `duplicate_count` is incremented on the existing row, no new row is inserted

#### Scenario: Hash dedup outside window

- GIVEN an observation created 20 minutes ago with `normalized_hash = X`
- WHEN `AddObservationAsync` is called with the same hash
- THEN a new observation is inserted (window expired)

### Requirement: Date Storage as TEXT

In v1, `PostgresStore` MUST store dates as `TEXT` (ISO-8601 strings), identical to `SqliteStore`. This ensures `ExportData` format compatibility and zero changes to `Models.cs`.

Queries requiring date arithmetic MUST cast explicitly: `created_at::timestamptz`.

#### Scenario: Export from SQLite, import to PostgreSQL

- GIVEN an `ExportData` JSON produced by `SqliteStore.ExportAsync()`
- WHEN `PostgresStore.ImportAsync()` is called
- THEN all sessions, observations, and prompts are imported with correct timestamps

### Requirement: StoreConfig Extension

`StoreConfig` MUST add:
- `DbType` property (`enum StoreDbType { Sqlite, Postgres }`) with default `Sqlite`
- `PgConnectionString` property read from `ENGRAM_PG_CONNECTION`
- Computed `IsPostgres` property

#### Scenario: SQLite by default

- GIVEN no `ENGRAM_DB_TYPE` env var
- WHEN `StoreConfig.FromEnvironment()` is called
- THEN `DbType` is `Sqlite` and `IsPostgres` is `false`

#### Scenario: PostgreSQL configuration

- GIVEN `ENGRAM_DB_TYPE=postgres` and `ENGRAM_PG_CONNECTION=Host=localhost;Database=engram;Username=engram;Password=secret`
- WHEN `StoreConfig.FromEnvironment()` is called
- THEN `DbType` is `Postgres`, `IsPostgres` is `true`, and `PgConnectionString` is set

### Requirement: Cross-Namespace Search

`SearchAsync(query, projects, opts)` MUST search across all provided project namespaces and merge results (dedup by ID, topic-key hits first, then by rank).

#### Scenario: Team + personal search

- GIVEN observations in `team/mi-api` and `victor.silgado/mi-api`
- WHEN `SearchAsync("auth", ["team/mi-api", "victor.silgado/mi-api"], opts)` is called
- THEN results from both namespaces are merged, deduplicated, topic-key matches ranked first

### Requirement: CLI Stats Output Distinction

`engram stats` MUST display the backend type in its output when using PostgreSQL.

#### Scenario: Stats with SQLite

- GIVEN `ENGRAM_DB_TYPE` is unset (SQLite)
- WHEN `engram stats` runs
- THEN output shows `Database: {DataDir}/engram.db`

#### Scenario: Stats with PostgreSQL

- GIVEN `ENGRAM_DB_TYPE=postgres`
- WHEN `engram stats` runs
- THEN output shows `Database: PostgreSQL ({host})`

### Requirement: Shared-Table Scalability

The PostgreSQL schema MUST use shared tables (one `observations` table for all projects) filtered by the `project` column. This is consistent with the SQLite schema and the existing `IStore` interface.

Per-project table separation, schema-per-tenant, or table partitioning MUST NOT be implemented in v1. These MAY be considered as future optimizations if `mem_stats` or monitoring indicates queries are slow beyond 5M observations.

The schema MUST include indexes that optimize project-scoped queries:
- `idx_obs_project ON observations(project)` — B-tree for project filtering
- `idx_obs_created ON observations(created_at DESC)` — sorted retrieval per project
- Partial GIN index `WHERE deleted_at IS NULL` — active-observation FTS only

#### Scenario: Query performance with 1M+ observations across 10 projects

- GIVEN 1,000,000 observations across 10 projects
- WHEN `SearchAsync("auth", SearchOptions { Project = "team/mi-api" })` is called
- THEN results are returned in under 200ms (p95) using project index + GIN scan

#### Scenario: Recent observations per project

- GIVEN 500,000 observations across multiple projects
- WHEN `RecentObservationsAsync("team/mi-api", null, 20)` is called
- THEN only the 20 most recent observations for that project are returned using index `idx_obs_project`

---

## MODIFIED Requirements

### Requirement: Store Selection in CLI

The CLI store selection MUST support three backends: SQLite (default), PostgreSQL, and HTTP (remote).

(Previously: only two backends — SQLite and HTTP)

#### Scenario: Local SQLite mode

- GIVEN no `ENGRAM_URL` and `ENGRAM_DB_TYPE` unset or `sqlite`
- WHEN `engram mcp` or `engram serve` starts
- THEN `SqliteStore` is instantiated

#### Scenario: PostgreSQL mode

- GIVEN `ENGRAM_DB_TYPE=postgres` and `ENGRAM_PG_CONNECTION` is set, no `ENGRAM_URL`
- WHEN `engram serve` starts
- THEN `PostgresStore` is instantiated

#### Scenario: Remote HTTP mode (unchanged)

- GIVEN `ENGRAM_URL` is set
- WHEN `engram mcp` starts
- THEN `HttpStore` is instantiated

---