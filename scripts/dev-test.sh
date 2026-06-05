#!/usr/bin/env bash
# scripts/dev-test.sh — T3 (Docker + Postgres integration) pre-commit check.
#
# Builds the current source, runs it in a container pointing to the host's
# Postgres via host.docker.internal, and runs a smoke test. Use this BEFORE
# commit/push to catch SQLite-specific bugs that escape T1/T2.
#
# Usage:
#   PG_PASS=tu_password bash scripts/dev-test.sh
#
# Env vars (with defaults):
#   PORT          - host port to bind (default 7437)
#   IMAGE_TAG     - Docker image tag (default engram-local:dev)
#   CONTAINER     - container name (default engram-test)
#   ENGRAM_VERSION - build arg version tag (default v0.3.0; must be semver)
#   PG_HOST       - Postgres host (default host.docker.internal)
#   PG_PORT       - Postgres port (default 5432)
#   PG_DB         - Postgres database (default engram-dev)
#   PG_USER       - Postgres user (default postgres)
#   PG_PASS       - Postgres password (REQUIRED - no default)
#   LOG_LEVEL     - ENGRAM_LOG_LEVEL (default Information)
#
# Requisitos:
#   - Docker 20.10+ (para host.docker.internal via host-gateway)
#   - Postgres local accesible en localhost:5432 con DB engram_dev creada
#   - createdb -U postgres engram_dev  (si no existe)

set -euo pipefail

PORT="${PORT:-7437}"
IMAGE_TAG="${IMAGE_TAG:-engram-local:dev}"
CONTAINER="${CONTAINER:-engram-test}"
ENGRAM_VERSION="${ENGRAM_VERSION:-v0.3.0}"
PG_HOST="${PG_HOST:-host.docker.internal}"
PG_PORT="${PG_PORT:-5432}"
PG_DB="${PG_DB:-engram_dev}"
PG_USER="${PG_USER:-postgres}"
LOG_LEVEL="${LOG_LEVEL:-Information}"

if [[ -z "${PG_PASS:-}" ]]; then
  echo "ERROR: PG_PASS env var is required."
  echo "Usage: PG_PASS=tu_password bash scripts/dev-test.sh"
  exit 1
fi

# Cleanup previous run if exists
if docker ps -a --format '{{.Names}}' | grep -q "^${CONTAINER}$"; then
  echo "=== Cleanup: removing previous container ${CONTAINER} ==="
  docker rm -f "${CONTAINER}" > /dev/null
fi

echo "=== T3 step 1/4: build image ${IMAGE_TAG} (version ${ENGRAM_VERSION}) ==="
docker build -t "${IMAGE_TAG}" -f Dockerfile . --build-arg "ENGRAM_VERSION=${ENGRAM_VERSION}"

echo ""
echo "=== T3 step 2/4: run container on port ${PORT} ==="
docker run -d \
  --name "${CONTAINER}" \
  -p "${PORT}:7437" \
  --add-host=host.docker.internal:host-gateway \
  -e ENGRAM_DB_TYPE=postgres \
  -e "ENGRAM_PG_CONNECTION=Host=${PG_HOST};Port=${PG_PORT};Database=${PG_DB};Username=${PG_USER};Password=${PG_PASS}" \
  -e "ENGRAM_LOG_LEVEL=${LOG_LEVEL}" \
  "${IMAGE_TAG}" > /dev/null

echo ""
echo "=== T3 step 3/4: wait for /health (max 30s) ==="
HEALTHY=""
for i in $(seq 1 30); do
  if curl -sf "http://localhost:${PORT}/health" > /dev/null 2>&1; then
    HEALTHY="${i}s"
    break
  fi
  sleep 1
done

if [[ -z "${HEALTHY}" ]]; then
  echo "ERROR: container did not become healthy in 30s."
  echo "--- last 20 log lines ---"
  docker logs --tail 20 "${CONTAINER}"
  echo "---"
  echo "Stopping container for inspection..."
  docker stop "${CONTAINER}" > /dev/null
  exit 1
fi
echo "  healthy after ${HEALTHY}"

echo ""
echo "=== T3 step 4/4: smoke test ==="
echo -n "  /health: "
curl -s "http://localhost:${PORT}/health"
echo ""

echo -n "  /stats (sessions, observations, prompts): "
STATS=$(curl -s "http://localhost:${PORT}/stats")
echo "${STATS}" | jq -r '"\(.total_sessions), \(.total_observations), \(.total_prompts)"' 2>/dev/null || echo "${STATS}"

echo -n "  /sync/status: "
curl -s "http://localhost:${PORT}/sync/status" | jq -c '{sync_enabled,phase}' 2>/dev/null || echo "(check manually)"

echo ""
echo "=== T3 PASSED ==="
echo "  Container: ${CONTAINER} (port ${PORT})"
echo "  Image:     ${IMAGE_TAG}"
echo "  Backend:   postgres (${PG_HOST}:${PG_PORT}/${PG_DB})"
echo ""
echo "Useful commands:"
echo "  docker logs -f ${CONTAINER}    # tail logs (JSON structured)"
echo "  docker exec -it ${CONTAINER} sh  # shell into container"
echo "  docker stop ${CONTAINER}        # stop"
echo "  docker rm ${CONTAINER}          # remove"
