#!/bin/bash
set -eEuo pipefail

# ─────────────────────────────────────────────────────────────────────────────
# Entrypoint for engram-dotnet Docker container.
# Runs as root to fix volume permissions, then drops to non-root user 'engram'.
# Pattern: https://github.com/tianon/gosu (used by postgres, redis, etc.)
# ─────────────────────────────────────────────────────────────────────────────

# Fix permissions for data directory (if mounted from host with root ownership)
# Only chown if necessary to avoid latency on large volumes
if [ -d "/data/engram" ] && [ ! -w "/data/engram" ]; then
    chown -R engram:engram /data/engram 2>&1 || echo "[entrypoint] Warning: chown failed — check volume permissions" >&2
fi

# Drop to non-root user and execute the command
exec gosu engram "$@"
