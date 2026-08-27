#!/usr/bin/env bash
# scripts/sync-integration-test.sh — T3 integration test: offline-first ↔ remote-server
# Dos containers en red compartida: SQLite local (offline) + PostgreSQL (remote).
#
# Usage: bash scripts/sync-integration-test.sh
#
set -euo pipefail

# ─── Config ──────────────────────────────────────────────────────────────────
NETWORK="engram-sync-test"
OFFLINE_NAME="engram-offline"
SERVER_NAME="engram-sync-server"
PG_CONTAINER="${SERVER_NAME}-postgres"
IMAGE_TAG="engram-local:dev"
PG_VERSION="${PG_VERSION:-16}"
PG_USER="${PG_USER:-postgres}"
PG_PASS="${PG_PASS:-test_password_change_me}"
PG_DB="${PG_DB:-engram_dev}"
SERVER_PORT="${SERVER_PORT:-7437}"
OFFLINE_PORT="${OFFLINE_PORT:-7438}"
# Usar puertos distintos en host para evitar conflicto con engram nativo (pid 197387)
SERVER_HOST_PORT="${SERVER_HOST_PORT:-7441}"
OFFLINE_HOST_PORT="${OFFLINE_HOST_PORT:-7440}"
SERVER_URL="http://${SERVER_NAME}:${SERVER_PORT}"
OFFLINE_DATA_DIR="/tmp/engram-offline-data"

# ─── Cleanup previous run ────────────────────────────────────────────────────
cleanup() {
    echo "=== Cleanup ==="
    docker rm -f "${OFFLINE_NAME}" 2>/dev/null || true
    docker rm -f "${SERVER_NAME}" 2>/dev/null || true
    docker rm -f "${PG_CONTAINER}" 2>/dev/null || true
    docker network rm "${NETWORK}" 2>/dev/null || true
    echo "  cleaned up"
}

trap cleanup EXIT

echo "=== Sync Integration Test ==="
echo "  Server URL: ${SERVER_URL}"
echo "  Postgres: ${PG_USER}/${PG_DB}"
echo ""

# ─── Step 1: Network ─────────────────────────────────────────────────────────
echo "=== Step 1: Create network ==="
docker network create "${NETWORK}" 2>/dev/null || echo "  network already exists"
echo "  done"

# ─── Step 2: Postgres inside server container ─────────────────────────────────
echo ""
echo "=== Step 2: Start Postgres (inside ${PG_CONTAINER}) ==="
docker run -d \
    --name "${PG_CONTAINER}" \
    --network "${NETWORK}" \
    -e POSTGRES_USER="${PG_USER}" \
    -e POSTGRES_PASSWORD="${PG_PASS}" \
    -e POSTGRES_DB="${PG_DB}" \
    postgres:${PG_VERSION}-alpine \
    > /dev/null
echo "  waiting for Postgres to be healthy..."
for i in $(seq 1 30); do
    if docker exec "${PG_CONTAINER}" pg_isready -U "${PG_USER}" -d "${PG_DB}" > /dev/null 2>&1; then
        echo "  healthy after ${i}s"
        break
    fi
    sleep 1
done

# ─── Step 3: Build image ──────────────────────────────────────────────────────
echo ""
echo "=== Step 3: Build image ${IMAGE_TAG} ==="
docker build -t "${IMAGE_TAG}" -f Dockerfile . \
    --build-arg ENGRAM_VERSION=v1.3.0 \
    > /dev/null 2>&1
echo "  done"

# ─── Step 4: Start remote-server container ───────────────────────────────────
echo ""
echo "=== Step 4: Start ${SERVER_NAME} (remote-server profile) ==="
docker rm -f "${SERVER_NAME}" 2>/dev/null || true
docker run -d \
    --name "${SERVER_NAME}" \
    --network "${NETWORK}" \
    -p "${SERVER_HOST_PORT}:${SERVER_PORT}" \
    --add-host=host.docker.internal:host-gateway \
    -e ENGRAM_PROFILE=remote-server \
    -e ENGRAM_DB_TYPE=postgres \
    -e "ENGRAM_PG_CONNECTION=Host=${PG_CONTAINER};Port=5432;Database=${PG_DB};Username=${PG_USER};Password=${PG_PASS}" \
    -e "ENGRAM_PORT=${SERVER_PORT}" \
    -e "ASPNETCORE_URLS=http://+:${SERVER_PORT}" \
    -e "ENGRAM_SERVER_URL=${SERVER_URL}" \
    -e ENGRAM_LOG_LEVEL=Information \
    "${IMAGE_TAG}" \
    > /dev/null
echo "  waiting for /health..."
for i in $(seq 1 30); do
    if curl -sf "${SERVER_URL}/health" > /dev/null 2>&1; then
        echo "  healthy after ${i}s"
        break
    fi
    sleep 1
done

# ─── Step 5: Start offline-first container ───────────────────────────────────
echo ""
echo "=== Step 5: Start ${OFFLINE_NAME} (offline-first profile) ==="
docker rm -f "${OFFLINE_NAME}" 2>/dev/null || true
mkdir -p "${OFFLINE_DATA_DIR}"
docker run -d \
    --name "${OFFLINE_NAME}" \
    --network "${NETWORK}" \
    -p "${OFFLINE_HOST_PORT}:${SERVER_PORT}" \
    -v "${OFFLINE_DATA_DIR}:${OFFLINE_DATA_DIR}" \
    -e ENGRAM_PROFILE=offline-first \
    -e ENGRAM_DB_TYPE=sqlite \
    -e "ENGRAM_DATA_DIR=${OFFLINE_DATA_DIR}" \
    -e "ENGRAM_SERVER_URL=${SERVER_URL}" \
    -e ENGRAM_SYNC_ENABLED=true \
    -e ENGRAM_SYNC_TARGET=cloud \
    -e ENGRAM_USER=testuser \
    -e ENGRAM_PROJECT=test-project \
    -e "ENGRAM_PORT=${SERVER_PORT}" \
    -e "ASPNETCORE_URLS=http://+:${SERVER_PORT}" \
    -e ENGRAM_LOG_LEVEL=Information \
    "${IMAGE_TAG}" \
    > /dev/null
