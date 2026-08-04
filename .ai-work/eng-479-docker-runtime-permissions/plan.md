# Plan: ENG-479 — Docker Runtime Permissions Fix

## Context

- **Problem**: A host-mounted `/data/engram` volume may be owned by `root:root`, while the application runs as the non-root `engram` user. SQLite then fails with `SQLite Error 14: 'unable to open database file'`.
- **Decision**: Run an entrypoint as root, repair the data-volume ownership, and drop privileges with `gosu` before starting the application.
- **Scope**: Both runtime Dockerfiles, the shared entrypoint, Docker vanilla documentation, Docker Compose command overrides, and configuration contract tests.
- **Compatibility fixes discovered during implementation**:
  - `Dockerfile.debian` used the SDK patch (`10.0.108`) as an ASP.NET runtime patch; the runtime is pinned separately to `10.0.8`.
  - `docker/docker-compose.test.yml` overrode the image command with `serve`, which is not an executable after the entrypoint handoff; overrides now pass `./engram serve`.

## Implementation checklist

### T-01: Entrypoint script

- [x] Create root-level `entrypoint.sh` with Bash strict exit handling.
- [x] If `/data/engram` exists, attempt `chown -R engram:engram` without preventing startup when a mounted filesystem rejects the operation.
- [x] Execute the supplied command through `exec gosu engram "$@"` so signals and exit codes reach the application.
- [x] Set executable permissions.

### T-02: Main Dockerfile

- [x] Install `gosu` in the runtime stage and validate the binary with `gosu nobody true`.
- [x] Copy and chmod `entrypoint.sh` at `/usr/local/bin/entrypoint.sh`.
- [x] Remove the `USER engram` directive so the entrypoint starts as root.
- [x] Set the entrypoint to the script and pass `./engram serve` as the default command.

### T-03: Debian Dockerfile

- [x] Install `gosu` in the Debian runtime stage after the .NET runtime dependencies.
- [x] Copy and chmod the shared entrypoint.
- [x] Remove the `USER engram` directive.
- [x] Set the same entrypoint and default command as the main Dockerfile.
- [x] Keep SDK and ASP.NET runtime patch arguments separate so the Debian fallback builds with current .NET feeds.

### T-03b: Compose command compatibility

- [x] Update test Compose command overrides to pass the executable explicitly through the entrypoint.

### T-04: Docker vanilla documentation

- [x] Add section 8 covering automatic and manual volume-permission fixes.
- [x] Add section 9 with the environment-variable reference table.
- [x] Add local SQLite, PostgreSQL/team, and custom-port examples.
- [x] Keep the existing Compose-compatible `/data/engram` contract intact.

### T-05: Automated contract tests

- [x] Add unit tests for the entrypoint contract.
- [x] Add unit tests for both Dockerfile contracts (gosu, copy, no `USER`, entrypoint/CMD).
- [x] Add unit tests for the documentation sections and environment-variable entries.

### T-06: Verification

- [x] Run shell syntax and executable-permission checks.
- [x] Run the ENG-479 unit tests and the repository test suite required by the development workflow.
- [x] Validate Docker Compose configuration compatibility when Docker is available.
- [x] Inspect the final diff for secrets and unrelated changes.

## Verification evidence

- `bash -n entrypoint.sh` and mode `755` passed.
- ENG-479 contract tests: **7 passed** in `Engram.Verification.Tests`.
- Repository SQLite-focused suite: **739 passed, 14 skipped** (the skipped tests are pre-existing Docker/PostgreSQL or explicitly skipped cases).
- Main image `engram-479-main:test` built and ran with a root-owned bind mount; the entrypoint changed ownership to UID/GID `1655:1655` and the application process ran as `engram`.
- Debian image `engram-479-debian:test` built and passed the same bind-mount/healthcheck/privilege-drop smoke test.
- T3 `scripts/dev-test.sh` passed against an isolated PostgreSQL 17 container: `/health`, `/stats`, and `/sync/status` all returned successfully with `backend=postgres`.
- `docker compose config` passed for both `docker/docker-compose.yml` and `docker/docker-compose.test.yml`.
- Dependency audit still reports the pre-existing transitive `SQLitePCLRaw.lib.e_sqlite3 2.1.10` NU1903 high-severity advisory; this change does not alter that dependency.

## Handoff notes

- The Docker image configuration intentionally has no `USER` directive: PID 1 starts as root only long enough to repair the mounted volume, then `gosu` replaces it with the non-root application process.
- The legacy `docker/Dockerfile` is a separate release-binary image and was not part of ENG-479's two runtime Dockerfiles.
- A direct `dotnet test tests/Engram.Postgres.Tests/Engram.Postgres.Tests.csproj -c Release` run has one pre-existing ENG-475 assertion mismatch (`Expected: 500`, `Actual: 201`) because production intentionally truncates long titles; the Docker/Postgres T3 smoke test passed independently.
