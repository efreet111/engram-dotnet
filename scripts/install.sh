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
        error "No se encontró el repo. Clonalo o specify la ruta."
        exit 1
    fi
    RID="linux-x64"
    [[ "$(uname -s)" == "Darwin" ]] && RID="macos-x64"
    dotnet publish "${REPO_ROOT}/src/Engram.Cli/Engram.Cli.csproj" \
        -c Release -r "$RID" --self-contained false \
        -o "${HOME}/.local/tmp-engram && mv "${HOME}/.local/tmp-engram/engram" "$ENGRAM_CMD" && rm -rf "${HOME}/.local/tmp-engram"
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

# Armar env vars para el bloque MCP
declare -A MCP_ENV
MCP_ENV[ENGRAM_DATA_DIR]="$DATA_DIR"
MCP_ENV[ENGRAM_USER]="$ENGRAM_USER"
MCP_ENV[ENGRAM_PROFILE]="$PROFILE"
MCP_ENV[ENGRAM_SYNC_ENABLED]="$SYNC_ENABLED"

[[ -n "$SERVER_URL" ]] && MCP_ENV[ENGRAM_SERVER_URL]="$SERVER_URL"
[[ -n "$PG_CONNECTION" ]] && MCP_ENV[ENGRAM_PG_CONNECTION]="$PG_CONNECTION"

# JSON env block
ENV_JSON=$(printf '        "%s": "%s",\n' \
    "${!MCP_ENV[@]}" \
    "${MCP_ENV[@]}" | sort | sed 's/^/    /' | sed '$ s/,$//')

# Si es método Docker, el command cambia
if [[ "$METHOD" == "3" ]]; then
    MCP_COMMAND='["docker", "run", "--rm", "-i", "ghcr.io/efreet111/engram-dotnet:latest", "engram", "mcp"]'
else
    MCP_COMMAND='["'"$ENGRAM_CMD"'", "mcp"]'
fi

MCP_BLOCK=$(cat << JSON
{
  "command": ["engram", "mcp"],
  "env": {
    "ENGRAM_DATA_DIR": "$DATA_DIR",
    "ENGRAM_USER": "$ENGRAM_USER",
    "ENGRAM_PROFILE": "$PROFILE",
    "ENGRAM_SYNC_ENABLED": "$SYNC_ENABLED"
    $([[ -n "$SERVER_URL" ]] && echo "," && echo "    \"ENGRAM_SERVER_URL\": \"$SERVER_URL\"")
    $([[ -n "$PG_CONNECTION" ]] && echo "," && echo "    \"ENGRAM_PG_CONNECTION\": \"$PG_CONNECTION\"")
  }
}
JSON
)

write_mcp_config() {
    local target="$1"
    local root_key="$2"
    mkdir -p "$(dirname "$target")"

    if [[ -f "$target" ]]; then
        # Merge con config existente
        local tmp=$(mktemp)
        python3 - "$target" "$MCP_BLOCK" "$root_key" << 'PY'
import json, sys
path, block, key = sys.argv[1:]
with open(path) as f: cfg = json.load(f)
cfg.setdefault(key if key else "mcpServers", {})["engram"] = block
with open(path, "w") as f: json.dump(cfg, f, indent=2)
PY
    else
        python3 - "$target" "$MCP_BLOCK" "$root_key" << 'PY'
import json, sys
path, block, key = sys.argv[1:]
root = {key if key else "mcpServers": {"engram": block}}
with open(path, "w") as f: json.dump(root, f, indent=2)
PY
    fi
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
