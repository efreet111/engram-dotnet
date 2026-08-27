# HU-017 — Refactor Install Profile System

**Status**: Post-development (AC1–AC6 implementados — ver `scripts/install.sh`)
**Owner**: @owner
**Created**: 2026-08-18

**As**: Developer or IT admin installing engram-dotnet
**I want**: a guided installation wizard that correctly maps each profile to its valid installation methods, handles navigation (back), and fully implements the desktop profile
**To**: avoid misconfiguration, clarify the offline-first vs desktop distinction, and provide a production-ready installation experience

---

## Motivation

The current `install.sh` has two fundamental problems:

1. **Order confusion**: The wizard asks for *method* (Docker/Build/Release) before *profile* (local/offline-first/remote-server/desktop). This leads to invalid combinations like "Docker + offline-first" being selectable even though offline-first requires SQLite local, not a Dockerized server.

2. **Desktop profile is incomplete**: The desktop profile (one user, multiple devices, local Docker server) was designed in HU-010 but never fully implemented in the installer. The current install.sh marks it as "⚠️ pendiente de diseño".

3. **offline-first misconfiguration**: When a user selects Docker with offline-first, the system attempts to use PostgreSQL in the Docker container as the local store. But offline-first specifically means **SQLite local + sync to a remote server** — not local PostgreSQL.

4. **No back navigation**: Users cannot return to previous steps to change their selections.

### Profile Definitions (corrected)

| Profile | Local BD | Sync | Docker | Notes |
|--------|----------|------|--------|-------|
| `local` | SQLite ✅ | ❌ | ❌ | Solo local |
| `offline-first` | SQLite ✅ | ✅ (remote server) | ❌ | **No Docker**. Sync target is a *remote* server. |
| `remote-server` | ❌ | ❌ | ✅ optional | PostgreSQL shared, no sync |
| `desktop` | SQLite ✅ | ✅ (local Docker) | ✅ (required) | **NEW**: one user, multiple devices, local Docker server. Variant of offline-first for power users. |

---

## Acceptance Criteria

### AC1 — Correct profile→method mapping

- [x] When user selects `local` → only methods **Release** and **Build from source** are offered
- [x] When user selects `offline-first` → only methods **Release** and **Build from source** are offered (no Docker)
- [x] When user selects `remote-server` → methods **Release**, **Build from source**, and **Docker** are offered
- [x] When user selects `desktop` → methods **Docker** (GHCR pre-built) and **Build from source** (compila + build local Docker image con `Dockerfile.allinone`) son ofrecidos. Cliente local SQLite + PostgreSQL Docker como sync hub.

### AC2 — Profile-first wizard flow

- [x] Step 1 is always **Profile selection** (not method)
- [x] Step 2 shows only valid **Methods** for the selected profile
- [x] Remaining steps (config, MCP, install) proceed in logical order

### AC3 — offline-first data collection

- [x] When `offline-first` is selected, wizard prompts for:
  - `ENGRAM_SERVER_URL` (required) — URL of the remote sync server
  - `ENGRAM_USER` (required) — user identifier for sync
  - `ENGRAM_SYNC_AUTO_SYNC` (optional, default `true`) — auto-sync on wake vs manual-only
- [x] If `ENGRAM_SERVER_URL` is empty → display message: "Without a remote server URL, please use the 'local' profile instead" and return to profile selection
- [x] If offline-first data is incomplete → same message and return to profile selection

### AC4 — desktop profile (100% functional)

- [x] desktop profile is **visible** in profile selection (not hidden)
- [x] When desktop is selected, wizard displays warning: "We recommend setting a fixed IP address on your local network to avoid connection issues"
- [x] Wizard asks PostgreSQL deployment mode:
  - `[1]` All-in-one container (PostgreSQL embedded inside engram container)
  - `[2]` Separate container (PostgreSQL as separate Docker container)
  - `[3]` Existing PostgreSQL server on my Docker network
- [x] If option `[3]` is selected → asks for: `PG_HOST`, `PG_PORT`, `PG_USER`, `PG_PASSWORD`, `PG_DATABASE`
- [x] Wizard asks for `ENGRAM_USER`
- [x] Wizard asks for `ENGRAM_SYNC_AUTO_SYNC` (default `true`, optional)
- [x] Wizard starts Docker services according to the selected configuration
- [x] Wizard displays `ENGRAM_SERVER_URL = http://localhost:7437` prominently and asks the user to save it
- [x] Wizard displays warning: "On your other devices, select the 'offline-first' profile and use this URL as the remote server"

### AC5 — Navigation (back)

- [x] Every step after profile selection has a `(b) back` option that returns to the previous step
- [x] Step 1 (profile selection) has `(b) quit` option to exit the wizard
- [x] `(q) quit` is also available at all steps to exit

### AC6 — MCP configuration

