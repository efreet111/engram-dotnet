# ADR-011: Backend-specific Docker Compose files

**Status:** Accepted  
**Date:** 2026-08-07  
**Deciders:** victor  
**Related:** ENG-478, ENG-479, optional-sqlite-volume

## Context

engram-dotnet supports two storage backends: SQLite and PostgreSQL. When deploying with Docker Compose, the original `docker-compose.yml` always mounts a volume for `/data/engram` (intended for SQLite database persistence). However, PostgreSQL backend (`PostgresStore`) never references `DataDir` — it only uses `PgConnectionString`. The volume mount is unnecessary for PostgreSQL deployments and creates confusion about what's actually needed.

### Options considered

| Option | Description | Pros | Cons |
|--------|-------------|------|------|
| **A. Separate compose files** | Create `docker-compose.postgres.yml` (no volume) and `docker-compose.sqlite.yml` (with volume) | Simple, explicit, easy to document | Duplication of common config (build, ports, healthcheck) |
| **B. Docker Compose profiles** | Use `--profile postgres` / `--profile sqlite` in single file | Single source of truth | More complex syntax, harder for users to understand |
| **C. Conditional volume (not possible)** | Mount volume only if `ENGRAM_DB_TYPE=sqlite` | Ideal UX | Docker Compose has no native conditional volumes |
| **D. Always mount (status quo)** | Keep volume mounted even for PostgreSQL | Zero changes | Confusing, unnecessary mount |

## Decision

**Option A: Separate backend-specific compose files.**

Create three compose files:

| File | Backend | Volume | Use case |
|------|---------|--------|----------|
| `docker-compose.yml` | Both (default: postgres) | Yes (backward compatible) | Existing deployments, no changes required |
| `docker-compose.postgres.yml` | PostgreSQL | No | New PostgreSQL-only deployments |
| `docker-compose.sqlite.yml` | SQLite | Yes | New SQLite-only deployments |

The original `docker-compose.yml` receives only comment additions (navigation pointers to backend-specific files) — zero structural changes, preserving backward compatibility.

## Consequences

### Positive

1. **Clarity**: Users immediately understand which file to use for their backend choice
2. **No unnecessary mounts**: PostgreSQL deployments don't mount unused volumes
3. **Backward compatible**: Existing deployments continue to work without changes
4. **Easy to document**: Comparison table in README answers "which file?" in < 30 seconds
5. **Maintainable**: Each file is self-contained; common elements (~10 lines) are duplicated but small

### Negative

1. **Duplication**: Build context, ports, healthcheck are duplicated across 3 files
2. **Maintenance burden**: Changes to shared config must be propagated to all 3 files
3. **File proliferation**: 3 compose files instead of 1

### Mitigations

- Duplication is minimal (~10 lines per file: build, ports, healthcheck)
- Common config is unlikely to change frequently
- Header comments in each file explain purpose and point to alternatives
- Comparison table in `docker/README.md` provides quick reference

## Evidence

**Why PostgreSQL doesn't need the volume:**

1. `PostgresStore.cs` — No references to `DataDir`, only uses `PgConnectionString`
2. `.engram-id` — Saved in git repo root (`repoPath`), not in `DataDir`
3. Exports — In-memory objects (`ExportData`), not written to filesystem
4. Logs — stdout/stderr, not filesystem

**Why not Docker profiles (Option B):**

- Profiles add syntax complexity: `docker compose --profile postgres up` vs `docker compose -f docker-compose.postgres.yml up`
- Less explicit for users unfamiliar with Docker Compose profiles
- Harder to document clearly
- Separate files are more "copy-paste friendly" for users

## Implementation

**Files created:**
- `docker/docker-compose.postgres.yml` — 44 lines, PostgreSQL-only, no volume
- `docker/docker-compose.sqlite.yml` — 35 lines, SQLite-only, with volume

**Files modified:**
- `docker/docker-compose.yml` — Navigation comments added (backward compatible)
- `docker/.env.example` — `ENGRAM_DATA_DIR_HOST` documented as "SQLite only"
- `docker/README.md` — "Backend-specific compose files" section with comparison table

**Verification:**
- All 3 compose files parse successfully with `docker compose config`
- `git diff` confirms `docker-compose.yml` has zero structural changes
- forge-verify: PASS (all FR and NFR met)

## Related

- **ENG-478**: Docker vanilla build (established Docker infrastructure)
- **ENG-479**: Docker runtime permissions (entrypoint + gosu pattern)
- **ADR-001**: No ORM (context: SQLite vs PostgreSQL backend selection)
- **docs/DOCKER-VANILLA.md**: Docker run examples (also updated to omit `-v` for PostgreSQL)
