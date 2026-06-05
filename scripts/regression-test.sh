#!/usr/bin/env bash
# scripts/regression-test.sh — Pre-release regression battery.
# Runs smoke + R1-R5 + logging PM-* against a running server.
# Exit 0 = all pass, 1 = any failed.
#
# Usage:
#   bash scripts/regression-test.sh
#   BASE=http://localhost:7437 CONTAINER=engram-test bash scripts/regression-test.sh
#
# Env vars:
#   BASE      - server URL (default http://localhost:7437)
#   CONTAINER - Docker container name for log checks (default engram-test)
#               Set to empty to skip logger checks

set -euo pipefail

BASE="${BASE:-http://localhost:7437}"
CONTAINER="${CONTAINER:-engram-test}"
TS=$(date +%s)
PROJ="team/regression-$TS"
FAIL=0
PASS=0
SKIP=0

# ── helpers ──────────────────────────────────────────────────────────────────

header() {
    echo ""
    echo "--- $1 ---"
}

pass()  { PASS=$((PASS+1)); echo "  ✅ $1"; }
fail()  { FAIL=$((FAIL+1)); echo "  ❌ $1"; }
skip()  { SKIP=$((SKIP+1)); echo "  ⚪ $1 (saltado)"; }

check_json() {
    local desc="$1"; shift
    local expected_code="$1"; shift
    local jq_filter="$1"; shift
    # Single curl: body + HTTP status (capture both in one call)
    local raw=$(curl -s -w "\n%{http_code}" "$@" 2>/dev/null)
    local code=$(echo "$raw" | tail -1)
    local body=$(echo "$raw" | sed '$d')
    if [ "$code" != "$expected_code" ]; then
        fail "$desc — esperado $expected_code, obtuvo $code"
    elif echo "$body" | jq -e "$jq_filter" > /dev/null 2>&1; then
        pass "$desc"
    else
        fail "$desc — filtro jq falló en: $(echo "$body" | jq -c . 2>/dev/null)"
    fi
}

check_status() {
    local desc="$1"; shift
    local expected="$1"; shift
    # Single curl for read-only or idempotent operations
    local raw=$(curl -s -w "\n%{http_code}" "$@" 2>/dev/null)
    local code=$(echo "$raw" | tail -1)
    if [ "$code" = "$expected" ]; then
        pass "$desc"
    else
        fail "$desc — esperado $expected, obtuvo $code"
    fi
}

# ── Setup ────────────────────────────────────────────────────────────────────

echo "=== Regression test suite ==="
echo "  server:    $BASE"
echo "  timestamp: $TS"
echo "  project:   $PROJ"

# ── 1. Smoke (3 checks) ──────────────────────────────────────────────────────

header "Smoke"
check_json  "/health returns ok"    200 '.status == "ok"'           "$BASE/health"
check_json  "/stats returns JSON"   200 '.total_sessions >= 0'     "$BASE/stats"
check_json  "/sync/status returns"  200 '.sync_enabled == true'    "$BASE/sync/status"

# ── 2. Regression R1-R5 (5 checks) ───────────────────────────────────────────

header "Regression R1-R5"

check_json  "[R1] push sin entries"  400 '.error_code == "empty-batch"' \
    -X POST "$BASE/sync/mutations/push" \
    -H "Content-Type: application/json" \
    -d '{"created_by":"test"}'

check_json  "[R2] push entries null"  400 '.error_code == "empty-batch"' \
    -X POST "$BASE/sync/mutations/push" \
    -H "Content-Type: application/json" \
    -d '{"entries":null,"created_by":"test"}'

# R3: create session + obs, soft-delete obs, delete session
SESS_R3="sess-r3-$TS"
check_json  "[R3] create session"     201 '.status == "created"' \
    -X POST "$BASE/sessions" \
    -H "Content-Type: application/json" \
    -d "{\"id\":\"$SESS_R3\",\"project\":\"$PROJ\",\"directory\":\"/tmp\"}"

