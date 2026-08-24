#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# test-install-wizard.sh — tests unitarios + integración del wizard de install.sh
#
# USO:
#   bash scripts/test-install-wizard.sh
#
# Hace `source` de install.sh y prueba las funciones directamente (sin ejecutar
# el installer). Cubre: mapeo perfil→método, tokens de navegación, recolección
# de datos por perfil, generación de Docker Compose desktop y config MCP.
# ─────────────────────────────────────────────────────────────────────────────

# Variables globales (FORCE, SELECTED_PROFILE, PG_MODE, etc.) se setean acá y se
# leen dentro de las funciones importadas con `source install.sh` — shellcheck
# no puede ver ese uso cross-file, por eso se deshabilita SC2034 en este archivo.
# shellcheck disable=SC2034

TEST_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
INSTALL_SH="$TEST_DIR/install.sh"

# Neutralizar el `set -euo pipefail` que importa install.sh.
# shellcheck disable=SC1090
source "$INSTALL_SH"
set +euo pipefail

PASS=0
FAIL=0

ok()   { ((PASS++)); echo "  ✓ $1"; }
fail() { ((FAIL++)); echo "  ✗ $1"; }

assert_eq() {
  local actual="$1" expected="$2" label="$3"
  if [[ "$actual" == "$expected" ]]; then ok "$label"; else fail "$label (esperado '$expected', obtenido '$actual')"; fi
}

assert_contains() {
  local haystack="$1" needle="$2" label="$3"
  if [[ "$haystack" == *"$needle"* ]]; then ok "$label"; else fail "$label (no contiene '$needle')"; fi
}

# assert_ok <label> -- <comando...>: pasa si el comando retorna 0.
assert_ok() {
  local label="$1"; shift
  if "$@"; then ok "$label"; else fail "$label"; fi
}

# assert_not <label> -- <comando...>: pasa si el comando retorna distinto de 0.
assert_not() {
  local label="$1"; shift
  if "$@"; then fail "$label"; else ok "$label"; fi
}

# ══════════════════════════════════════════════════════════════════════════════
echo ""
echo "=== Unit: mapeo perfil→método (T3.1) ==="
assert_eq "$(methods_for_profile local)" "release build" "local → release build"
assert_eq "$(methods_for_profile offline-first)" "release build" "offline-first → release build"
assert_eq "$(methods_for_profile remote-server)" "release build docker" "remote-server → release build docker"
# HU-026: desktop permite docker y build (build para construir imagen local)
assert_eq "$(methods_for_profile desktop)" "docker build" "desktop → docker build (HU-026)"

assert_ok   "local permite release"        method_allowed local release
assert_ok   "local permite build"          method_allowed local build
assert_not  "local rechaza docker"         method_allowed local docker
assert_not  "offline-first rechaza docker" method_allowed offline-first docker
assert_ok   "desktop permite docker"       method_allowed desktop docker
assert_not  "desktop rechaza release"      method_allowed desktop release
assert_ok   "remote-server permite docker" method_allowed remote-server docker

echo ""
echo "=== Unit: constantes de perfil sin 'pendiente' (T4.1) ==="
assert_contains "${PROFILE_DESCRIPTIONS[desktop]}" "PostgreSQL" "descripción desktop completa"
if [[ "${PROFILE_DESCRIPTIONS[desktop]}" == *"pendiente"* ]]; then
  fail "desktop NO debe decir 'pendiente'"
else
  ok "desktop sin 'pendiente'"
fi

# ══════════════════════════════════════════════════════════════════════════════
echo ""
echo "=== Navigation: go_next / go_back (T1.3) ==="
SELECTED_PROFILE="local"; STEP="profile"
go_next; assert_eq "$STEP" "method" "go_next profile→method"
go_next; assert_eq "$STEP" "config" "go_next method→config (local sin pg_mode)"
go_back; assert_eq "$STEP" "method" "go_back config→method"
go_back; assert_eq "$STEP" "profile" "go_back method→profile"

SELECTED_PROFILE="desktop"; STEP="method"
go_next; assert_eq "$STEP" "pg_mode" "go_next method→pg_mode (desktop)"
go_next; assert_eq "$STEP" "config" "go_next pg_mode→config"
go_back; assert_eq "$STEP" "pg_mode" "go_back config→pg_mode"

