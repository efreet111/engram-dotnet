# HU-010 — Deployment Profile System

> **⚠️ HU-012 supersedes este documento para definición de profiles.**
> HU-012 renombró los profiles: `sync` → `offline-first`, `server` → `remote-server`, y agregó `desktop`.
> Ver [HU-012](./HU-012-offline-first-profiles.md) para la definición actual.

---

**As**: Developer or IT Admin deploying engram-dotnet  
**I want**: Set deployment behavior via a single `ENGRAM_PROFILE` environment variable (`local`, `server`, `sync`)  
**To**: Simplify configuration instead of setting 10+ environment variables manually, and prevent misconfiguration at startup

---

## Acceptance Criteria

- [x] `ENGRAM_PROFILE=local` → SQLite backend, sync disabled (current default for dev)
- [x] `ENGRAM_PROFILE=server` → PostgreSQL backend, sync disabled, multi-user isolation via `X-Engram-User` header
- [x] `ENGRAM_PROFILE=sync` → PostgreSQL backend + SyncManager enabled, offline-first mode
- [x] `ENGRAM_PROFILE` defaults to `local` if not set (backward compatible)
- [x] Individual env vars (`ENGRAM_DB_TYPE`, `ENGRAM_SYNC_ENABLED`, etc.) override profile defaults (explicit > profile > hardcoded)
- [x] Missing required vars for a profile produce a clear validation error at startup (not silent fallback)
- [x] Docker Compose uses `ENGRAM_PROFILE` to select which profile preset to apply
- [x] `docker-compose.yml` works out-of-the-box for each profile with minimal extra config
- [x] Documentation updated: `INSTALL.md`, `01-QUICK-START.md`, `DOCKER-VANILLA.md`
- [x] Deploy script (`scripts/deploy.sh`) created with start/stop/remove/recreate commands

---

## Tasks (Implementation)

- [x] Create `DeployProfile` enum (`Local`, `Server`, `Sync`) in `Engram.Store` or new `Engram.Configuration` namespace
- [x] Create `ProfileDefaults` static class with `For(DeployProfile)` method returning `Dictionary<string, string?>`
- [x] Modify `StoreConfig.FromEnvironment()` to merge profile defaults before reading individual env vars
- [x] Modify `SyncManagerConfig.FromEnvironment()` same pattern
- [x] Add `ValidateProfile()` method that throws with clear message if required vars are missing (e.g., `ENGRAM_PG_CONNECTION` missing when `profile=server`)
- [x] Add `ENGRAM_PROFILE` to `Dockerfile` ENV defaults
- [x] Update `docker-compose.yml` to use `ENGRAM_PROFILE` env var with profile-specific overrides
- [x] Update `docker/.env.example` with `ENGRAM_PROFILE` and per-profile documentation
- [x] Update `docs/INSTALL.md` to show `ENGRAM_PROFILE` usage
- [x] Update `docs/01-QUICK-START.md` — replace manual var lists with profile selection
- [x] Add T1 test: `ENGRAM_PROFILE=local` starts with SQLite
- [x] Add T1 test: `ENGRAM_PROFILE=server` with missing `ENGRAM_PG_CONNECTION` → clear validation error
- [x] Add T1 test: explicit `ENGRAM_DB_TYPE=sqlite` overrides `ENGRAM_PROFILE=server`
- [x] Create `scripts/deploy.sh` with: start, stop, remove, recreate, logs, status, restart commands
- [x] Script validates required env vars before deploy (profile validation)
- [x] Script supports `--profile local|server|sync` flag
- [x] Script reads `.env` from `docker/` directory automatically
- [x] `ENGRAM_DB_MODE` env var: `external` (PostgreSQL on host/network) or `embedded` (PostgreSQL as Docker service)
- [x] `external` mode: assumes PostgreSQL is external (host), uses `host.docker.internal`
- [x] `embedded` mode: includes PostgreSQL as a service in docker-compose (fullstack)
- [x] Create `docker/docker-compose.embedded.yml` with PostgreSQL service included
- [x] Create `scripts/backup.sh` for data backup before recreate/update
- [x] Create `scripts/update.sh` for pulling new image + recreate
- [x] Update `docker/.env.example` with new vars and profiles documented
- [x] Deploy script supports `--image` flag to use pre-built image (`ghcr.io/efreet111/engram-dotnet:latest`) instead of local build
- [x] Deploy script validates that `.env` file is not committed to git (safety check)
- [x] Documentation updated: `INSTALL.md`, `01-QUICK-START.md`, `DOCKER-VANILLA.md`, `docker/README.md`

