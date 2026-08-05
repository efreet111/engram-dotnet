# Plan: Optional SQLite Volume for PostgreSQL Backend

## 1. Impact and dependencies

**What changes:** Docker Compose files and documentation only. Zero C# code changes.

**Existing components affected:**
- `docker/docker-compose.yml` — add comments, no behavioral change
- `docker/.env.example` — clarify `ENGRAM_DATA_DIR_HOST` is SQLite-only
- `docker/README.md` — add backend-specific compose file section
- `docs/DOCKER-VANILLA.md` — update PostgreSQL examples to omit volume

**New files:**
- `docker/docker-compose.postgres.yml` — PostgreSQL-only compose (no volume)
- `docker/docker-compose.sqlite.yml` — SQLite compose (with volume)

**Dependencies:** None. No new packages, no image rebuild, no config changes.

**Open questions from spec (all [OPTIONAL] / [FOLLOW-UP]):**
- OQ-1: Keep default `docker-compose.yml` as-is with added comments → **Assumed YES**
- OQ-2: Separate files vs profiles → **Assumed separate files** (simpler)
- OQ-3: C# validation for `DataDir` → **Out of scope** (follow-up)

---

## 2. File changes (Proposed Changes)

- [NEW] `docker/docker-compose.postgres.yml` — Compose file for PostgreSQL backend. No `/data/engram` volume mount. Keeps: build context, ports, extra_hosts, environment (PG connection), healthcheck.
- [NEW] `docker/docker-compose.sqlite.yml` — Compose file for SQLite backend. Explicitly mounts `${ENGRAM_DATA_DIR_HOST:-./data}:/data/engram`. Sets `ENGRAM_DB_TYPE=sqlite`.
- [MODIFY] `docker/docker-compose.yml` — Add header comment block pointing to backend-specific files. No structural changes.
- [MODIFY] `docker/.env.example` — Update `ENGRAM_DATA_DIR_HOST` comment to state it is required for SQLite only, optional/ignored for PostgreSQL.
- [MODIFY] `docker/README.md` — Add section "Backend-specific compose files" with PostgreSQL-only and SQLite setup guides. Update Quick Start to mention file selection.
- [MODIFY] `docs/DOCKER-VANILLA.md` — Update §2.3 (PostgreSQL run example) to show the command WITHOUT `-v` flag, with a note that the volume is optional for PostgreSQL.

---

## 3. Contracts and schemas

### 3.1 `docker-compose.postgres.yml` — required structure

```yaml
version: "3.8"

# Header comment: PostgreSQL-only — no data volume needed

services:
  engram:
    image: engram-dotnet:latest
    build:
      context: ..
      dockerfile: Dockerfile
      args:
        ENGRAM_VERSION: v1.3.0
    container_name: engram
    restart: unless-stopped
    ports:
      - "7437:7437"
    extra_hosts:
      - "host.docker.internal:host-gateway"
    # NO volumes section
    environment:
      ENGRAM_DB_TYPE: ${ENGRAM_DB_TYPE:-postgres}
      ENGRAM_PORT: "7437"
      ENGRAM_PG_CONNECTION: "Host=${ENGRAM_PG_HOST:-host.docker.internal};Port=${ENGRAM_PG_PORT:-5432};Database=${ENGRAM_PG_DATABASE:-engram};Username=${ENGRAM_PG_USER:-engram};Password=${ENGRAM_PG_PASSWORD}"
      # Sync comments (same as current docker-compose.yml)
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:7437/health"]
      interval: 30s
      timeout: 5s
      retries: 3
      start_period: 10s
```

### 3.2 `docker-compose.sqlite.yml` — required structure

```yaml
version: "3.8"

# Header comment: SQLite backend — data volume required

services:
  engram:
    image: engram-dotnet:latest
    build:
      context: ..
      dockerfile: Dockerfile
      args:
        ENGRAM_VERSION: v1.3.0
    container_name: engram
    restart: unless-stopped
    ports:
      - "7437:7437"
    volumes:
      - ${ENGRAM_DATA_DIR_HOST:-./data}:/data/engram
    environment:
      ENGRAM_DB_TYPE: sqlite
      ENGRAM_DATA_DIR: /data/engram
      ENGRAM_PORT: "7437"
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:7437/health"]
      interval: 30s
      timeout: 5s
      retries: 3
      start_period: 10s
```

### 3.3 `docker-compose.yml` — comment block to add (top of file, after `version`)

```yaml
# ─────────────────────────────────────────────────────────────────────────────
# Engram — Docker Compose (backward-compatible default)
#
# This file mounts the SQLite data volume for backward compatibility.
# For cleaner backend-specific setups, use:
#   - docker-compose.postgres.yml  → PostgreSQL only (no volume mount)
#   - docker-compose.sqlite.yml    → SQLite only (volume mount required)
#
# See docker/README.md for details.
# ─────────────────────────────────────────────────────────────────────────────
```

### 3.4 `.env.example` — comment update for `ENGRAM_DATA_DIR_HOST`

Replace current comment block (lines 10-17) with:

```env
# ─── Data Volume (SQLite only) ───────────────────────────────────────────────
# Ruta en el HOST donde se almacenan los datos persistentes (SQLite, exports)
# El contenedor monta esta ruta en /data/engram
#
# REQUIRED for SQLite backend (docker-compose.sqlite.yml)
# OPTIONAL for PostgreSQL backend (docker-compose.postgres.yml) — ignored
#
# Ejemplos:
#   - ./data                          (relativo al docker-compose.yml)
#   - /var/lib/engram                 (Linux estándar)
#   - /mnt/mydata/engram              (disco externo / NAS)
ENGRAM_DATA_DIR_HOST=./data
```

### 3.5 `docker/README.md` — new section to insert after "Quick Start" (before "Ubicación del archivo .env")

```markdown
## Backend-specific compose files

engram-dotnet supports two backends. Use the compose file that matches your choice:

| File | Backend | Volume required? |
|------|---------|-----------------|
| `docker-compose.postgres.yml` | PostgreSQL | No |
| `docker-compose.sqlite.yml` | SQLite | Yes |
| `docker-compose.yml` | Both (default: postgres) | Yes (backward-compatible) |

### PostgreSQL-only setup (recommended)

```bash
cd docker
cp .env.example .env
# Edit .env: set ENGRAM_DB_TYPE=postgres and PG credentials
# ENGRAM_DATA_DIR_HOST is NOT needed — you can leave it or comment it out
docker compose -f docker-compose.postgres.yml up -d --build
```

### SQLite setup

```bash
cd docker
cp .env.example .env
# Edit .env: set ENGRAM_DB_TYPE=sqlite and ENGRAM_DATA_DIR_HOST
docker compose -f docker-compose.sqlite.yml up -d --build
```
```

### 3.6 `docs/DOCKER-VANILLA.md` — §2.3 update

Replace the PostgreSQL run example (lines 90-98) with:

```bash
docker run -d \
    --name engram \
    --restart unless-stopped \
    -p 7437:7437 \
    -e ENGRAM_DB_TYPE=postgres \
    -e ENGRAM_PG_CONNECTION="Host=db.example.com;Port=5432;Database=engram;Username=engram;Password=REPLACE_ME" \
    engram-dotnet:latest
```

Add note after the example:

> **Note:** The `-v /var/lib/engram:/data/engram` volume flag is **not required** when using PostgreSQL backend. `PostgresStore` uses only the connection string and never reads `DataDir`. You may omit the volume mount entirely for PostgreSQL deployments.

---

## 4. Implementation checklist

### Docker Compose files

- [x] **T-001** Create `docker/docker-compose.postgres.yml`
  - Copy structure from existing `docker-compose.yml`
  - Remove `volumes:` section entirely
  - Remove `ENGRAM_DATA_DIR` from environment
  - Keep: build, ports, extra_hosts, PG connection, healthcheck, sync comments
  - Add header comment explaining this is PostgreSQL-only with no volume
  - **Verify:** `docker compose -f docker/docker-compose.postgres.yml config` parses without errors

- [x] **T-002** Create `docker/docker-compose.sqlite.yml`
  - Copy structure from existing `docker-compose.yml`
  - Set `ENGRAM_DB_TYPE: sqlite` (hardcoded, not from env)
  - Keep `volumes:` section with `${ENGRAM_DATA_DIR_HOST:-./data}:/data/engram`
  - Remove: extra_hosts, PG connection string, sync comments
  - Add header comment explaining this is SQLite-only with required volume
  - **Verify:** `docker compose -f docker/docker-compose.sqlite.yml config` parses without errors

- [x] **T-003** Update `docker/docker-compose.yml` with navigation comments
  - Add comment block after `version:` line pointing to backend-specific files
  - Do NOT change any services, volumes, environment, or healthcheck
  - **Verify:** `docker compose -f docker/docker-compose.yml config` still parses; behavior unchanged

### Environment and documentation

- [x] **T-004** Update `docker/.env.example` with backend-specific comments
  - Change section header from "Data Volume" to "Data Volume (SQLite only)"
  - Add note that `ENGRAM_DATA_DIR_HOST` is required for SQLite, optional/ignored for PostgreSQL
  - **Verify:** Read the file — comment makes the SQLite/PostgreSQL distinction clear

- [x] **T-005** Update `docker/README.md` with backend-specific compose guide
  - Add new section "Backend-specific compose files" after Quick Start
  - Include comparison table (file → backend → volume required?)
  - Add PostgreSQL-only setup example (no volume, use `-f docker-compose.postgres.yml`)
  - Add SQLite setup example (use `-f docker-compose.sqlite.yml`)
  - **Verify:** Follow the PostgreSQL-only instructions end-to-end — they should be complete and accurate

