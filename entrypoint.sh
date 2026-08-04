#!/bin/bash
set -e

# ─────────────────────────────────────────────────────────────────────────────
# Entrypoint for engram-dotnet Docker container.
# Runs as root to fix volume permissions, then drops to non-root user 'engram'.
# Pattern: https://github.com/tianon/gosu (used by postgres, redis, etc.)
# ─────────────────────────────────────────────────────────────────────────────

# Fix permissions for data directory (if mounted from host with root ownership)
if [ -d "/data/engram" ]; then
    chown -R engram:engram /data/engram 2>/dev/null || true
fi

# Drop to non-root user and execute the command
exec gosu engram "$@"