---

## Deploy Script (`scripts/deploy.sh`)

### Commands

```bash
./scripts/deploy.sh start [--profile local|server|sync] [--image]
                                                             # Levanta el contenedor
./scripts/deploy.sh stop                                    # Detiene el contenedor
./scripts/deploy.sh remove                                  # Elimina el contenedor
./scripts/deploy.sh recreate                               # Recrea desde cero
./scripts/deploy.sh logs [-f]                              # Ver logs
./scripts/deploy.sh status                                  # Muestra estado del contenedor
./scripts/deploy.sh restart                                 # Reinicia
./scripts/deploy.sh validate                                # Valida vars de entorno antes de deploy
./scripts/deploy.sh update                                  # Pull nueva imagen + recreate
./scripts/deploy.sh backup                                  # Backup de datos antes de recreate
```

### Behavior

| Command | What it does |
|---------|-------------|
| `start` | `docker compose up -d --build` en `docker/` dir |
| `stop` | `docker compose stop` |
| `remove` | `docker compose down` (elimina contenedores y redes, preserva volúmenes) |
| `recreate` | `remove` + `start` (forcing rebuild) |
| `logs` | `docker compose logs [-f]` |
| `status` | `docker compose ps` + `docker inspect` health |
| `restart` | `docker compose restart` |
| `validate` | Lee `.env` y valida que las vars obligatorias del perfil estén |

### Profile and database mode

All configuration is in `.env`. Scripts just execute commands.

```bash
# .env — todo configurado acá
ENGRAM_PROFILE=sync
ENGRAM_DB_MODE=external   # or embedded
ENGRAM_PG_CONNECTION=Host=...;Database=...;Username=...;Password=...
ENGRAM_SERVER_URL=http://...
ENGRAM_USER=tu_nombre

# Uso simple — sin flags
./scripts/deploy.sh validate  # valida antes de levantar
./scripts/deploy.sh start      # usa valores del .env
./scripts/deploy.sh recreate   # rebuild + restart
```

### Database mode (`ENGRAM_DB_MODE`)

| Mode | Description | Use case |
|------|-------------|----------|
| `external` (default) | PostgreSQL already exists on host or network | TrueNAS, existing DB server |
| `embedded` | PostgreSQL as Docker service in same compose | Dev/local testing, fullstack |

Configuration goes in `.env`:
```bash
# TrueNAS / PostgreSQL externo
ENGRAM_DB_MODE=external

# Dev local con PostgreSQL embebido
ENGRAM_DB_MODE=embedded
```

**`docker-compose.embedded.yml`** includes a `postgres:` service:

```yaml
services:
  engram:
    depends_on:
      postgres:
        condition: service_healthy
    environment:
      ENGRAM_PG_HOST: postgres  # overrides host.docker.internal
  postgres:
    image: postgres:17-alpine
    environment:
      POSTGRES_DB: engram
      POSTGRES_USER: engram
      POSTGRES_PASSWORD: ${ENGRAM_PG_PASSWORD}
    volumes:
      - pgdata:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U engram"]
      interval: 10s
      timeout: 5s
      retries: 5

volumes:
  pgdata:
```

### Validation rules

```bash
# Ejemplo: validate antes de start
$ ./scripts/deploy.sh validate
Error: ENGRAM_PROFILE=server requires ENGRAM_PG_CONNECTION but it is not set.
Error: ENGRAM_PROFILE=server requires ENGRAM_USER but it is not set.
Aborting deploy.

# Con todas las vars necesarias:
$ ./scripts/deploy.sh validate
✓ ENGRAM_PROFILE=sync
✓ ENGRAM_PG_CONNECTION configured
✓ ENGRAM_SERVER_URL configured
✓ ENGRAM_USER configured
Ready to deploy.
```

### Script draft

