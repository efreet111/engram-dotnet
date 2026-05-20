# Offline-First Sync — Setup Guide

> **Version**: 1.0  
> **Requires**: engram-dotnet v1.1.0+ with PostgreSQL backend

---

## When to Use Sync

| Scenario | Recommended |
|----------|-------------|
| Single developer, local only | SQLite (default, no sync needed) |
| 2-5 devs, shared server | PostgreSQL + multi-user (no sync needed) |
| **5-20 devs, offline work** | **PostgreSQL + Offline-First Sync** |
| Disaster recovery | Sync + periodic backups |

---

## How It Works

```
┌─────────────────────────────────────────────────────┐
│                  CLIENT (each dev)                    │
│  ┌─────────────────────────────────────────────────┐ │
│  │  Local SQLite: fast reads/writes, works offline │ │
│  │  SyncManager: background service               │ │
│  │    → Push: local mutations → server (POST)     │ │
│  │    → Pull: server mutations → local (GET)      │ │
│  └─────────────────────────────────────────────────┘ │
└────────────────────────┬────────────────────────────┘
                         │ HTTP (port 7437)
┌────────────────────────▼────────────────────────────┐
│                  SERVER (TrueNAS/Linux)                │
│  ┌─────────────────────────────────────────────────┐ │
│  │  PostgreSQL: cloud_mutations, enrolled_projects │ │
│  │  REST API: 8 sync endpoints                     │ │
│  │  Audit log: cloud_sync_audit_log                │ │
│  └─────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────┘
```

---

## Server Setup (TrueNAS / Debian)

### Prerequisites

- Linux server (Debian/Ubuntu recommended)
- PostgreSQL 15+
- .NET 10 SDK (to build) or pre-built binary
- Docker (optional, for containerized deployment)

### Option A: Docker Compose (Recommended)

```yaml
# docker-compose.yml
services:
  postgres:
    image: postgres:17-alpine
    environment:
      POSTGRES_DB: engram
      POSTGRES_USER: engram
      POSTGRES_PASSWORD: secret
    volumes:
      - pgdata:/var/lib/postgresql/data

  engram:
    build: .
    ports:
      - "7437:7437"
    environment:
      ENGRAM_DB_TYPE: postgres
      ENGRAM_PG_CONNECTION: "Host=postgres;Database=engram;Username=engram;Password=secret"
      ENGRAM_SYNC_ENABLED: "true"
      ENGRAM_SYNC_TARGET: "cloud"
    depends_on:
      - postgres

volumes:
  pgdata:
```

```bash
docker compose up -d
```

### Option B: Systemd (Manual)

```bash
# 1. Build
git clone https://github.com/efreet111/engram-dotnet
cd engram-dotnet
dotnet publish src/Engram.Cli -c Release -r linux-x64 --self-contained -o /opt/engram/

# 2. Create systemd service
cat > /etc/systemd/system/engram.service << 'EOF'
[Unit]
Description=Engram Memory Server
After=network.target postgresql.service

[Service]
Type=simple
ExecStart=/opt/engram/engram serve
Restart=always
User=engram
Environment="ENGRAM_DB_TYPE=postgres"
Environment="ENGRAM_PG_CONNECTION=Host=localhost;Database=engram;Username=engram;Password=secret"
Environment="ENGRAM_SYNC_ENABLED=true"
Environment="ENGRAM_SYNC_TARGET=cloud"

[Install]
WantedBy=multi-user.target
EOF

systemctl daemon-reload
systemctl enable --now engram
```

---

## Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `ENGRAM_SYNC_ENABLED` | `true` | Enable the background SyncManager |
| `ENGRAM_SYNC_TARGET` | `cloud` | Sync target key |
| `ENGRAM_SYNC_POLL_SECONDS` | `30` | Interval between sync cycles (seconds) |
| `ENGRAM_SYNC_DEBOUNCE_MS` | `500` | Debounce before starting a cycle (ms) |
| `ENGRAM_SYNC_PUSH_BATCH` | `100` | Max mutations per push batch |
| `ENGRAM_SYNC_PULL_BATCH` | `100` | Max mutations per pull batch |
| `ENGRAM_SYNC_MAX_FAILURES` | `10` | Failure ceiling before disabling sync |
| `ENGRAM_SYNC_BACKOFF_BASE_MS` | `1000` | Initial backoff (ms, exponential) |
| `ENGRAM_SYNC_BACKOFF_MAX_MS` | `300000` | Maximum backoff (ms = 5min) |

---

## Client Setup (Each Developer)

### 1. Run Engram Locally

```bash
ENGRAM_DATA_DIR=~/.engram \
ENGRAM_SERVER_URL=http://server:7437 \
ENGRAM_USER=victor.silgado \
ENGRAM_SYNC_ENABLED=true \
ENGRAM_SYNC_TARGET=cloud \
./engram serve --port 7442
```

### 2. Enroll Your Project

```bash
curl -X POST http://localhost:7442/sync/enroll \
  -H "X-Engram-User: victor.silgado" \
  -d '{"project":"team/mi-api"}'
```

> **Note**: The local enrollment is stored in your local SQLite. The SyncManager checks this before pushing.

### 3. Verify

```bash
# Check sync status
curl http://localhost:7442/sync/status

# Check enrolled projects
curl -H "X-Engram-User: victor" http://localhost:7442/sync/enroll
```

---

## Workflow

### Normal Operation

```
1. Agent saves memory via mem_save()
2. Memory goes to LOCAL SQLite (instant, always works)
3. SyncManager detects pending mutation (poll every 30s)
4. SyncManager pushes to server POST /sync/mutations/push
5. Server stores in PostgreSQL cloud_mutations
6. Other devs' SyncManager pulls GET /sync/mutations/pull
7. Mutations applied to their local SQLite
```

### Offline Scenario

```
1. Developer works offline (no server connection)
2. Agent saves memories → local SQLite (works fine)
3. SyncManager tries to push → fails → retries with backoff
4. When connection is restored → SyncManager pushes pending
5. All offline memories are now on the server
```

---

## Monitoring

### Key Metrics

```bash
# Server health
curl http://server:7437/health

# Sync status
curl http://server:7437/sync/status

# Server logs
docker logs -f engram
```

### Log Output

```
info: HTTP[0] POST /sync/mutations/push → 200 (142ms)
info: SyncManager[2001] Cycle completed: pushed=5, pulled=3
warn: SyncManager[2002] Cycle failed (failure 3/10): connection refused
crit: SyncManager[2006] Panic exit: ...
```

---

## Troubleshooting

| Problem | Cause | Solution |
|---------|-------|----------|
| Sync disabled in `/sync/status` | `ENGRAM_SYNC_ENABLED` not set | Set to `true` |
| Push fails with 500 | Old binary on server | `docker compose up -d --build` |
| Pull returns no data | Project not enrolled | `POST /sync/enroll` |
| SyncManager in backoff | Too many consecutive failures | Check server logs |
| Mutation transport 501 | SQLite backend (no ICloudMutationStore) | Use PostgreSQL |
| Address already in use | Port 7437 occupied | `fuser -k 7437/tcp` |
