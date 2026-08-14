#!/usr/bin/env bash
# scripts/deploy.sh — Deploy wrapper for engram-dotnet Docker containers.
#
# Sources docker/.env automatically. All configuration goes in that file.
#
# USES `docker run` WITH `-e` FLAGS instead of `docker-compose up`.
# docker-compose v1 has a known interpolation bug where nested ${VAR:-default}
# in YAML environment blocks silently fails. Passing vars directly via `-e`
# avoids the problem entirely.
#
# docker-compose build is still used for local image builds (the build
# section has no variable interpolation, so it's safe).
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
PROJECT_ROOT="$SCRIPT_DIR/.."
ENV_FILE="$COMPOSE_DIR/.env"
DEFAULT_IMAGE="ghcr.io/efreet111/engram-dotnet:latest"
LOCAL_IMAGE="engram-dotnet:latest"

CONTAINER_NAME="engram"
POSTGRES_CONTAINER="engram-postgres"
NETWORK_NAME="engram-net"

# ─── Help ────────────────────────────────────────────────────────────────────

usage() {
  cat <<EOF
Usage: $(basename "$0") <command> [--profile local|remote-server|offline-first|desktop] [--image]

Commands:
  start     Start the container (local build or --image for pre-built)
  stop      Stop the container
  remove    Remove containers and networks (docker rm -f, volumes preserved)
  recreate  Recreate from scratch (stop + remove + start)
  logs      Show logs (add -f for tail, e.g. logs -f)
  status    Show container status and health
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
    # shellcheck disable=SC1090
    set -a; source "$ENV_FILE"; set +a
  fi
}

# ─── Docker run helpers ──────────────────────────────────────────────────────

# Build the array of -e flags for docker run.
# $1: effective PG host used to build ENGRAM_PG_CONNECTION when not set directly.
#     "host.docker.internal" for external mode, "postgres" for embedded mode.
build_env_args() {
  local effective_pg_host="${1:-host.docker.internal}"
  local env_args=()

  # ── Core vars (always passed) ──
  env_args+=("-e" "ENGRAM_DATA_DIR=${ENGRAM_DATA_DIR:-/data/engram}")
  env_args+=("-e" "ENGRAM_PORT=${ENGRAM_PORT:-7437}")
  env_args+=("-e" "ENGRAM_PROFILE=${ENGRAM_PROFILE:-local}")
  env_args+=("-e" "ENGRAM_DB_TYPE=${ENGRAM_DB_TYPE:-postgres}")

  # ── PostgreSQL connection string ──
  # KEY FIX: build the string in bash instead of relying on docker-compose
  # YAML interpolation which silently fails with nested ${VAR:-default}.
  local pg_conn="${ENGRAM_PG_CONNECTION:-}"
  if [[ -z "$pg_conn" ]]; then
    pg_conn="Host=${effective_pg_host};Port=${ENGRAM_PG_PORT:-5432};Database=${ENGRAM_PG_DATABASE:-engram};Username=${ENGRAM_PG_USER:-engram};Password=${ENGRAM_PG_PASSWORD}"
  fi
  env_args+=("-e" "ENGRAM_PG_CONNECTION=${pg_conn}")

  # ── Optional vars (only pass if set in .env) ──
  [[ -n "${ENGRAM_USER:-}" ]] && env_args+=("-e" "ENGRAM_USER=${ENGRAM_USER}")
  [[ -n "${ENGRAM_JWT_SECRET:-}" ]] && env_args+=("-e" "ENGRAM_JWT_SECRET=${ENGRAM_JWT_SECRET}")
  [[ -n "${ENGRAM_CORS_ORIGINS:-}" ]] && env_args+=("-e" "ENGRAM_CORS_ORIGINS=${ENGRAM_CORS_ORIGINS}")

  printf '%s\n' "${env_args[@]}"
}

