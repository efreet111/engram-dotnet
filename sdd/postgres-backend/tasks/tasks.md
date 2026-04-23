# Tasks: PostgreSQL Backend (`postgres-backend`)

## Phase 1: Foundation — Config and Schema

- [ ] 1.1 Add `Npgsql` NuGet dependency to `src/Engram.Store/Engram.Store.csproj`
- [ ] 1.2 Add `StoreDbType` enum (`Sqlite`, `Postgres`) and properties (`DbType`, `PgConnectionString`, `IsPostgres`) to `src/Engram.Store/StoreConfig.cs`
- [ ] 1.3 Update `StoreConfig.FromEnvironment()` to read `ENGRAM_DB_TYPE` and `ENGRAM_PG_CONNECTION`
- [ ] 1.4 Create `src/Engram.Store/PostgresStore.cs` with constructor, `Dispose()`, and schema migration (`Migrate()`) using `CREATE TABLE IF NOT EXISTS` / safe `ALTER TABLE`
- [ ] 1.5 Add `search_vector tsvector GENERATED ALWAYS AS STORED` column, GIN index, and partial GIN index `WHERE deleted_at IS NULL` to optimize FTS on active observations
- [ ] 1.6 Add `Testcontainers.PostgreSQL` NuGet to a new `tests/Engram.Postgres.Tests/` project

## Phase 2: Core Implementation — PostgresStore Methods

- [ ] 2.1 Implement Sessions group: `CreateSessionAsync`, `EndSessionAsync`, `GetSessionAsync`, `RecentSessionsAsync`
- [ ] 2.2 Implement Observations group: `AddObservationAsync` (3-path dedup: topic_key upsert → hash window → fresh insert), `GetObservationAsync`, `RecentObservationsAsync`, `UpdateObservationAsync`, `DeleteObservationAsync`
- [ ] 2.3 Implement Search group: `SearchAsync(string, SearchOptions)`, `SearchAsync(string, IList<string>, SearchOptions)` with `plainto_tsquery` + GIN, `TimelineAsync`
- [ ] 2.4 Implement Prompts group: `AddPromptAsync`, `RecentPromptsAsync`, `SearchPromptsAsync`
- [ ] 2.5 Implement Context & Stats: `FormatContextAsync(string, string)`, `FormatContextAsync(IList<string>, string)`, `StatsAsync`
- [ ] 2.6 Implement Export/Import: `ExportAsync`, `ImportAsync` — ensure `ExportData` compatibility with `SqliteStore`
- [ ] 2.7 Implement Projects & Sync: `MergeProjectsAsync`, `GetSyncedChunksAsync`, `RecordSyncedChunkAsync`
- [ ] 2.8 Add `MaxObservationLength` property and `DedupeWindowExpression` for PG dialect (`NOW() - INTERVAL`)

## Phase 3: Wiring — CLI and DI

- [ ] 3.1 Update `src/Engram.Cli/Program.cs` store selection: `(config.DbType, config.IsRemote) switch { ("postgres", false) => new PostgresStore(config), ... }`
- [ ] 3.2 Update `OpenStore()` helper in `Program.cs` to support `DbType.Postgres`
- [ ] 3.3 Add validation: if `DbType == Postgres` and `PgConnectionString` is empty, print error and exit with non-zero code
- [ ] 3.4 Update `engram stats` output to show PostgreSQL host when `DbType == Postgres`

## Phase 4: Testing — Parity and Integration

- [ ] 4.1 Create `PostgresStoreFixture` (Testcontainers lifecycle) in `tests/Engram.Postgres.Tests/`
- [ ] 4.2 Port `SqliteStore` session tests to `PostgresStore` — verify CRUD and `RecentSessionsAsync`
- [ ] 4.3 Port observation tests: `AddObservationAsync` dedup paths (topic_key, hash window, fresh insert), `UpdateObservationAsync`, `DeleteObservationAsync`
- [ ] 4.4 Port search tests: FTS via `tsvector`, topic-key shortcut, multi-namespace merge, empty results
- [ ] 4.5 Port prompt tests: `AddPromptAsync`, `RecentPromptsAsync`, `SearchPromptsAsync`
- [ ] 4.6 Port context & stats tests, export/import tests, merge tests, sync tests
- [ ] 4.7 Add CI GitHub Actions service for PostgreSQL (or Testcontainers) to run `Engram.Postgres.Tests`

## Phase 5: Documentation and Docker

- [ ] 5.1 Create `docs/POSTGRES-SETUP.md`: requirements, env vars, Docker Compose, migration from SQLite, verification steps
- [ ] 5.2 Update `docs/ARCHITECTURE.md`: add `PostgresStore` to diagrams, dependency graph, backend selection section
- [ ] 5.3 Add PostgreSQL service to `docker/docker-compose.yml` (or create `docker/docker-compose.pg.yml`)
- [ ] 5.4 Update `README.md` configuration table (already has `ENGRAM_DB_TYPE` and `ENGRAM_PG_CONNECTION` — verify accuracy)
- [ ] 5.5 Ensure `.gitignore` excludes PG connection strings from logs/output