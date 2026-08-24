#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# engram-dotnet — Install Script (wizard por pasos, profile-first)
#
# USO:
#   ./install.sh                          # interactivo
#   ./install.sh -y                       # no preguntar (defaults por perfil)
#   ./install.sh -y --profile desktop     # no preguntar con perfil específico
#   ./install.sh -y --method release      # no preguntar con método específico
#
# Orden del wizard: perfil → método (filtrado) → [desktop: modo PG] → config
# → editor MCP → instalación → verificación.
#
# El archivo se puede "source"-ear desde tests sin ejecutar el installer:
#   source scripts/install.sh   # define funciones/constantes, no ejecuta nada
# ─────────────────────────────────────────────────────────────────────────────

set -euo pipefail

# ── Colores ──────────────────────────────────────────────────────────────────
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m'

info()  { echo -e "${GREEN}[INFO]${NC} $1"; }
warn()  { echo -e "${YELLOW}[WARN]${NC} $1"; }
error() { echo -e "${RED}[ERROR]${NC} $1" >&2; }

# ── Rutas ────────────────────────────────────────────────────────────────────
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

# ── Constantes de perfil (T1.2) ──────────────────────────────────────────────
PROFILE_LOCAL="local"
PROFILE_OFFLINE_FIRST="offline-first"
PROFILE_REMOTE_SERVER="remote-server"
PROFILE_DESKTOP="desktop"

declare -A PROFILE_DESCRIPTIONS=(
  [local]="SQLite local, sin sync"
  [offline-first]="SQLite local + sync a servidor remoto"
  [remote-server]="PostgreSQL compartido, sin sync"
  [desktop]="CLI SQLite local + contenedor Docker (remote-server + PostgreSQL) como hub de sync — un usuario, varios dispositivos"
)

# ── Tabla de mapeo perfil→métodos (T1.1) ─────────────────────────────────────
# Fuente única de verdad: filtra el paso 2, los defaults de -y y los tests.
declare -A PROFILE_METHODS=(
  [local]="release build"
  [offline-first]="release build"
  [remote-server]="release build docker"
  [desktop]="docker build"
)

declare -A METHOD_LABELS=(
  [release]="Release pre-built (descarga binario)"
  [build]="Build from source (.NET 10 SDK requerido)"
  [docker]="Docker (levantamos servidor en contenedor)"
)

# methods_for_profile(): lista de métodos (separados por espacio) para un perfil.
methods_for_profile() {
  echo "${PROFILE_METHODS[$1]:-}"
}

# method_allowed(): 0 si el método es válido para el perfil, 1 en caso contrario.
method_allowed() {
  [[ " ${PROFILE_METHODS[$1]:-} " == *" $2 "* ]]
}

# ── Estado de navegación (T1.3) ──────────────────────────────────────────────
STEP="profile"
# export: PREV_STEP es estado de navegación documentado (T1.3), inspeccionado por tests que hacen source
export PREV_STEP=""
TOKEN=""
WIZARD_QUIT=0
WIZARD_ERROR=0
SELECTED_PROFILE=""
SELECTED_METHOD=""
PG_MODE=""
DATA_DIR=""
ENGRAM_USER=""
SERVER_URL=""
SYNC_ENABLED=""
SYNC_AUTO_SYNC=""
PG_CONNECTION=""
PG_HOST=""
PG_PORT=""
PG_USER=""
PG_PASSWORD=""
PG_DATABASE=""
EDITOR_CHOICE=""
ENGRAM_CMD=""

# Secuencia de pasos para el perfil actual (desktop inserta pg_mode).
step_sequence() {
  local seq=("profile" "method")
  [[ "$SELECTED_PROFILE" == "$PROFILE_DESKTOP" ]] && seq+=("pg_mode")
  seq+=("config" "mcp_editor" "install" "verify")
  printf '%s\n' "${seq[@]}"
}

# Número de paso (1-based) para mostrarlo en pantalla.
step_number() {
  local s idx=0
  while IFS= read -r s; do
    [[ "$s" == "$STEP" ]] && { echo "$((idx + 1))"; return 0; }
    ((idx++))
  done < <(step_sequence)
  echo "?"
}

