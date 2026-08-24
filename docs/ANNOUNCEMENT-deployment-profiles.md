# 🚀 Deployment Profile System — Now Available

**Date:** 2026-08-12  
**Merged to:** `main` (commit `20b5e53`)  
**Author:** Crhistian Mendoza  
**Reviewed by:** Victor

---

## What's New

We've merged a **complete deployment profile system** that simplifies engram setup from "configure 10+ environment variables" to "pick a profile and go".

### 4 Pre-configured Profiles

| Profile | Backend | Sync | Use Case |
|---------|---------|------|----------|
| **`local`** | SQLite | ❌ | Solo developer, local testing |
| **`remote-server`** | PostgreSQL | ❌ | Small team (2-5), shared DB on server |
| **`offline-first`** | SQLite + PostgreSQL | ✅ | Large team, offline-first workflow |
| **`desktop`** | PostgreSQL | ✅ | Desktop app with full stack |

### Quick Start

```bash
# Before (complex):
ENGRAM_DB_TYPE=postgres \
ENGRAM_PG_HOST=192.168.1.100 \
ENGRAM_PG_PORT=5432 \
ENGRAM_PG_DATABASE=engram \
ENGRAM_PG_USER=engram \
ENGRAM_PG_PASSWORD=secret \
ENGRAM_USER=victor \
./engram serve

# After (simple):
ENGRAM_PROFILE=remote-server \
ENGRAM_PG_CONNECTION="Host=192.168.1.100;Database=engram;Username=engram;Password=secret" \
ENGRAM_USER=victor \
./engram serve
```

Profiles define sensible defaults; you can override individual variables as needed.

---

## What's Included

### Code Changes
- ✅ `DeployProfile.cs` — Profile detection and configuration logic
- ✅ `StoreConfig.cs` — Profile-aware configuration merging
- ✅ `SyncManagerConfig.cs` — Profile-aware sync configuration
- ✅ 631 lines of new tests (all passing)

### Documentation
- ✅ `DEPLOYMENT.md` (599 lines) — Complete deployment guide
- ✅ `HU-010` — Deploy profile system spec
- ✅ `HU-011` — Docs and script QA
- ✅ `HU-012` — Deploy profiles rename
- ✅ `ADR-011` — Engram URL env var
- ✅ `ADR-012` — Remote server localhost blocking

### Scripts
- ✅ `deploy.sh` (670 lines) — Automated deployment script
- ✅ `run-tests.sh` (35 lines) — Test runner script

### Docker
- ✅ `docker-compose.embedded.yml` — PostgreSQL as embedded service
- ✅ Updated `.env.example` with profile documentation
- ✅ Updated `DOCKER-VANILLA.md` with profile examples

---

## How to Use

### 1. Solo Development (local)

```bash
ENGRAM_PROFILE=local ./engram serve
```

**What you get:**
- SQLite database (local file)
- No sync
- Zero configuration

### 2. Small Team (remote-server)

```bash
ENGRAM_PROFILE=remote-server \
ENGRAM_PG_CONNECTION="Host=server.example.com;Database=engram;Username=engram;Password=secret" \
./engram serve
```

**What you get:**
- PostgreSQL backend (shared DB)
- No sync (direct DB access)
- Multi-user isolation via `ENGRAM_USER`

### 3. Large Team (offline-first)

```bash
ENGRAM_PROFILE=offline-first \
ENGRAM_SERVER_URL="http://server.example.com:7437" \
ENGRAM_USER=victor \
./engram serve
```

**What you get:**
- SQLite local database
- Sync to remote PostgreSQL server
- Offline work capability
- Project enrollment

### 4. Desktop App (desktop)

```bash
ENGRAM_PROFILE=desktop \
ENGRAM_PG_CONNECTION="Host=localhost;Database=engram;Username=engram;Password=secret" \
ENGRAM_SERVER_URL="http://server.example.com:7437" \
ENGRAM_USER=victor \
./engram serve
```

**What you get:**
- PostgreSQL backend (local or remote)
- Sync to remote server
- Full stack for desktop applications

---

## Migration Guide

### If you're already using engram

**No changes required!** The profile system is backward-compatible. Your existing environment variables continue to work.

**Optional:** Simplify your setup by switching to profiles:

1. Identify your current setup:
   - SQLite only? → `ENGRAM_PROFILE=local`
   - PostgreSQL without sync? → `ENGRAM_PROFILE=remote-server`
   - SQLite with sync? → `ENGRAM_PROFILE=offline-first`
   - PostgreSQL with sync? → `ENGRAM_PROFILE=desktop`

2. Update your startup script/docker-compose:
   ```bash
   # Old way (still works):
   ENGRAM_DB_TYPE=postgres ENGRAM_PG_HOST=... ./engram serve
   
   # New way (simpler):
   ENGRAM_PROFILE=remote-server ENGRAM_PG_CONNECTION="..." ./engram serve
   ```

---

## Testing

All tests pass:
- ✅ 731 T2 tests (SQLite)
- ✅ 9 tests skipped (expected)
- ✅ 0 failures

---

## Documentation Links

- **[DEPLOYMENT.md](docs/DEPLOYMENT.md)** — Complete deployment guide with examples
- **[HU-010](docs/tasks/HU-001-HU-099/HU-010-deploy-profile-system.md)** — Feature spec
- **[ADR-011](docs/architecture/adr/ADR-011-engram-url-env-var.md)** — Architecture decision
- **[ADR-012](docs/architecture/adr/ADR-012-remote-server-localhost-blocking.md)** — Security gate decision

---

## Questions?

If you have questions about the deployment profile system:
1. Read [DEPLOYMENT.md](docs/DEPLOYMENT.md) for detailed examples
2. Check the [HU-010 spec](docs/tasks/HU-001-HU-099/HU-010-deploy-profile-system.md) for technical details
3. Open an issue on GitHub if you find bugs or need help

---

**Thanks to Crhistian Mendoza for this excellent contribution!** 🎉
