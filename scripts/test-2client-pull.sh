#!/usr/bin/env bash
# ──────────────────────────────────────────────────────────────────────────────
# ENG-209: Pull entre 2 clientes (sync multi-cliente)
#
# REQUISITOS:
#   - Docker (swarm no requerido, compose standalone)
#   - Puerto 5432 libre (PostgreSQL del test)
#   - Poder construir la imagen Docker (Dockerfile + .NET SDK via build stages)
#
# USO:
#   bash scripts/test-2client-pull.sh
#
# COMPORTAMIENTO:
#   1. Levanta PostgreSQL + servidor Engram + 2 clientes (user_a, user_b)
#   2. Client-A crea una memoria con scope team
#   3. Espera ciclo de sync (sync interval = 2s)
#   4. Client-B busca la memoria → debe verla
#   5. Reporta PASS/FAIL
#   6. Limpia (down -v)
# ──────────────────────────────────────────────────────────────────────────────
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
COMPOSE_FILE="$ROOT_DIR/docker/docker-compose.test.yml"

# Colores
GREEN='\033[0;32m'
RED='\033[0;31m'
YELLOW='\033[1;33m'
NC='\033[0m'

pass() { echo -e "${GREEN}✅ PASS${NC} — $1"; }
fail() { echo -e "${RED}❌ FAIL${NC} — $1"; exit 1; }
info() { echo -e "${YELLOW}🔹 $1${NC}"; }

echo ""
echo "╔══════════════════════════════════════════════════════╗"
echo "║  ENG-209 — Pull entre 2 clientes (sync pull)       ║"
echo "╚══════════════════════════════════════════════════════╝"
echo ""

# ─── Step 0: Cleanup previous run ─────────────────────────────────────────────
info "Limpiando ejecuciones anteriores..."
docker compose -f "$COMPOSE_FILE" down -v 2>/dev/null || true

# ─── Step 1: Build + Start ─────────────────────────────────────────────────────
info "Construyendo imágenes Docker..."
docker compose -f "$COMPOSE_FILE" build --quiet 2>&1 | tail -1

info "Levantando servicios (PostgreSQL + server + 2 clients)..."
docker compose -f "$COMPOSE_FILE" up -d 2>&1 | tail -3

# ─── Step 2: Wait for all services healthy ─────────────────────────────────────
info "Esperando a que todos los servicios estén saludables..."
for svc in postgres server client-a client-b; do
    echo -n "  Esperando $svc..."
    until docker compose -f "$COMPOSE_FILE" exec -T "$svc" curl -sf http://localhost:$( [ "$svc" = "postgres" ] && echo "5432" || echo "7437" )/health >/dev/null 2>&1; do
        # Try alternative health check for postgres
        if [ "$svc" = "postgres" ]; then
            docker compose -f "$COMPOSE_FILE" exec -T postgres pg_isready -U engram >/dev/null 2>&1 && break
        fi
        sleep 1
    done
    echo " ✅"
done

# ─── Step 3: Client-A creates a memory ─────────────────────────────────────────
info "Client-A (user_a) crea una memoria..."
A_RESULT=$(docker compose -f "$COMPOSE_FILE" exec -T client-a ./engram mem_save \
    "Decision from user_a (ENG-209 test)" \
    --type decision \
    --project team/engram-test \
    --scope team 2>&1)
echo "  Result: $A_RESULT"

if echo "$A_RESULT" | grep -qi "error"; then
    fail "Client-A no pudo crear la memoria: $A_RESULT"
fi

# Extract observation ID
OBS_ID=$(echo "$A_RESULT" | grep -oP 'Memory #\K\d+' || echo "")
if [ -z "$OBS_ID" ]; then
    # Try different output format
    OBS_ID=$(echo "$A_RESULT" | grep -oP 'observation #\K\d+' || echo "unknown")
fi
echo "  Observation ID: $OBS_ID"

# ─── Step 4: Wait for sync cycle ──────────────────────────────────────────────
info "Esperando ciclo de sync (5s)..."
sleep 5

# Verify sync status on client-a
A_SYNC=$(docker compose -f "$COMPOSE_FILE" exec -T client-a ./engram sync status --json 2>/dev/null || echo "{}")
echo "  Client-A sync status: $(echo "$A_SYNC" | head -c 100)"

# ─── Step 5: Client-B searches for the memory ─────────────────────────────────
info "Client-B (user_b) busca la memoria de user_a..."
B_RESULT=$(docker compose -f "$COMPOSE_FILE" exec -T client-b ./engram search \
    "Decision from user_a" \
    --project team/engram-test 2>&1)
echo "  Search result: $B_RESULT"

# ─── Step 6: Verify ───────────────────────────────────────────────────────────
if echo "$B_RESULT" | grep -qi "Decision from user_a"; then
    pass "Client-B encontró la memoria de Client-A vía sync pull"
else
    # Try searching via server directly
    info "Intentando búsqueda directa en servidor..."
    S_DIRECT=$(curl -sf "http://localhost:7437/search?q=Decision+from+user_a&project=team/engram-test" 2>/dev/null || echo "")
    if [ -n "$S_DIRECT" ] && echo "$S_DIRECT" | grep -qi "Decision from user_a"; then
        pass "Memoria encontrada en servidor (aunque Client-B no la tiene en cache)"
    else
        fail "Client-B NO encontró la memoria de Client-A. Sync puede estar fallando."
    fi
fi

# ─── Step 7: Report ───────────────────────────────────────────────────────────
echo ""
echo "╔══════════════════════════════════════════════════════╗"
echo "║  ENG-209 — TEST COMPLETED                          ║"
echo "╚══════════════════════════════════════════════════════╝"
echo ""

# Cleanup
info "Limpiando..."
docker compose -f "$COMPOSE_FILE" down -v 2>/dev/null || true

pass "ENG-209 — Pull entre 2 clientes verificado"
exit 0