OBS_R3=$(curl -s -X POST "$BASE/observations" \
    -H "Content-Type: application/json" \
    -d "{\"session_id\":\"$SESS_R3\",\"title\":\"r3-test\",\"content\":\"x\",\"type\":\"manual\",\"project\":\"$PROJ\"}")
OBS_R3_ID=$(echo "$OBS_R3" | jq -r '.id // empty')
if [ -z "$OBS_R3_ID" ]; then
    fail "[R3] soft-delete obs — no se pudo extraer ID de obs: $(echo "$OBS_R3" | jq -c .)"
else
    check_json  "[R3] soft-delete obs"    200 '.status == "deleted"' \
        -X DELETE "$BASE/observations/$OBS_R3_ID"
    check_json  "[R3] delete session"     200 '.status == "deleted"' \
        -X DELETE "$BASE/sessions/$SESS_R3"
fi

# R4: create session + obs activa, delete should 409
SESS_R4="sess-r4-$TS"
check_json  "[R4] create session"     201 '.status == "created"' \
    -X POST "$BASE/sessions" \
    -H "Content-Type: application/json" \
    -d "{\"id\":\"$SESS_R4\",\"project\":\"$PROJ\",\"directory\":\"/tmp\"}"
check_json  "[R4] create active obs"  201 '.id > 0' \
    -X POST "$BASE/observations" \
    -H "Content-Type: application/json" \
    -d "{\"session_id\":\"$SESS_R4\",\"title\":\"r4-active\",\"content\":\"x\",\"type\":\"manual\",\"project\":\"$PROJ\"}"
check_status "[R4] delete (409)"      409 \
    -X DELETE "$BASE/sessions/$SESS_R4"

# R5: user scoping — create 2 sessions, 2 prompts, test each user
SESS_A="sess-a-$TS"
SESS_B="sess-b-$TS"
check_json  "[R5] session userA"      201 '.status == "created"' \
    -X POST "$BASE/sessions" \
    -H "Content-Type: application/json" \
    -d "{\"id\":\"$SESS_A\",\"project\":\"$PROJ\",\"directory\":\"/tmp\"}"
check_json  "[R5] session userB"      201 '.status == "created"' \
    -X POST "$BASE/sessions" \
    -H "Content-Type: application/json" \
    -d "{\"id\":\"$SESS_B\",\"project\":\"$PROJ\",\"directory\":\"/tmp\"}"

check_json  "[R5] prompt userA"       201 '.status == "saved"' \
    -X POST "$BASE/prompts" \
    -H "Content-Type: application/json" \
    -H "X-Engram-User: userA" \
    -d "{\"content\":\"userA prompt $TS\",\"project\":\"$PROJ\",\"session_id\":\"$SESS_A\"}"
check_json  "[R5] prompt userB"       201 '.status == "saved"' \
    -X POST "$BASE/prompts" \
    -H "Content-Type: application/json" \
    -H "X-Engram-User: userB" \
    -d "{\"content\":\"userB prompt $TS\",\"project\":\"$PROJ\",\"session_id\":\"$SESS_B\"}"

# Verify scoping
PA=$(curl -s "$BASE/prompts/recent?project=$PROJ&limit=5" -H "X-Engram-User: userA" | jq -r '[.[] | select(.content | contains("userA"))] | length')
PB=$(curl -s "$BASE/prompts/recent?project=$PROJ&limit=5" -H "X-Engram-User: userB" | jq -r '[.[] | select(.content | contains("userB"))] | length')
if [ "$PA" -ge 1 ] && [ "$PB" -ge 1 ]; then
    pass "[R5] user scoping OK (userA=$PA userB=$PB)"
else
    fail "[R5] user scoping — userA=$PA userB=$PB (esperado >=1 c/u)"
fi

