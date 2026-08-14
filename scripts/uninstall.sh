#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# engram-dotnet — Uninstall Script
#
# Removes ALL engram-dotnet artifacts from the system.
# Leaves NO traces (100% clean).
#
# USO:
#   ./uninstall.sh          # interactivo
#   ./uninstall.sh -y       # sin confirmación
#   ./uninstall.sh --dry-run # solo mostrar qué se borraría
# ─────────────────────────────────────────────────────────────────────────────

set -euo pipefail

DRY_RUN=false
FORCE=false

# ── Colores ──────────────────────────────────────────────────────────────────
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m'

info()    { echo -e "${GREEN}[INFO]${NC} $1"; }
warn()    { echo -e "${YELLOW}[WARN]${NC} $1"; }
error()   { echo -e "${RED}[ERROR]${NC} $1" >&2; }
dry()     { [[ "$DRY_RUN" == true ]] && echo -e "${CYAN}[DRY]${NC} $1" || "$@"; }
confirm() {
    local msg="$1"
    if [[ "$FORCE" == true ]]; then return 0; fi
    echo -ne "${YELLOW}[?]${NC} $msg [y/N]: "
    read -r resp
    [[ "$resp" =~ ^[Yy]$ ]]
}

# ── Parse args ────────────────────────────────────────────────────────────────
for arg in "$@"; do
    case $arg in
        -y|--yes|--force) FORCE=true ;;
        --dry-run) DRY_RUN=true ;;
        -h|--help)
            echo "Uso: $0 [-y|--yes] [--dry-run]"
            echo ""
            echo "  -y, --yes      No preguntar confirmación"
            echo "  --dry-run      Solo mostrar qué se borraría"
            exit 0
            ;;
    esac
done

echo ""
echo "═══════════════════════════════════════════════════════════"
echo "  engram-dotnet — UNINSTALL"
echo "═══════════════════════════════════════════════════════════"
echo ""

if [[ "$DRY_RUN" == true ]]; then
    warn "MODO DRY-RUN — no se borrará nada"
    echo ""
fi

# ── 1. Detener servicios ────────────────────────────────────────────────────
echo "── 1. Servicios activos ──────────────────────────────────"

# systemd
if command -v systemctl >/dev/null 2>&1; then
    SERVICES=(
        "engram.service"
        "engram-server.service"
    )
    for svc in "${SERVICES[@]}"; do
        if systemctl is-active --quiet "$svc" 2>/dev/null; then
            echo -n "  Stopping $svc... "
            dry systemctl stop "$svc" 2>/dev/null && echo "done" || echo "not found"
        fi
        if systemctl is-enabled --quiet "$svc" 2>/dev/null; then
            echo -n "  Disabling $svc... "
            dry systemctl disable "$svc" 2>/dev/null && echo "done" || echo "not found"
        fi
    done
fi

# Docker containers
if command -v docker >/dev/null 2>&1; then
    CONTAINERS=(engram engram-server engram-postgres engram-postgres-test engram-opencode)
    for c in "${CONTAINERS[@]}"; do
        if docker ps -a --format '{{.Names}}' | grep -q "^${c}$"; then
            echo -n "  Removing Docker container $c... "
            dry docker rm -f "$c" >/dev/null 2>&1 && echo "done" || echo "not found"
        fi
    done
    if confirm "¿Eliminar las imágenes Docker de engram (engram-test, opencode-test)?"; then
        echo -n "  Removing Docker images... "
        dry docker rmi -f \
            "$(docker images -q 'engram-test:latest' 2>/dev/null)" \
            "$(docker images -q 'opencode-test:latest' 2>/dev/null)" \
            "$(docker images -q 'ghcr.io/efreet111/engram-dotnet*' 2>/dev/null)" \
            2>/dev/null && echo "done" || echo "none found"
    fi
fi

# ── 2. Binaries ──────────────────────────────────────────────────────────────
echo ""
echo "── 2. Binarios ───────────────────────────────────────────"

BINARIES=(
    "${HOME}/.local/bin/engram"
    "${HOME}/.local/bin/engram-mcp"
    "${HOME}/dist/engram"
    "/usr/local/bin/engram"
    "/usr/local/bin/engram-mcp"
)

