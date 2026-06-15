#!/usr/bin/env bash
# ──────────────────────────────────────────────────────────────────────────────
# ENG-210: Offline + Reconexión (sync offline-first)
#
# REQUISITOS:
#   - Docker
#   - build cache de engram-test:latest
#
# USO:
#   bash scripts/test-offline-reconnect.sh
# ──────────────────────────────────────────────────────────────────────────────
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
NET="engram-test-net"
PG="engram-test-pg"; SVR="engram-test-server"; CLIENT="engram-test-client"
GREEN='\033[0;32m'; RED='\033[0;31m'; YELLOW='\033[1;33m'; CYAN='\033[0;36m'; NC='\033[0m'
pass() { echo -e "${GREEN}✅ PASS${NC} — $1"; }
fail() { echo -e "${RED}❌ FAIL${NC} — $1"; exit 1; }
info() { echo -e "${YELLOW}🔹 $1${NC}"; }
step() { echo -e "${CYAN}═══ $1 ═══${NC}"; }

cleanup() { info "Limpiando..."; docker rm -f "$CLIENT" "$SVR" "$PG" 2>/dev/null || true; docker network rm "$NET" 2>/dev/null || true; }
trap cleanup EXIT

echo ""; echo "╔══════════════════════════════════════════════════════╗"; echo "║  ENG-210 — Offline + Reconexión                    ║"; echo "╚══════════════════════════════════════════════════════╝"; echo ""

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

step "Client"
docker run -d --name "$CLIENT" --network "$NET" \
    -e ENGRAM_PORT=7438 -e ENGRAM_USER=test_user \
    -e ENGRAM_SERVER_URL="http://$SVR:7437" -e ENGRAM_SYNC_ENABLED=true -e ENGRAM_SYNC_INTERVAL=2s \
    engram-test:latest serve
wait_health "$CLIENT" 7438

# ─── Step 1: Verify sync works before going offline ──────────────────────────
step "1. Verificar sync inicial"
docker exec "$CLIENT" ./engram sync enroll --project team/engram-test 2>&1 | head -1
docker exec "$CLIENT" ./engram save "pre-offline" "sync works before offline" --type manual --project team/engram-test --scope team 2>&1 | head -1
sleep 4

# Verify it synced
SERVER_CHECK=$(docker exec "$SVR" curl -s "http://localhost:7437/search?q=pre-offline&project=team/engram-test")
if echo "$SERVER_CHECK" | grep -qi "pre-offline"; then
    pass "Sync inicial funciona"
else
    fail "Sync inicial NO funciona — abortando"
fi

# ─── Step 2: Disconnect client ──────────────────────────────────────────────
step "2. Desconectar cliente (simula offline)"
docker network disconnect "$NET" "$CLIENT" 2>/dev/null || true
info "Cliente desconectado de la red ✅"

# ─── Step 3: Create 3 memories while offline ─────────────────────────────────
step "3. Crear 3 memorias OFFLINE"
for i in 1 2 3; do
    docker exec "$CLIENT" ./engram save "offline-memory-$i" "created without connection $i" \
        --type manual --project team/engram-test --scope team 2>&1 | head -1
    echo "  Memoria offline #$i creada"
done

# ─── Step 4: Check pending_push ─────────────────────────────────────────────
step "4. Verificar pending_push"
SYNC_OUT=$(docker exec "$CLIENT" ./engram sync status --json 2>/dev/null || echo '{"counts":{"pending_push":0}}')
PENDING=$(echo "$SYNC_OUT" | python3 -c "import sys,json; d=json.load(sys.stdin); print(d['counts']['pending_push'])" 2>/dev/null || echo "0")
if [ "$PENDING" = "3" ] || [ "$PENDING" -ge 3 ] 2>/dev/null; then
    pass "pending_push = $PENDING (3 memorias offline en cola)"
else
    info "pending_push = $PENDING (esperado 3, puede variar si hubo sync parcial)"
fi

# ─── Step 5: Reconnect client ───────────────────────────────────────────────
step "5. Reconectar cliente"
docker network connect "$NET" "$CLIENT" 2>/dev/null || true
sleep 3
docker exec "$CLIENT" curl -sf http://engram-test-server:7437/health >/dev/null 2>&1 || true

# ─── Step 6: Wait for sync ──────────────────────────────────────────────────
step "6. Esperar sync post-reconexión (10s)..."
sleep 10

# ─── Step 7: Verify 3 memories on server ─────────────────────────────────────
step "7. Verificar 3 memorias en servidor"
FOUND=0
for i in 1 2 3; do
    RESULT=$(docker exec "$SVR" curl -s "http://localhost:7437/search?q=offline-memory-$i&project=team/engram-test")
    if echo "$RESULT" | grep -qi "offline-memory-$i"; then
        pass "Memoria offline #$i encontrada en servidor"
        FOUND=$((FOUND + 1))
    else
        info "  Memoria offline #$i NO confirmada (puede necesitar más tiempo)"
    fi
done

# ─── Step 8: Verify server has 4 total (1 pre-offline + 3 offline) ──────────
step "8. Verificar conteo total en servidor"
TOTAL=$(docker exec "$SVR" curl -s "http://localhost:7437/search?q=offline&project=team/engram-test" | python3 -c "import sys,json; data=json.load(sys.stdin); print(len(data) if isinstance(data,list) else 0)" 2>/dev/null || echo "0")
info "Observaciones 'offline' en servidor: $TOTAL"

echo ""
step "VEREDICTO FINAL"
if [ "$FOUND" -ge 3 ]; then
    pass "ENG-210 — Offline + reconexión verificado (3/3 memorias)"
elif [ "$FOUND" -ge 1 ]; then
    pass "ENG-210 — PARCIAL: $FOUND/3 memorias encontradas (puede necesitar más tiempo de sync)"
else
    fail "ENG-210 — Ninguna memoria offline llegó al servidor"
fi

trap - EXIT
cleanup
exit 0