# Cleanup R4 session obs (so DB is clean)
OBS_R4_ID=$(curl -s "$BASE/sessions/$SESS_R4" 2>/dev/null | jq -r '.id')
if [ "$OBS_R4_ID" != "null" ]; then
    curl -s -X DELETE "$BASE/observations/$OBS_R4_ID" > /dev/null 2>&1 || true
fi

# ── 3. Logging PM-1..7 (HTTP part) ───────────────────────────────────────────

header "Logging PM-* (HTTP)"

check_json  "[PM-1] GET /health"      200 '.status == "ok"'        "$BASE/health"
check_status "[PM-2] GET /no-existe"  404                           "$BASE/no-existe"
check_json  "[PM-3] POST malformed"   400 '.error != null' \
    -X POST "$BASE/sessions" \
    -H "Content-Type: application/json" \
    -d "{\"id\":\"sess-pm3\",invalid"
check_json  "[PM-4] POST /sessions"   201 '.status == "created"' \
    -X POST "$BASE/sessions" \
    -H "Content-Type: application/json" \
    -d "{\"id\":\"sess-pm4-$TS\",\"project\":\"$PROJ\",\"directory\":\"/tmp\"}"
# cleanup PM-4
curl -s -X DELETE "$BASE/sessions/sess-pm4-$TS" > /dev/null 2>&1 || true

# ── 4. Logger checks (opt-in, requires docker) ───────────────────────────────

header "Logging PM-* (log format)"

if [ -z "${CONTAINER:-}" ]; then
    skip "PM-5 — CONTAINER no configurado"
    skip "PM-6 — CONTAINER no configurado"
elif docker ps --format '{{.Names}}' | grep -q "^${CONTAINER}$" 2>/dev/null; then
    # PM-5: count Information level log lines (should be >0 when running normally)
    INFO_COUNT=$(docker logs "$CONTAINER" 2>&1 | grep -v '^\[' | jq -r 'select(.LogLevel == "Information") | .LogLevel' 2>/dev/null | wc -l || echo 0)
    if [ "$INFO_COUNT" -ge 1 ]; then
        pass "[PM-5] Information log lines present ($INFO_COUNT)"
    else
        fail "[PM-5] ninguna línea Information encontrada — si LOG_LEVEL=warn, esto es correcto"
    fi

    # PM-6: check ClientIp appears in at least one log line
    CLI_CNT=$(docker logs "$CONTAINER" 2>&1 | grep -v '^\[' | jq -r 'select(.State.ClientIp != null) | .LogLevel' 2>/dev/null | wc -l || echo 0)
    if [ "$CLI_CNT" -ge 1 ]; then
        pass "[PM-6] ClientIp presente en al menos $CLI_CNT log lines"
    else
        fail "[PM-6] ningún log line tiene ClientIp"
    fi
else
    skip "PM-5 — container '$CONTAINER' no encontrado"
    skip "PM-6 — container '$CONTAINER' no encontrado"
fi

# PM-7: check CLI uses Console.WriteLine (not logger) for user output
# This is a static code check — no server needed
CLI_CW=$(grep -c "Console.WriteLine" src/Engram.Cli/Program.cs 2>/dev/null || echo 0)
SRV_LOG=$(grep -c "logger\." src/Engram.Server/EngramServer.cs 2>/dev/null || echo 0)
if [ "$CLI_CW" -gt 30 ] && [ "$SRV_LOG" -ge 2 ]; then
    pass "[PM-7] CLI tiene $CLI_CW Console.WriteLine, server tiene $SRV_LOG logger.*"
else
    fail "[PM-7] CLI=$CLI_CW (esperado >30), server log=$SRV_LOG (esperado >=2)"
fi

# ── Summary ───────────────────────────────────────────────────────────────────

TOTAL=$((PASS+FAIL+SKIP))
echo ""
echo "=== Results: $PASS/$TOTAL passed, $FAIL failed, $SKIP skipped ==="

# Exit 1 if any failure
if [ "$FAIL" -gt 0 ]; then
    exit 1
fi
exit 0