SELECTED_PROFILE="local"; STEP="profile"; WIZARD_QUIT=0
go_back; assert_eq "$WIZARD_QUIT" "1" "go_back en paso 1 → quit"
WIZARD_QUIT=0

# Bug #1 (go_back PREV_STEP): debe guardar el step ANTERIOR, no el nuevo.
SELECTED_PROFILE="local"; STEP="config"; PREV_STEP=""
go_back
assert_eq "$STEP" "method" "go_back config→method"
assert_eq "$PREV_STEP" "config" "go_back guarda PREV_STEP (no el nuevo STEP)"

# Confirmar que go_next sigue funcionando correctamente (regresión).
SELECTED_PROFILE="local"; STEP="profile"; PREV_STEP=""
go_next
assert_eq "$PREV_STEP" "profile" "go_next guarda PREV_STEP anterior"

# ══════════════════════════════════════════════════════════════════════════════
echo ""
echo "=== Integración: selección de perfil (stdin) ==="
FORCE=false; SELECTED_PROFILE=""; TOKEN=""
step_profile <<< "2" >/dev/null 2>&1
assert_eq "$SELECTED_PROFILE" "offline-first" "elegir 2 → offline-first"

SELECTED_PROFILE=""; TOKEN=""
step_profile <<< "b" >/dev/null 2>&1
assert_eq "$TOKEN" "quit" "step_profile (b) → quit"

SELECTED_PROFILE=""; TOKEN=""
step_profile <<< "99" >/dev/null 2>&1
assert_eq "$TOKEN" "stay" "input inválido → stay"

# ══════════════════════════════════════════════════════════════════════════════
echo ""
echo "=== Integración: método filtrado por perfil ==="
# desktop tiene 2 métodos (docker, build) tras HU-026 — debe mostrar ambos
FORCE=false; SELECTED_PROFILE="desktop"; SELECTED_METHOD=""; TOKEN=""
step_method <<< "1" >/dev/null 2>&1
assert_eq "$SELECTED_METHOD" "docker" "desktop elige docker cuando se selecciona opción 1"

# back en step_method vuelve a perfil
SELECTED_PROFILE="local"; SELECTED_METHOD=""; TOKEN=""
step_method <<< "b" >/dev/null 2>&1
assert_eq "$TOKEN" "back" "step_method (b) → back"

# Bug #2 (step_method ignora FORCE_METHOD con 1 método válido):
# Si un perfil tiene 1 solo método y el usuario pasa --method inválido,
# debe errorear loud, NO auto-seleccionar silenciosamente.
# (desktop normalmente tiene 2 métodos tras HU-026; temporalmente lo
#  forzamos a 1 para reproducir la ruta del bug.)
ORIG_DESKTOP_METHODS="${PROFILE_METHODS[desktop]}"
PROFILE_METHODS[desktop]="docker"
FORCE=true; FORCE_METHOD="release"
SELECTED_PROFILE="desktop"; SELECTED_METHOD=""; TOKEN=""; WIZARD_QUIT=0; WIZARD_ERROR=0
step_method >/dev/null 2>&1
assert_eq "$WIZARD_ERROR" "1" "FORCE_METHOD inválido en perfil 1-method → WIZARD_ERROR"
assert_eq "$WIZARD_QUIT" "1" "FORCE_METHOD inválido en perfil 1-method → WIZARD_QUIT"
PROFILE_METHODS[desktop]="$ORIG_DESKTOP_METHODS"

# ══════════════════════════════════════════════════════════════════════════════
echo ""
echo "=== Integración: recolección de datos offline-first (T2.3) ==="
SELECTED_PROFILE="offline-first"; STEP="config"; FORCE=false
DATA_DIR=""; ENGRAM_USER=""; SERVER_URL=""; SYNC_AUTO_SYNC=""; TOKEN=""
step_config <<< $'\n\n\n' >/dev/null 2>&1
assert_eq "$TOKEN" "jump_to_profile" "offline-first URL vacío → jump_to_profile"

