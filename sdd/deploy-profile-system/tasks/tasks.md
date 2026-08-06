# Tasks: Deploy Profile System (HU-010)

## Phase 1: Foundation — DeployProfile Types

- [x] 1.1 Create `src/Engram.Store/DeployProfile.cs` with `DeployProfile` enum (`Local`, `Server`, `Sync`)
- [x] 1.2 Add `DeployProfileExtensions.FromEnvironment()` — parse `ENGRAM_PROFILE` case-insensitive, default `Local`, throw on invalid
- [x] 1.3 Add `ProfileDefaults.For(DeployProfile)` — return `Dictionary<string,string?>` per profile
- [x] 1.4 Add `ProfileValidator.Validate(DeployProfile)` — throw with missing var names per profile
- [x] 1.5 Add `DeployProfile` property to `StoreConfig` class

## Phase 2: Config Merge — StoreConfig & SyncManagerConfig

- [x] 2.1 Modify `StoreConfig.FromEnvironment()` — add `Resolve()` helper (env > profile default > hardcoded), set `Profile`, `DbType`, `PgConnectionString`, `User` through it
- [x] 2.2 Modify `SyncManagerConfig.FromEnvironment()` — same `Resolve()` pattern for `Enabled`, `TargetKey`, `PollInterval`
- [x] 2.3 Add unit tests: `DeployProfileExtensions.FromEnvironment()` — unset→Local, "Sync"→Sync, "lokal"→throws
- [x] 2.4 Add unit tests: `ProfileDefaults.For()` — assert correct keys/values for all 3 profiles
- [x] 2.5 Add unit tests: `ProfileValidator.Validate()` — Local passes, Server missing PG throws, Sync missing URL throws

## Phase 3: Startup Validation — Program.cs

- [x] 3.1 Modify `OpenStore()` in `src/Engram.Cli/Program.cs` — call `ProfileValidator.Validate(cfg.Profile)` before store initialization
- [x] 3.2 Add integration test: `StoreConfig.FromEnvironment()` merge precedence — explicit env var overrides profile default overrides hardcoded
- [x] 3.3 Add integration test: `SyncManagerConfig.FromEnvironment()` merge — `ENGRAM_PROFILE=sync` enables sync, explicit `ENGRAM_SYNC_ENABLED=false` overrides

## Phase 4: Docker & Compose

- [x] 4.1 Modify `docker/docker-compose.yml` — add `ENGRAM_PROFILE` passthrough, add `profiles: [embedded]` postgres service with healthcheck
- [x] 4.2 Create `docker/docker-compose.embedded.yml` — postgres service + engram depends_on service_healthy
- [x] 4.3 Update `docker/.env.example` — add `ENGRAM_PROFILE`, `ENGRAM_DB_MODE`, `ENGRAM_USER` with per-profile docs
- [x] 4.4 Add `ENGRAM_PROFILE` ENV default to `docker/Dockerfile`

## Phase 5: Deploy Script

- [x] 5.1 Create `scripts/deploy.sh` — commands: start, stop, remove, recreate, logs, status, restart, validate, backup, update
- [x] 5.2 Implement `validate` subcommand — mirror `ProfileValidator` logic + git-tracking safety check on `.env`
- [x] 5.3 Implement `start` subcommand — `docker compose up -d --build` or `--image` flag for pre-built image
- [x] 5.4 Implement `backup` subcommand — pg_dump for external postgres, tar for sqlite data dir
- [x] 5.5 Implement `update` subcommand — pull latest image + recreate

## Phase 6: Documentation

- [x] 6.1 Update `docs/INSTALL.md` — replace manual var lists with `ENGRAM_PROFILE` selection
- [x] 6.2 Update `docs/01-QUICK-START.md` — profile-based setup instructions
- [x] 6.3 Update `docs/DOCKER-VANILLA.md` — explain profile + db-mode
- [x] 6.4 Update `docker/README.md` — document profile-driven deployment

## Phase 7: Verification

- [x] 7.1 Run `dotnet test` — build succeeds (Docker build)
- [x] 7.2 Manual: `ENGRAM_PROFILE=local` starts with SQLite (T1) ✅
- [x] 7.3 Manual: `ENGRAM_PROFILE=server` with missing `ENGRAM_PG_CONNECTION` → clear validation error (T1) ✅
- [x] 7.4 Manual: `ENGRAM_PROFILE=server` + `ENGRAM_DB_TYPE=sqlite` → SQLite wins (T1) ✅
- [x] 7.5 Manual: `scripts/deploy.sh validate` → shows profile validation (T3) ✅
- [x] 7.6 Manual: `scripts/deploy.sh start` with `ENGRAM_PROFILE=local` → container starts ✅

## Bug Fixes

- [x] Fix `ENGRAM_SYNC_POLL_SECONDS` parsing — was interpreting "30" as 30ms instead of 30s
- [x] Fix `ProfileValidator.Validate()` — was validating based on Profile, not effective DbType (override now works correctly)
- [x] Fix `IsSyncEnabled` — was returning true when env var not set (should be false unless explicitly enabled)

## Parallel Opportunities

| Group | Tasks | Can run parallel with |
|-------|-------|----------------------|
| Phase 1 | 1.1–1.5 | — (foundation, must be first) |
| Phase 2 | 2.1–2.2 | 2.3–2.5 (code vs tests) |
| Phase 3 | 3.1 | 3.2–3.3 (wiring vs tests) |
| Phase 4 | 4.1–4.4 | — (depends on Phase 1 types) |
| Phase 5 | 5.1–5.5 | Phase 6 (script vs docs) |
| Phase 6 | 6.1–6.4 | Phase 5 (docs vs script) |

## Implementation Order

1. **Phase 1** first — types are the foundation everything else depends on.
2. **Phase 2** — config merge logic, can write tests (2.3–2.5) in parallel with code (2.1–2.2).
3. **Phase 3** — wire validation into Program.cs, tests in parallel.
4. **Phase 4** — Docker/compose changes (depend on Phase 1 enum existing).
5. **Phase 5 + 6** — deploy script and docs can be done in parallel (no code dependency).
6. **Phase 7** — verification after everything is implemented.