- [x] MCP config is generated correctly for each profile combination
- [x] For `offline-first`: `ENGRAM_SYNC_ENABLED=true`, `ENGRAM_SERVER_URL`, `ENGRAM_USER` are set
- [x] For `desktop`: `ENGRAM_SYNC_ENABLED=true`, `ENGRAM_SERVER_URL=http://localhost:7437`, `ENGRAM_USER` are set

---

## Tasks (Implementation)

### T1 — Profile→Method mapping table

- [x] Create a mapping structure in `install.sh` that defines valid methods per profile
- [x] Implement profile-first selection flow (Step 1 = profile, Step 2 = method filtered by profile)

### T2 — offline-first data collection

- [x] Add data collection for `ENGRAM_SERVER_URL`, `ENGRAM_USER`, `ENGRAM_SYNC_AUTO_SYNC`
- [x] Add validation: if `ENGRAM_SERVER_URL` is empty → message + return to profile selection

### T3 — desktop profile implementation

- [x] Implement PostgreSQL mode selection (all-in-one / separate / existing)
- [x] Implement Docker Compose generation for each PostgreSQL mode
- [x] Implement `ENGRAM_SERVER_URL = http://localhost:7437` configuration
- [x] Display prominent warning with the server URL for other devices
- [x] Implement `ENGRAM_USER` and `ENGRAM_SYNC_AUTO_SYNC` collection

### T4 — Navigation system

- [x] Implement step function with `back` and `quit` options
- [x] Each step returns to previous step when `(b)` is selected
- [x] Step 1 returns to profile re-selection or exits

### T5 — MCP config for all profiles

- [x] Ensure MCP config generation works for local, offline-first, remote-server, and desktop profiles
- [x] Verify env vars are correctly set for each combination

### T6 — Cleanup legacy code

- [x] Remove invalid combinations that were previously selectable (e.g., Docker + offline-first)
- [x] Remove or update the "⚠️ pending" label for desktop profile

### T7 — Tests

- [x] Add unit tests for profile→method validation
- [x] Add integration tests for offline-first data collection flow
- [x] Add integration tests for desktop Docker configuration generation

---

## Notes

### Profile vs Method Matrix

```
Profile        | Release | Build | Docker
-------------- | ------- | ----- | ------
local          | ✅      | ✅    | ❌
offline-first  | ✅      | ✅    | ❌
remote-server  | ✅      | ✅    | ✅
desktop       | ❌      | ❌   | ✅
```

### offline-first vs desktop

- **offline-first**: User works locally on SQLite, syncs to a *remote* server (could be another machine, cloud, etc.). Docker is NOT involved.
- **desktop**: User has one "master" machine that runs a local Docker server (PostgreSQL + engram). All other devices sync to that local Docker server using offline-first profile.

### Variables per profile

**local**:
- `ENGRAM_DATA_DIR`
- `ENGRAM_USER`
- `ENGRAM_PROFILE=local`
- `ENGRAM_SYNC_ENABLED=false`

**offline-first**:
- `ENGRAM_DATA_DIR`
- `ENGRAM_USER`
- `ENGRAM_PROFILE=offline-first`
- `ENGRAM_SYNC_ENABLED=true`
- `ENGRAM_SYNC_AUTO_SYNC=true|false` (user choice)
- `ENGRAM_SERVER_URL` (remote server URL, **required**)

**remote-server**:
- `ENGRAM_DATA_DIR`
- `ENGRAM_USER`
- `ENGRAM_PROFILE=remote-server`
- `ENGRAM_SYNC_ENABLED=false`
- `ENGRAM_PG_CONNECTION` (if Docker: constructed from docker-compose)

**desktop**:
- `ENGRAM_DATA_DIR`
- `ENGRAM_USER`
- `ENGRAM_PROFILE=desktop`
- `ENGRAM_SYNC_ENABLED=true`
- `ENGRAM_SYNC_AUTO_SYNC=true|false` (default true)
- `ENGRAM_SERVER_URL=http://localhost:7437` (auto)
- `ENGRAM_PG_CONNECTION` (depends on PostgreSQL mode)

### Docker PostgreSQL modes for desktop

1. **All-in-one container**: PostgreSQL runs inside the same container as engram server (single docker-compose service with both)
2. **Separate container**: PostgreSQL runs as a separate Docker container, engram server connects to it via Docker network
3. **Existing server**: User provides connection details for a PostgreSQL server already running on their Docker network

### Dependencies

- HU-010 (Deploy Profile System) — defines the profile concepts
- HU-014 (Smart Sync Triggers) — defines sync variables (`ENGRAM_SYNC_AUTO_SYNC`)
- deploy.sh — already has embedded/external PostgreSQL logic; reuse for desktop

### Related Documents

- `scripts/install.sh` — current installer (to be refactored)
- `scripts/deploy.sh` — Docker deployment logic (reference for desktop Docker config)
- `docs/DEVELOPMENT.md` — environment variables reference
