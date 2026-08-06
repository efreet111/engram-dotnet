# Docker Vanilla — build & run guide

> **Audience**: anyone who needs to run engram-dotnet as a plain Docker
> container, without Compose, Kubernetes, or the FlowForge installer. The
> recipes in this file work on any host with Docker 20.10+ and outbound
> access to a container registry.

This guide was written after diagnosing two issues found during a vanilla
`docker build` on a server with restricted egress:

1. **NuGet version error** — `error: 'dev' is not a valid version string`
   (default `ARG ENGRAM_VERSION=dev` in the original Dockerfile).
2. **mcr.microsoft.com not reachable** — some environments (air-gapped,
   corporate proxies, certain geo-restrictions) cannot pull the
   `mcr.microsoft.com/dotnet/*` images.

Both are fixed below. Use the section that matches your environment.

---

## 1. Prerequisites

| Requirement | Minimum | Notes |
|---|---|---|
| Docker Engine | 20.10+ | Tested on Docker 29.5 / 29.6 |
| Outbound HTTPS | Required | To a container registry (see below) |
| Free RAM | 512 MB | For the running container |
| Free disk | 2 GB | Build context + image layers |

Choose one of two registries:

| Path | Registry | When to use |
|---|---|---|
| **A. Default** | `mcr.microsoft.com` (Microsoft) | Open internet, no proxy |
| **B. Debian fallback** | `docker.io` (Docker Hub) via `debian:12-slim` + `dot.net` install script | Server cannot reach `mcr.microsoft.com` |

---

## 2. Profiles — Pick your deployment mode

Instead of configuring `ENGRAM_DB_TYPE`, `ENGRAM_SYNC_ENABLED`, and other vars
one by one, you can use the `ENGRAM_PROFILE` env var to set sensible defaults
for your use case:

| Profile | Backend | Sync | Required vars | Typical use |
|---------|---------|------|---------------|-------------|
| `local` (default) | SQLite | ❌ | *(none)* | Solo developer |
| `remote-server` | PostgreSQL | ❌ | `ENGRAM_PG_CONNECTION`, `ENGRAM_USER` | Small team, shared DB |
| `offline-first` | SQLite (local) + PostgreSQL (server) | ✅ | `ENGRAM_SERVER_URL`, `ENGRAM_USER` | Large team, offline-first |
| `desktop` | PostgreSQL | ❌ | `ENGRAM_PG_CONNECTION`, `ENGRAM_USER` | Personal/shared workstation |

Profile defaults (applied when the var is not explicitly set):

| Key | `local` | `remote-server` | `offline-first` | `desktop` |
|-----|---------|----------------|-----------------|-----------|
| `ENGRAM_DB_TYPE` | `sqlite` | `postgres` | `sqlite` | `postgres` |
| `ENGRAM_SYNC_ENABLED` | `false` | `false` | `true` | `false` |
| `ENGRAM_SYNC_POLL_SECONDS` | — | — | `30` | — |
| `ENGRAM_SYNC_TARGET` | — | — | `cloud` | — |

**Merging rules**: explicit env var > profile default > built-in default.
If you set `ENGRAM_PROFILE=remote-server` but also `ENGRAM_DB_TYPE=sqlite`,
SQLite wins — your explicit override always takes precedence.

### 2.1 Database mode (`ENGRAM_DB_MODE`)

Controls whether PostgreSQL runs as an embedded service alongside Engram or
connects to an external instance:

| Value | Behavior |
|-------|----------|
| `external` (default) | PostgreSQL is on the host or network — pass `ENGRAM_PG_CONNECTION` with host/port |
| `embedded` | Docker Compose starts a `postgres` service alongside Engram — no manual PG setup |

`ENGRAM_DB_MODE` is only meaningful with `ENGRAM_PROFILE=remote-server` or `desktop`
(both require PostgreSQL). With `local` or `offline-first`, the mode is ignored —
SQLite has no external dependency.