go_next() {
  local seq=() s i=0 found=-1
  while IFS= read -r s; do seq+=("$s"); done < <(step_sequence)
  for i in "${!seq[@]}"; do
    [[ "${seq[$i]}" == "$STEP" ]] && { found=$i; break; }
  done
  if [[ $found -ge 0 && $found -lt $((${#seq[@]} - 1)) ]]; then
    PREV_STEP="$STEP"
    STEP="${seq[$((found + 1))]}"
  fi
}

go_back() {
  local seq=() s i=0 found=-1
  while IFS= read -r s; do seq+=("$s"); done < <(step_sequence)
  for i in "${!seq[@]}"; do
    [[ "${seq[$i]}" == "$STEP" ]] && { found=$i; break; }
  done
  if [[ $found -le 0 ]]; then
    quit_wizard
  else
    STEP="${seq[$((found - 1))]}"
    PREV_STEP="$STEP"
  fi
}

quit_wizard() {
  echo ""
  warn "Instalación cancelada."
  WIZARD_QUIT=1
  return 0
}

# ask(): imprime el prompt (a stderr) y devuelve la respuesta por stdout.
# Devuelve "__BACK__" o "__QUIT__" si el usuario escribe (b)/(q).
ask() {
  local msg="$1" val=""
  echo -ne "${YELLOW}[?]${NC} ${msg} " >&2
  read -r val || true
  case "$val" in
    b|B) echo "__BACK__"; return 0 ;;
    q|Q) echo "__QUIT__"; return 0 ;;
  esac
  echo "$val"
}

editor_name() {
  case "$1" in
    2) echo "Cursor" ;;
    3) echo "Claude Desktop" ;;
    4) echo "VS Code" ;;
    5) echo "OpenCode" ;;
    *) echo "ninguno" ;;
  esac
}

# ── Paso 1: Perfil ───────────────────────────────────────────────────────────
step_profile() {
  echo ""
  echo "═══════════════════════════════════════════════════════"
  echo "  Paso $(step_number) — Perfil de uso"
  echo "═══════════════════════════════════════════════════════"
  echo ""
  echo "  [1] local         — ${PROFILE_DESCRIPTIONS[$PROFILE_LOCAL]}"
  echo "  [2] offline-first — ${PROFILE_DESCRIPTIONS[$PROFILE_OFFLINE_FIRST]}"
  echo "  [3] remote-server — ${PROFILE_DESCRIPTIONS[$PROFILE_REMOTE_SERVER]}"
  echo "  [4] desktop       — ${PROFILE_DESCRIPTIONS[$PROFILE_DESKTOP]}"
  echo ""

  if [[ "$FORCE" == true ]]; then
    local preset="${FORCE_PROFILE:-$PROFILE_LOCAL}"
    case "$preset" in
      local|offline-first|remote-server|desktop) SELECTED_PROFILE="$preset" ;;
      *) error "Perfil inválido: $preset"; WIZARD_ERROR=1; WIZARD_QUIT=1; return 0 ;;
    esac
    TOKEN="next"
    return 0
  fi

  local ans
  ans="$(ask "Elegí 1-4 [1] (b: salir, q: salir):")"
  case "$ans" in
    __BACK__) TOKEN="quit"; return 0 ;;
    __QUIT__) TOKEN="quit"; return 0 ;;
  esac

  case "${ans:-1}" in
    1) SELECTED_PROFILE="$PROFILE_LOCAL" ;;
    2) SELECTED_PROFILE="$PROFILE_OFFLINE_FIRST" ;;
    3) SELECTED_PROFILE="$PROFILE_REMOTE_SERVER" ;;
    4) SELECTED_PROFILE="$PROFILE_DESKTOP" ;;
    *) warn "Opción inválida (1-4)."; TOKEN="stay"; return 0 ;;
  esac

  info "  Perfil seleccionado: $SELECTED_PROFILE"
  TOKEN="next"
  return 0
}

# ── Paso 2: Método (filtrado por perfil) ─────────────────────────────────────
step_method() {
  local methods=()
  read -r -a methods <<< "$(methods_for_profile "$SELECTED_PROFILE")"

  # Un solo método disponible (desktop → docker): auto-selección sin prompt.
  if [[ ${#methods[@]} -eq 1 ]]; then
    SELECTED_METHOD="${methods[0]}"
    info "Paso $(step_number) — Método: ${METHOD_LABELS[$SELECTED_METHOD]} (único disponible para este perfil)"
    TOKEN="next"
    return 0
  fi

  echo ""
  info "Paso $(step_number) — Método de instalación"
  local i=1 m
  for m in "${methods[@]}"; do
    echo "  [$i] ${METHOD_LABELS[$m]}"
    ((i++))
  done
  echo ""

  if [[ "$FORCE" == true ]]; then
    local preset="${FORCE_METHOD:-${methods[0]}}"
    if method_allowed "$SELECTED_PROFILE" "$preset"; then
      SELECTED_METHOD="$preset"
    else
      error "Método inválido para el perfil '$SELECTED_PROFILE': $preset"
      WIZARD_ERROR=1; WIZARD_QUIT=1
      return 0
    fi
    TOKEN="next"
    return 0
  fi

  local ans
  ans="$(ask "Elegí 1-$((i - 1)) [1] (b: volver, q: salir):")"
  case "$ans" in
    __BACK__) TOKEN="back"; return 0 ;;
    __QUIT__) TOKEN="quit"; return 0 ;;
  esac

  if [[ "${ans:-1}" =~ ^[0-9]+$ ]] && (( ans >= 1 && ans < i )); then
    SELECTED_METHOD="${methods[$((ans - 1))]}"
    TOKEN="next"
  else
    warn "Opción inválida (1-$((i - 1)))."
    TOKEN="stay"
  fi
  return 0
}

# ── Paso (desktop): modo de PostgreSQL ───────────────────────────────────────
step_pg_mode() {
  echo ""
  info "Paso $(step_number) — Modo de PostgreSQL (desktop)"
  warn "We recommend setting a fixed IP address on your local network to avoid connection issues"
  echo ""
  echo "  [1] Todo en uno — PostgreSQL dentro del contenedor de engram"
  echo "  [2] Contenedor separado — PostgreSQL como contenedor Docker aparte"
  echo "  [3] PostgreSQL existente en tu red Docker"
  echo ""

  if [[ "$FORCE" == true ]]; then
    PG_MODE="${FORCE_PG_MODE:-all-in-one}"
    TOKEN="next"
    return 0
  fi

  local ans
  ans="$(ask "Elegí 1-3 [1] (b: volver, q: salir):")"
  case "$ans" in
    __BACK__) TOKEN="back"; return 0 ;;
    __QUIT__) TOKEN="quit"; return 0 ;;
  esac

  case "${ans:-1}" in
    1) PG_MODE="all-in-one" ;;
    2) PG_MODE="separate" ;;
    3) PG_MODE="existing" ;;
    *) warn "Opción inválida (1-3)."; TOKEN="stay"; return 0 ;;
  esac

  info "  Modo PostgreSQL: $PG_MODE"
  TOKEN="next"
  return 0
}

