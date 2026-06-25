#!/usr/bin/env bash
# Registra engram-dotnet en ~/.engram/config.json tras la instalación.
# Invocado por el FlowForge installer después de colocar el binario.
# También puede correrse manualmente. Idempotente.
#
# Uso:
#   post-install.sh                          # auto-detecta engram en PATH
#   post-install.sh --binary /usr/local/bin/engram
#   post-install.sh --binary /path/engram --engram-version 0.3.0
set -euo pipefail

ENGRAM_BINARY=""
ENGRAM_VERSION=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --binary)          ENGRAM_BINARY="$2";  shift 2 ;;
    --engram-version)  ENGRAM_VERSION="$2"; shift 2 ;;
    *) echo "Argumento desconocido: $1" >&2; exit 1 ;;
  esac
done

# ── Buscar binario ───────────────────────────────────────────────────────────

if [[ -z "$ENGRAM_BINARY" ]]; then
  if command -v engram >/dev/null 2>&1; then
    ENGRAM_BINARY="$(command -v engram)"
  else
    echo "ERROR: engram no encontrado en PATH. Pasá --binary /ruta/al/binario." >&2
    exit 1
  fi
fi

if [[ ! -x "$ENGRAM_BINARY" ]]; then
  echo "ERROR: '$ENGRAM_BINARY' no existe o no es ejecutable." >&2
  exit 1
fi

# ── Obtener versión ──────────────────────────────────────────────────────────

if [[ -z "$ENGRAM_VERSION" ]]; then
  raw="$("$ENGRAM_BINARY" version 2>/dev/null || true)"
  # "engram 0.3.0" → "0.3.0"
  ENGRAM_VERSION="${raw#engram }"
  ENGRAM_VERSION="${ENGRAM_VERSION//[[:space:]]/}"
fi

if [[ -z "$ENGRAM_VERSION" ]]; then
  echo "ERROR: no se pudo obtener la versión del binario." >&2
  exit 1
fi

# ── Directorios y archivos ───────────────────────────────────────────────────

ENGRAM_DIR="${HOME}/.engram"
CONFIG_FILE="${ENGRAM_DIR}/config.json"
LOG_FILE="${ENGRAM_DIR}/install.log"
mkdir -p "$ENGRAM_DIR"

log() {
  local level="$1"; shift
  local ts
  ts="$(date '+%Y-%m-%d %H:%M:%S')"
  echo "[$ts] [$level] $*" >> "$LOG_FILE"
}

log "INFO" "post-install.sh: engram ${ENGRAM_VERSION} en ${ENGRAM_BINARY}"

# ── Actualizar config.json (idempotente) ─────────────────────────────────────

python3 - "$CONFIG_FILE" "$ENGRAM_VERSION" "$ENGRAM_BINARY" <<'PY'
import json, sys, os
from datetime import datetime, timezone

config_path, version, binary = sys.argv[1], sys.argv[2], sys.argv[3]

cfg = {}
if os.path.isfile(config_path):
    try:
        with open(config_path, encoding="utf-8") as f:
            cfg = json.load(f)
    except (json.JSONDecodeError, OSError):
        cfg = {}

cfg.setdefault("channel", "stable")
cfg.setdefault("auto_update", False)
cfg.setdefault("flowdoc", {"enabled": True})
cfg.setdefault("components", {})

cfg["components"]["engram_dotnet"] = {
    "installed": True,
    "version": version,
    "binary": binary,
    "registered_at": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
}

with open(config_path, "w", encoding="utf-8") as f:
    json.dump(cfg, f, indent=2)
    f.write("\n")

print(f"  config.json actualizado: engram_dotnet {version}")
PY

log "INFO" "post-install.sh: config.json actualizado correctamente"
echo "engram-dotnet ${ENGRAM_VERSION} registrado en ${CONFIG_FILE}"