# Also check common dist/ locations in repos
for d in ~/dev/ ~/projects/ ~/code/; do
    if [[ -d "$d" ]]; then
        find "$d" -maxdepth 3 -name "engram" -type f -executable 2>/dev/null | while read -r f; do
            echo "  Found: $f"
            BINARIES+=("$f")
        done
    fi
done

for bin in "${BINARIES[@]}"; do
    if [[ -f "$bin" ]]; then
        echo -n "  Removing $bin... "
        dry rm -f "$bin" && echo "done"
    fi
done

# dist/ directories
DIST_DIRS=(
    "${HOME}/dist"
    "${HOME}/.local/share/engram"
    "/usr/local/share/engram"
)
for d in "${DIST_DIRS[@]}"; do
    if [[ -d "$d" ]]; then
        echo -n "  Removing directory $d... "
        dry rm -rf "$d" && echo "done"
    fi
done

# ── 3. Data directory ───────────────────────────────────────────────────────
echo ""
echo "── 3. Datos ─────────────────────────────────────────────"

DATA_DIRS=(
    "${HOME}/.engram"
    "${HOME}/.engram.d"
    "${HOME}/Library/Application Support/Engram"  # macOS
    "${APPDATA:-}/Engram"  # Windows
)

for d in "${DATA_DIRS[@]}"; do
    if [[ -d "$d" ]]; then
        if confirm "¿Eliminar datos en $d?"; then
            echo -n "  Removing $d... "
            dry rm -rf "$d" && echo "done"
        fi
    fi
done

# ── 4. MCP configs ──────────────────────────────────────────────────────────
echo ""
echo "── 4. Configuración MCP ────────────────────────────────"

MCP_FILES=(
    "${HOME}/.cursor/mcp.json"
    "${HOME}/.config/opencode/opencode.json"
    "${HOME}/.config/Claude/claude_desktop_config.json"
    "${HOME}/.vscode/mcp.json"
    "${HOME}/.config/Code/User/globalStorage/storage.json"  # VS Code MCP
    "${HOME}/Library/Application Support/Code/User/globalStorage/storage.json"  # VS Code macOS
)

for f in "${MCP_FILES[@]}"; do
    if [[ -f "$f" ]]; then
        echo -n "  Removing $f... "
        dry rm -f "$f" && echo "done"
    fi
done

# Generated configs from setup.sh
GENERATED_DIRS=(
    "${HOME}/.engram/mcp.config.json"
)
if [[ -f "${HOME}/.engram/mcp.config.json" ]]; then
    echo -n "  Removing ~/.engram/mcp.config.json... "
    dry rm -f "${HOME}/.engram/mcp.config.json" && echo "done"
fi

# ── 5. Shell completions ────────────────────────────────────────────────────
echo ""
echo "── 5. Shell completions ────────────────────────────────"

COMPLETION_FILES=(
    "/etc/bash_completion.d/engram"
    "/etc/bash_completion.d/engram-mcp"
    "/usr/local/share/bash-completion/completions/engram"
    "/usr/local/share/zsh/site-functions/_engram"
    "${HOME}/.bash_completion.d/engram"
    "${HOME}/.bash_completion.d/engram-mcp"
    "${HOME}/.local/share/zsh/site-functions/_engram"
)

for f in "${COMPLETION_FILES[@]}"; do
    if [[ -f "$f" ]]; then
        echo -n "  Removing $f... "
        dry rm -f "$f" && echo "done"
    fi
done

# ── 6. PATH modifications ───────────────────────────────────────────────────
echo ""
echo "── 6. PATH modifications ─────────────────────────────────"

RC_FILES=(
    "${HOME}/.bashrc"
    "${HOME}/.bash_profile"
    "${HOME}/.zshrc"
    "${HOME}/.zprofile"
    "${HOME}/.profile"
)

for rc in "${RC_FILES[@]}"; do
    if [[ -f "$rc" ]]; then
        # Patterns to remove
        PATTERNS=(
            'export PATH=.*engram.*:$PATH'
            'export PATH=.*dist.*:$PATH'
            'export ENGRAM_.*=.*'
            'alias engram=.*'
            'source.*engram.*completion'
        )
        for pat in "${PATTERNS[@]}"; do
            if grep -qE "$pat" "$rc" 2>/dev/null; then
                echo "  Cleaning $rc of engram entries..."
                dry sed -i.bak -E "/$pat/d" "$rc"
            fi
        done
    fi
