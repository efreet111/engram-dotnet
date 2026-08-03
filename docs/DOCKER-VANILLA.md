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

## 2. Path A — Default Dockerfile (uses `mcr.microsoft.com`)

This is the recommended path. The build uses Microsoft's pre-layered .NET
SDK and ASP.NET images, so it is fast (~2–4 min on a warm cache) and small
(~250 MB final image).

### 2.1 Build

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
> `error: 'dev' is not a valid version string`. See [§ 5.1](#51-nuget-version-error).

### 2.2 Run (SQLite, local mode)

```bash
docker run -d \
    --name engram \
    --restart unless-stopped \
    -p 7437:7437 \
    -v /var/lib/engram:/data/engram \
    engram-dotnet:latest
```

The container stores its SQLite database in `/data/engram` (mounted from
the host). The MCP HTTP transport is exposed on port 7437.

Verify it is healthy:

```bash
sleep 5
curl -fsS http://localhost:7437/health
# → {"status":"healthy",...}
```

### 2.3 Run (PostgreSQL, sync mode)

```bash
docker run -d \
    --name engram \
    --restart unless-stopped \
    -p 7437:7437 \
    -e ENGRAM_DB_TYPE=postgres \
    -e ENGRAM_PG_CONNECTION="Host=db.example.com;Port=5432;Database=engram;Username=engram;Password=REPLACE_ME" \
    -v /var/lib/engram:/data/engram \
    engram-dotnet:latest
```

`ENGRAM_PG_CONNECTION` follows the standard Npgsql connection-string
syntax. See [API-REFERENCE.md](API-REFERENCE.md) for all environment
variables.

### 2.4 Logs and lifecycle

```bash
docker logs -f engram               # tail logs
docker restart engram               # restart
docker stop engram && docker rm engram   # tear down (data volume kept)
```

---

## 3. Path B — Debian fallback (`Dockerfile.debian`)

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

### 3.1 Build

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

### 3.2 Run

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

### 3.3 Behind a registry mirror

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

## 4. Verification checklist

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

## 5. Troubleshooting

### 5.1 NuGet version error

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

### 5.2 Cannot reach `mcr.microsoft.com`

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

### 5.3 Port 7437 already in use

**Symptom:**

```
docker: error: bind: address already in use
```

**Fix:** Map to a different host port:

```bash
docker run -d --name engram -p 18080:7437 engram-dotnet:latest
# Then: curl http://localhost:18080/health
```

### 5.4 `SQLitePCLRaw.lib.e_sqlite3 2.1.10 has a known high severity vulnerability`

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

### 5.5 Build context too large

**Symptom:** `Sending build context to Docker daemon  1.5GB` is slow.

**Fix:** Add a `.dockerignore` at the repo root. The project ships with
`bin/`, `obj/`, `.git/`, `out/`, `node_modules/` already excluded. If
your tree has other heavy directories (test fixtures, generated docs),
add them to `.dockerignore`.

---

## 6. Image layout reference

Both Dockerfiles produce a two-stage image with this final structure:

```
/app/engram                  # Single-file executable (CLI entrypoint)
/app/*.dll                   # Managed assemblies (loaded by engram)
/app/appsettings*.json       # Default config (override via env vars)
/app/docs/                   # Per-project docs mount point
/data/engram/                # SQLite database + project identity
```

The runtime image runs as a non-root user (`engram`, UID assigned at
image build time). The `/data/engram` and `/app/docs` directories are
writable by that user.

---

## 7. See also

- [DOCKER.md](DOCKER.md) — Compose / Kubernetes recipes (if you want
  more than a single container).
- [API-REFERENCE.md](API-REFERENCE.md) — all environment variables and
  endpoints.
- [MANUAL-TESTING-CHECKLIST.md](MANUAL-TESTING-CHECKLIST.md) — what to
  click after the container is up.
- [DEVELOPMENT.md](DEVELOPMENT.md) — local dev loop (without Docker).
