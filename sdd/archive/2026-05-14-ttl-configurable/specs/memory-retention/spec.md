# Memory Retention Specification

## Purpose

Implement a 3-layer system for long-term memory health: retention metrics visibility, configurable TTL-based expiration, and project redirect hints for renamed/consolidated projects.

---

## Requirements

### Requirement: Retention Metrics Visibility

The system MUST provide visibility into memory health through multiple access channels.

#### Scenario: HTTP endpoint returns correct stats

- GIVEN the database contains observations of varying ages (15, 60, 120, and 400 days old)
- WHEN a client sends `GET /retention/stats`
- THEN the response SHALL include accurate `age_buckets` with counts matching each bucket range
- AND `inactive_projects` SHALL list projects with no activity in the last 90 days
- AND `total_observations` SHALL match the count of non-deleted observations

#### Scenario: CLI displays formatted report

- GIVEN the user runs `engram retention check` with no observations in the store
- WHEN the command executes
- THEN it SHALL output a readable report showing "0 total observations"
- AND it SHALL NOT crash or throw an exception

#### Scenario: CLI with JSON output

- GIVEN the user runs `engram retention check --json`
- WHEN the command executes
- THEN it SHALL output valid JSON matching the `RetentionStats` schema
- AND the JSON SHALL be parseable by standard JSON parsers

#### Scenario: MCP tool returns formatted string

- GIVEN the MCP server is running and a client invokes `mem_retention_stats`
- WHEN the tool executes
- THEN it SHALL return a human-readable string summarizing the retention state
- AND the string SHALL include total count and top age bucket

#### Scenario: Empty store edge case

- GIVEN the database has zero observations
- WHEN requesting retention stats
- THEN all bucket counts SHALL be zero
- AND `recommendation` SHALL suggest "No observations to prune"

---

### Requirement: TTL Configuration via Environment Variables

The system MUST support configurable TTL values per observation type via environment variables.

#### Scenario: Default TTL values are applied

- GIVEN no ENGRAM_TTL_* environment variables are set
- WHEN `PruneOldObservationsAsync` is called with a cutoff date of 90 days ago
- THEN observations of type `tool_use`, `file_change`, and `command` older than 30 days SHALL be marked as deleted
- AND observations of type `bugfix` and `pattern` older than 90 days SHALL be marked as deleted
- AND observations of type `learning` and `discovery` older than 60 days SHALL be marked as deleted

#### Scenario: Custom TTL overrides defaults

- GIVEN environment variable `ENGRAM_TTL_TOOL_USE=60d` is set
- WHEN pruning executes with cutoff of 60 days ago
- THEN tool_use observations older than 60 days SHALL be marked as deleted

#### Scenario: decision and architecture types never expire

- GIVEN an observation of type `decision` was created 500 days ago with no topic_key
- WHEN TTL pruning runs with any cutoff date
- THEN this observation SHALL NOT be marked as deleted
- AND the same behavior SHALL apply to `architecture` type observations

#### Scenario: TTL by observation type table

| Type | Default TTL | TTL Configurable |
|------|-------------|------------------|
| tool_use | 30d | YES |
| file_change | 30d | YES |
| command | 30d | YES |
| bugfix | 90d | YES |
| pattern | 90d | YES |
| learning | 60d | YES |
| discovery | 60d | YES |
| decision | never | NO |
| architecture | never | NO |

---

### Requirement: Observations with topic_key NEVER Expire

The system MUST preserve knowledge-structured observations regardless of age.

#### Scenario: topic_key observation excluded from pruning

- GIVEN an observation of type `tool_use` was created 60 days ago with `topic_key="architecture/auth-model"`
- WHEN TTL pruning runs with 30-day cutoff
- THEN this observation SHALL NOT be marked as deleted
- AND it SHALL remain visible in search results

#### Scenario: observation without topic_key IS pruned

- GIVEN an observation of type `tool_use` was created 60 days ago with no topic_key
- WHEN TTL pruning runs with 30-day cutoff
- THEN this observation SHALL be marked as deleted (deleted_at set)

---

### Requirement: Soft-Delete Instead of Hard-Delete

The system MUST use soft-delete to preserve audit trail and enable recovery.

#### Scenario: soft-delete sets deleted_at timestamp

- GIVEN an observation exists with `deleted_at` equal to NULL
- WHEN `PruneOldObservationsAsync` is called without dry-run
- THEN the observation's `deleted_at` field SHALL be set to the current timestamp
- AND the observation's data SHALL remain in the database

#### Scenario: soft-deleted observations excluded from search

- GIVEN an observation has `deleted_at` set to a past timestamp
- WHEN a client performs a search query
- THEN that observation SHALL NOT appear in the results