done

# ── 7. Systemd service ─────────────────────────────────────────────────────
echo ""
echo "── 7. Systemd service ───────────────────────────────────"

SYSTEMD_FILES=(
    "/etc/systemd/system/engram.service"
    "/etc/systemd/system/engram-server.service"
    "/etc/systemd/system/engram-mcp.service"
)

for f in "${SYSTEMD_FILES[@]}"; do
    if [[ -f "$f" ]]; then
        echo -n "  Removing $f... "
        dry rm -f "$f" && echo "done"
    fi
done

if command -v systemctl >/dev/null 2>&1; then
    dry systemctl daemon-reload 2>/dev/null || true
fi

# ── 8. Installed packages (brew, etc) ─────────────────────────────────────
echo ""
echo "── 8. Package managers ───────────────────────────────────"

# Homebrew (macOS/Linux)
if command -v brew >/dev/null 2>&1; then
    if brew list engram &>/dev/null; then
        if confirm "Uninstall engram via Homebrew?"; then
            echo -n "  brew uninstall engram... "
            dry brew uninstall engram && echo "done"
        fi
    fi
fi

# pip (if installed as Python package)
if command -v pip3 >/dev/null 2>&1; then
    if pip3 show engram &>/dev/null; then
        if confirm "Uninstall engram Python package?"; then
            echo -n "  pip3 uninstall engram... "
            dry pip3 uninstall -y engram && echo "done"
        fi
    fi
fi

# ── 9. Repo clone (if present) ─────────────────────────────────────────────
echo ""
echo "── 9. Repository clones ─────────────────────────────────"

REPO_DIRS=(
    "${HOME}/dev/engram-dotnet"
    "${HOME}/projects/engram-dotnet"
    "${HOME}/code/engram-dotnet"
    "${HOME}/engram-dotnet"
)

for d in "${REPO_DIRS[@]}"; do
    if [[ -d "$d/.git" ]]; then
        if confirm "¿Eliminar repositorio clonado en $d?"; then
            echo -n "  Removing $d... "
            dry rm -rf "$d" && echo "done"
        fi
    fi
done

# ── 10. Docker volumes ──────────────────────────────────────────────────────
echo ""
echo "── 10. Docker volumes ───────────────────────────────────"

if command -v docker >/dev/null 2>&1; then
    VOLUMES=(pgdata-test engram-data engram-postgres-data)
    for v in "${VOLUMES[@]}"; do
        if docker volume ls --format '{{.Name}}' | grep -q "^${v}$"; then
            if confirm "¿Eliminar volumen Docker $v?"; then
                echo -n "  docker volume rm $v... "
                dry docker volume rm "$v" 2>/dev/null && echo "done" || echo "in use, skipping"
            fi
        fi
    done
fi

# ── 11. Log files ──────────────────────────────────────────────────────────
echo ""
echo "── 11. Log files ───────────────────────────────────────"

LOG_FILES=(
    "/var/log/engram*.log"
    "/tmp/engram*.log"
    "${HOME}/.engram/engram.log"
)

for f in "${LOG_FILES[@]}"; do
    if ls $f &>/dev/null 2>&1; then
        echo -n "  Removing $f... "
        dry rm -f $f && echo "done"
    fi
done

# ── Summary ────────────────────────────────────────────────────────────────
echo ""
echo "═══════════════════════════════════════════════════════════"
echo "  ${GREEN}Uninstall completo${NC}"
echo "═══════════════════════════════════════════════════════════"
echo ""
echo "  Lo que se removió (ver arriba para detalles):"
echo "    • Binarios y directorios dist/"
echo "    • Datos (~/.engram/)"
echo "    • Configuraciones MCP"
echo "    • Shell completions"
echo "    • Modificaciones a PATH (~/.bashrc, etc.)"
echo "    • Servicios systemd"
echo "    • Imágenes Docker (si se confirmó)"
echo "    • Volúmenes Docker (si se confirmó)"
echo "    • Paquetes (brew, pip)"
echo ""
echo "  Para verificar que no queda nada:"
echo "    which engram        # → no debería encontrar nada"
echo "    ls ~/.engram        # → no debería existir"
echo "    cat ~/.config/opencode/opencode.json  # → no debería existir"
echo ""
