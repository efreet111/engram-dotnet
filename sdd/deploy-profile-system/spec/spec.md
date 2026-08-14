# Specification: Deployment Profile System (`deploy-profile-system`)

**Source**: HU-010 — `docs/tasks/HU-001-HU-099/HU-010-deploy-profile-system.md`
**Related capability**: `postgres-backend` (store selection / `StoreConfig`)

## Purpose

Define behavioral requirements for selecting deployment behavior via a single `ENGRAM_PROFILE` environment variable (`local`, `server`, `sync`), so operators set profile + overrides rather than 10+ vars manually. Profiles compose a `Dictionary<string,string?>` of defaults that are merged under individual env vars, with startup validation that fails fast on missing required values.

---

## ADDED Requirements

### Requirement: DeployProfile Enum

The system MUST define a `DeployProfile` enum with members `Local`, `Server`, `Sync`. The enum MUST be the sole source of truth for profile identity; no stringly-typed profile switching in store/sync code.

| Member | Semantics |
|--------|-----------|
| `Local` | SQLite backend, sync disabled — solo developer |
| `Server` | PostgreSQL backend, sync disabled, multi-user isolation via `X-Engram-User` — small team shared DB |
| `Sync` | PostgreSQL backend + SyncManager enabled, offline-first — large team with local SQLite + sync |

`ENGRAM_PROFILE` is parsed case-insensitive and trimmed. Unset or empty MUST resolve to `Local` (backward compatibility).

#### Scenario: Default profile when unset

- GIVEN no `ENGRAM_PROFILE` env var
- WHEN the app starts
- THEN the effective profile is `Local` and the SQLite backend is selected

#### Scenario: Case-insensitive parse

- GIVEN `ENGRAM_PROFILE=Sync`
- WHEN the app starts
- THEN the effective profile is `Sync` (case-insensitive)

### Requirement: ProfileDefaults Provider

The system MUST provide a `ProfileDefaults.For(DeployProfile)` method returning `Dictionary<string,string?>` of default config keys per profile. Defaults from the HU MUST be honored:

| Key | `Local` | `Server` | `Sync` |
|-----|---------|----------|--------|
| `ENGRAM_DB_TYPE` | `sqlite` | `postgres` | `postgres` |
| `ENGRAM_SYNC_ENABLED` | `false` | `false` | `true` |
| `ENGRAM_SYNC_POLL_SECONDS` | — | — | `30` |
| `ENGRAM_SYNC_TARGET` | — | — | `cloud` |

Profile defaults MUST NOT include values for required-only vars (`ENGRAM_PG_CONNECTION`, `ENGRAM_SERVER_URL`, `ENGRAM_USER`); those are user-supplied and validated separately.

#### Scenario: Server profile defaults

- GIVEN `ENGRAM_PROFILE=server` and no individual overrides
- WHEN defaults are applied
- THEN `ENGRAM_DB_TYPE=postgres` and `ENGRAM_SYNC_ENABLED=false` are effective

#### Scenario: Sync profile defaults

- GIVEN `ENGRAM_PROFILE=sync`
- WHEN defaults are applied
- THEN `ENGRAM_SYNC_ENABLED=true`, `ENGRAM_SYNC_POLL_SECONDS=30`, `ENGRAM_SYNC_TARGET=cloud`

### Requirement: Config Merge Precedence

Effective config MUST resolve with strict precedence:

```
explicit env var  >  profile default  >  hardcoded default
```

Explicit env vars (`ENGRAM_DB_TYPE`, `ENGRAM_SYNC_ENABLED`, ...) MUST override profile defaults; profile defaults MUST override hardcoded defaults. `StoreConfig.FromEnvironment()` and `SyncManagerConfig.FromEnvironment()` MUST apply profile defaults BEFORE reading individual env vars.

#### Scenario: Explicit overrides profile

- GIVEN `ENGRAM_PROFILE=server` and `ENGRAM_DB_TYPE=sqlite`
- WHEN config is resolved
- THEN `ENGRAM_DB_TYPE=sqlite` wins (SQLite backend), profile's `postgres` default is overridden

#### Scenario: Profile overrides hardcoded

- GIVEN `ENGRAM_PROFILE=server` and no `ENGRAM_DB_TYPE`
- WHEN config is resolved
- THEN `ENGRAM_DB_TYPE=postgres` from profile default wins over hardcoded `sqlite`

