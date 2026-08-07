#!/usr/bin/env bash
# scripts/deploy.sh — Deploy wrapper for engram-dotnet Docker containers.
#
# Sources docker/.env automatically. All configuration goes in that file.
# The script is a thin wrapper around docker compose — no magic.
#
# Usage:
#   ./scripts/deploy.sh <command> [--profile local|remote-server|offline-first|desktop] [--image]
#
# Commands:
#   start     Start the container
#   stop      Stop the container
#   remove    Remove containers and networks (preserves volumes)
#   recreate  Recreate from scratch (stop + remove + start)
#   logs      Show logs (add -f for tail)
#   status    Show container status
#   restart   Restart the container
#   validate  Validate environment variables and safety checks
#   backup    Backup data before recreate/update
#   update    Pull latest image and recreate
#
# Options:
#   --profile   Deployment profile: local (default), remote-server, offline-first, desktop
#   --image     Use pre-built image from GHCR instead of local build

set -eEuo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
COMPOSE_DIR="$SCRIPT_DIR/../docker"
ENV_FILE="$COMPOSE_DIR/.env"
DEFAULT_IMAGE="ghcr.io/efreet111/engram-dotnet:latest"

# ─── Help ────────────────────────────────────────────────────────────────────

usage() {
  cat <<EOF
Usage: $(basename "$0") <command> [--profile local|remote-server|offline-first|desktop] [--image]

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
  --profile   Deployment profile: local (default), remote-server, offline-first, desktop
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
  ENGRAM_PROFILE       Deployment profile: local (default), remote-server, offline-first, desktop
  ENGRAM_DB_MODE       Database mode: external (default), embedded
  ENGRAM_PG_HOST, ENGRAM_PG_PASSWORD  Required for remote-server and desktop profiles
  ENGRAM_SERVER_URL    Required for offline-first and desktop profiles
  ENGRAM_USER          Required for remote-server, offline-first, and desktop profiles
  ENGRAM_IMAGE         Override image (default: ${DEFAULT_IMAGE})

Safety checks:
  - Validates .env is not committed to git (secrets protection)
  - Validates required environment variables per profile
  - remote-server profile rejects localhost PG connections

EOF
}

# ─── Environment ─────────────────────────────────────────────────────────────

load_env() {
  if [[ -f "$ENV_FILE" ]]; then
    set -a; source "$ENV_FILE"; set +a
  fi
}

get_compose_file() {
  local db_mode="${ENGRAM_DB_MODE:-external}"
  local compose_file
  if [[ "$db_mode" == "embedded" ]]; then
    compose_file="$COMPOSE_DIR/docker-compose.embedded.yml"
    if [[ ! -f "$compose_file" ]]; then
      echo "Error: docker-compose.embedded.yml not found. Did you run Phase 4 setup?" >&2
      echo "  Falling back to docker-compose.yml with ENGRAM_DB_MODE=external" >&2
      compose_file="$COMPOSE_DIR/docker-compose.yml"
    fi
  else
    compose_file="$COMPOSE_DIR/docker-compose.yml"
  fi
  echo "$compose_file"
}

compose_cmd() {
  local compose_file
  compose_file=$(get_compose_file)
  docker compose -f "$compose_file" "$@"
}

# ─── Validation ──────────────────────────────────────────────────────────────

