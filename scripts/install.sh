#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# engram-dotnet — Install Script
#
# Instalación completa con MCP y opción Docker.
#
# USO:
#   ./install.sh          # interactivo
#   ./install.sh -y       # sin confirmar (defaults)
# ─────────────────────────────────────────────────────────────────────────────

set -euo pipefail

FORCE=false

# ── Colores ──────────────────────────────────────────────────────────────────
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m'

info()  { echo -e "${GREEN}[INFO]${NC} $1"; }
warn()  { echo -e "${YELLOW}[WARN]${NC} $1"; }
error() { echo -e "${RED}[ERROR]${NC} $1" >&2; }

# ── Argumentos ────────────────────────────────────────────────────────────────
for arg in "$@"; do
    case $arg in
        -y|--yes|--force) FORCE=true ;;
        -h|--help)
            echo "Uso: $0 [-y]"
            echo ""
            echo "  -y  No preguntar confirmación"
            exit 0
            ;;
    esac
done

confirm() {
    [[ "$FORCE" == true ]] && return 0
    echo -ne "${YELLOW}[?]${NC} $1 [y/N]: "
    read -r resp
    [[ "$resp" =~ ^[Yy]$ ]]
}

# ── Bienvenida ────────────────────────────────────────────────────────────────
echo ""
echo "═══════════════════════════════════════════════════════════"
echo "  engram-dotnet — Instalación completa"
echo "═══════════════════════════════════════════════════════════"
echo ""

# ── 1. Método de instalación ───────────────────────────────────────────────
info "1. Método de instalación"
echo "  [1] Release pre-built (descarga binario)"
echo "  [2] Build from source (.NET 10 SDK requerido)"
echo "  [3] Docker (levantamos servidor en contenedor)"

if [[ "$FORCE" == true ]]; then
    METHOD=1
else
    read -r -p "Elegí 1-3 [1]: " METHOD
    METHOD="${METHOD:-1}"
fi

# ── 2. Perfil ────────────────────────────────────────────────────────────────
info "2. Perfil de uso"
echo "  [1] local        — SQLite local, sin sync"
echo "  [2] offline-first — SQLite local + sync a servidor"
echo "  [3] remote-server — PostgreSQL compartido, sin sync"
echo "  [4] desktop     — PostgreSQL local, sin sync"

if [[ "$FORCE" == true ]]; then
    PROFILE_CHOICE=1
else
    read -r -p "Elegí 1-4 [1]: " PROFILE_CHOICE
    PROFILE_CHOICE="${PROFILE_CHOICE:-1}"
fi

case "$PROFILE_CHOICE" in
    1) PROFILE="local"; SYNC_ENABLED="false" ;;
    2) PROFILE="offline-first"; SYNC_ENABLED="true" ;;
    3) PROFILE="remote-server"; SYNC_ENABLED="false" ;;
    4) PROFILE="desktop"; SYNC_ENABLED="false" ;;
    *) PROFILE="local"; SYNC_ENABLED="false" ;;
esac

info "  Perfil: $PROFILE"

# ── 3. Docker ────────────────────────────────────────────────────────────────
DOCKER=false
if [[ "$METHOD" == "3" ]]; then
    DOCKER=true
elif [[ "$PROFILE" == "offline-first" ]] || [[ "$PROFILE" == "remote-server" ]] || [[ "$PROFILE" == "desktop" ]]; then
    if confirm "¿Querés levantar PostgreSQL en Docker?"; then
        DOCKER=true
    fi
fi

# ── 4. Datos y usuario ────────────────────────────────────────────────────
info "3. Configuración"

DATA_DIR_DEFAULT="${HOME}/.engram"
read -r -p "ENGRAM_DATA_DIR [$DATA_DIR_DEFAULT]: " DATA_DIR
DATA_DIR="${DATA_DIR:-$DATA_DIR_DEFAULT}"

USER_DEFAULT="$(whoami)"
read -r -p "ENGRAM_USER [$USER_DEFAULT]: " ENGRAM_USER
ENGRAM_USER="${ENGRAM_USER:-$USER_DEFAULT}"

SERVER_URL=""
if [[ "$PROFILE" == "offline-first" ]]; then
    read -r -p "ENGRAM_SERVER_URL [http://localhost:7437]: " SERVER_URL
    SERVER_URL="${SERVER_URL:-http://localhost:7437}"