# ── Paso: Configuración / datos por perfil ───────────────────────────────────
step_config() {
  echo ""
  info "Paso $(step_number) — Configuración"

  # Datos comunes a todos los perfiles: ENGRAM_DATA_DIR + ENGRAM_USER
  if [[ "$FORCE" == true ]]; then
    DATA_DIR="${DATA_DIR:-${HOME}/.engram}"
    ENGRAM_USER="${ENGRAM_USER:-$(whoami)}"
  else
    local ans
    local data_dir="${DATA_DIR:-${HOME}/.engram}"
    ans="$(ask "ENGRAM_DATA_DIR [$data_dir]:")"
    case "$ans" in
      __BACK__) TOKEN="back"; return 0 ;;
      __QUIT__) TOKEN="quit"; return 0 ;;
    esac
    DATA_DIR="${ans:-$data_dir}"

    local user_default="${ENGRAM_USER:-$(whoami)}"
    ans="$(ask "ENGRAM_USER [$user_default]:")"
    case "$ans" in
      __BACK__) TOKEN="back"; return 0 ;;
      __QUIT__) TOKEN="quit"; return 0 ;;
    esac
    ENGRAM_USER="${ans:-$user_default}"
  fi

  case "$SELECTED_PROFILE" in
    "$PROFILE_OFFLINE_FIRST")
      SYNC_ENABLED="true"
      if [[ "$FORCE" == true ]]; then
        SERVER_URL="${SERVER_URL:-http://localhost:7437}"
        SYNC_AUTO_SYNC="${SYNC_AUTO_SYNC:-true}"
      else
        local ans
        ans="$(ask "ENGRAM_SERVER_URL (obligatorio):")"
        case "$ans" in
          __BACK__) TOKEN="back"; return 0 ;;
          __QUIT__) TOKEN="quit"; return 0 ;;
        esac
        if [[ -z "$ans" ]]; then
          warn "Without a remote server URL, please use the 'local' profile instead"
          TOKEN="jump_to_profile"
          return 0
        fi
        SERVER_URL="$ans"

        ans="$(ask "ENGRAM_SYNC_AUTO_SYNC [true]:")"
        case "$ans" in
          __BACK__) TOKEN="back"; return 0 ;;
          __QUIT__) TOKEN="quit"; return 0 ;;
        esac
        SYNC_AUTO_SYNC="${ans:-true}"
      fi
      ;;

    "$PROFILE_REMOTE_SERVER")
      SYNC_ENABLED="false"
      if [[ "$FORCE" == true ]]; then
        PG_CONNECTION="${PG_CONNECTION:-Host=localhost;Port=5432;Database=engram;Username=engram;Password=tu_password}"
      else
        local ans
        ans="$(ask "ENGRAM_PG_CONNECTION [Host=localhost;Port=5432;Database=engram;Username=engram;Password=tu_password]:")"
        case "$ans" in
          __BACK__) TOKEN="back"; return 0 ;;
          __QUIT__) TOKEN="quit"; return 0 ;;
        esac
        PG_CONNECTION="${ans:-Host=localhost;Port=5432;Database=engram;Username=engram;Password=tu_password}"
      fi
      ;;

    "$PROFILE_DESKTOP")
      SYNC_ENABLED="true"
      SERVER_URL="http://localhost:7437"
      if [[ "$FORCE" == true ]]; then
        SYNC_AUTO_SYNC="${SYNC_AUTO_SYNC:-true}"
      else
        local ans
        ans="$(ask "ENGRAM_SYNC_AUTO_SYNC [true]:")"
        case "$ans" in
          __BACK__) TOKEN="back"; return 0 ;;
          __QUIT__) TOKEN="quit"; return 0 ;;
        esac
        SYNC_AUTO_SYNC="${ans:-true}"
      fi

      if [[ "$PG_MODE" == "existing" ]]; then
        if [[ "$FORCE" == true ]]; then
          PG_HOST="${PG_HOST:-localhost}"
          PG_PORT="${PG_PORT:-5432}"
          PG_USER="${PG_USER:-engram}"
          PG_DATABASE="${PG_DATABASE:-engram}"
          PG_PASSWORD="${PG_PASSWORD:-engram_password}"
        else
          local ans
          ans="$(ask "PG_HOST [localhost]:")"
          case "$ans" in __BACK__) TOKEN="back"; return 0 ;; __QUIT__) TOKEN="quit"; return 0 ;; esac
          PG_HOST="${ans:-localhost}"

          ans="$(ask "PG_PORT [5432]:")"
          case "$ans" in __BACK__) TOKEN="back"; return 0 ;; __QUIT__) TOKEN="quit"; return 0 ;; esac
          PG_PORT="${ans:-5432}"

          ans="$(ask "PG_USER [engram]:")"
          case "$ans" in __BACK__) TOKEN="back"; return 0 ;; __QUIT__) TOKEN="quit"; return 0 ;; esac
          PG_USER="${ans:-engram}"

          ans="$(ask "PG_PASSWORD:")"
          case "$ans" in __BACK__) TOKEN="back"; return 0 ;; __QUIT__) TOKEN="quit"; return 0 ;; esac
          PG_PASSWORD="$ans"

          ans="$(ask "PG_DATABASE [engram]:")"
          case "$ans" in __BACK__) TOKEN="back"; return 0 ;; __QUIT__) TOKEN="quit"; return 0 ;; esac
          PG_DATABASE="${ans:-engram}"
        fi
        PG_CONNECTION="Host=${PG_HOST};Port=${PG_PORT};Database=${PG_DATABASE};Username=${PG_USER};Password=${PG_PASSWORD}"
      else
        # all-in-one / separate: PostgreSQL embebido con password generado.
        if [[ -z "${PG_PASSWORD:-}" ]]; then
          PG_PASSWORD="$(od -An -N 12 -tx1 /dev/urandom 2>/dev/null | tr -d ' \n' || echo "engram_password")"
        fi
        local pg_host="postgres"
        [[ "$PG_MODE" == "all-in-one" ]] && pg_host="localhost"
        PG_CONNECTION="Host=${pg_host};Port=5432;Database=engram;Username=engram;Password=${PG_PASSWORD}"
      fi
      ;;

    *)
      SYNC_ENABLED="false"
      ;;
  esac

  TOKEN="next"
  return 0
}

