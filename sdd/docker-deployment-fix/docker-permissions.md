# Bug: Docker mem_sync_md_to_repo Permission Denied

**Severity**: 🟢 Baja  
**Effort**: 5min  
**Reported**: 2026-05-21 (MCP tools test)

---

## Problem

When calling `mem_sync_md_to_repo` or `POST /md/sync` inside the Docker container:

```
Error: Access to the path '/app/docs' is denied.
```

**Cause**: The Docker container runs as non-root user (`engram`) but `/app/docs` doesn't exist or isn't writable.

## Fix

Add to `Dockerfile`:

```dockerfile
# Before the USER engram line:
RUN mkdir -p /app/docs && chown engram:engram /app/docs
```

Or make the docs directory configurable via environment variable:

```dockerfile
ENV ENGRAM_MD_DIR=/app/docs
RUN mkdir -p $ENGRAM_MD_DIR && chown engram:engram $ENGRAM_MD_DIR
```

## Verification

```bash
curl -X POST http://server:7437/md/sync -d '{}'
# → 200 OK
```