fi

PG_CONNECTION=""
if [[ "$PROFILE" == "remote-server" ]] || [[ "$PROFILE" == "desktop" ]]; then
    read -r -p "ENGRAM_PG_CONNECTION [Host=localhost;Port=5432;Database=engram;Username=engram;Password=tu_password]: " PG_CONNECTION
    PG_CONNECTION="${PG_CONNECTION:-Host=localhost;Port=5432;Database=engram;Username=engram;Password=tu_password}"
fi

# ── 5. Editor MCP ──────────────────────────────────────────────────────────
info "4. Configuración MCP"
echo "  [1] Ninguno (solo CLI)"
echo "  [2] Cursor"
echo "  [3] Claude Desktop"
echo "  [4] VS Code"
echo "  [5] OpenCode"

if [[ "$FORCE" == true ]]; then
    EDITOR_CHOICE=5
else
    read -r -p "Elegí 1-5 [5]: " EDITOR_CHOICE
    EDITOR_CHOICE="${EDITOR_CHOICE:-5}"
fi

# ── 6. Instalar según método ────────────────────────────────────────────────
echo ""
info "5. Instalando..."

mkdir -p "${DATA_DIR}"
mkdir -p "${HOME}/.local/bin"
mkdir -p "${HOME}/.config/opencode"

ENGRAM_CMD="${HOME}/.local/bin/engram"

if [[ "$METHOD" == "1" ]]; then
    # Release pre-built
    OS_NAME="linux-x64"
    if [[ "$(uname -s)" == "Darwin" ]]; then OS_NAME="macos-x64"; fi
    LATEST="v1.3.0"
    URL="https://github.com/efreet111/engram-dotnet/releases/download/${LATEST}/engram-${OS_NAME}"
    info "  Descargando $LATEST..."
    curl -L --fail -o "$ENGRAM_CMD" "$URL" || {
        error "Descarga falló. Probá método 2 (build from source)."
        exit 1
    }
    chmod +x "$ENGRAM_CMD"

elif [[ "$METHOD" == "2" ]]; then
    if ! command -v dotnet >/dev/null 2>&1; then
        error ".NET 10 SDK no encontrado. Instalalo o usá método 1."
        exit 1
    fi
    info "  Compilando (puede tardar varios minutos)..."
    REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
    if [[ ! -f "${REPO_ROOT}/src/Engram.Cli/Engram.Cli.csproj" ]]; then
        error "No se encontró el repo. Clonalo o especificá la ruta."
        exit 1
    fi
    RID="linux-x64"
    [[ "$(uname -s)" == "Darwin" ]] && RID="macos-x64"
    TMPDIR="${HOME}/.local/tmp-engram"
    dotnet publish "${REPO_ROOT}/src/Engram.Cli/Engram.Cli.csproj" \
        -c Release -r "$RID" --self-contained false \
        -o "$TMPDIR"
    mv "${TMPDIR}/engram" "$ENGRAM_CMD"
    rm -rf "$TMPDIR"
    chmod +x "$ENGRAM_CMD"

elif [[ "$METHOD" == "3" ]]; then
    info "  Usando Docker..."
    if ! command -v docker >/dev/null 2>&1; then
        error "Docker no encontrado. Instalalo primero."
        exit 1
    fi
    # El binary se corre desde el contenedor, no se instala en host
    ENGRAM_CMD="docker run --rm ghcr.io/efreet111/engram-dotnet:latest engram"
fi

# ── 7. Docker: levantar servicios ──────────────────────────────────────────
if [[ "$DOCKER" == "true" ]]; then
    echo ""
    info "6. Levantando servicios Docker..."

    if ! command -v docker >/dev/null 2>&1; then
        error "Docker no está corriendo."
    else
        # Docker Compose para postgres
        COMPOSE_DIR="$(mktemp -d)"
        cat > "${COMPOSE_DIR}/docker-compose.yml" << 'EOF'
services:
  postgres:
    image: postgres:16-alpine
    container_name: engram-postgres
    ports:
      - "5432:5432"
    environment:
      POSTGRES_DB: engram
      POSTGRES_USER: engram
      POSTGRES_PASSWORD: engram_password
    volumes:
      - pgdata:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U engram -d engram"]
      interval: 5s
      timeout: 5s
      retries: 10