# ── Paso: Editor MCP ─────────────────────────────────────────────────────────
step_mcp_editor() {
  echo ""
  info "Paso $(step_number) — Configuración MCP"
  echo "  [1] Ninguno (solo CLI)"
  echo "  [2] Cursor"
  echo "  [3] Claude Desktop"
  echo "  [4] VS Code"
  echo "  [5] OpenCode"

  if [[ "$FORCE" == true ]]; then
    EDITOR_CHOICE=5
    TOKEN="next"
    return 0
  fi

  local ans
  ans="$(ask "Elegí 1-5 [5] (b: volver, q: salir):")"
  case "$ans" in
    __BACK__) TOKEN="back"; return 0 ;;
    __QUIT__) TOKEN="quit"; return 0 ;;
  esac

  case "${ans:-5}" in
    1|2|3|4|5) EDITOR_CHOICE="${ans:-5}" ;;
    *) warn "Opción inválida (1-5)."; TOKEN="stay"; return 0 ;;
  esac
  TOKEN="next"
  return 0
}

# ── MCP config (writer Python, perfil-aware) ─────────────────────────────────
write_mcp_config() {
  local target="$1"
  local root_key="$2"
  mkdir -p "$(dirname "$target")"

  python3 - "$target" "$root_key" "$DATA_DIR" "$ENGRAM_USER" "$SELECTED_PROFILE" \
    "$SYNC_ENABLED" "$SERVER_URL" "$PG_CONNECTION" "$SYNC_AUTO_SYNC" << 'PYTHON_SCRIPT'
import json, sys
path = sys.argv[1]
key = sys.argv[2]
data_dir = sys.argv[3]
user = sys.argv[4]
profile = sys.argv[5]
sync_enabled = sys.argv[6]
server_url = sys.argv[7] if len(sys.argv) > 7 else ""
pg_connection = sys.argv[8] if len(sys.argv) > 8 else ""
sync_auto_sync = sys.argv[9] if len(sys.argv) > 9 else ""

env = {
    "ENGRAM_DATA_DIR": data_dir,
    "ENGRAM_USER": user,
    "ENGRAM_PROFILE": profile,
    "ENGRAM_SYNC_ENABLED": sync_enabled,
}
if server_url:
    env["ENGRAM_SERVER_URL"] = server_url
if pg_connection:
    env["ENGRAM_PG_CONNECTION"] = pg_connection
if sync_auto_sync:
    env["ENGRAM_SYNC_AUTO_SYNC"] = sync_auto_sync

if key == "mcp":
    # OpenCode: requiere transport local + flag enabled + environment (no "env").
    block = {
        "type": "local",
        "enabled": True,
        "command": ["engram", "mcp"],
        "environment": env,
    }
    root = {"mcp": {"engram": block}}
else:
    block = {
        "command": ["engram", "mcp"],
        "env": env,
    }
    root = {key: {"engram": block}}

with open(path, "w") as f:
    json.dump(root, f, indent=2)
PYTHON_SCRIPT
  info "  Escrito: $target"
}

