# PostgreSQL Setup — engram-dotnet

> Guide for running engram-dotnet with PostgreSQL as the persistence backend.

---

## When to Use PostgreSQL

| Scenario | Recommendation |
|----------|---------------|
| 1-4 concurrent developers | SQLite (default) |
| 5+ concurrent developers | PostgreSQL |
| Automated backups needed | PostgreSQL |
| High availability / replication | PostgreSQL |
| Enterprise infrastructure | PostgreSQL |

PostgreSQL eliminates SQLite's write contention and enables horizontal scaling with connection pooling.

---

## Firewall & Ports

Make sure port **7437** is open between:
- Developers ↔ Server (for REST API access)
- Server ↔ PostgreSQL (for database connection)
- SyncManager ↔ Server (for mutation push/pull)

```bash
# On the server (Debian/Ubuntu)
sudo ufw allow 7437/tcp
```

---

## Quick Start (Docker)

```yaml
# docker-compose.yml
services:
  postgres:
    image: postgres:17-alpine
    environment:
      POSTGRES_DB: engram
      POSTGRES_USER: engram
      POSTGRES_PASSWORD: supersecret
    ports:
      - "5432:5432"
    volumes:
      - pgdata:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U engram"]
      interval: 5s
      timeout: 5s
      retries: 5

  engram:
    build: .
    ports:
      - "7437:7437"
    environment:
      ENGRAM_DB_TYPE: postgres
      ENGRAM_PG_CONNECTION: "Host=postgres;Database=engram;Username=engram;Password=supersecret"
      # Sync push/pull: API en este servicio; SyncManager en cada cliente MCP (ver SYNC-SETUP.md).
    depends_on:
      postgres:
        condition: service_healthy

volumes:
  pgdata:
```

---

## Manual Setup

### 1. Install PostgreSQL

```bash
# Debian/Ubuntu
sudo apt install postgresql postgresql-contrib
sudo systemctl enable --now postgresql

# Verify
psql --version
```

### 2. Create Database and User

```bash
sudo -u postgres psql
```

```sql
CREATE DATABASE engram;
CREATE USER engram WITH PASSWORD 'supersecret';
GRANT ALL PRIVILEGES ON DATABASE engram TO engram;
\q
```

### 3. Test Connection

```bash
# From local machine
PGPASSWORD=supersecret psql -h 192.168.0.178 -U engram -d engram -c "SELECT 1;"
# → ?column?
# →        1
```

### 4. Start engram

```bash
ENGRAM_DB_TYPE=postgres \
ENGRAM_PG_CONNECTION="Host=localhost;Database=engram;Username=engram;Password=supersecret" \
./engram serve
```

Tables are created **automatically** on first startup.

---

## Environment Variables

| Variable | Required | Description |
|----------|----------|-------------|
| `ENGRAM_DB_TYPE` | ✅ | Must be `postgres` |
| `ENGRAM_PG_CONNECTION` | ✅ | PostgreSQL connection string |

### Connection String Format

```
Host=server;Database=engram;Username=user;Password=pass
Host=server;Port=5432;Database=engram;Username=user;Password=pass  # Custom port
Host=server;Database=engram;Username=user;Password=pass;SSLMode=Require  # SSL
```

---

## Verification

```bash
# Check backend
curl http://localhost:7437/health
# → {"status":"ok","service":"engram","version":"1.1.0","backend":"postgres"}

# Check tables (via psql)
sudo -u postgres psql -d engram -c "\dt"

# Expected tables:
# - sessions
# - observations
# - user_prompts
# - sync_mutations
# - sync_state
# - sync_enrolled_projects
# - cloud_mutations
# - cloud_project_controls
# - cloud_sync_audit_log
# - project_migrations
```

---

## Troubleshooting

| Error | Cause | Solution |
|-------|-------|----------|
| `28P01` (password auth failed) | Wrong password | Check `ENGRAM_PG_CONNECTION` |
| `42P01` (relation does not exist) | Tables not created | First startup creates them automatically |
| `57P03` (cannot connect to server) | PostgreSQL not running | `systemctl start postgresql` |
| `08001` (connection refused) | Wrong host/port | Check PostgreSQL port (default 5432) |
| Column type mismatch | Schema vs code mismatch | `docker compose up -d --build` |

---

## Maintenance

### Backup

```bash
pg_dump -h localhost -U engram -d engram > engram_backup_$(date +%Y%m%d).sql
```

### Restore

```bash
createdb -h localhost -U engram engram_restore
psql -h localhost -U engram -d engram_restore < engram_backup.sql
```

### Monitor active connections

```sql
SELECT count(*) FROM pg_stat_activity WHERE datname = 'engram';
```
