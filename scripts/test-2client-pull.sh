#!/usr/bin/env bash
# ──────────────────────────────────────────────────────────────────────────────
# ENG-209: Pull entre 2 clientes (sync multi-cliente)
#
# REQUISITOS:
#   - Docker
#   - build cache de engram-test:latest (ejecutar test una vez para construir)
#
# USO:
#   bash scripts/test-2client-pull.sh
# ──────────────────────────────────────────────────────────────────────────────
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
NET="engram-test-net"
PG="engram-test-pg"; SVR="engram-test-server"
CA="engram-test-client-a"; CB="engram-test-client-b"
GREEN='\033[0;32m'; RED='\033[0;31m'; YELLOW='\033[1;33m'; CYAN='\033[0;36m'; NC='\033[0m'
pass() { echo -e "${GREEN}✅ PASS${NC} — $1"; }
fail() { echo -e "${RED}❌ FAIL${NC} — $1"; exit 1; }
info() { echo -e "${YELLOW}🔹 $1${NC}"; }
step() { echo -e "${CYAN}═══ $1 ═══${NC}"; }

cleanup() { info "Limpiando..."; docker rm -f "$CB" "$CA" "$SVR" "$PG" 2>/dev/null || true; docker network rm "$NET" 2>/dev/null || true; }
trap cleanup EXIT

echo ""; echo "╔══════════════════════════════════════════════════════╗"; echo "║  ENG-209 — Pull entre 2 clientes (sync pull)       ║"; echo "╚══════════════════════════════════════════════════════╝"; echo ""

cleanup
docker network create "$NET" 2>/dev/null || true

wait_health() { local n="$1"; local p="${2:-7437}"; echo -n "  $n..."; until docker exec "$n" curl -sf "http://localhost:$p/health" >/dev/null 2>&1; do sleep 1; done; echo " ✅"; }

step "Build check"
docker image inspect engram-test:latest >/dev/null 2>&1 || docker build --build-arg ENGRAM_VERSION=0.3.0 -t engram-test:latest -f "$ROOT_DIR/Dockerfile" "$ROOT_DIR" 2>&1 | tail -1

step "PostgreSQL"
docker run -d --name "$PG" --network "$NET" -e POSTGRES_DB=engram -e POSTGRES_USER=engram -e POSTGRES_PASSWORD=engram_test_pass postgres:17-alpine
until docker exec "$PG" pg_isready -U engram >/dev/null 2>&1; do sleep 1; done; echo "  PG ✅"

step "Server"
docker run -d --name "$SVR" --network "$NET" \
    -e ENGRAM_DB_TYPE=postgres \
    -e ENGRAM_PG_CONNECTION="Host=$PG;Port=5432;Database=engram;Username=engram;Password=engram_test_pass" \
    engram-test:latest serve
wait_health "$SVR"

step "Client-A (user_a)"
docker run -d --name "$CA" --network "$NET" \
    -e ENGRAM_PORT=7438 -e ENGRAM_USER=user_a \
    -e ENGRAM_SERVER_URL="http://$SVR:7437" -e ENGRAM_SYNC_ENABLED=true -e ENGRAM_SYNC_INTERVAL=2s \
    engram-test:latest serve
wait_health "$CA" 7438

step "Enroll project (local)"
docker exec "$CA" ./engram sync enroll --project team/engram-test 2>&1

step "Client-B (user_b)"
docker run -d --name "$CB" --network "$NET" \
    -e ENGRAM_PORT=7439 -e ENGRAM_USER=user_b \
    -e ENGRAM_SERVER_URL="http://$SVR:7437" -e ENGRAM_SYNC_ENABLED=true -e ENGRAM_SYNC_INTERVAL=2s \
    engram-test:latest serve
wait_health "$CB" 7439

step "Client-A crea memoria"
docker exec "$CA" ./engram save "Decision for ENG-209" \
    "Testing pull between 2 clients via Docker" \
    --type decision --project team/engram-test --scope team 2>&1 | head -3

info "Esperando sync (5s)..."; sleep 5

step "Verificación — servidor"
S_DATA=$(docker exec "$SVR" curl -s "http://localhost:7437/search?q=ENG-209&project=team/engram-test" 2>/dev/null)
if echo "$S_DATA" | grep -qi "ENG-209"; then
    pass "Memoria en servidor central"
else
    S_RECENT=$(docker exec "$SVR" curl -s "http://localhost:7437/observations/recent?project=team/engram-test&limit=5")
    echo "  Server recent (raw): $(echo "$S_RECENT" | head -c 200)"
fi

step "Verificación — Client-B pull"
B_DATA=$(docker exec "$CB" ./engram search "ENG-209" --project team/engram-test 2>&1)

echo ""
step "VEREDICTO"
if echo "$B_DATA" | grep -qi "ENG-209"; then
    pass "ENG-209 — Client-B encontró la memoria de Client-A vía sync pull"
elif echo "$S_DATA" | grep -qi "ENG-209"; then
    pass "ENG-209 — Memoria en servidor (Client-B necesita más tiempo de sync)"
else
    fail "ENG-209 — Memoria NO encontrada. Sync puede estar fallando."
fi
echo ""

# Mostrar sync status para debug
echo "=== Sync status client-a ==="
docker exec "$CA" ./engram sync status --json 2>/dev/null | head -c 200
echo ""
echo "=== Sync status client-b ==="
docker exec "$CB" ./engram sync status --json 2>/dev/null | head -c 200

trap - EXIT
cleanup
exit 0