write_mcp_configs() {
  case "$EDITOR_CHOICE" in
    2) write_mcp_config "${HOME}/.cursor/mcp.json" "mcpServers" ;;
    3) write_mcp_config "${HOME}/.config/Claude/claude_desktop_config.json" "mcpServers" ;;
    4) mkdir -p "${HOME}/.config/Code/User"; write_mcp_config "${HOME}/.config/Code/User/mcp.json" "servers" ;;
    5) write_mcp_config "${HOME}/.config/opencode/opencode.json" "mcp" ;;
  esac
  # Copia de respaldo en el data dir
  write_mcp_config "${DATA_DIR}/mcp.config.json" "mcpServers"
}

# ── Métodos de instalación ───────────────────────────────────────────────────
install_release() {
  local os_name="linux-x64"
  [[ "$(uname -s)" == "Darwin" ]] && os_name="macos-x64"
  local latest="v1.3.0"
  local base_url="https://github.com/efreet111/engram-dotnet/releases/download/${latest}"
  info "  Descargando engram $latest..."
  curl -L --fail -o "$ENGRAM_CMD" "${base_url}/engram-${os_name}" || {
    error "Descarga falló. Probá método 2 (build from source)."
    return 1
  }
  chmod +x "$ENGRAM_CMD"

  # HU-022: SQLitePCLRaw busca libe_sqlite3.so y e_sqlite3.so en Linux.
  # Descargar la native library y crear symlinks si estamos en Linux.
  if [[ "$(uname -s)" == "Linux" ]]; then
    local lib_dir
    lib_dir="$(dirname "$ENGRAM_CMD")"
    local lib_target="${lib_dir}/libe_sqlite3.so"
    local use_sudo=""

    # Si el directorio o archivo no es escribible por el usuario actual,
    # intentamos con sudo (有用 si se instaló antes con root).
    if [[ ! -w "$lib_dir" ]] || { [[ -f "$lib_target" ]] && [[ ! -w "$lib_target" ]]; }; then
      if command -v sudo >/dev/null 2>&1; then
        use_sudo="sudo"
        info "  libe_sqlite3.so requiere permisos de root..."
      else
        error "No se puede escribir en $lib_dir. Ejecutá con permisos de root o cambiá el dueño: sudo chown -R \$(id -u):\$(id -g) ~/.local/bin"
        return 1
      fi
    fi

    info "  Descargando libe_sqlite3.so..."
    if ! ${use_sudo} curl -L --fail -o "${lib_target}" "${base_url}/libe_sqlite3.so"; then
      error "Descarga de libe_sqlite3.so falló."
      return 1
    fi

    # SQLitePCLRaw busca e_sqlite3.so (alias del provider).
    # Crear symlink si no existe ya.
    if [[ ! -e "${lib_dir}/e_sqlite3.so" ]]; then
      ${use_sudo} ln -sf "libe_sqlite3.so" "${lib_dir}/e_sqlite3.so"
    fi
    info "  SQLite native library configurada."
  fi

  return 0
}