SELECTED_PROFILE="offline-first"; STEP="config"; FORCE=false
DATA_DIR=""; ENGRAM_USER=""; SERVER_URL=""; SYNC_AUTO_SYNC=""; TOKEN=""
step_config <<< $'/tmp/engram-test\nuser1\nhttp://remote:7437\nfalse\n' >/dev/null 2>&1
assert_eq "$DATA_DIR" "/tmp/engram-test" "offline-first DATA_DIR"
assert_eq "$ENGRAM_USER" "user1" "offline-first USER"
assert_eq "$SERVER_URL" "http://remote:7437" "offline-first SERVER_URL"
assert_eq "$SYNC_AUTO_SYNC" "false" "offline-first auto-sync respeta elección"
assert_eq "$TOKEN" "next" "offline-first happy path → next"

# ══════════════════════════════════════════════════════════════════════════════
echo ""
echo "=== Integración: generación Docker Compose desktop (T3.4) ==="
TMP_DATA="$(mktemp -d)"
DATA_DIR="$TMP_DATA"; SELECTED_PROFILE="desktop"; SYNC_AUTO_SYNC="true"; ENGRAM_USER="tester"
COMPOSE="$TMP_DATA/desktop/docker-compose.yml"

PG_MODE="all-in-one"; PG_PASSWORD="secret123"; PG_CONNECTION="Host=localhost;Port=5432;Database=engram;Username=engram;Password=secret123"
generate_desktop_compose >/dev/null 2>&1
if [[ -f "$COMPOSE" ]]; then ok "all-in-one compose generado"; else fail "all-in-one compose generado"; fi
assert_contains "$(cat "$COMPOSE" 2>/dev/null)" "Dockerfile.allinone" "all-in-one referencia Dockerfile.allinone"
assert_contains "$(cat "$COMPOSE" 2>/dev/null)" "/data/postgres" "all-in-one monta PGDATA"
assert_contains "$(cat "$COMPOSE" 2>/dev/null)" "Host=localhost" "all-in-one PG apunta a localhost"
# HU-024: el contenedor server usa perfil remote-server + DB postgres (no desktop)
assert_contains "$(cat "$COMPOSE" 2>/dev/null)" "ENGRAM_PROFILE: remote-server" "all-in-one usa ENGRAM_PROFILE=remote-server (HU-024)"
assert_contains "$(cat "$COMPOSE" 2>/dev/null)" "ENGRAM_DB_TYPE: postgres" "all-in-one usa ENGRAM_DB_TYPE=postgres (HU-024)"

PG_MODE="separate"; PG_CONNECTION="Host=postgres;Port=5432;Database=engram;Username=engram;Password=secret123"
generate_desktop_compose >/dev/null 2>&1
assert_contains "$(cat "$COMPOSE" 2>/dev/null)" "postgres:16-alpine" "separate tiene servicio postgres"
assert_contains "$(cat "$COMPOSE" 2>/dev/null)" "Host=postgres" "separate PG apunta a postgres"
# HU-024: el contenedor engram (no postgres) usa remote-server + postgres
assert_contains "$(cat "$COMPOSE" 2>/dev/null)" "ENGRAM_PROFILE: remote-server" "separate usa ENGRAM_PROFILE=remote-server (HU-024)"
assert_contains "$(cat "$COMPOSE" 2>/dev/null)" "ENGRAM_DB_TYPE: postgres" "separate usa ENGRAM_DB_TYPE=postgres (HU-024)"

PG_MODE="existing"; PG_CONNECTION="Host=192.168.1.50;Port=5432;Database=engram;Username=admin;Password=pw"
generate_desktop_compose >/dev/null 2>&1
assert_contains "$(cat "$COMPOSE" 2>/dev/null)" "host.docker.internal:host-gateway" "existing usa host-gateway"
assert_contains "$(cat "$COMPOSE" 2>/dev/null)" "Host=192.168.1.50" "existing usa PG externo"
if [[ "$(cat "$COMPOSE" 2>/dev/null)" == *"postgres:16-alpine"* ]]; then
  fail "existing NO debe tener servicio postgres"
else
  ok "existing sin servicio postgres"
fi

rm -rf "$TMP_DATA"

