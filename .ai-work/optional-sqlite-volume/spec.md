---
capability_matrix:
  ai_reasoning:
    - "Documentation wording and structure for Docker setup guides"
    - "Choosing which compose files to create and how to name them"
  deterministic:
    - "PostgreSQL backend MUST NOT require `/data/engram` volume mount"
    - "SQLite backend MUST continue to work with volume mount"
    - "Existing `docker-compose.yml` MUST remain backward compatible"
    - "No C# code changes allowed — documentation and Docker config only"
---

# Spec: Optional SQLite Volume for PostgreSQL Backend

## 1. Objective and scope

**Problem:** When deploying engram-dotnet with PostgreSQL backend (`ENGRAM_DB_TYPE=postgres`), the Docker configuration still requires/mounts a volume for SQLite data (`/data/engram`). This is unnecessary because `PostgresStore` never references `DataDir` — it only uses `PgConnectionString`. The volume mount creates confusion about what's actually needed for PostgreSQL-only deployments.

**Goal:** Make the SQLite volume clearly optional when using PostgreSQL backend, providing clean separation between backend-specific Docker configurations while maintaining backward compatibility.

**Out of scope:**
- C# code changes (not needed — `PostgresStore` already ignores `DataDir`)
- Changing the backend selection logic
- Adding new environment variables for conditional behavior
- Modifying the SQLite backend behavior

---

## 2. Functional requirements (FR)

- **FR-001:** Separate compose file for PostgreSQL backend
  Create `docker-compose.postgres.yml` that does NOT mount the `/data/engram` volume, containing only the configuration needed for PostgreSQL deployments.

  *Scenario A:*
  Given a user wants to deploy engram with PostgreSQL backend
  When they run `docker compose -f docker-compose.postgres.yml up -d`
  Then the container starts without requiring a data volume mount
  And the container connects to PostgreSQL successfully

  *Scenario B:*
  Given a user has PostgreSQL running on the host
  When they use `docker-compose.postgres.yml` with valid `.env` credentials
  Then the healthcheck passes at `http://localhost:7437/health`
  And no `/data/engram` directory is created or required

- **FR-002:** Separate compose file for SQLite backend
  Create `docker-compose.sqlite.yml` that explicitly mounts the `/data/engram` volume for SQLite deployments, making the requirement clear.

  *Scenario A:*
  Given a user wants to deploy engram with SQLite backend
  When they run `docker compose -f docker-compose.sqlite.yml up -d`
  Then the container starts with the data volume mounted at `/data/engram`
  And SQLite database is created in the mounted volume

  *Scenario B:*
  Given a user has an existing SQLite deployment
  When they switch from `docker-compose.yml` to `docker-compose.sqlite.yml`
  Then their existing data in `./data` is preserved and accessible

- **FR-003:** Backward-compatible default compose file
  Update the existing `docker-compose.yml` with clear comments explaining when each compose file should be used, while maintaining its current behavior for existing deployments.

  *Scenario A:*
  Given an existing user has a working deployment with `docker-compose.yml`
  When they run `docker compose up -d` after the update
  Then their deployment continues to work without changes
  And the volume mount behavior is unchanged

  *Scenario B:*
  Given a new user reads the updated `docker-compose.yml`
  When they review the comments
  Then they understand which compose file to use for their backend choice
  And they can easily switch between PostgreSQL and SQLite configurations

- **FR-004:** Updated documentation for PostgreSQL-only setup
  Update `docker/README.md` and `docs/DOCKER-VANILLA.md` to clearly document that the data volume is optional for PostgreSQL backend.

  *Scenario A:*
  Given a user wants to deploy PostgreSQL-only setup
  When they read the documentation
  Then they find clear instructions stating the volume is not needed
  And they can follow a step-by-step guide for PostgreSQL deployment

  *Scenario B:*
  Given a user is troubleshooting volume-related issues
  When they consult the documentation
  Then they find explicit guidance on when the volume is required vs optional
  And they understand the difference between SQLite and PostgreSQL requirements