volumes:
  pgdata:
EOF
        (cd "$COMPOSE_DIR" && docker compose up -d)
        sleep 3
        info "  PostgreSQL corriendo en localhost:5432"
        PG_CONNECTION="Host=localhost;Port=5432;Database=engram;Username=engram;Password=engram_password"
    fi
fi

# ── 8. Generar MCP config ──────────────────────────────────────────────────
echo ""
info "7. Configurando MCP..."

# Función para escribir config
write_mcp_config() {
    local target="$1"
    local root_key="$2"
    mkdir -p "$(dirname "$target")"

    # Build del JSON manualmente para evitar issues con heredocs
    local json
    json=$(python3 - "$target" "$root_key" "$DATA_DIR" "$ENGRAM_USER" "$PROFILE" "$SYNC_ENABLED" "$SERVER_URL" "$PG_CONNECTION" << 'PYTHON_SCRIPT'
import json, sys
path = sys.argv[1]
key = sys.argv[2]
data_dir = sys.argv[3]
user = sys.argv[4]
profile = sys.argv[5]
sync_enabled = sys.argv[6]
server_url = sys.argv[7] if len(sys.argv) > 7 else ""
pg_connection = sys.argv[8] if len(sys.argv) > 8 else ""

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

block = {
    "command": ["engram", "mcp"],
    "env": env
}

if key == "mcp":
    # Formato OpenCode
    root = {"mcp": {"engram": block}}
else:
    root = {key: {"engram": block}}

with open(path, "w") as f:
    json.dump(root, f, indent=2)
PYTHON_SCRIPT
    )
    info "  Escrito: $target"
}

case "$EDITOR_CHOICE" in
    2) # Cursor
        write_mcp_config "${HOME}/.cursor/mcp.json" "mcpServers"
        ;;
    3) # Claude Desktop
        write_mcp_config "${HOME}/.config/Claude/claude_desktop_config.json" "mcpServers"
        ;;
    4) # VS Code
        mkdir -p "${HOME}/.config/Code/User"
        write_mcp_config "${HOME}/.config/Code/User/mcp.json" "servers"
        ;;
    5) # OpenCode
        write_mcp_config "${HOME}/.config/opencode/opencode.json" "mcp"
        ;;
esac

# También guardar en data dir
write_mcp_config "${DATA_DIR}/mcp.config.json" "mcpServers"

# ── 9. Verificación ────────────────────────────────────────────────────────
echo ""
info "8. Verificación..."

if [[ "$METHOD" != "3" ]]; then
    VERSION=$("$ENGRAM_CMD" --version 2>/dev/null || echo "error")
    info "  Versión: $VERSION"
fi

info "  Datos: $DATA_DIR"
info "  Usuario: $ENGRAM_USER"
info "  Perfil: $PROFILE"
[[ -n "$SERVER_URL" ]] && info "  Server: $SERVER_URL"

# ── Resumen ────────────────────────────────────────────────────────────────
echo ""
echo "═══════════════════════════════════════════════════════════"
echo "  ${GREEN}Instalación completa${NC}"
echo "═══════════════════════════════════════════════════════════"
echo ""
echo "  Datos:      $DATA_DIR"
echo "  Perfil:     $PROFILE"
echo "  Método:     $([[ "$METHOD" == "1" ]] && echo "Release pre-built" || [[ "$METHOD" == "2" ]] && echo "Build from source" || echo "Docker")"
[[ "$DOCKER" == "true" ]] && echo "  Docker:     postgres arriba en :5432"
echo ""
echo "  MCP configurado para: $(case "$EDITOR_CHOICE" in 2) echo "Cursor" ;; 3) echo "Claude Desktop" ;; 4) echo "VS Code" ;; 5) echo "OpenCode" ;; *) echo "ninguno" ;; esac)"
echo ""
echo "  Comandos:"
echo "    engram serve         # iniciar servidor"
echo "    engram mcp          # iniciar MCP (para IDE)"
echo "    engram doctor       # diagnóstico"
echo ""
echo "  Para desinstalar TODO:"
echo "    ./scripts/uninstall.sh"
echo ""