# Build the `docker run` array for the engram container and launch it.
# $1: image name
# $2: "embedded" or "external"
run_engram_container() {
  local image="$1"
  local db_mode="${2:-external}"
  local pg_host

  # Resolve data dir to absolute path
  local data_dir_host="${ENGRAM_DATA_DIR_HOST:-./data}"
  if [[ "$data_dir_host" != /* ]]; then
    data_dir_host="$COMPOSE_DIR/$data_dir_host"
  fi
  mkdir -p "$data_dir_host"

  # Build env arg array
  local env_args=()
  if [[ "$db_mode" == "embedded" ]]; then
    pg_host="postgres"
  else
    pg_host="${ENGRAM_PG_HOST:-host.docker.internal}"
  fi

  while IFS= read -r line; do
    env_args+=("$line")
  done < <(build_env_args "$pg_host")

  # Remove existing container (stop + rm in one shot)
  docker rm -f "$CONTAINER_NAME" 2>/dev/null || true

  echo "=== Starting engram container ==="

  if [[ "$db_mode" == "embedded" ]]; then
    # Embedded mode: use shared network so engram can reach postgres by name
    docker network inspect "$NETWORK_NAME" >/dev/null 2>&1 || \
      docker network create "$NETWORK_NAME"

    docker run -d \
      --name "$CONTAINER_NAME" \
      --restart unless-stopped \
      --network "$NETWORK_NAME" \
      -p 7437:7437 \
      -v "${data_dir_host}:/data/engram" \
      "${env_args[@]}" \
      "$image"
  else
    # External mode: add host-gateway so container can reach host PostgreSQL
    docker run -d \
      --name "$CONTAINER_NAME" \
      --restart unless-stopped \
      -p 7437:7437 \
      --add-host "host.docker.internal:host-gateway" \
      -v "${data_dir_host}:/data/engram" \
      "${env_args[@]}" \
      "$image"
  fi

  echo ""
  echo "Waiting for container to become healthy..."
  sleep 5

  # Check health
  local health
  health=$(docker inspect --format='{{.State.Health.Status}}' "$CONTAINER_NAME" 2>/dev/null || echo "no container")

  if [[ "$health" == "healthy" ]]; then
    echo "✓ Container is healthy"
  elif [[ "$health" == "starting" ]]; then
    echo "⚠ Container is starting (health check in progress)"
  else
    echo "Container status: $health"
  fi
}

start_embedded_postgres() {
  # Start embedded PostgreSQL if not already running
  if docker ps --format '{{.Names}}' | grep -q "^${POSTGRES_CONTAINER}$"; then
    echo "  ℹ Embedded PostgreSQL already running"
    return 0
  fi

  echo "=== Starting embedded PostgreSQL ==="

  docker network inspect "$NETWORK_NAME" >/dev/null 2>&1 || \
    docker network create "$NETWORK_NAME"

  docker rm -f "$POSTGRES_CONTAINER" 2>/dev/null || true

  docker run -d \
    --name "$POSTGRES_CONTAINER" \
    --restart unless-stopped \
    --network "$NETWORK_NAME" \
    -p "${ENGRAM_PG_PORT:-5432}:5432" \
    -e "POSTGRES_DB=${ENGRAM_PG_DATABASE:-engram}" \
    -e "POSTGRES_USER=${ENGRAM_PG_USER:-engram}" \
    -e "POSTGRES_PASSWORD=${ENGRAM_PG_PASSWORD}" \
    -v pgdata:/var/lib/postgresql/data \
    postgres:16-alpine

  echo "  ✓ Embedded PostgreSQL started (waiting for readiness...)"

  # Wait for postgres to be ready
  local retries=30
  while [[ $retries -gt 0 ]]; do
    if docker exec "$POSTGRES_CONTAINER" pg_isready -U "${ENGRAM_PG_USER:-engram}" -d "${ENGRAM_PG_DATABASE:-engram}" >/dev/null 2>&1; then
      echo "  ✓ PostgreSQL is ready"
      return 0
    fi
    sleep 1
    ((retries--))
  done

  echo "  ⚠ PostgreSQL did not become ready in time" >&2
  return 1
}

stop_embedded_postgres() {
  if docker ps --format '{{.Names}}' | grep -q "^${POSTGRES_CONTAINER}$"; then
    echo "=== Stopping embedded PostgreSQL ==="
    docker stop "$POSTGRES_CONTAINER" 2>/dev/null || true
    echo "  ✓ Stopped"
  fi
}

remove_embedded_postgres() {
  docker rm -f "$POSTGRES_CONTAINER" 2>/dev/null || true
  docker network rm "$NETWORK_NAME" 2>/dev/null || true
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
    embedded) echo "  ✓ DB mode: embedded (PostgreSQL as separate container)" ;;
    *)
      echo "  ✗ Error: Unknown ENGRAM_DB_MODE='$db_mode'. Use external or embedded." >&2
      return 1
      ;;
  esac
}

validate_env_safety() {
  if [[ ! -f "$ENV_FILE" ]]; then
    echo "  ⚠ Warning: $ENV_FILE not found. Create it from .env.example:"
    echo "    cp docker/.env.example docker/.env"
    return 0
  fi

  if git -C "$COMPOSE_DIR" ls-files --error-unmatch .env >/dev/null 2>&1; then
    echo "  ⚠ WARNING: docker/.env is tracked by git. This may expose secrets." >&2
    echo "    Fix: git rm --cached docker/.env" >&2
    echo "    Or:  git update-index --assume-unchanged docker/.env" >&2
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
  local db_mode="${ENGRAM_DB_MODE:-external}"
  local image

  if [[ "${USE_IMAGE_FLAG:-}" == "yes" ]]; then
    image="${ENGRAM_IMAGE:-$DEFAULT_IMAGE}"
    echo "=== Pulling pre-built image: $image ==="
    docker pull "$image"
  else
    # Local build using docker compose build (safe — build section has no ${VAR} interpolation)
    image="$LOCAL_IMAGE"
    echo "=== Building image locally ==="
    # docker compose build respects the build section from docker-compose.yml
    # but we only use it for building, not for running
    cd "$COMPOSE_DIR"
    docker compose -f docker-compose.yml build
    cd "$PROJECT_ROOT"
  fi

  # If embedded mode, start PostgreSQL first
  if [[ "$db_mode" == "embedded" ]]; then
    start_embedded_postgres
  fi

  run_engram_container "$image" "$db_mode"
}

cmd_stop() {
  local db_mode="${ENGRAM_DB_MODE:-external}"

  echo "=== Stopping engram container ==="
  if docker ps --format '{{.Names}}' | grep -q "^${CONTAINER_NAME}$"; then
    docker stop "$CONTAINER_NAME"
    echo "✓ Stopped"
  else
    echo "  ℹ Container is not running"
  fi

  if [[ "$db_mode" == "embedded" ]]; then
    stop_embedded_postgres
  fi
}

cmd_remove() {
  local db_mode="${ENGRAM_DB_MODE:-external}"

  echo "=== Removing containers (volumes preserved) ==="
  docker rm -f "$CONTAINER_NAME" 2>/dev/null && echo "  ✓ Removed engram" || echo "  ℹ engram container not found"

  if [[ "$db_mode" == "embedded" ]]; then
    remove_embedded_postgres
    echo "  ✓ Removed embedded PostgreSQL and network"
  fi

  echo "✓ Done"
}

cmd_recreate() {
  load_env
  local db_mode="${ENGRAM_DB_MODE:-external}"

  cmd_remove
  echo ""

  # Build fresh image (--no-cache equivalent: docker compose build --no-cache)
  echo "=== Building image with clean cache ==="
  cd "$COMPOSE_DIR"
  docker compose -f docker-compose.yml build --no-cache
  cd "$PROJECT_ROOT"

  if [[ "$db_mode" == "embedded" ]]; then
    start_embedded_postgres
  fi

  run_engram_container "$LOCAL_IMAGE" "$db_mode"
}

cmd_logs() {
  if [[ $# -eq 0 ]]; then
    docker logs -t --tail=50 "$CONTAINER_NAME" 2>/dev/null || echo "  No container found."
  else
    docker logs "$CONTAINER_NAME" "$@" 2>/dev/null || echo "  No container found."
  fi
}

cmd_status() {
  echo "=== Container status ==="
  if docker ps --format 'table {{.Names}}\t{{.Status}}\t{{.Ports}}' --filter "name=engram" 2>/dev/null; then
    echo ""
  else
    echo "  No containers running."
    echo ""
  fi

  # Health status
  local health
  health=$(docker inspect --format='{{.State.Health.Status}}' "$CONTAINER_NAME" 2>/dev/null || echo "no container")
  if [[ "$health" != "no container" ]]; then
    echo "Health: $health"
  else
    echo "  ℹ engram container not found"
  fi

  # Embedded postgres status
  if [[ "${ENGRAM_DB_MODE:-external}" == "embedded" ]]; then
    echo ""
    if docker ps --format '{{.Names}}' | grep -q "^${POSTGRES_CONTAINER}$"; then
      echo "Embedded PostgreSQL: running"
    else
      echo "Embedded PostgreSQL: not running"
    fi
  fi
}

cmd_restart() {
  echo "=== Restarting container ==="
  if docker ps --format '{{.Names}}' | grep -q "^${CONTAINER_NAME}$"; then
    docker restart "$CONTAINER_NAME"
    echo "✓ Restarted"
  else
    echo "  ℹ Container is not running. Use 'start' instead."
  fi
}

cmd_backup() {
  local backup_dir="${BACKUP_DIR:-$COMPOSE_DIR/backups}"
  mkdir -p "$backup_dir"
  local ts
  ts=$(date +%Y%m%d_%H%M%S)

  echo "=== Backing up to $backup_dir ==="
  echo ""

  load_env
  local db_mode="${ENGRAM_DB_MODE:-external}"

  # Backup PostgreSQL data
  if [[ "$db_mode" == "embedded" ]]; then
    # Embedded: dump from the postgres container
    if docker ps --format '{{.Names}}' | grep -q "^${POSTGRES_CONTAINER}$"; then
      local pg_user="${ENGRAM_PG_USER:-engram}"
      local pg_db="${ENGRAM_PG_DATABASE:-engram}"
      local pg_file="$backup_dir/engram_pg_${ts}.sql"
      echo "PostgreSQL dump (embedded container: $POSTGRES_CONTAINER, db: $pg_db)..."
      if docker exec "$POSTGRES_CONTAINER" pg_dump -U "$pg_user" "$pg_db" > "$pg_file" 2>/dev/null; then
        echo "  ✓ PostgreSQL dump: $pg_file"
      else
        echo "  ⚠ pg_dump failed" >&2
      fi
    else
      echo "  ⚠ Embedded PostgreSQL is not running — skipping pg_dump" >&2
    fi
  else
    # External: try to find a running postgres container
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
        echo "  ⚠ pg_dump failed" >&2
      fi
    else
      echo "  ℹ No postgres container found. Use pg_dump manually:"
      echo "    pg_dump -U ${ENGRAM_PG_USER:-engram} -h ${ENGRAM_PG_HOST:-localhost} ${ENGRAM_PG_DATABASE:-engram} > backup.sql"
    fi
  fi

  # Backup SQLite data volume if it exists
  local data_dir="${ENGRAM_DATA_DIR_HOST:-./data}"
  if [[ "$data_dir" != /* ]]; then
    data_dir="$COMPOSE_DIR/$data_dir"
  fi
  if [[ -d "$data_dir" ]]; then
    local sqlite_file="$backup_dir/engram_data_${ts}.tar.gz"
    echo "SQLite data backup..."
    if tar -czf "$sqlite_file" -C "$(dirname "$data_dir")" "$(basename "$data_dir")" 2>/dev/null; then
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
  docker pull "$image" 2>/dev/null || {
    echo "⚠ Could not pull image '$image'. Falling back to local build." >&2
    image="$LOCAL_IMAGE"
    cd "$COMPOSE_DIR"
    docker compose -f docker-compose.yml build
    cd "$PROJECT_ROOT"
  }

  echo ""
  local db_mode="${ENGRAM_DB_MODE:-external}"
  cmd_remove

  if [[ "$db_mode" == "embedded" ]]; then
    start_embedded_postgres
  fi

  run_engram_container "$image" "$db_mode"
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

# Export image flag for cmd_start
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