- [x] **T-006** Update `docs/DOCKER-VANILLA.md` — PostgreSQL examples without volume
  - In §2.3 "Run (PostgreSQL, sync mode)": remove `-v /var/lib/engram:/data/engram` from the `docker run` command
  - Add explanatory note that volume is not required for PostgreSQL
  - In §9 "Environment variables reference" examples: update "Team mode (PostgreSQL)" example to omit `-v`
  - In §10 "PostgreSQL connection guide": update Scenario A, B, C examples to omit `-v` flag
  - **Verify:** All PostgreSQL `docker run` examples in the file omit the volume mount

### Manual testing

- [ ] **T-007** Manual testing — PM-1: PostgreSQL deployment without volume
  > Pending: Docker daemon is unavailable in this environment, so the deployment and healthcheck could not be run.
  - Steps: `cp docker/.env.example docker/.env` → set `ENGRAM_DB_TYPE=postgres` + valid PG credentials → `docker compose -f docker/docker-compose.postgres.yml up -d --build` → check logs
  - Expected: Container starts, connects to PostgreSQL, healthcheck passes, no volume errors
  - Mark PM-1 `[x]` in spec.md

- [ ] **T-008** Manual testing — PM-2: SQLite deployment with volume
  > Pending: Docker daemon is unavailable in this environment, so the deployment and database creation could not be run.
  - Steps: set `ENGRAM_DB_TYPE=sqlite` → `docker compose -f docker/docker-compose.sqlite.yml up -d --build` → check `./data/engram.db` exists
  - Expected: Container starts, SQLite DB created in mounted volume
  - Mark PM-2 `[x]` in spec.md

- [ ] **T-009** Manual testing — PM-3: Backward compatibility
  > Pending: The before/after rendered Compose configuration is identical, but Docker daemon unavailability prevented the required deployment test.
  - Steps: use existing `docker-compose.yml` without changes → `docker compose up -d --build` → verify existing behavior
  - Expected: Container starts with volume mounted (same as before)
  - Mark PM-3 `[x]` in spec.md

- [ ] **T-010** Manual testing — PM-4: Documentation accuracy
  > Pending: Documentation was reviewed statically; human end-to-end confirmation remains required.
  - Steps: read `docker/README.md` → follow PostgreSQL-only setup → verify instructions match actual behavior
  - Expected: Instructions are clear, complete, and match deployment process
  - Mark PM-4 `[x]` in spec.md

---

## 5. Effort estimates

| Task | Description | Estimate |
|------|-------------|----------|
| T-001 | Create `docker-compose.postgres.yml` | 5 min |
| T-002 | Create `docker-compose.sqlite.yml` | 5 min |
| T-003 | Update `docker-compose.yml` comments | 2 min |
| T-004 | Update `.env.example` comments | 3 min |
| T-005 | Update `docker/README.md` | 10 min |
| T-006 | Update `docs/DOCKER-VANILLA.md` | 10 min |
| T-007 | PM-1: Test PostgreSQL compose | 5 min |
| T-008 | PM-2: Test SQLite compose | 5 min |
| T-009 | PM-3: Test backward compatibility | 5 min |
| T-010 | PM-4: Documentation review | 5 min |
| **Total** | | **~55 min** |

---

## 6. Testing strategy

### Per-task verification

| Task | Verification method |
|------|-------------------|
| T-001 | `docker compose -f docker/docker-compose.postgres.yml config` — validates YAML syntax |
| T-002 | `docker compose -f docker/docker-compose.sqlite.yml config` — validates YAML syntax |
| T-003 | `docker compose -f docker/docker-compose.yml config` — validates no regression |
| T-004 | Visual review — comment clarity |
| T-005 | Visual review — instructions completeness |
| T-006 | Visual review — all PG examples updated |

### Integration tests (T-007 through T-010)

Each PM test from the spec maps to a task:

| PM | Task | Pass criteria |
|----|------|---------------|
| PM-1 | T-007 | `curl http://localhost:7437/health` returns `"backend":"postgres"`, no volume errors in logs |
| PM-2 | T-008 | `ls ./data/engram.db` shows file exists after container start |
| PM-3 | T-009 | `docker compose up` works identically to before changes |
| PM-4 | T-010 | Human reviewer confirms docs match reality |

### Regression guard

The existing `docker-compose.yml` must produce identical `docker compose config` output before and after T-003 (only comments change). Run:

```bash
# Before T-003
docker compose -f docker/docker-compose.yml config > /tmp/before.yml

# After T-003
docker compose -f docker/docker-compose.yml config > /tmp/after.yml

# Compare (should be identical — comments are stripped by `config`)
diff /tmp/before.yml /tmp/after.yml
```

---

## 7. Execution order

Tasks T-001 → T-002 → T-003 are independent of each other and can be done in any order. T-004 → T-005 → T-006 are documentation tasks, also independent. T-007 → T-010 require all prior tasks complete.

```
T-001 ─┐
T-002 ─┼─→ T-007 (PM-1)
T-003 ─┤    T-008 (PM-2)
T-004 ─┤    T-009 (PM-3)
T-005 ─┤    T-010 (PM-4)
T-006 ─┘
```

Recommended sequential order for forge-dev: T-001 → T-002 → T-003 → T-004 → T-005 → T-006 → T-007 → T-008 → T-009 → T-010.