#### Scenario: soft-deleted observation can be recovered

- GIVEN an observation was previously soft-deleted (deleted_at is set)
- WHEN an administrator updates the observation to set `deleted_at` to NULL
- THEN the observation SHALL reappear in search results

---

### Requirement: Dry-Run Support

The system MUST support previewing prune operations without modifying data.

#### Scenario: dry-run returns count without deletion

- GIVEN the database contains 50 observations eligible for pruning
- WHEN `PruneOldObservationsAsync` is called with `dryRun=true`
- THEN the returned `PruneResult` SHALL show `ObservationsDeleted=50`
- AND no observation SHALL have its `deleted_at` field modified

#### Scenario: CLI dry-run shows preview

- GIVEN the user runs `engram retention prune --dry-run --older-than 90d`
- WHEN the command executes
- THEN it SHALL display "Would prune X observations" without making changes
- AND it SHALL exit with code 0

#### Scenario: CLI apply executes real prune

- GIVEN the user runs `engram retention prune --apply --older-than 90d`
- WHEN the command executes
- THEN it SHALL display "Pruned X observations"
- AND the observations SHALL have `deleted_at` set

---

### Requirement: Project Redirect Hints Storage

The system MUST persist project migration information for redirect hints.

#### Scenario: migrate endpoint stores redirect

- GIVEN a client sends `POST /projects/migrate` with body `{"from_project": "login", "to_project": "auth-service", "notes": "Consolidated into auth service"}`
- WHEN the request is processed
- THEN a row SHALL be inserted into `project_migrations` table
- AND subsequent queries for "login" redirects SHALL return the migration info

#### Scenario: redirects endpoint lists migrations

- GIVEN two migrations exist: "login" → "auth-service" and "old-login" → "auth-service"
- WHEN a client sends `GET /projects/redirects`
- THEN the response SHALL include both redirect entries with `from_project`, `to_project`, and `migrated_at` fields

---

### Requirement: Search Results Include Redirect Hints

The system MUST provide redirect information in search results when applicable.

#### Scenario: search result includes redirect field

- GIVEN project "login" has a migration to "auth-service" recorded in project_migrations
- AND observations exist for project "login"
- WHEN a client searches for "login auth"
- THEN each SearchResult where observation.project equals "login" SHALL include a non-null `redirect` field
- AND the redirect SHALL contain `from: "login"` and `to: "auth-service"`

#### Scenario: no redirect when no migration exists

- GIVEN no migrations are recorded in project_migrations
- WHEN a client searches for any term
- THEN all SearchResult objects SHALL have their `redirect` field set to null

#### Scenario: MCP tool lists project redirects

- GIVEN migrations exist in the database
- WHEN a client invokes `mem_project_redirects` MCP tool
- THEN it SHALL return a formatted string listing all active project redirects

---

### Requirement: Graceful Error Handling

The system MUST handle edge cases and errors gracefully without crashing.

#### Scenario: invalid TTL format in environment variable

- GIVEN environment variable `ENGRAM_TTL_TOOL_USE=invalid` is set
- WHEN the store initializes
- THEN it SHALL fall back to the default TTL value (30d)
- AND it SHALL NOT throw an exception

#### Scenario: prune with future cutoff date

- GIVEN the user runs `engram retention prune --older-than -5d` (negative/future date)
- WHEN the command executes
- THEN it SHALL return zero observations to prune
- AND it SHALL NOT error

#### Scenario: search with null project in migration table

- GIVEN project_migrations contains a row with null `to_project`
- WHEN search results are generated
- THEN that migration SHALL be ignored (no redirect generated)

---

## Acceptance Criteria

| Capability | Criterion | Verification |
|------------|-----------|--------------|
| Retention Metrics | GET /retention/stats returns correct data | Manual API test |
| Retention Metrics | engram retention check shows formatted output | CLI test |
| Retention Metrics | mem_retention_stats MCP tool available | MCP client test |
| Retention Metrics | Works with 0 observations | Empty database test |
| TTL Configurable | PruneOldObservationsAsync marks observations as deleted | Integration test |
| TTL Configurable | topic_key observations never deleted | Unit test |
| TTL Configurable | --dry-run shows count without modification | CLI test |
| TTL Configurable | ENGRAM_TTL_* env vars override defaults | Env var test |
| TTL Configurable | Works in SQLite and PostgreSQL | Cross-backend test |
| TTL Configurable | mem_retention_prune MCP tool available | MCP client test |
| Project Redirects | POST /projects/migrate stores redirect | API test |
| Project Redirects | Search results include redirect field | Integration test |
| Project Redirects | GET /projects/redirects lists migrations | API test |
| Project Redirects | mem_project_redirects MCP tool available | MCP client test |