# ══════════════════════════════════════════════════════════════════════════════
echo ""
echo "=== Integración: config MCP por perfil (AC6) ==="
if command -v python3 >/dev/null 2>&1; then
  TMP_MCP="$(mktemp -d)"
  DATA_DIR="$TMP_MCP/data"; PG_CONNECTION=""

  SELECTED_PROFILE="offline-first"; ENGRAM_USER="alice"; SYNC_ENABLED="true"; SERVER_URL="http://remote:7437"; SYNC_AUTO_SYNC="true"
  write_mcp_config "$TMP_MCP/offline.json" "mcpServers" >/dev/null 2>&1
  oj="$(cat "$TMP_MCP/offline.json" 2>/dev/null)"
  assert_contains "$oj" "ENGRAM_SYNC_ENABLED" "offline-first MCP: SYNC_ENABLED presente"
  assert_contains "$oj" "http://remote:7437" "offline-first MCP: SERVER_URL"
  assert_contains "$oj" "alice" "offline-first MCP: USER"
  assert_contains "$oj" "ENGRAM_SYNC_AUTO_SYNC" "offline-first MCP: SYNC_AUTO_SYNC"

  SELECTED_PROFILE="desktop"; ENGRAM_USER="bob"; SERVER_URL="http://localhost:7437"; SYNC_AUTO_SYNC="true"
  write_mcp_config "$TMP_MCP/desktop.json" "mcpServers" >/dev/null 2>&1
  dj="$(cat "$TMP_MCP/desktop.json" 2>/dev/null)"
  assert_contains "$dj" "http://localhost:7437" "desktop MCP: SERVER_URL localhost"
  assert_contains "$dj" "desktop" "desktop MCP: perfil desktop"

  SELECTED_PROFILE="local"; ENGRAM_USER="carol"; SYNC_ENABLED="false"; SERVER_URL=""; SYNC_AUTO_SYNC=""
  write_mcp_config "$TMP_MCP/local.json" "mcpServers" >/dev/null 2>&1
  lj="$(cat "$TMP_MCP/local.json" 2>/dev/null)"
  assert_contains "$lj" "false" "local MCP: SYNC_ENABLED=false"
  if [[ "$lj" == *"ENGRAM_SERVER_URL"* ]]; then
    fail "local NO debe tener SERVER_URL"
  else
    ok "local MCP sin SERVER_URL"
  fi

  # HU-021: OpenCode exige type:local + enabled:true dentro de mcp.engram
  write_mcp_config "$TMP_MCP/opencode.json" "mcp" >/dev/null 2>&1
  ocj="$(cat "$TMP_MCP/opencode.json" 2>/dev/null)"
  assert_contains "$ocj" '"enabled": true' "opencode MCP: enabled true en mcp.engram"
  assert_contains "$ocj" '"type": "local"' "opencode MCP: type local en mcp.engram"
  assert_contains "$ocj" '"environment"' "opencode MCP: usa environment (no env)"

  rm -rf "$TMP_MCP"
else
  echo "  (python3 no disponible — saltando tests de config MCP)"
fi

# ══════════════════════════════════════════════════════════════════════════════
echo ""
echo "=== Integración: flujo -y por perfil (stubs curl/docker) ==="
STUB_DIR="$(mktemp -d)"
cat > "$STUB_DIR/curl" <<'EOF'
#!/bin/bash
target=""
prev=""
for a in "$@"; do
  if [[ "$prev" == "-o" ]]; then target="$a"; fi
  prev="$a"
done
if [[ -n "$target" ]]; then
  mkdir -p "$(dirname "$target")"
  printf '#!/bin/bash\necho "engram 1.3.0 (stub)"\n' > "$target"
  chmod +x "$target"
fi
exit 0
EOF
chmod +x "$STUB_DIR/curl"
cat > "$STUB_DIR/docker" <<'EOF'
#!/bin/bash
echo "stub docker"
exit 0
EOF
chmod +x "$STUB_DIR/docker"

export PATH="$STUB_DIR:$PATH"
TMP_HOME="$(mktemp -d)"
export HOME="$TMP_HOME"

# HU-022: curl stub también debe crear libe_sqlite3.so cuando se lo piden
# (para verificar que install_release crea el symlink e_sqlite3.so)
cat >> "$STUB_DIR/curl" <<'EOF'
# Si el target incluye libe_sqlite3.so, crear un archivo stub
if [[ "$target" == *"libe_sqlite3.so"* ]]; then
  mkdir -p "$(dirname "$target")"
  printf '#!/bin/bash\necho "stub libe_sqlite3"\n' > "$target"
  chmod +x "$target"
