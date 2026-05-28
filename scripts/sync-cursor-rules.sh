#!/usr/bin/env bash
# Sync project Cursor rules: config/cursor/rules -> .cursor/rules
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
SRC="$ROOT/config/cursor/rules"
DST="$ROOT/.cursor/rules"

mkdir -p "$DST"
cp -f "$SRC"/*.mdc "$DST/"
echo "Synced rules to $DST"
ls -1 "$DST"/*.mdc 2>/dev/null | xargs -I{} basename {}