```bash
#!/bin/bash
set -eEuo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
COMPOSE_DIR="$SCRIPT_DIR/../docker"
ENV_FILE="$COMPOSE_DIR/.env"

usage() {
  cat <<EOF
Usage: $(basename "$0") <command> [--profile local|server|sync] [--image]

Commands:
  start     Start the container (docker compose up -d [--build] or --image)
  stop      Stop the container (docker compose stop)
  remove    Remove containers and networks (docker compose down)
  recreate  Recreate from scratch (stop + remove + start)
  logs      Show logs (add -f for tail)
  status    Show container status
  restart   Restart the container
  validate  Validate environment variables and safety checks before deploy
  backup    Backup data before recreate/update
  update    Pull latest image and recreate

Options:
  --profile   Deployment profile: local (default), server, sync
  --image     Use pre-built image from GHCR instead of local build

Examples:
  $(basename "$0") start                           # local build, start
  $(basename "$0") start --image                  # pre-built image, start
  $(basename "$0") recreate
  $(basename "$0") logs -f
  $(basename "$0") validate
  $(basename "$0") backup
  $(basename "$0") update

Environment (in docker/.env):
  ENGRAM_PROFILE       Deployment profile: local (default), server, sync
  ENGRAM_DB_MODE       Database mode: external (default), embedded
  ENGRAM_PG_CONNECTION Required for server and sync profiles
  ENGRAM_SERVER_URL    Required for sync profile
  ENGRAM_USER          Required for server and sync profiles
  ENGRAM_IMAGE         Override image (default: ghcr.io/efreet111/engram-dotnet:latest)

Safety checks:
  - Validates .env is not committed to git (secrets protection)

EOF
}

load_env() {
  if [[ -f "$ENV_FILE" ]]; then
    set -a; source "$ENV_FILE"; set +a
  fi
}

get_compose_file() {
  local db_mode="${ENGRAM_DB_MODE:-external}"
  if [[ "$db_mode" == "embedded" ]]; then
    echo "$COMPOSE_DIR/docker-compose.embedded.yml"
  else
    echo "$COMPOSE_DIR/docker-compose.yml"
  fi
}

validate_profile() {
  local profile="${ENGRAM_PROFILE:-local}"

  case "$profile" in
    local)
      echo "✓ Profile: local (SQLite, no sync)"
      ;;
    server)
      if [[ -z "${ENGRAM_PG_CONNECTION:-}" ]]; then
        echo "Error: ENGRAM_PROFILE=server requires ENGRAM_PG_CONNECTION but it is not set." >&2
        exit 1
      fi
      if [[ -z "${ENGRAM_USER:-}" ]]; then
        echo "Error: ENGRAM_PROFILE=server requires ENGRAM_USER but it is not set." >&2
        exit 1
      fi
      echo "✓ Profile: server (PostgreSQL, no sync)"
      ;;
    sync)
      if [[ -z "${ENGRAM_PG_CONNECTION:-}" ]]; then
        echo "Error: ENGRAM_PROFILE=sync requires ENGRAM_PG_CONNECTION but it is not set." >&2
        exit 1
      fi
      if [[ -z "${ENGRAM_SERVER_URL:-}" ]]; then
        echo "Error: ENGRAM_PROFILE=sync requires ENGRAM_SERVER_URL but it is not set." >&2
        exit 1
      fi
      if [[ -z "${ENGRAM_USER:-}" ]]; then
        echo "Error: ENGRAM_PROFILE=sync requires ENGRAM_USER but it is not set." >&2
        exit 1
      fi
      echo "✓ Profile: sync (PostgreSQL + SyncManager)"
      ;;
    *)
      echo "Error: Unknown profile '$profile'. Use local, server, or sync." >&2
      exit 1
      ;;
  esac
}

validate_db_mode() {
  local db_mode="${ENGRAM_DB_MODE:-external}"
  case "$db_mode" in
    external|embedded) ;;
    *)
      echo "Error: Unknown db-mode '$db_mode'. Use external or embedded." >&2
      exit 1
      ;;
  esac
}

cmd_start() {
  local compose_file
  compose_file=$(get_compose_file)
  cd "$COMPOSE_DIR"

  if [[ "${USE_IMAGE_FLAG:-}" == "--image" ]]; then
    # Pull pre-built image and run without local build
    local image="${ENGRAM_IMAGE:-ghcr.io/efreet111/engram-dotnet:latest}"
    echo "Using pre-built image: $image"
    docker compose -f "$compose_file" pull engram
    docker compose -f "$compose_file" up -d engram
  else
    # Local build
    docker compose -f "$compose_file" up -d --build
  fi

  echo "Container started. Health check..."
  sleep 5
  docker compose -f "$compose_file" exec engram curl -sf http://localhost:7437/health \
    && echo "✓ Container is healthy" \
    || echo "⚠ Container may not be healthy yet"
}

cmd_stop() {
  local compose_file
  compose_file=$(get_compose_file)
  cd "$COMPOSE_DIR"
  docker compose -f "$compose_file" stop
}

cmd_remove() {
  local compose_file
  compose_file=$(get_compose_file)
  cd "$COMPOSE_DIR"
  docker compose -f "$compose_file" down
}

cmd_recreate() {
  cmd_remove
  cmd_start
}

cmd_logs() {
  local compose_file
  compose_file=$(get_compose_file)
  cd "$COMPOSE_DIR"
  docker compose -f "$compose_file" logs "${@:--t --tail=50}"
}

cmd_status() {
  local compose_file
  compose_file=$(get_compose_file)
  cd "$COMPOSE_DIR"
  docker compose -f "$compose_file" ps
  echo ""
  docker inspect --format='{{.State.Health.Status}}' engram 2>/dev/null \
    && echo " (health: $(docker inspect --format='{{.State.Health.Status}}' engram))" \
    || echo " (no health check)"
}

cmd_restart() {
  local compose_file
  compose_file=$(get_compose_file)
  cd "$COMPOSE_DIR"
  docker compose -f "$compose_file" restart
}

cmd_backup() {
  local backup_dir="${BACKUP_DIR:-$COMPOSE_DIR/backups}"
  mkdir -p "$backup_dir"
  local ts
  ts=$(date +%Y%m%d_%H%M%S)
  local backup_file="$backup_dir/engram_backup_$ts.tar.gz"

  # Backup PostgreSQL data if using external mode and PG is running
  if [[ "${ENGRAM_DB_MODE:-external}" == "external" ]] && docker ps --format '{{.Names}}' | grep -q postgres; then
    echo "Backing up PostgreSQL..."
    docker exec postgres pg_dump -U engram engram > "$backup_dir/engram_pg_$ts.sql" \
      && echo "✓ PostgreSQL dump: $backup_dir/engram_pg_$ts.sql"
  fi

  # Backup SQLite data volume if it exists
  if [[ -d "$COMPOSE_DIR/../data" ]]; then
    echo "Backing up SQLite data..."
    tar -czf "$backup_file" -C "$COMPOSE_DIR/../" data \
      && echo "✓ SQLite backup: $backup_file"
  fi

  echo "✓ Backup complete"
}

cmd_update() {
  echo "Pulling latest image..."
  cd "$COMPOSE_DIR"
  docker pull ghcr.io/efreet111/engram-dotnet:latest \
    || echo "⚠ Could not pull image, using local build"
  cmd_recreate
}

cmd_validate() {
  load_env
  validate_db_mode
  validate_profile
  validate_env_safety
  echo "✓ Ready to deploy."
}

validate_env_safety() {
  # Check that .env file is NOT tracked by git (would expose secrets)
  if [[ -f "$ENV_FILE" ]]; then
    if git ls-files --error-unmatch "$ENV_FILE" 2>/dev/null; then
      echo "⚠ WARNING: $ENV_FILE is tracked by git. This may expose secrets." >&2
      echo "  Add it to .gitignore: echo '$ENV_FILE' >> ~/.gitignore_global" >&2
      echo "  Or use: git update-index --assume-unchanged $ENV_FILE" >&2
      # Not exiting — just warning, in case of git worktrees or special setups
    fi
  fi
}

# ─── Main ───────────────────────────────────────────────────────────────────
PROFILE=""
USE_IMAGE=""
COMMAND=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    start|stop|remove|recreate|logs|status|restart|validate|backup|update)
      COMMAND="$1"; shift
      ;;
    --profile)
      PROFILE="$2"; shift 2
      ;;
    --image)
      USE_IMAGE="yes"; shift
      ;;
    -h|--help)
      usage; exit 0
      ;;
    *)
      echo "Unknown argument: $1"; usage; exit 1
      ;;
  esac
done

# Apply profile from args (overrides .env if provided)
[[ -n "$PROFILE" ]] && export ENGRAM_PROFILE="$PROFILE"

# Load env from .env file
load_env

[[ -z "$COMMAND" ]] && { usage; exit 1; }

# Pass --image flag to start/recreate commands
[[ -n "$USE_IMAGE" ]] && export USE_IMAGE_FLAG="--image"

case "$COMMAND" in
  start)     cmd_start ;;
  stop)      cmd_stop ;;
  remove)    cmd_remove ;;
  recreate)  cmd_recreate ;;
  logs)      cmd_logs "$@" ;;
  status)    cmd_status ;;
  restart)   cmd_restart ;;
  validate)  cmd_validate ;;
  backup)    cmd_backup ;;
  update)    cmd_update ;;
esac
```