fi
EOF

FORCE=true; FORCE_PROFILE="local"; FORCE_METHOD=""; FORCE_PG_MODE=""
STEP="profile"; SELECTED_PROFILE=""; SELECTED_METHOD=""; EDITOR_CHOICE=""; DATA_DIR=""; WIZARD_QUIT=0; WIZARD_ERROR=0
run_wizard >/dev/null 2>&1
assert_eq "$WIZARD_ERROR" "0" "-y local+release completa sin error"
assert_eq "$SELECTED_PROFILE" "local" "-y perfil local"
assert_eq "$SELECTED_METHOD" "release" "-y método release"
assert_eq "$EDITOR_CHOICE" "5" "-y editor opencode"

# HU-022: verificar que install_release creó los symlinks de SQLite native lib
ENGRAM_BIN="${TMP_HOME}/.local/bin/engram"
LIB_DIR="${TMP_HOME}/.local/bin"
if [[ -L "${LIB_DIR}/e_sqlite3.so" ]]; then
  ok "e_sqlite3.so symlink creado"
else
  fail "e_sqlite3.so symlink NO creado"
fi
if [[ -f "${LIB_DIR}/libe_sqlite3.so" ]]; then
  ok "libe_sqlite3.so descargado"
else
  fail "libe_sqlite3.so NO descargado"
fi

FORCE=true; FORCE_PROFILE="offline-first"; FORCE_METHOD=""; FORCE_PG_MODE=""
STEP="profile"; SELECTED_PROFILE=""; SELECTED_METHOD=""; EDITOR_CHOICE=""; DATA_DIR=""; SERVER_URL=""; WIZARD_QUIT=0; WIZARD_ERROR=0
run_wizard >/dev/null 2>&1
assert_eq "$WIZARD_ERROR" "0" "-y offline-first completa sin error"
assert_eq "$SERVER_URL" "http://localhost:7437" "-y offline-first URL default"

FORCE=true; FORCE_PROFILE="remote-server"; FORCE_METHOD=""; FORCE_PG_MODE=""
STEP="profile"; SELECTED_PROFILE=""; SELECTED_METHOD=""; EDITOR_CHOICE=""; DATA_DIR=""; PG_CONNECTION=""; WIZARD_QUIT=0; WIZARD_ERROR=0
run_wizard >/dev/null 2>&1
assert_eq "$WIZARD_ERROR" "0" "-y remote-server completa sin error"
assert_contains "$PG_CONNECTION" "Host=localhost" "-y remote-server PG default"

FORCE=true; FORCE_PROFILE="desktop"; FORCE_METHOD=""; FORCE_PG_MODE="all-in-one"
STEP="profile"; SELECTED_PROFILE=""; SELECTED_METHOD=""; EDITOR_CHOICE=""; DATA_DIR=""; SERVER_URL=""; WIZARD_QUIT=0; WIZARD_ERROR=0
run_wizard >/dev/null 2>&1
assert_eq "$WIZARD_ERROR" "0" "-y desktop (all-in-one) completa sin error"
assert_eq "$SERVER_URL" "http://localhost:7437" "-y desktop URL localhost"
assert_eq "$SELECTED_METHOD" "docker" "-y desktop método docker"

# HU-026 (install_build desktop): verificar que con método build se intenta
# construir imagen local. Stub dotnet (crea binary fake) + stub docker.
STUB_BUILD_DIR="$(mktemp -d)"
cat > "$STUB_BUILD_DIR/docker" <<'EOF'
#!/bin/bash
# Detectar "docker build" en los args ($1 = "build")
if [[ "${1:-}" == "build" ]]; then
  echo "STUB_DOCKER_BUILD_INVOKED" >&2
fi
exit 0
EOF
chmod +x "$STUB_BUILD_DIR/docker"
# Stub dotnet: `dotnet add` no-op; `dotnet publish -o DIR` crea binary fake.
cat > "$STUB_BUILD_DIR/dotnet" <<'EOF'
#!/bin/bash
prev=""
output_dir=""
for a in "$@"; do
  if [[ "$prev" == "-o" ]]; then output_dir="$a"; fi
  prev="$a"