validate_profile() {
  local profile="${ENGRAM_PROFILE:-local}"

  case "$profile" in
    local)
      echo "  ✓ Profile: local (SQLite, no sync)"
      ;;
    remote-server)
      local errors=0
      # Validate PG connection — either ENGRAM_PG_CONNECTION directly OR individual vars
      if [[ -z "${ENGRAM_PG_CONNECTION:-}" && -z "${ENGRAM_PG_HOST:-}" ]]; then
        echo "  ✗ Error: ENGRAM_PROFILE=remote-server requires ENGRAM_PG_CONNECTION or ENGRAM_PG_HOST." >&2
        ((errors++))
      fi
      if [[ -z "${ENGRAM_PG_CONNECTION:-}" && -z "${ENGRAM_PG_PASSWORD:-}" ]]; then
        echo "  ✗ Error: ENGRAM_PROFILE=remote-server requires ENGRAM_PG_PASSWORD." >&2
        ((errors++))
      fi
      # Safety: reject localhost connections for remote-server profile
      if [[ -n "${ENGRAM_PG_CONNECTION:-}" ]]; then
        local lower
        lower=$(echo "${ENGRAM_PG_CONNECTION}" | tr '[:upper:]' '[:lower:]')
        if echo "$lower" | grep -qE '(host|server|data source)\s*=\s*(localhost|127\.0\.0\.1|::1)'; then
          echo "  ✗ Error: ENGRAM_PROFILE=remote-server must NOT use localhost in ENGRAM_PG_CONNECTION." >&2
          ((errors++))
        fi
      elif [[ -n "${ENGRAM_PG_HOST:-}" ]]; then
        local lower
        lower=$(echo "${ENGRAM_PG_HOST}" | tr '[:upper:]' '[:lower:]')
        if [[ "$lower" == "localhost" || "$lower" == "127.0.0.1" || "$lower" == "::1" ]]; then
          echo "  ✗ Error: ENGRAM_PROFILE=remote-server must NOT use localhost for PG host." >&2
          ((errors++))
        fi
      fi
      # ENGRAM_USER is optional for remote-server (server doesn't need identity, clients identify via header)
      if [[ $errors -eq 0 ]]; then
        echo "  ✓ Profile: remote-server (PostgreSQL, no sync)"
      fi
      return $errors
      ;;
    offline-first)
      local errors=0
      if [[ -z "${ENGRAM_SERVER_URL:-}" ]]; then
        echo "  ✗ Error: ENGRAM_PROFILE=offline-first requires ENGRAM_SERVER_URL but it is not set." >&2
        ((errors++))
      fi
      if [[ -z "${ENGRAM_USER:-}" ]]; then
        echo "  ✗ Error: ENGRAM_PROFILE=offline-first requires ENGRAM_USER but it is not set." >&2
        ((errors++))
      fi
      if [[ $errors -eq 0 ]]; then
        echo "  ✓ Profile: offline-first (SQLite + SyncManager)"
      fi
      return $errors
      ;;
    desktop)
      local errors=0
      if [[ -z "${ENGRAM_PG_CONNECTION:-}" ]]; then
        echo "  ✗ Error: ENGRAM_PROFILE=desktop requires ENGRAM_PG_CONNECTION but it is not set." >&2
        ((errors++))
      fi
      if [[ -z "${ENGRAM_SERVER_URL:-}" ]]; then
        echo "  ✗ Error: ENGRAM_PROFILE=desktop requires ENGRAM_SERVER_URL but it is not set." >&2
        ((errors++))
      fi
      if [[ -z "${ENGRAM_USER:-}" ]]; then
        echo "  ✗ Error: ENGRAM_PROFILE=desktop requires ENGRAM_USER but it is not set." >&2
        ((errors++))
      fi
      if [[ $errors -eq 0 ]]; then
        echo "  ✓ Profile: desktop (PostgreSQL + SyncManager)"
      fi
      return $errors
      ;;
    *)
      echo "  ✗ Error: Unknown profile '$profile'. Use local, remote-server, offline-first, or desktop." >&2
      return 1
      ;;
  esac
}

validate_db_mode() {
  local db_mode="${ENGRAM_DB_MODE:-external}"
  case "$db_mode" in
    external) echo "  ✓ DB mode: external (PostgreSQL on host or network)" ;;
    embedded) echo "  ✓ DB mode: embedded (PostgreSQL as Docker service)" ;;
    *)
      echo "  ✗ Error: Unknown ENGRAM_DB_MODE='$db_mode'. Use external or embedded." >&2
      return 1
      ;;
  esac
}

validate_env_safety() {
  # Check that .env file is NOT tracked by git (would expose secrets)
  if [[ ! -f "$ENV_FILE" ]]; then
    echo "  ⚠ Warning: $ENV_FILE not found. Create it from .env.example:"
    echo "    cp docker/.env.example docker/.env"
    return 0
  fi

  if git -C "$COMPOSE_DIR" ls-files --error-unmatch .env >/dev/null 2>&1; then
    echo "  ⚠ WARNING: docker/.env is tracked by git. This may expose secrets." >&2
    echo "    Fix: git rm --cached docker/.env" >&2
    echo "    Or:  git update-index --assume-unchanged docker/.env" >&2
    # Not exiting — just warning, in case of git worktrees or special setups
  else
    echo "  ✓ .env is not tracked by git (safe)"
  fi
}

cmd_validate() {
  echo "=== Validating deployment configuration ==="
  echo ""

  load_env

  local exit_code=0

  echo "Profile:"
  validate_profile || exit_code=1
  echo ""

  echo "Database:"
  validate_db_mode || exit_code=1
  echo ""

  echo "Safety:"
  validate_env_safety
  echo ""

  if [[ $exit_code -eq 0 ]]; then
    echo "✓ Ready to deploy."
  else
    echo ""
    echo "✗ Validation failed. Fix the errors above before deploying."
  fi
  return $exit_code
}

# ─── Commands ────────────────────────────────────────────────────────────────

cmd_start() {
  cd "$COMPOSE_DIR"

  if [[ "${USE_IMAGE_FLAG:-}" == "yes" ]]; then
    # Pull pre-built image and run without local build
    local image="${ENGRAM_IMAGE:-$DEFAULT_IMAGE}"
    echo "=== Using pre-built image: $image ==="
    compose_cmd pull engram
    compose_cmd up -d engram
  else
    # Local build
    echo "=== Building image locally ==="
    compose_cmd up -d --build
  fi

  echo ""
  echo "Waiting for container to become healthy..."
  sleep 5

  # Check health via docker compose ps
  local health
  health=$(compose_cmd ps --format json 2>/dev/null | \
    python3 -c "import sys,json; [print(d.get('Health','')) for d in [json.loads(l) for l in sys.stdin]]" 2>/dev/null || true)

  if [[ "$health" == "healthy" ]]; then
    echo "✓ Container is healthy"
  elif [[ "$health" == "starting" ]]; then
    echo "⚠ Container is starting (health check in progress)"
  else
    # Fallback: try curl inside container or just report status
    echo "Container status:"
    compose_cmd ps 2>/dev/null || true
  fi
}

