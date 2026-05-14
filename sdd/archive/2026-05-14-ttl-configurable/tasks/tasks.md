# Tasks: TTL Configurable — Memory Health & Continuity

## Phase 1: Foundation — Config, Models, Interface

- [x] 1.1 Create `src/Engram.Store/RetentionConfig.cs` — `RetentionConfig` with env-var parsing `ENGRAM_TTL_*`, `ShouldExpire()`, `GetTtl()`
- [x] 1.2 Add models to `src/Engram.Store/Models.cs` — `RetentionStats`, `AgeBucket`, `InactiveProject`, `RetentionPruneResult`, `ProjectMigration`, `RetentionPruneParams`
- [x] 1.3 Add 4 methods to `src/Engram.Store/IStore.cs` — `GetRetentionStatsAsync`, `PruneOldObservationsAsync(RetentionPruneParams)`, `AddProjectMigrationAsync`, `GetProjectMigrationsAsync`

## Phase 2: Store Implementations

- [x] 2.1 Implement 4 new methods + `project_migrations` table in `Migrate()` + redirect enrichment stub in `SearchAsync` in `src/Engram.Store/SqliteStore.cs`
- [x] 2.2 Implement 4 new methods + `project_migrations` table in `Migrate()` + redirect enrichment stub in `SearchAsync` in `src/Engram.Store/PostgresStore.cs`
- [x] 2.3 Add 4 endpoint proxies (`/retention/stats`, `/retention/prune`, `/projects/migrate`, `/projects/migrations`) in `src/Engram.Store/HttpStore.cs`

## Phase 3: Surface — Server, CLI, MCP

- [x] 3.1 Add `GET /retention/stats`, `POST /retention/prune`, `GET /projects/migrations` routes + `HandleRetentionStats`, `HandleRetentionPrune`, `HandleProjectMigrations` handlers + `RetentionPruneBody` DTO + migration recording in `HandleMigrateProject` + redirect enrichment in `HandleSearch` in `src/Engram.Server/EngramServer.cs`
- [x] 3.2 Add `engram retention check` and `engram retention prune` (--dry-run, --type) commands to `src/Engram.Cli/Program.cs`
- [x] 3.3 Add `mem_retention_stats`, `mem_retention_prune`, `mem_project_redirects` MCP tools to `src/Engram.Mcp/EngramTools.cs`

## Phase 4: Testing

- [x] 4.1 Unit tests for `RetentionConfig` — `ShouldExpire`, `GetTtl`, case-insensitivity, unknown types
- [x] 4.2 Integration tests for `SqliteStore` — stats (0/3 obs), prune dry-run vs real, project migrations roundtrip, non-expiring types preserved, backdated prune with type filter
- [x] 4.3 E2E tests — prune with type filter, dry-run shows count without delete, idempotent double-prune, age buckets with backdated data
- [x] 4.4 Integration tests for `PostgresStore` — all 3 capabilities (stats, prune, migrations) + MCP tools already covered by `EngramTools`