echo "  waiting for /health..."
for i in $(seq 1 30); do
    if curl -sf "http://localhost:${OFFLINE_PORT}/health" > /dev/null 2>&1; then
        echo "  healthy after ${i}s"
        break
    fi
    sleep 1
done

# ─── Step 6: Smoke tests ─────────────────────────────────────────────────────
echo ""
echo "=== Step 6: Smoke tests ==="

echo "  [OFFLINE] /health:"
curl -s "http://localhost:${OFFLINE_HOST_PORT}/health" | jq .
echo ""

echo "  [SERVER] /health:"
curl -s "http://localhost:${SERVER_HOST_PORT}/health" | jq .
echo ""

echo "  [OFFLINE] /stats:"
curl -s "http://localhost:${OFFLINE_HOST_PORT}/stats" | jq .
echo ""

echo "  [SERVER] /stats:"
curl -s "http://localhost:${SERVER_HOST_PORT}/stats" | jq .
echo ""

echo "  [OFFLINE] /sync/status:"
curl -s "http://localhost:${OFFLINE_HOST_PORT}/sync/status" | jq .
echo ""

echo "  [SERVER] /sync/status:"
curl -s "http://localhost:${SERVER_HOST_PORT}/sync/status" | jq .
echo ""

# ─── Step 7: Enroll project ───────────────────────────────────────────────────
echo ""
echo "=== Step 7: Enroll project on server ==="
ENROLL_RESP=$(curl -s -X POST "http://localhost:${SERVER_HOST_PORT}/sync/enroll" \
    -H "Content-Type: application/json" \
    -d '{"project": "test-project", "user": "testuser"}')
echo "  Response: ${ENROLL_RESP}" | jq .

# ─── Step 8: Create observation on OFFLINE ────────────────────────────────────
echo ""
echo "=== Step 8: Create observation on offline (SQLite) ==="
OBS_DATA=$(jq -n '{
    content: "Test observation from offline-first container",
    content_type: "text/markdown",
    project: "test-project"
}')
OBS_RESP=$(curl -s -X POST "http://localhost:${OFFLINE_HOST_PORT}/observations" \
    -H "Content-Type: application/json" \
    -d "${OBS_DATA}")
echo "  Response: ${OBS_RESP}" | jq .
OFFLINE_OBS_ID=$(echo "${OBS_RESP}" | jq -r '.id // .entity_key // empty')

# ─── Step 9: Push from offline to server ─────────────────────────────────────
echo ""
echo "=== Step 9: Push from offline to server ==="
PUSH_RESP=$(curl -s -X POST "http://localhost:${OFFLINE_HOST_PORT}/sync/push" \
    -H "Content-Type: application/json" \
    -d '{}')
echo "  Response: ${PUSH_RESP}" | jq .

# ─── Step 10: Verify server received observation ─────────────────────────────
echo ""
echo "=== Step 10: Verify server received observation ==="
SERVER_STATS=$(curl -s "http://localhost:${SERVER_HOST_PORT}/stats")
echo "  Server stats: ${SERVER_STATS}" | jq .
SERVER_OBS_COUNT=$(echo "${SERVER_STATS}" | jq '.total_observations // 0')
echo "  Server observation count: ${SERVER_OBS_COUNT}"

if [[ "${SERVER_OBS_COUNT}" -gt 0 ]]; then
    echo "  ✅ OBSERVATION REACHED SERVER"
else
    echo "  ❌ OBSERVATION DID NOT REACH SERVER"
fi

# ─── Step 11: Pull from server to offline ────────────────────────────────────
echo ""
echo "=== Step 11: Pull from server to offline ==="
PULL_RESP=$(curl -s -X POST "http://localhost:${OFFLINE_HOST_PORT}/sync/pull" \
    -H "Content-Type: application/json" \
    -d '{}')
echo "  Pull response: ${PULL_RESP}" | jq .

# ─── Step 12: Offline /sync/status ──────────────────────────────────────────
echo ""
echo "=== Step 12: Offline sync status after pull ==="
curl -s "http://localhost:${OFFLINE_PORT}/sync/status" | jq .

# ─── Final ───────────────────────────────────────────────────────────────────
echo ""
echo "=== Integration Test Complete ==="
echo "  Containers:"
echo "    - ${SERVER_NAME} (remote-server): ${SERVER_URL}"
echo "    - ${OFFLINE_NAME} (offline-first): http://localhost:${OFFLINE_PORT}"
echo "    - ${PG_CONTAINER} (postgres): internal"
echo "  Network: ${NETWORK}"
echo ""
echo "  Useful commands:"
echo "    docker logs -f ${SERVER_NAME}"
echo "    docker logs -f ${OFFLINE_NAME}"
echo "    docker exec ${PG_CONTAINER} psql -U ${PG_USER} -d ${PG_DB} -c 'SELECT * FROM observations;'"
echo ""
echo "  To stop and clean up:"
echo "    docker rm -f ${OFFLINE_NAME} ${SERVER_NAME} ${PG_CONTAINER}; docker network rm ${NETWORK}"