install_build() {
  if ! command -v dotnet >/dev/null 2>&1; then
    error ".NET 10 SDK no encontrado. Instalalo o usá método 1."
    return 1
  fi
  if [[ ! -f "${REPO_ROOT}/src/Engram.Cli/Engram.Cli.csproj" ]]; then
    error "No se encontró el repo. Clonalo o especificá la ruta."
    return 1
  fi

  # Actualizar System.CommandLine a versión estable (fija el bug de los pre-built)
  info "  Actualizando System.CommandLine a 2.0.11..."
  dotnet add "${REPO_ROOT}/src/Engram.Cli/Engram.Cli.csproj" \
    package System.CommandLine --version 2.0.11 || {
    warn "  No se pudo actualizar System.CommandLine, continuando con la versión existente..."
  }

  info "  Compilando (puede tardar varios minutos)..."
  local rid="linux-x64"
  [[ "$(uname -s)" == "Darwin" ]] && rid="macos-x64"
  local tmpdir="${HOME}/.local/tmp-engram"
  # --self-contained true: el binary incluye el runtime de .NET completo,
  # necesario para que funcione dentro del contenedor Docker que solo tiene
  # el runtime de ASP.NET Core (no el SDK completo).
  dotnet publish "${REPO_ROOT}/src/Engram.Cli/Engram.Cli.csproj" \
    -c Release -r "$rid" --self-contained true \
    -o "$tmpdir" || return 1

  # Si es desktop, también buildamos la imagen Docker local con este binary
  if [[ "$SELECTED_PROFILE" == "$PROFILE_DESKTOP" ]]; then
    info "  Buildando imagen Docker local con el binary compilado..."
    if ! command -v docker >/dev/null 2>&1; then
      error "Docker no encontrado. No se puede buildar la imagen."
      return 1
    fi
    local binary_path="${tmpdir}/engram"
    # Copiar el binary al contexto de build (repo root) para que COPY pueda usarlo
    local context_binary="${REPO_ROOT}/engram-local"
    # Crear dummy si no existe (para que COPY en Dockerfile nunca falle)
    touch "$context_binary"
    cp "$binary_path" "$context_binary"

    # Copiar la librería nativa SQLite al contexto de build.
    # dotnet publish --self-contained true coloca libe_sqlite3.so en la raíz del output,
    # NO dentro de runtimes/<rid>/native/ (ese es el path de otros packages).
    local native_lib="${tmpdir}/libe_sqlite3.so"
    local context_native="${REPO_ROOT}/libe_sqlite3-local"
    if [[ -f "$native_lib" ]]; then
      cp "$native_lib" "$context_native"
    else
      touch "$context_native"
    fi

    if ! docker build \
        --build-arg ENGRAM_BINARY=local \
        -t "engram-dotnet-allinone:latest" \
        -f "${REPO_ROOT}/docker/Dockerfile.allinone" \
        "${REPO_ROOT}"; then
      error "Falló el build de la imagen Docker."
      return 1
    fi
    info "  Imagen Docker local lista: engram-dotnet-allinone:latest"
    # Limpiar archivos de contexto de build (dummies + copias)
    rm -f "$context_binary" "$context_native"
  fi

  mv "${tmpdir}/engram" "$ENGRAM_CMD"
  rm -rf "$tmpdir"
  chmod +x "$ENGRAM_CMD"
  return 0
}

install_docker() {
  info "  Usando Docker (imagen ghcr.io/efreet111/engram-dotnet:latest)..."
  if ! command -v docker >/dev/null 2>&1; then
    error "Docker no encontrado. Instalalo primero."
    return 1
  fi
  ENGRAM_CMD="docker run --rm ghcr.io/efreet111/engram-dotnet:latest engram"
  return 0
}

# ── Generación de Docker Compose (desktop) ───────────────────────────────────
generate_desktop_compose() {
  local compose_dir="${DATA_DIR}/desktop"
  mkdir -p "$compose_dir"
  local compose_file="$compose_dir/docker-compose.yml"

  case "$PG_MODE" in
    all-in-one) generate_compose_allinone > "$compose_file" ;;
    separate)   generate_compose_separate > "$compose_file" ;;
    existing)   generate_compose_existing > "$compose_file" ;;
    *) error "Modo PostgreSQL desconocido: $PG_MODE"; return 1 ;;
  esac

  cat > "$compose_dir/.env" <<EOF
# Credenciales del PostgreSQL del desktop (generado por install.sh)
ENGRAM_DATA_DIR_HOST=${DATA_DIR}
ENGRAM_PG_PASSWORD=${PG_PASSWORD:-}
EOF

  info "  Escrito: $compose_file"
  info "  Escrito: $compose_dir/.env"
  return 0
}

generate_compose_allinone() {
  local image_block
  if [[ -f "$REPO_ROOT/docker/Dockerfile.allinone" ]]; then
    image_block="    image: engram-dotnet-allinone:latest
    build:
      context: ${REPO_ROOT}
      dockerfile: docker/Dockerfile.allinone"
  else
    image_block="    image: ghcr.io/efreet111/engram-dotnet-allinone:latest"
  fi

  cat <<EOF
services:
  engram:
${image_block}
    container_name: engram
    restart: unless-stopped
    ports:
      - "7437:7437"
    volumes:
      - ${DATA_DIR}/engram:/data/engram
      - ${DATA_DIR}/postgres:/data/postgres
    environment:
      ENGRAM_PROFILE: remote-server
      ENGRAM_DB_TYPE: postgres
      ENGRAM_DATA_DIR: /data/engram
      ENGRAM_PORT: "7437"
      ENGRAM_PG_CONNECTION: "${PG_CONNECTION}"
      ENGRAM_SYNC_AUTO_SYNC: "${SYNC_AUTO_SYNC}"
      ENGRAM_USER: "${ENGRAM_USER}"
EOF
}