```bash
# External PostgreSQL (the default — PG already running somewhere):
docker run -d --name engram \
  -p 7437:7437 \
  -e ENGRAM_PROFILE=remote-server \
  -e ENGRAM_PG_CONNECTION="Host=db.example.com;Port=5432;Database=engram;Username=engram;Password=REPLACE_ME" \
  -e ENGRAM_USER=admin \
  engram-dotnet:latest

# Embedded PostgreSQL (via docker compose — see docker/README.md):
ENGRAM_PROFILE=remote-server ENGRAM_DB_MODE=embedded docker compose up -d
```

### 2.2 Quick comparison — with and without profiles

```bash
# Before (manual vars — still works):
docker run -d --name engram -p 7437:7437 \
  -e ENGRAM_DB_TYPE=postgres \
  -e ENGRAM_PG_CONNECTION="Host=...;..." \
  engram-dotnet:latest

# After (profile — cleaner):
docker run -d --name engram -p 7437:7437 \
  -e ENGRAM_PROFILE=remote-server \
  -e ENGRAM_PG_CONNECTION="Host=...;..." \
  -e ENGRAM_USER=admin \
  engram-dotnet:latest
```

Both produce identical behavior. Profiles are a convenience — they don't lock
you in. You can always override individual vars.

---

## 3. Path A — Default Dockerfile (uses `mcr.microsoft.com`)

This is the recommended path. The build uses Microsoft's pre-layered .NET
SDK and ASP.NET images, so it is fast (~2–4 min on a warm cache) and small
(~250 MB final image).

### 3.1 Build

```bash
# From the repo root:
docker build -t engram-dotnet:latest -f Dockerfile .
```

To pin a version in the published binary, pass `--build-arg ENGRAM_VERSION`:

```bash
docker build \
    --build-arg ENGRAM_VERSION=1.3.0 \
    -t engram-dotnet:1.3.0 \
    -f Dockerfile .
```