done
if [[ "$1" == "publish" && -n "$output_dir" ]]; then
  mkdir -p "$output_dir"
  printf '#!/bin/bash\necho "stub engram"\n' > "$output_dir/engram"
  chmod +x "$output_dir/engram"
  # stub native lib
  printf 'fake lib' > "$output_dir/libe_sqlite3.so"
fi
exit 0
EOF
chmod +x "$STUB_BUILD_DIR/dotnet"
export PATH="$STUB_BUILD_DIR:$PATH"
TMP_HOME_BUILD="$(mktemp -d)"
export HOME="$TMP_HOME_BUILD"
# Crear dummy csproj en repo para que install_build no se queje.
mkdir -p "${REPO_ROOT}/src/Engram.Cli"
touch "${REPO_ROOT}/src/Engram.Cli/Engram.Cli.csproj"
# Set vars AFTER sourcing install.sh — install.sh resets them on source.
SELECTED_PROFILE="desktop"; SELECTED_METHOD="build"; DATA_DIR="$TMP_HOME_BUILD/data"
mkdir -p "$DATA_DIR"
OUT="$(install_build 2>&1)"
if [[ "$OUT" == *"STUB_DOCKER_BUILD_INVOKED"* ]]; then
  ok "install_build desktop invoca docker build localmente (HU-026)"
else
  fail "install_build desktop NO invoca docker build (HU-026): $OUT"
fi
# No debe haber dejado archivos leak en el repo root
if [[ -f "${REPO_ROOT}/engram-local" || -f "${REPO_ROOT}/libe_sqlite3-local" ]]; then
  fail "install_build desktop dejó archivos leak en repo root"
else
  ok "install_build desktop limpia archivos de contexto"
fi
rm -rf "$STUB_BUILD_DIR" "$TMP_HOME_BUILD"

rm -rf "$STUB_DIR" "$TMP_HOME"

# ══════════════════════════════════════════════════════════════════════════════
echo ""
echo "=== Bug #3: install_docker crea wrapper ejecutable ==="
# Setup fresh: HOME aislado, stub docker que solo verifica exec.
DOCKER_STUB_DIR="$(mktemp -d)"
cat > "$DOCKER_STUB_DIR/docker" <<'EOF'
#!/bin/bash
exit 0
EOF
chmod +x "$DOCKER_STUB_DIR/docker"
export PATH="$DOCKER_STUB_DIR:$PATH"
TMP_HOME_D="$(mktemp -d)"
export HOME="$TMP_HOME_D"
ENGRAM_CMD=""
install_docker >/dev/null 2>&1

WRAPPER="${HOME}/.local/bin/engram"
if [[ -x "$ENGRAM_CMD" ]]; then
  ok "ENGRAM_CMD es ejecutable: $ENGRAM_CMD"
else
  fail "ENGRAM_CMD NO es ejecutable (bug #3): $ENGRAM_CMD"
fi
if [[ -f "$WRAPPER" ]]; then
  ok "wrapper existe en $WRAPPER"
else
  fail "wrapper NO existe en $WRAPPER"
fi
if [[ -f "$WRAPPER" ]] && grep -q "docker run" "$WRAPPER" 2>/dev/null; then
  ok "wrapper invoca docker run"
else
  fail "wrapper NO contiene 'docker run'"
fi
# step_verify hace [[ -x "$ENGRAM_CMD" ]]; simular esa verificación.
if [[ -x "$ENGRAM_CMD" ]]; then
  ok "step_verify [[ -x ]] pasa para install_docker (bug #3 fixed)"
else
  fail "step_verify [[ -x ]] falla para install_docker (bug #3)"
fi

rm -rf "$DOCKER_STUB_DIR" "$TMP_HOME_D"
# Restaurar PATH por las dudas
export PATH="$(echo "$PATH" | sed "s|$STUB_DIR||g; s|^:||; s|:$||")"

# ══════════════════════════════════════════════════════════════════════════════
echo ""
echo "═══════════════════════════════════════════════════════"
echo "  Resultado: $PASS pasaron, $FAIL fallaron"
echo "═══════════════════════════════════════════════════════"
[[ "$FAIL" -eq 0 ]]