### File location

- `scripts/deploy.sh` — main deploy wrapper (reads `.env` from `docker/` automatically)
- `scripts/backup.sh` — data backup utility (can be called standalone or from deploy.sh)
- `docker/docker-compose.embedded.yml` — compose file with PostgreSQL service included
- Exit codes: 0 success, 1 error

---

## Profile Definitions

### Profile: `local` (Solo Developer)

```
ENGRAM_DB_TYPE=sqlite
ENGRAM_SYNC_ENABLED=false
ENGRAM_DATA_DIR=~/.engram (default)
```

**When to use**: Individual developer, no sharing, no offline requirement.

---

### Profile: `server` (Team Leader)

```
ENGRAM_DB_TYPE=postgres
ENGRAM_SYNC_ENABLED=false
ENGRAM_PG_CONNECTION=<required — must be provided>
ENGRAM_USER=<required — identifies the developer>
```

**When to use**: 2–5 person team, PostgreSQL shared server, direct HTTP (no sync).

---

### Profile: `sync` (IT Admin)

```
ENGRAM_DB_TYPE=postgres
ENGRAM_SYNC_ENABLED=true
ENGRAM_SYNC_POLL_SECONDS=30 (default)
ENGRAM_SYNC_TARGET=cloud (default)
ENGRAM_SERVER_URL=<required — URL of the sync server>
ENGRAM_USER=<required — identifies the developer>
```

