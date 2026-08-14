#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# engram-dotnet — Install Script
#
# Instalación simple y directa. Complemento de uninstall.sh.
#
# USO:
#   ./install.sh          # interactivo
#   ./install.sh -y       # sin confirmación (valores por defecto)
# ─────────────────────────────────────────────────────────────────────────────

set -euo pipefail

FORCE=false
PROFILE="local"

# ── Colores ──────────────────────────────────────────────────────────────────
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m'

info()    { echo -e "${GREEN}[INFO]${NC} $1"; }
warn()    { echo -e "${YELLOW}[WARN]${NC} $1"; }
error()   { echo -e "${RED}[ERROR]${NC} $1" >&2; }

# ── Parse args ────────────────────────────────────────────────────────────────
for arg in "$@"; do
    case $arg in
        -y|--yes|--force) FORCE=true ;;
        --profile=*) PROFILE="${arg#*=}" ;;
        -h|--help)
            echo "Uso: $0 [-y|--yes] [--profile=local|offline-first|remote-server|desktop]"
            echo ""
            echo "Perfiles:"
            echo "  local          SQLite local, sin sync (default)"
            echo "  offline-first  SQLite local + sync a servidor"
            echo "  remote-server  PostgreSQL compartido, sin sync local"
            echo "  desktop        PostgreSQL local, sin sync"
            exit 0
            ;;
    esac
done

# ── Detectar SO ─────────────────────────────────────────────────────────────
OS="$(uname -s)"
ARCH="$(uname -m)"
case "$OS" in
    Linux*)     OS_NAME="linux" ;;
    Darwin*)    OS_NAME="macos" ;;
    MINGW*|MSYS*|CYGWIN*) OS_NAME="windows" ;;
    *)          error "SO no soportado: $OS" && exit 1 ;;
esac

case "$ARCH" in
    x86_64|amd64)  ARCH_NAME="x64" ;;
    aarch64|arm64)  ARCH_NAME="arm64" ;;
    *)               error "Arquitectura no soportada: $ARCH" && exit 1 ;;
esac

# ── Rutas ────────────────────────────────────────────────────────────────────
INSTALL_DIR="${HOME}/.local"
ENGRAM_BIN="${INSTALL_DIR}/bin/engram"
ENGRAM_DATA="${HOME}/.engram"
DEST_BIN="/usr/local/bin/engram"

echo ""
echo "═══════════════════════════════════════════════════════════"
echo "  engram-dotnet — INSTALL"
echo "═══════════════════════════════════════════════════════════"
echo ""
info "Sistema: $OS_NAME ($ARCH_NAME)"
info "Directorio de instalación: $INSTALL_DIR"
info "Directorio de datos: $ENGRAM_DATA"
info "Perfil: $PROFILE"
echo ""

# ── Detener versión anterior ─────────────────────────────────────────────────
if command -v engram >/dev/null 2>&1; then
    CURRENT="$(command -v engram)"
    warn "engram ya instalado en: $CURRENT"
    if confirm "¿Reemplazar la instalación existente?"; then
        info "Removiendo versión anterior..."
        "$CURRENT" mcp stop 2>/dev/null || true
        rm -f "$CURRENT"
    else
        info "Instalación cancelada."
        exit 0
    fi
fi

# ── Crear directorios ────────────────────────────────────────────────────────
mkdir -p "${INSTALL_DIR}/bin"
mkdir -p "${ENGRAM_DATA}"

# ── Opción 1: Descargar binario release ─────────────────────────────────────
info "¿Cómo querés instalar?"

OPTIONS=(
    "Release pre-built (recomendado — descarga binario)"
    "Build from source (requiere .NET 10 SDK)"
)

select opt in "${OPTIONS[@]}"; do
    case $REPLY in
        1) METHOD="release"; break ;;
        2) METHOD="source"; break ;;
    esac
done

# ── Instalar según método ────────────────────────────────────────────────────
if [[ "$METHOD" == "release" ]]; then
    LATEST_VERSION="v1.3.0"
    DOWNLOAD_URL="https://github.com/efreet111/engram-dotnet/releases/download/${LATEST_VERSION}/engram-${OS_NAME}-${ARCH_NAME}"

    info "Descargando ${LATEST_VERSION}..."
    if command -v curl >/dev/null 2>&1; then
        curl -L --fail -o "${ENGRAM_BIN}" "$DOWNLOAD_URL" || {
            error "Descarga falló. Probá build from source."
            exit 1
        }
    elif command -v wget >/dev/null 2>&1; then
        wget -O "${ENGRAM_BIN}" "$DOWNLOAD_URL" || {
            error "Descarga falló. Probá build from source."
            exit 1
        }
    else
        error "Necesitás curl o wget para descargar."
        exit 1
    fi