generate_compose_separate() {
  cat <<EOF
services:
  postgres:
    image: postgres:16-alpine
    container_name: engram-postgres
    restart: unless-stopped
    ports:
      - "5432:5432"
    environment:
      POSTGRES_DB: engram
      POSTGRES_USER: engram
      POSTGRES_PASSWORD: "${PG_PASSWORD}"
    volumes:
      - ${DATA_DIR}/postgres:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U engram -d engram"]
      interval: 5s
      timeout: 5s
      retries: 10

  engram:
    image: ghcr.io/efreet111/engram-dotnet:latest
    container_name: engram
    restart: unless-stopped
    depends_on:
      postgres:
        condition: service_healthy
    ports:
      - "7437:7437"
    volumes:
      - ${DATA_DIR}/engram:/data/engram
    environment:
      ENGRAM_PROFILE: remote-server
      ENGRAM_DB_TYPE: postgres
      ENGRAM_DATA_DIR: /data/engram
      ENGRAM_PORT: "7437"
      ENGRAM_PG_CONNECTION: "${PG_CONNECTION}"
      ENGRAM_SYNC_AUTO_SYNC: "${SYNC_AUTO_SYNC}"
      ENGRAM_USER: "${ENGRAM_USER}"
EOF
}

generate_compose_existing() {
  cat <<EOF
services:
  engram:
    image: ghcr.io/efreet111/engram-dotnet:latest
    container_name: engram
    restart: unless-stopped
    ports:
      - "7437:7437"
    extra_hosts:
      - "host.docker.internal:host-gateway"
    volumes:
      - ${DATA_DIR}/engram:/data/engram
    environment:
      ENGRAM_PROFILE: remote-server
      ENGRAM_DB_TYPE: postgres
      ENGRAM_DATA_DIR: /data/engram
      ENGRAM_PORT: "7437"
      ENGRAM_PG_CONNECTION: "${PG_CONNECTION}"
      ENGRAM_SYNC_AUTO_SYNC: "${SYNC_AUTO_SYNC}"
      ENGRAM_USER: "${ENGRAM_USER}"
EOF
}

start_desktop_services() {
  local compose_dir="${DATA_DIR}/desktop"
  if ! command -v docker >/dev/null 2>&1; then
    warn "Docker no encontrado. Los archivos quedaron en $compose_dir — ejecutá 'docker compose up -d' cuando instales Docker."
    return 0
  fi
  info "  Levantando servicios Docker..."
  if (cd "$compose_dir" && docker compose up -d --build); then
    info "  Servicios iniciados."
  else
    warn "No se pudieron iniciar los servicios. Ejecutá manualmente: cd $compose_dir && docker compose up -d"
  fi
  return 0
}

# ── Paso: Instalación ────────────────────────────────────────────────────────
step_install() {
  echo ""
  info "Paso $(step_number) — Instalando..."

  mkdir -p "${DATA_DIR}" "${HOME}/.local/bin" "${HOME}/.config/opencode"
  ENGRAM_CMD="${HOME}/.local/bin/engram"

  if [[ "$SELECTED_PROFILE" == "$PROFILE_DESKTOP" ]]; then
    # Desktop: servidor vía Docker Compose + CLI local para el cliente MCP.
    # El método determina cómo se instala el CLI:
    #   - release: descarga de GitHub (pre-built)
    #   - build: compila localmente (fix System.CommandLine) + build imagen Docker local
    case "$SELECTED_METHOD" in
      release)
        install_release || { error "Instalación falló."; TOKEN="quit"; WIZARD_ERROR=1; return 0; }
        ;;
      build)
        install_build || { error "Instalación falló."; TOKEN="quit"; WIZARD_ERROR=1; return 0; }
        ;;
      docker)
        install_docker || { error "Instalación falló."; TOKEN="quit"; WIZARD_ERROR=1; return 0; }
        ;;
    esac
    if ! generate_desktop_compose; then
      TOKEN="quit"; WIZARD_ERROR=1
      return 0
    fi

    local start_choice="2"
    if [[ "$FORCE" != true ]]; then
      echo ""
      info "Servicios Docker (desktop)"
      echo "  [1] Solo generar archivos (ejecutar 'docker compose up' más tarde)"
      echo "  [2] Generar archivos Y levantar servicios ahora"
      local ans
      ans="$(ask "Elegí 1-2 [2] (b: volver, q: salir):")"
      case "$ans" in
        __BACK__) TOKEN="back"; return 0 ;;
        __QUIT__) TOKEN="quit"; return 0 ;;
      esac
      start_choice="${ans:-2}"
    fi

    if [[ "$start_choice" == "2" ]]; then
      start_desktop_services
    fi
  else
    case "$SELECTED_METHOD" in
      release)
        install_release || { error "Instalación falló."; TOKEN="quit"; WIZARD_ERROR=1; return 0; }
        ;;
      build)
        install_build || { error "Instalación falló."; TOKEN="quit"; WIZARD_ERROR=1; return 0; }
        ;;
      docker)
        install_docker || { error "Instalación falló."; TOKEN="quit"; WIZARD_ERROR=1; return 0; }
        ;;
    esac
  fi

  # Generar MCP config
  echo ""
  info "Configurando MCP..."
  write_mcp_configs

  TOKEN="next"
  return 0
}