### Requirement: Startup Profile Validation

The system MUST run `ValidateProfile()` BEFORE store initialization. Validation MUST throw (or exit non-zero in CLI) with a message that names BOTH the missing var AND the profile that requires it. Silent fallback to another backend/profile MUST NOT occur.

Required vars per profile:

| Profile | Required (must be set & non-empty) |
|---------|------------------------------------|
| `Local` | (none) |
| `Server` | `ENGRAM_PG_CONNECTION`, `ENGRAM_USER` |
| `Sync` | `ENGRAM_PG_CONNECTION`, `ENGRAM_SERVER_URL`, `ENGRAM_USER` |

#### Scenario: Server missing connection

- GIVEN `ENGRAM_PROFILE=server` and `ENGRAM_PG_CONNECTION` unset
- WHEN `ValidateProfile()` runs
- THEN startup aborts with message naming `ENGRAM_PG_CONNECTION` and `ENGRAM_PROFILE=server`

#### Scenario: Sync missing all three

- GIVEN `ENGRAM_PROFILE=sync` and `ENGRAM_SERVER_URL` unset
- WHEN `ValidateProfile()` runs
- THEN startup aborts naming `ENGRAM_SERVER_URL` and `ENGRAM_PROFILE=sync`

#### Scenario: Local never validates

- GIVEN `ENGRAM_PROFILE=local` and no other vars
- WHEN `ValidateProfile()` runs
- THEN validation passes (no required vars)

### Requirement: Invalid Profile Rejection

An unrecognized `ENGRAM_PROFILE` value MUST cause startup to fail with a clear message listing valid values (`local`, `server`, `sync`). Fallback to `Local` MUST NOT occur for an explicit-but-invalid value (only for unset/empty).

#### Scenario: Typo in profile name

- GIVEN `ENGRAM_PROFILE=lokal`
- WHEN the app starts
- THEN startup aborts with `Unknown profile 'lokal'. Use local, server, or sync.`

### Requirement: Database Mode (`ENGRAM_DB_MODE`)

The system MUST support `ENGRAM_DB_MODE` with values `external` (default) or `embedded`. `external` means PostgreSQL exists on host/network (compose references `host.docker.internal`); `embedded` includes a `postgres:` service in compose. Docker-compose file selection is driven by this var.

#### Scenario: External mode

- GIVEN `ENGRAM_DB_MODE=external` (or unset)
- WHEN docker compose starts
- THEN only the `engram` service runs; PostgreSQL connection uses host-supplied `ENGRAM_PG_CONNECTION`

#### Scenario: Embedded mode

- GIVEN `ENGRAM_DB_MODE=embedded`
- WHEN docker compose starts
- THEN the embedded `postgres` service starts and `engram` depends on its `service_healthy`

### Requirement: Deploy Script Behavior

`scripts/deploy.sh` MUST source `.env` from `docker/` and provide commands `start`, `stop`, `remove`, `recreate`, `logs`, `status`, `restart`, `validate`, `backup`, `update`. `validate` MUST mirror `ValidateProfile()` (required vars per profile) plus a git-tracking safety check on `.env`. Exit codes: `0` success, `1` error. The script MUST NOT require flags; all config comes from `.env`.

#### Scenario: Validate fails fast

- GIVEN `.env` has `ENGRAM_PROFILE=server` and no `ENGRAM_PG_CONNECTION`
- WHEN `./scripts/deploy.sh validate` runs
- THEN it exits `1` with a message naming the missing var

#### Scenario: Tracked .env warning

- GIVEN `docker/.env` is git-tracked
- WHEN `./scripts/deploy.sh validate` runs
- THEN a WARNING is printed (non-fatal) advising `.gitignore`

## Non-Functional Requirements

### Requirement: Backward Compatibility

The addition of `ENGRAM_PROFILE` MUST NOT break existing deployments that do not set it. All existing env vars MUST continue to work identically when no profile is set. No new required var is introduced for the default (`Local`) path.

#### Scenario: Existing SQLite deployment untouched

- GIVEN an existing deployment with `ENGRAM_DB_TYPE=sqlite` and no `ENGRAM_PROFILE`
- WHEN the app starts after upgrade
- THEN behavior is identical to before upgrade (SQLite, no sync)

### Requirement: Single Source of Truth