elif [[ "$METHOD" == "source" ]]; then
    if ! command -v dotnet >/dev/null 2>&1; then
        error ".NET 10 SDK no encontrado. Instalalo desde https://dotnet.microsoft.com/download/dotnet/10.0"
        exit 1
    fi

    info "Compilando desde source (puede tardar varios minutos)..."
    BUILD_DIR=$(mktemp -d)
    git clone --depth 1 https://github.com/efreet111/engram-dotnet.git "$BUILD_DIR" 2>/dev/null || {
        warn "git clone falló, usando el repo actual si existe..."
        if [[ -d "$(dirname "$0")/.." ]]; then
            BUILD_DIR="$(cd "$(dirname "$0")/.." && pwd)"
        else
            error "No se encontró el repo. Clonealo o instala el release."
            exit 1
        fi
    }

    dotnet publish "${BUILD_DIR}/src/Engram.Cli/Engram.Cli.csproj" \
        -c Release \
        -r "${OS_NAME}-${ARCH_NAME}" \
        --self-contained false \
        -o "${INSTALL_DIR}/tmp-publish" || {
        rm -rf "${INSTALL_DIR}/tmp-publish"
        error "Build falló."
        exit 1
    }
    mv "${INSTALL_DIR}/tmp-publish/engram" "${ENGRAM_BIN}"
    rm -rf "${INSTALL_DIR}/tmp-publish"
    [[ "$BUILD_DIR" != "$(pwd)" ]] && rm -rf "$BUILD_DIR"
fi

chmod +x "${ENGRAM_BIN}"

# ── Instalar en PATH (symlink o copy) ───────────────────────────────────────
if [[ -w "/usr/local/bin" ]]; then
    ln -sf "${ENGRAM_BIN}" "${DEST_BIN}"
    info "Instalado en: ${DEST_BIN}"
else
    warn "No tenés permisos de escritura en /usr/local/bin."
    warn "Agregá esto a tu ~/.bashrc o ~/.zshrc:"
    echo ""
    echo -e "  ${CYAN}export PATH=\"${INSTALL_DIR}/bin:\$PATH\"${NC}"
    echo ""
fi

# Agregar al PATH del shell actual si no está
if [[ ":$PATH:" != *":${INSTALL_DIR}/bin:"* ]]; then
    export PATH="${INSTALL_DIR}/bin:$PATH"
fi

# ── Configuración inicial ────────────────────────────────────────────────────
echo ""
echo "── Configuración ─────────────────────────────────────────"

read -r -p "Tu nombre de usuario (ENGRAM_USER) [$(whoami)]: " ENGRAM_USER
ENGRAM_USER="${ENGRAM_USER:-$(whoami)}"

if [[ "$PROFILE" == "offline-first" ]]; then
    read -r -p "URL del servidor de sync [http://localhost:7437]: " SERVER_URL
    SERVER_URL="${SERVER_URL:-http://localhost:7437}"
fi

# ── Verificar instalación ────────────────────────────────────────────────────
echo ""
echo "── Verificación ─────────────────────────────────────────"

if ! "${ENGRAM_BIN}" --version &>/dev/null; then
    error "engram no funciona. Probá:"
    echo "  ${ENGRAM_BIN} --version"
    exit 1
fi

info "Versión: $("${ENGRAM_BIN}" --version)"

# ── Inicializar ─────────────────────────────────────────────────────────────
echo ""
echo "── Inicialización ──────────────────────────────────────"

export ENGRAM_DATA_DIR="${ENGRAM_DATA}"
export ENGRAM_USER="${ENGRAM_USER}"

if [[ "$PROFILE" == "local" ]]; then
    export ENGRAM_PROFILE=local
elif [[ "$PROFILE" == "offline-first" ]]; then
    export ENGRAM_PROFILE=offline-first
    export ENGRAM_SERVER_URL="${SERVER_URL}"
    export ENGRAM_SYNC_ENABLED=true
fi

# Crear DB inicial si no existe
"${ENGRAM_BIN}" doctor &>/dev/null || true

info "Instalación completa."
echo ""

# ── Agregar al shell rc si es necesario ────────────────────────────────────
RC_MARKER="# engram-dotnet"
BASHRC="${HOME}/.bashrc"
ZSHRC="${HOME}/.zshrc"

for rc in "$BASHRC" "$ZSHRC"; do
    if [[ -f "$rc" ]] && ! grep -q "$RC_MARKER" "$rc" 2>/dev/null; then
        cat >> "$rc" << EOF

${RC_MARKER}
export PATH="${INSTALL_DIR}/bin:\$PATH"
export ENGRAM_DATA_DIR="${ENGRAM_DATA}"
export ENGRAM_USER="${ENGRAM_USER}"
EOF
        info "Agregado PATH a $rc"
    fi
done

# ── Resumen ────────────────────────────────────────────────────────────────
echo ""
echo "═══════════════════════════════════════════════════════════"
echo "  ${GREEN}Instalación completa${NC}"
echo "═══════════════════════════════════════════════════════════"
echo ""
echo "  Datos:      ${ENGRAM_DATA}"
echo "  Binario:    ${ENGRAM_BIN}"
echo "  Perfil:     ${PROFILE}"
echo ""
echo "  Comandos útiles:"
echo "    engram --version    # verificar versión"
echo "    engram serve       # iniciar servidor"
echo "    engram doctor      # diagnóstico"
echo "    engram mcp         # iniciar MCP (para IDE)"
echo ""
echo "  Para desinstalar:"
echo "    ./scripts/uninstall.sh"
echo ""
echo "  Documentación: docs/INSTALL.md"
echo ""