> **Note:** If you omit `--build-arg ENGRAM_VERSION`, the image is tagged
> `0.0.0-dev`. This is a valid NuGet SemVer 2.0 string; the previous
> default of `dev` was **invalid** and made `dotnet publish` fail with
> `error: 'dev' is not a valid version string`. See [§ 6.1](#61-nuget-version-error).

### 3.2 Run (SQLite, local mode)

```bash
docker run -d \
    --name engram \
    --restart unless-stopped \
    -p 7437:7437 \
    -v /var/lib/engram:/data/engram \
    engram-dotnet:latest
```

The container stores its SQLite database in `/data/engram` (mounted from
the host). The MCP HTTP transport is exposed on port 7437. Profile defaults
to `local` — you don't need to set `ENGRAM_PROFILE` for SQLite.

To be explicit (same result):
```bash
docker run -d --name engram --restart unless-stopped \
  -p 7437:7437 -v /var/lib/engram:/data/engram \
  -e ENGRAM_PROFILE=local \
  engram-dotnet:latest
```

Verify it is healthy:

```bash
sleep 5
curl -fsS http://localhost:7437/health
# → {"status":"healthy",...}
```

### 3.3 Run (PostgreSQL, remote-server or desktop)

```bash
# With explicit vars (backward compatible):
docker run -d \
    --name engram \
    --restart unless-stopped \
    -p 7437:7437 \
    -e ENGRAM_DB_TYPE=postgres \
    -e ENGRAM_PG_CONNECTION="Host=db.example.com;Port=5432;Database=engram;Username=engram;Password=REPLACE_ME" \
    engram-dotnet:latest

# With profile (recommended — cleaner):
docker run -d \
    --name engram \
    --restart unless-stopped \
    -p 7437:7437 \
    -e ENGRAM_PROFILE=remote-server \
    -e ENGRAM_PG_CONNECTION="Host=db.example.com;Port=5432;Database=engram;Username=engram;Password=REPLACE_ME" \
    -e ENGRAM_USER=admin \
    engram-dotnet:latest
```

> **Note:** The `-v /var/lib/engram:/data/engram` volume flag is **not required** when using PostgreSQL backend. `PostgresStore` uses only the connection string and never reads `DataDir`. You may omit the volume mount entirely for PostgreSQL deployments.

`ENGRAM_PG_CONNECTION` follows the standard Npgsql connection-string
syntax. See [API-REFERENCE.md](API-REFERENCE.md) for all environment
variables.

### 3.4 Logs and lifecycle

```bash
docker logs -f engram               # tail logs
docker restart engram               # restart
docker stop engram && docker rm engram   # tear down (data volume kept)
```

---

## 5. Path B — Debian fallback (`Dockerfile.debian`)

Use this when your server cannot reach `mcr.microsoft.com`. The build
downloads `debian:12-slim` from Docker Hub (or any mirror you have
configured) and installs the .NET 10 SDK / runtime via the official
`dotnet-install.sh` script from `dot.net`.

Trade-offs vs Path A:

| Aspect | Path A (mcr) | Path B (debian) |
|---|---|---|
| Final image size (on disk) | ~100 MB | ~100 MB |
| Final image size (virtual, summed layers) | ~360 MB | ~360 MB |
| Cold build time | ~3 min | ~5 min |
| Outbound destinations | `mcr.microsoft.com`, `api.nuget.org` | `docker.io`, `dot.net`, `api.nuget.org` |
| When to use | Default | mcr.microsoft.com blocked |

> **Why sizes are similar:** the runtime stage of both Dockerfiles is
> small. Path A reuses Microsoft's pre-layered `aspnet:10.0` image
> (~100 MB on disk). Path B starts from `debian:12-slim` (~75 MB) and
> layers the ASP.NET Core shared framework on top. The on-disk delta is
> only the package manager state, which is much smaller than the
> estimates (some guides quote ~500 MB for this approach, but that's
> based on installing the full SDK in both stages).

### 4.1 Build

```bash
docker build \
    -f Dockerfile.debian \
    --build-arg ENGRAM_VERSION=1.3.0 \
    -t engram-dotnet:debian .
```

The build has two stages:

- **build** — `debian:12-slim` + installs .NET SDK 10.0 (channel `10.0`,
  matches `global.json: 10.0.108` with `rollForward: latestFeature`).
- **runtime** — `debian:12-slim` + installs only the ASP.NET Core shared
  framework (no SDK, no compiler).

### 4.2 Run

Identical to Path A — the runtime image exposes the same `engram`
executable and listens on the same port:

```bash
docker run -d \
    --name engram \
    -p 7437:7437 \
    -v /var/lib/engram:/data/engram \
    engram-dotnet:debian

curl -fsS http://localhost:7437/health
```

### 4.3 Behind a registry mirror

If you have an internal Docker registry that mirrors `mcr.microsoft.com`,
configure the daemon and stick with Path A:

```json
// /etc/docker/daemon.json
{
  "registry-mirrors": [
    "https://my-mirror.example.com"
  ]
}
```

Then restart Docker and rebuild — the build will fetch images from the
mirror transparently.

---

## 5. Verification checklist

After `docker run`, confirm the following before declaring success:

```bash
# 1. Container is running
docker ps --filter name=engram

# 2. Healthcheck returns 200
curl -fsS http://localhost:7437/health

# 3. Stats endpoint reports backend=sqlite (or postgres)
curl -fsS http://localhost:7437/stats

# 4. Search works (empty DB → empty list, not 500)
curl -fsS "http://localhost:7437/search?q=test"
```

The official `scripts/regression-test.sh` script automates 31 checks
against a running container and is safe to wire into CI.

---

## 6. Troubleshooting

### 6.1 NuGet version error

**Symptom:**

```
/usr/share/dotnet/sdk/10.0.302/NuGet.targets(198,5): error :
'dev' is not a valid version string. (Parameter 'value')
```

**Cause:** The Dockerfile used to default `ARG ENGRAM_VERSION=dev`.
`dotnet publish -p:Version=dev` is rejected by NuGet because `dev` is not
a valid SemVer 2.0 string.

**Fix:** The default in `Dockerfile` (and `Dockerfile.debian`) is now
`0.0.0-dev`, which is valid. If you are still seeing this error, upgrade
to a version that contains the fix, or pass
`--build-arg ENGRAM_VERSION=1.3.0` (or any valid SemVer) explicitly.

### 6.2 Cannot reach `mcr.microsoft.com`

**Symptom:**

```
ERROR: failed to solve: mcr.microsoft.com/dotnet/sdk:10.0: failed to
resolve source metadata: dial tcp: lookup mcr.microsoft.com: no such host
```

**Cause:** DNS / firewall / proxy is blocking Microsoft Container
Registry. This is a common restriction in corporate networks, air-gapped
environments, and some geographic regions.

**Fix:** Use [Path B — Dockerfile.debian](#3-path-b--debian-fallback-dockerfiledebian).
It only requires `docker.io` and `dot.net` to be reachable.

If you cannot reach either registry, pre-stage the images on a host that
**can** pull them, then `docker save` / `docker load`:

```bash
# On a host with internet:
docker pull mcr.microsoft.com/dotnet/sdk:10.0
docker pull mcr.microsoft.com/dotnet/aspnet:10.0
docker save -o base-images.tar \
    mcr.microsoft.com/dotnet/sdk:10.0 \
    mcr.microsoft.com/dotnet/aspnet:10.0

# On the restricted server:
docker load -i base-images.tar
```

### 6.3 Port 7437 already in use

**Symptom:**

```
docker: error: bind: address already in use
```

**Fix:** Map to a different host port:

```bash
docker run -d --name engram -p 18080:7437 engram-dotnet:latest
# Then: curl http://localhost:18080/health
```

### 6.4 `SQLitePCLRaw.lib.e_sqlite3 2.1.10 has a known high severity vulnerability`

**Symptom:** A `NU1903` warning during `dotnet restore`. The build
succeeds but NuGet emits:

```
warning NU1903: Package 'SQLitePCLRaw.lib.e_sqlite3' 2.1.10 has a known
high severity vulnerability, https://github.com/advisories/GHSA-2m69-gcr7-jv3q
```

**Status:** Known, tracked separately. Pulled in transitively by
`Microsoft.Data.Sqlite 9.0.*`. The vulnerability (CVE in `e_sqlite3`
2.1.10) is not exploitable in engram's usage (we never call the affected
codec path). The fix is to bump `Microsoft.Data.Sqlite` to a version that
pulls `e_sqlite3 >= 3.46.x`; tracked in a follow-up CHANGELOG entry.

This is a **warning**, not an error — the build is not blocked.

### 6.5 Build context too large

**Symptom:** `Sending build context to Docker daemon  1.5GB` is slow.

**Fix:** Add a `.dockerignore` at the repo root. The project ships with
`bin/`, `obj/`, `.git/`, `out/`, `node_modules/` already excluded. If
your tree has other heavy directories (test fixtures, generated docs),
add them to `.dockerignore`.

---

## 7. Image layout reference

Both Dockerfiles produce a two-stage image with this final structure:

```
/app/engram                  # Single-file executable (CLI entrypoint)
/app/*.dll                   # Managed assemblies (loaded by engram)
/app/appsettings*.json       # Default config (override via env vars)
/app/docs/                   # Per-project docs mount point
/data/engram/                # SQLite database + project identity
```

The entrypoint starts as root so it can repair mounted-volume ownership; the
application process then runs as a non-root user (`engram`, UID assigned at
image build time). The `/data/engram` and `/app/docs` directories are writable
by that user.

---

## 8. See also

- [DOCKER.md](DOCKER.md) — Compose / Kubernetes recipes (if you want
  more than a single container).
- [API-REFERENCE.md](API-REFERENCE.md) — all environment variables and
  endpoints.
- [MANUAL-TESTING-CHECKLIST.md](MANUAL-TESTING-CHECKLIST.md) — what to
  click after the container is up.
- [DEVELOPMENT.md](DEVELOPMENT.md) — local dev loop (without Docker).

## 9. Volume permissions

If you see `SQLite Error 14: 'unable to open database file'`, the volume
mounted from the host has incorrect permissions. Docker mounts volumes as
`root:root`, but the container runs as user `engram` (non-root).

### Automatic fix (recommended)

The entrypoint script automatically fixes permissions on startup. No
manual intervention needed:

```bash
docker run -d --name engram \
  -p 7437:7437 \
  -v /path/to/data:/data/engram \
  engram-dotnet:latest
```

The entrypoint runs as root, does `chown -R engram:engram /data/engram`,
then drops to the `engram` user via `gosu` before starting the app.

### How it works

1. Entrypoint script runs as **root** (default in Docker)
2. Fixes ownership of `/data/engram` → `engram:engram`
3. `exec gosu engram "$@"` — drops privileges and starts the app
4. App runs as non-root user `engram`

This pattern is used by official Docker images (PostgreSQL, Redis, etc.).
See [gosu on GitHub](https://github.com/tianon/gosu).

### Manual fix (if needed)

If the automatic fix doesn't work, pre-create the directory with correct
permissions:

```bash
# Create directory on host
mkdir -p /path/to/data

# Set ownership (UID 1000 is typically 'engram' in the container)
sudo chown -R 1000:1000 /path/to/data

# Run container
docker run -d --name engram \
  -p 7437:7437 \
  -v /path/to/data:/data/engram \
  engram-dotnet:latest
```

## 10. Environment variables reference

| Variable | Default | Description | Example |
|----------|---------|-------------|---------|
| `ENGRAM_PROFILE` | `local` | Deployment profile: `local`, `remote-server`, `offline-first`, `desktop` | `remote-server` |
| `ENGRAM_DATA_DIR` | `/data/engram` | Data directory (SQLite DB, exports) | `/custom/path` |
| `ENGRAM_PORT` | `7437` | HTTP port for MCP server | `8080` |
| `ENGRAM_DB_TYPE` | (profile default) | Backend: `sqlite` or `postgres`. Auto-set by `ENGRAM_PROFILE`. | `postgres` |
| `ENGRAM_DB_MODE` | `external` | PostgreSQL mode: `external` (host/network) or `embedded` (compose service) | `embedded` |
| `ENGRAM_PG_CONNECTION` | — | PostgreSQL connection string (required for `remote-server`/`desktop` profiles) | `Host=db;Port=5432;Database=engram;Username=engram;Password=secret` |
| `ENGRAM_SERVER_URL` | `http://localhost:7437` | Engram server URL (required for `offline-first` profile) | `http://your-server:7437` |
| `ENGRAM_SYNC_ENABLED` | (profile default) | Enable sync. Auto-set by `ENGRAM_PROFILE`. | `true` |
| `ENGRAM_USER` | — | User identity (required for `remote-server`, `offline-first`, `desktop` profiles) | `user@example.com` |
| `ENGRAM_AUTO_ENROLL` | `true` | Auto-generate `.engram-id` on startup | `false` |
| `ENGRAM_PROJECT` | — | Project name (auto-detected from git if not set) | `my-project` |
| `ASPNETCORE_URLS` | `http://+:7437` | ASP.NET Core listening URLs | `http://+:8080` |

### Examples

**Local mode (SQLite, single user)**:
```bash
docker run -d --name engram \
  -p 7437:7437 \
  -v /path/to/data:/data/engram \
  engram-dotnet:latest
```

**Server mode (PostgreSQL, team — with profile, recommended)**:
```bash
docker run -d --name engram \
  -p 7437:7437 \
  -e ENGRAM_PROFILE=remote-server \
  -e ENGRAM_PG_CONNECTION="Host=db.example.com;Port=5432;Database=engram;Username=engram;Password=secret" \
  -e ENGRAM_USER=admin \
  engram-dotnet:latest
```

**Server mode (PostgreSQL, team — manual vars, backward compatible)**:
```bash
docker run -d --name engram \
  -p 7437:7437 \
  -e ENGRAM_DB_TYPE=postgres \
  -e ENGRAM_PG_CONNECTION="Host=db.example.com;Port=5432;Database=engram;Username=engram;Password=secret" \
  engram-dotnet:latest
```

**Custom port**:
```bash
docker run -d --name engram \
  -p 8080:8080 \
  -e ENGRAM_PORT=8080 \
  -e ASPNETCORE_URLS="http://+:8080" \
  -v /path/to/data:/data/engram \
  engram-dotnet:latest
```

## 11. PostgreSQL connection guide

When running engram-dotnet in Docker with PostgreSQL, you need to configure the connection correctly. The connection string uses the standard Npgsql format.

### Connection string format

```
Host=<hostname>;Port=<port>;Database=<dbname>;Username=<user>;Password=<password>
```

**Example**:
```
Host=db.example.com;Port=5432;Database=engram;Username=engram;Password=your-secure-password
```

### Scenario A: PostgreSQL on the same host (Docker Desktop / Docker Engine)

If PostgreSQL is running on your host machine (not in a container), use `host.docker.internal`:

```bash
docker run -d --name engram \
  -p 7437:7437 \
  --add-host host.docker.internal:host-gateway \
  -e ENGRAM_DB_TYPE=postgres \
  -e ENGRAM_PG_CONNECTION="Host=host.docker.internal;Port=5432;Database=engram;Username=engram;Password=secret" \
  engram-dotnet:latest
```

**Notes**:
- `--add-host host.docker.internal:host-gateway` is required on Linux (Docker Desktop includes it by default)
- PostgreSQL must be configured to accept connections from Docker network (check `postgresql.conf` → `listen_addresses = '*'`)
- Firewall must allow connections on port 5432

### Scenario B: PostgreSQL in another Docker container

If PostgreSQL is running in a separate container, use a Docker network:

```bash
# Create a custom network
docker network create engram-net

# Start PostgreSQL
docker run -d --name postgres \
  --network engram-net \
  -e POSTGRES_DB=engram \
  -e POSTGRES_USER=engram \
  -e POSTGRES_PASSWORD=secret \
  -v /path/to/pgdata:/var/lib/postgresql/data \
  postgres:15

# Start engram
docker run -d --name engram \
  --network engram-net \
  -p 7437:7437 \
  -e ENGRAM_DB_TYPE=postgres \
  -e ENGRAM_PG_CONNECTION="Host=postgres;Port=5432;Database=engram;Username=engram;Password=secret" \
  engram-dotnet:latest
```

**Notes**:
- Both containers must be on the same Docker network
- Use the container name (`postgres`) as the hostname
- No need for `host.docker.internal` or IP addresses

### Scenario C: PostgreSQL on a remote server

If PostgreSQL is on a different server (e.g., cloud database, remote server):

```bash
docker run -d --name engram \
  -p 7437:7437 \
  -e ENGRAM_DB_TYPE=postgres \
  -e ENGRAM_PG_CONNECTION="Host=db.example.com;Port=5432;Database=engram;Username=engram;Password=secret" \
  engram-dotnet:latest
```

**Notes**:
- Use the IP address or hostname of the remote server
- Ensure firewall allows connections from your Docker host
- PostgreSQL must be configured to accept remote connections

### Testing the connection

Verify that engram can connect to PostgreSQL:

```bash
# Check logs for connection errors
docker logs engram | grep -i "postgres\|connection\|error"

# Check health endpoint
curl http://localhost:7437/health
# Expected: {"status":"ok","service":"engram","version":"...","backend":"postgres"}

# Check stats endpoint
curl http://localhost:7437/stats
# Expected: {"backend":"postgres",...}
```

### Common connection errors

| Error | Cause | Solution |
|-------|-------|----------|
| `Connection refused` | PostgreSQL not listening or firewall blocking | Check `listen_addresses` in `postgresql.conf`, verify firewall rules |
| `Authentication failed` | Wrong username/password | Verify credentials in connection string |
| `Database "engram" does not exist` | Database not created | Create database: `CREATE DATABASE engram;` |
| `host not found` | Incorrect hostname | Use correct hostname (container name, IP, or `host.docker.internal`) |
| `timeout expired` | Network unreachable | Check network connectivity, Docker network configuration |

### Using environment file for secrets

Instead of passing secrets in the command line, use an environment file:

```bash
# Create .env file
cat > .env <<EOF
ENGRAM_DB_TYPE=postgres
ENGRAM_PG_CONNECTION=Host=db.example.com;Port=5432;Database=engram;Username=engram;Password=secret
EOF

# Run with env file
docker run -d --name engram \
  -p 7437:7437 \
  --env-file .env \
  engram-dotnet:latest
```

**Security**: Add `.env` to `.gitignore` to avoid committing secrets.