**When to use**: 5–20 person team, offline-first, each dev has local SQLite + SyncManager.

---

## Verification

**Fecha de verificación:** 2026-08-06

### Tests
- 260 tests passed via `bash scripts/run-tests.sh`
- Tests de `DeployProfileTests.cs` cubren:
  - `ENGRAM_PROFILE=local` → SQLite backend, sync disabled
  - `ENGRAM_PROFILE=server` → PostgreSQL backend, sync disabled
  - `ENGRAM_PROFILE=sync` → PostgreSQL backend + SyncManager enabled
  - Override: explicit `ENGRAM_DB_TYPE=sqlite` prevalece sobre profile default
  - Validación: `ENGRAM_PROFILE=server` sin `ENGRAM_PG_CONNECTION` → `InvalidOperationException`

### Bug fix: `StoreConfig.RemoteUrl` canonical env var
- **Problema**: `StoreConfig.RemoteUrl` leía `ENGRAM_URL` pero el resto del codebase usa `ENGRAM_SERVER_URL`
- **Fix**: `StoreConfig.cs:45` ahora lee `ENGRAM_SERVER_URL` como variable canónica
- **ADR**: Ver [ADR-011](docs/architecture/adr/ADR-011-engram-url-env-var.md)
- **Impacto**: Deployments existentes que usan `ENGRAM_URL` deben migrar a `ENGRAM_SERVER_URL`

### Test pollution pre-existente
Los tests de `DeployProfileTests.cs` modifican `Environment.GetEnvironmentVariable()` lo cual puede afectar otros tests corriendo en el mismo proceso. Los tests usan `try/finally` para restaurar el estado original, pero tests que no siguen este patrón pueden ver valores contaminados. Ver `DeployProfileTests.cs:180-200` para el patrón correcto de isolation.

---

## Notes

- **Priority of config**: explicit env var > profile default > hardcoded default
  ```csharp
  // Pseudocode:
  var effectiveDbType = ENGRAM_DB_TYPE ?? ProfileDefaults[ENGRAM_PROFILE]["ENGRAM_DB_TYPE"] ?? "sqlite";
  ```
- **Validation strategy**: Profile validation runs before store initialization. Clear error message naming the missing var and the profile that requires it.
- **Breaking change**: None — `ENGRAM_PROFILE` is new and all existing deployments work because they don't set it (defaults to `local`).
- **Relationship with existing work**:
  - `StoreConfig.cs` already reads all these env vars — this HU just adds a profile defaults layer on top
  - `SyncManagerConfig.cs` already has all the sync vars — same pattern applies
  - `docker-compose.yml` already has partial profile awareness via `ENGRAM_DB_TYPE` — this formalizes it
- **ADR candidate**: Consider documenting the "profile as config composition" pattern if it proves useful — could be a general pattern for other config groups (e.g., `ENGRAM_VERIFICATION_PROFILE` for verifier settings)