- **FR-005:** Updated `.env.example` with backend-specific comments
  Update `docker/.env.example` to clarify that `ENGRAM_DATA_DIR_HOST` is only required for SQLite backend.

  *Scenario A:*
  Given a user configures `.env` for PostgreSQL
  When they review the `ENGRAM_DATA_DIR_HOST` variable
  Then they see a comment indicating it's optional for PostgreSQL
  And they can safely omit or comment out the variable

  *Scenario B:*
  Given a user configures `.env` for SQLite
  When they review the `ENGRAM_DATA_DIR_HOST` variable
  Then they see it's required for SQLite backend
  And they understand how to configure the host path

---

## 3. Non-functional requirements (NFR)

- **NFR-001:** Backward compatibility
  Existing deployments using `docker-compose.yml` MUST continue to work without any changes. The update must be non-breaking for current users.

- **NFR-002:** Documentation clarity
  Documentation MUST clearly state which compose file to use for each backend, with concrete examples. A user should be able to choose the right file in under 30 seconds.

- **NFR-003:** Maintainability
  The separate compose files MUST be easy to maintain. Changes to shared configuration (ports, healthcheck, etc.) should be easy to propagate across files without duplication errors.

- **NFR-004:** No code changes
  This feature MUST NOT require any changes to C# source code. The solution is limited to Docker configuration files and documentation.

- **NFR-005:** File naming convention
  Compose files MUST follow a clear naming pattern: `docker-compose.{backend}.yml` where `{backend}` is `sqlite` or `postgres`. The default `docker-compose.yml` remains for backward compatibility.

---

## 4. Developer manual tests (required — mark [x] before /flow-close)

| ID | Case / flow | Steps (summary) | Expected result | [x] |
|----|-------------|-----------------|-----------------|-----|
| PM-1 | PostgreSQL deployment without volume | 1. Copy `docker/.env.example` to `docker/.env`<br>2. Set `ENGRAM_DB_TYPE=postgres` and valid PG credentials<br>3. Run `docker compose -f docker-compose.postgres.yml up -d --build`<br>4. Check `docker compose logs` | Container starts, connects to PostgreSQL, healthcheck passes, no volume mount errors | [ ] |
| PM-2 | SQLite deployment with volume | 1. Copy `docker/.env.example` to `docker/.env`<br>2. Set `ENGRAM_DB_TYPE=sqlite`<br>3. Run `docker compose -f docker-compose.sqlite.yml up -d --build`<br>4. Check `./data/engram.db` exists | Container starts, SQLite database created in mounted volume | [ ] |
| PM-3 | Backward compatibility | 1. Use existing `docker-compose.yml` without changes<br>2. Run `docker compose up -d --build`<br>3. Verify existing behavior | Container starts with volume mounted (same as before update) | [ ] |
| PM-4 | Documentation accuracy | 1. Read `docker/README.md`<br>2. Follow PostgreSQL-only setup instructions<br>3. Verify instructions match actual behavior | Instructions are clear, complete, and match the actual deployment process | [ ] |

---

## 5. Open questions for human (OQ-*)

| ID | Tag | Question | Default / assumption |
|----|-----|---------|---------------------|
| OQ-1 | [OPTIONAL] | Should we keep the default `docker-compose.yml` as-is for backward compatibility, or update it to reference the new backend-specific files? | Assumed: Keep as-is with added comments pointing to backend-specific files |
| OQ-2 | [OPTIONAL] | Should we create a `docker-compose.yml` that uses Docker Compose profiles (`--profile sqlite` / `--profile postgres`) instead of separate files? | Assumed: No — separate files are simpler and more explicit for users |
| OQ-3 | [FOLLOW-UP] | Should we add validation in the C# code to skip `DataDir` creation when `ENGRAM_DB_TYPE=postgres`? (Currently it creates the directory even if unused) | — |

---

## Memory Signal

- type: decision
- significance: low
- summary: "Docker volume for SQLite is optional when using PostgreSQL backend — solution is documentation + separate compose files, no code changes needed"
