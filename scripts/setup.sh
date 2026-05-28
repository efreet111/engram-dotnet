#!/usr/bin/env bash
# Wizard de configuración MCP (Linux/macOS) — agnóstico de editor.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$REPO_ROOT"

if [[ ! -f src/Engram.Cli/Engram.Cli.csproj ]]; then
  echo "Ejecutá este script desde el repo engram-dotnet (scripts/setup.sh)." >&2
  exit 1
fi

if ! command -v python3 >/dev/null 2>&1; then
  echo "Se requiere python3 para escribir mcp.json. Instalalo o usá scripts/setup.ps1 en Windows." >&2
  exit 1
fi

echo ""
echo "=== engram-dotnet — configuración MCP ==="
echo ""

echo "Modo de uso:"
echo "  [1] Solo local (SQLite, sin sync)"
echo "  [2] Offline-first sync (SQLite + servidor)"
read -r -p "Elegí 1 o 2 (default 1): " mode
mode="${mode:-1}"

SYNC_ENABLED="false"
SERVER_URL=""

if [[ "$mode" == "2" ]]; then
  SYNC_ENABLED="true"
  read -r -p "ENGRAM_SERVER_URL [http://192.168.0.178:7437]: " SERVER_URL
  SERVER_URL="${SERVER_URL:-http://192.168.0.178:7437}"
  SERVER_URL="${SERVER_URL%/}"
  if command -v curl >/dev/null 2>&1; then
    curl -sf "$SERVER_URL/health" >/dev/null && echo "  Servidor OK" || echo "  Advertencia: health check falló"
  fi
fi

read -r -p "ENGRAM_USER [$(whoami)@local.dev]: " ENGRAM_USER
ENGRAM_USER="${ENGRAM_USER:-$(whoami)@local.dev}"

DATA_DEFAULT="${HOME}/.engram"
read -r -p "ENGRAM_DATA_DIR [$DATA_DEFAULT]: " ENGRAM_DATA_DIR
ENGRAM_DATA_DIR="${ENGRAM_DATA_DIR:-$DATA_DEFAULT}"
mkdir -p "$ENGRAM_DATA_DIR"

read -r -p "¿Compilar engram ahora? (S/n): " build
if [[ -z "$build" || "$build" =~ ^[sSyY]$ ]]; then
  RID="linux-x64"
  [[ "$(uname -s)" == "Darwin" ]] && RID="osx-x64"
  echo "  Compilando ($RID)..."
  dotnet publish src/Engram.Cli/Engram.Cli.csproj -c Release -r "$RID" --self-contained false -o "dist/$RID" --nologo
fi

ENGRAM_CMD="engram"
for c in "$REPO_ROOT/dist/linux-x64/engram" "$REPO_ROOT/dist/osx-x64/engram"; do
  if [[ -x "$c" ]]; then ENGRAM_CMD="$c"; break; fi
done
if command -v engram >/dev/null 2>&1; then ENGRAM_CMD="$(command -v engram)"; fi
echo "  Comando MCP: $ENGRAM_CMD"

GEN_DIR="$REPO_ROOT/config/mcp/generated"
mkdir -p "$GEN_DIR"

python3 - "$GEN_DIR" "$ENGRAM_CMD" "$ENGRAM_DATA_DIR" "$ENGRAM_USER" "$SYNC_ENABLED" "$SERVER_URL" "$HOME" <<'PY'
import json, os, sys
from datetime import datetime

gen_dir, cmd, data_dir, user, sync, url, home = sys.argv[1:8]
env = {
    "ENGRAM_DATA_DIR": data_dir,
    "ENGRAM_USER": user,
    "ENGRAM_SYNC_ENABLED": sync,
}
if sync == "true":
    env["ENGRAM_SERVER_URL"] = url

block = {"type": "stdio", "command": cmd, "args": ["mcp"], "env": env}
mode = "offline-first sync" if sync == "true" else "solo local"

def write(name, obj):
    path = os.path.join(gen_dir, name)
    with open(path, "w", encoding="utf-8") as f:
        json.dump(obj, f, indent=2)
        f.write("\n")

write("cursor.mcp.json", {"mcpServers": {"engram": block}})
write("claude-desktop.mcp.json", {"mcpServers": {"engram": {k: v for k, v in block.items() if k != "type"}}})
write("vscode.mcp.json", {"servers": {"engram": block}})
write("opencode.mcp.json", {
    "mcp": {
        "engram": {
            "type": "stdio",
            "command": [cmd, "mcp"],
            "environment": env,
        }
    }
})
write("engram.server.json", block)

readme = f"""# Configuraciones MCP generadas — engram-dotnet

Modo: **{mode}**
Generado: {datetime.now().strftime("%Y-%m-%d %H:%M")}

| Archivo | Copiar a |
|---------|----------|
| `cursor.mcp.json` | `{os.path.join(home, ".cursor", "mcp.json")}` |
| `claude-desktop.mcp.json` | Claude Desktop config (`mcpServers`) |
| `vscode.mcp.json` | VS Code MCP extension (`servers`) |
| `opencode.mcp.json` | `~/.config/opencode/opencode.json` |
| `engram.server.json` | Bloque interno si el editor pide solo el servidor |

Guía: `config/mcp/INSTALL.md`
"""
with open(os.path.join(gen_dir, "README.md"), "w", encoding="utf-8") as f:
    f.write(readme)
PY

echo "  Generados en: $GEN_DIR"

write_mcp() {
  local target="$1"
  python3 - "$target" "$ENGRAM_CMD" "$ENGRAM_DATA_DIR" "$ENGRAM_USER" "$SYNC_ENABLED" "$SERVER_URL" <<'PY'
import json, os, sys
target, cmd, data_dir, user, sync, url = sys.argv[1:7]
env = {"ENGRAM_DATA_DIR": data_dir, "ENGRAM_USER": user, "ENGRAM_SYNC_ENABLED": sync}
if sync == "true":
    env["ENGRAM_SERVER_URL"] = url
block = {"type": "stdio", "command": cmd, "args": ["mcp"], "env": env}
cfg = {"mcpServers": {}}
if os.path.isfile(target):
    with open(target, encoding="utf-8") as f:
        cfg = json.load(f)
cfg.setdefault("mcpServers", {})["engram"] = block
os.makedirs(os.path.dirname(target) or ".", exist_ok=True)
with open(target, "w", encoding="utf-8") as f:
    json.dump(cfg, f, indent=2)
    f.write("\n")
PY
}

echo ""
echo "¿Instalar también en un editor ahora?"
echo "  [1] No — solo config/mcp/generated/"
echo "  [2] Sí — Cursor (~/.cursor/mcp.json)"
read -r -p "Elegí 1-2 (default 1): " editor
editor="${editor:-1}"
[[ "$editor" == "2" ]] && write_mcp "${HOME}/.cursor/mcp.json"

write_mcp "$ENGRAM_DATA_DIR/mcp.config.json"

echo ""
echo "Listo. Recargá tu editor."
[[ "$SYNC_ENABLED" == "true" ]] && echo "Sync: ver docs/SYNC-SETUP.md (enroll de proyecto)."