# ── Paso: Verificación + resumen ─────────────────────────────────────────────
print_summary() {
  echo ""
  echo "═══════════════════════════════════════════════════════"
  echo -e "  ${GREEN}Instalación completa${NC}"
  echo "═══════════════════════════════════════════════════════"
  echo ""
  echo "  Datos:      $DATA_DIR"
  echo "  Perfil:     $SELECTED_PROFILE"
  echo "  Método:     ${METHOD_LABELS[$SELECTED_METHOD]}"
  echo "  Usuario:    $ENGRAM_USER"
  echo ""

  if [[ "$SELECTED_PROFILE" == "$PROFILE_DESKTOP" ]]; then
    echo -e "  ${CYAN}ENGRAM_SERVER_URL = http://localhost:7437${NC}"
    echo "  On your other devices, select the 'offline-first' profile and use this URL as the remote server"
    echo ""
    echo "  Docker Compose: ${DATA_DIR}/desktop/docker-compose.yml"
    [[ -n "$PG_PASSWORD" ]] && echo "  PostgreSQL password: $PG_PASSWORD"
    echo ""
  elif [[ -n "$SERVER_URL" ]]; then
    echo "  Server: $SERVER_URL"
    echo ""
  fi

  echo "  MCP configurado para: $(editor_name "$EDITOR_CHOICE")"
  echo ""
  echo "  Comandos:"
  echo "    engram serve         # iniciar servidor"
  echo "    engram mcp          # iniciar MCP (para IDE)"
  echo "    engram doctor       # diagnóstico"
  echo ""
  echo "  Para desinstalar TODO:"
  echo "    ./scripts/uninstall.sh"
  echo ""
}

step_verify() {
  echo ""
  info "Paso $(step_number) — Verificación..."

  if [[ -x "$ENGRAM_CMD" ]]; then
    local version
    version="$("$ENGRAM_CMD" --version 2>/dev/null || echo "error")"
    info "  Versión: $version"
  fi

  info "  Datos: $DATA_DIR"
  info "  Usuario: $ENGRAM_USER"
  info "  Perfil: $SELECTED_PROFILE"
  [[ -n "$SERVER_URL" ]] && info "  Server: $SERVER_URL"

  print_summary
  TOKEN="finish"
  return 0
}

# ── Help ─────────────────────────────────────────────────────────────────────
usage() {
  cat <<EOF
Uso: $(basename "$0") [-y] [--profile PERFIL] [--method METODO] [--pg-mode MODO]

  -y                No preguntar (defaults por perfil)
  --profile PERFIL  local | offline-first | remote-server | desktop (con -y)
  --method METODO   release | build | docker (con -y; debe ser válido para el perfil)
  --pg-mode MODO    all-in-one | separate | existing (con -y; solo desktop)
  -h, --help        Esta ayuda
EOF
}

# ── Variables de argumentos ──────────────────────────────────────────────────
FORCE=false
FORCE_PROFILE=""
FORCE_METHOD=""
FORCE_PG_MODE=""

# ── Dispatcher del wizard ────────────────────────────────────────────────────
run_wizard() {
  while true; do
    [[ "$WIZARD_QUIT" == 1 ]] && break
    TOKEN=""
    case "$STEP" in
      profile)    step_profile ;;
      method)     step_method ;;
      pg_mode)    step_pg_mode ;;
      config)     step_config ;;
      mcp_editor) step_mcp_editor ;;
      install)    step_install ;;
      verify)     step_verify ;;
    esac

    case "$TOKEN" in
      next)            go_next ;;
      back)            go_back ;;
      quit)            quit_wizard ;;
      stay)            : ;;
      jump_to_profile) STEP="profile"; PREV_STEP="" ;;
      finish)          break ;;
    esac
  done
}

# ── Main guard: solo ejecuta si es el script principal (no si es "source"-ado) ──
if [[ "${BASH_SOURCE[0]}" == "$0" ]]; then
  while [[ $# -gt 0 ]]; do
    case "$1" in
      -y|--yes|--force) FORCE=true ;;
      --profile) FORCE_PROFILE="${2:-}"; shift ;;
      --method) FORCE_METHOD="${2:-}"; shift ;;
      --pg-mode) FORCE_PG_MODE="${2:-}"; shift ;;
      -h|--help) usage; exit 0 ;;
      *) error "Argumento desconocido: $1"; usage; exit 1 ;;
    esac
    shift
  done

  echo ""
  echo "═══════════════════════════════════════════════════════"
  echo "  engram-dotnet — Instalación completa"
  echo "═══════════════════════════════════════════════════════"
  echo ""

  run_wizard
  exit "$WIZARD_ERROR"
fi