All deployment config MUST live in `docker/.env`. No profile behavior is hidden in scripts or compose internals. Scripts are thin wrappers over `docker compose` that read `.env`.

#### Scenario: No flags needed

- GIVEN `.env` fully configured
- WHEN `./scripts/deploy.sh start` runs
- THEN the container starts using only `.env` (no CLI flags required)

### Requirement: Fail-Fast over Silent Misconfiguration

Preference order for "missing config" handling MUST be: abort with actionable message > silent default. The system MUST NOT silently choose a heavier backend (postgres) when required vars for it are missing.

### Requirement: Documentation Coverage

Docs MUST be updated for `INSTALL.md`, `01-QUICK-START.md`, `DOCKER-VANILLA.md`, `docker/README.md` to use `ENGRAM_PROFILE` selection instead of manual var lists.

---

## User Flows per Profile

### Flow: `local` (Solo Developer)

1. Operator sets `ENGRAM_PROFILE=local` (or omits the var).
2. `ValidateProfile()` passes (no required vars).
3. Profile defaults apply: SQLite, sync disabled.
4. `engram serve` starts with `SqliteStore` → data in `ENGRAM_DATA_DIR/engram.db`.

### Flow: `server` (Team Leader)

1. Operator sets `ENGRAM_PROFILE=server`, `ENGRAM_PG_CONNECTION`, `ENGRAM_USER`.
2. `ValidateProfile()` verifies connection + user are present.
3. Profile defaults apply: PostgreSQL backend, sync disabled.
4. `engram serve` starts with `PostgresStore`; `X-Engram-User` header isolates per-developer data.

### Flow: `sync` (IT Admin)

1. Operator sets `ENGRAM_PROFILE=sync`, `ENGRAM_PG_CONNECTION`, `ENGRAM_SERVER_URL`, `ENGRAM_USER`, optionally `ENGRAM_DB_MODE`.
2. `ValidateProfile()` verifies all three required vars.
3. Profile defaults apply: PostgreSQL, `ENGRAM_SYNC_ENABLED=true`, poll 30s, target cloud.
4. `engram serve` starts with `PostgresStore` + `SyncManager` running offline-first.

---

## MODIFIED Requirements

### Requirement: Store Selection in CLI

The CLI store selection MUST consider `ENGRAM_PROFILE` as the first composition layer, then apply explicit env var overrides on top. The three backends (SQLite, PostgreSQL, HTTP) remain the same; only the precedence feed into config changes.

(Previously: `StoreConfig.FromEnvironment()` read individual env vars directly; no profile layer existed.)

#### Scenario: Local profile selects SQLite

- GIVEN `ENGRAM_PROFILE=local` and no `ENGRAM_DB_TYPE`
- WHEN `engram serve` starts
- THEN `SqliteStore` is used (effective `ENGRAM_DB_TYPE=sqlite` from profile default)

#### Scenario: Server profile selects PostgreSQL

- GIVEN `ENGRAM_PROFILE=server`, `ENGRAM_PG_CONNECTION` set, no `ENGRAM_DB_TYPE`, no `ENGRAM_URL`
- WHEN `engram serve` starts
- THEN `PostgresStore` is used (effective `ENGRAM_DB_TYPE=postgres` from profile default)

#### Scenario: Sync profile enables SyncManager

- GIVEN `ENGRAM_PROFILE=sync` and required vars set
- WHEN `engram serve` starts
- THEN `PostgresStore` is used AND `SyncManager` is enabled (effective `ENGRAM_SYNC_ENABLED=true`)

#### Scenario: Explicit DB type overrides profile

- GIVEN `ENGRAM_PROFILE=server` and `ENGRAM_DB_TYPE=sqlite`
- WHEN `engram serve` starts
- THEN `SqliteStore` is used (explicit `sqlite` overrides profile's `postgres` default)

#### Scenario: Remote URL still takes precedence

- GIVEN `ENGRAM_PROFILE=server` and `ENGRAM_URL=http://remote:7437`
- WHEN `engram mcp` starts
- THEN `HttpStore` is used (remote mode overrides profile backend selection)

---

## Coverage

- Happy paths: covered (local/server/sync defaults + merges)
- Edge cases: covered (case-insensitive, explicit override, invalid profile, missing required vars, db-mode, .env safety)
- Error states: covered (validation abort messages naming var + profile, non-zero exit, no silent fallback)