cmd_stop() {
  cd "$COMPOSE_DIR"
  echo "=== Stopping container ==="
  compose_cmd stop
  echo "✓ Stopped"
}

cmd_remove() {
  cd "$COMPOSE_DIR"
  echo "=== Removing containers and networks (volumes preserved) ==="
  compose_cmd down
  echo "✓ Removed"
}

cmd_recreate() {
  cmd_remove
  echo ""
  cd "$COMPOSE_DIR"
  echo "=== Building image with clean cache ==="
  compose_cmd build --no-cache
  compose_cmd up -d
}

cmd_logs() {
  cd "$COMPOSE_DIR"
  compose_cmd logs "${@:--t --tail=50}"
}

cmd_status() {
  cd "$COMPOSE_DIR"
  echo "=== Container status ==="
  compose_cmd ps 2>/dev/null || echo "  No containers running."
  echo ""

  # Try to get health status via docker inspect
  local health
  health=$(docker inspect --format='{{.State.Health.Status}}' engram 2>/dev/null || echo "no container")
  if [[ "$health" != "no container" ]]; then
    echo "Health: $health"
  fi
}

cmd_restart() {
  cd "$COMPOSE_DIR"
  echo "=== Restarting container ==="
  compose_cmd restart
  echo "✓ Restarted"
}

cmd_backup() {
  local backup_dir="${BACKUP_DIR:-$COMPOSE_DIR/backups}"
  mkdir -p "$backup_dir"
  local ts
  ts=$(date +%Y%m%d_%H%M%S)

  echo "=== Backing up to $backup_dir ==="
  echo ""

  # Backup PostgreSQL data if using external PG mode and a postgres container is running
  if [[ "${ENGRAM_DB_MODE:-external}" == "external" ]]; then
    local pg_container
    pg_container=$(docker ps --format '{{.Names}}' 2>/dev/null | grep -i postgres | head -1 || true)
    if [[ -n "$pg_container" ]]; then
      local pg_user="${ENGRAM_PG_USER:-engram}"
      local pg_db="${ENGRAM_PG_DATABASE:-engram}"
      local pg_file="$backup_dir/engram_pg_${ts}.sql"
      echo "PostgreSQL dump (container: $pg_container, db: $pg_db)..."
      if docker exec "$pg_container" pg_dump -U "$pg_user" "$pg_db" > "$pg_file" 2>/dev/null; then
        echo "  ✓ PostgreSQL dump: $pg_file"
      else
        echo "  ⚠ pg_dump failed. Is the postgres container running and accessible?" >&2
      fi
    else
      echo "  ℹ No postgres container found (ENGRAM_DB_MODE=external). Use pg_dump manually:"
      echo "    pg_dump -U ${ENGRAM_PG_USER:-engram} -h ${ENGRAM_PG_HOST:-localhost} ${ENGRAM_PG_DATABASE:-engram} > backup.sql"
    fi
  fi

  # Backup SQLite data volume if it exists
  local data_dir="$COMPOSE_DIR/../data"
  if [[ -d "$data_dir" ]]; then
    local sqlite_file="$backup_dir/engram_data_${ts}.tar.gz"
    echo "SQLite data backup..."
    if tar -czf "$sqlite_file" -C "$COMPOSE_DIR/.." data 2>/dev/null; then
      echo "  ✓ SQLite backup: $sqlite_file"
    else
      echo "  ⚠ Failed to create SQLite backup" >&2
    fi
  else
    echo "  ℹ No data/ directory found — skipping SQLite backup"
  fi

  echo ""
  echo "✓ Backup complete"
}

cmd_update() {
  local image="${ENGRAM_IMAGE:-$DEFAULT_IMAGE}"
  echo "=== Pulling latest image: $image ==="
  cd "$COMPOSE_DIR"

  docker pull "$image" 2>/dev/null || {
    echo "⚠ Could not pull image '$image'. Falling back to local build." >&2
  }

  echo ""
  cmd_recreate
}

# ─── Main ────────────────────────────────────────────────────────────────────

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
      echo "Unknown argument: $1" >&2
      usage; exit 1
      ;;
  esac
done

# Load env from .env file first
load_env

# Apply profile from args AFTER loading .env (overrides .env if provided)
[[ -n "$PROFILE" ]] && export ENGRAM_PROFILE="$PROFILE"

if [[ -z "$COMMAND" ]]; then
  usage
  exit 1
fi

# Export image flag for cmd_start and cmd_recreate
export USE_IMAGE_FLAG="$USE_IMAGE"

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
