# Quick Start — engram-dotnet by Persona

> **📖 Looking for installation instructions?** See the [**Installation Guide**](INSTALL.md) for all methods (FlowForge installer, build from git, Docker).

---

## 🚀 TL;DR — Profiles at a Glance

Pick a profile and set the vars it asks for:

| Profile | Who | Backend | Required vars | Command |
|---------|-----|---------|---------------|---------|
| `local` | Solo dev | SQLite | *(none)* | `./engram serve` |
| `remote-server` | Small team (2-5) | PostgreSQL | `ENGRAM_PG_CONNECTION`, `ENGRAM_USER` | `ENGRAM_PROFILE=remote-server ENGRAM_PG_CONNECTION=... ENGRAM_USER=... ./engram serve` |
| `offline-first` | Large team (5-20) | SQLite + Sync | `ENGRAM_SERVER_URL`, `ENGRAM_USER` | `ENGRAM_PROFILE=offline-first ENGRAM_SERVER_URL=... ENGRAM_USER=... ./engram serve` |
| `desktop` | Personal/shared workstation | PostgreSQL | `ENGRAM_PG_CONNECTION`, `ENGRAM_USER` | `ENGRAM_PROFILE=desktop ENGRAM_PG_CONNECTION=... ENGRAM_USER=... ./engram serve` |

> **Backward compatible**: Don't want to use profiles? All existing env vars (`ENGRAM_DB_TYPE`, `ENGRAM_SYNC_ENABLED`, etc.) continue working identically. No migration needed.

---

## 🧑 Solo Developer

**Goal**: Use Engram locally with SQLite, no shared server. Ideal for a single developer working with their AI agent.

### Profile

```
ENGRAM_PROFILE=local   # ← or just omit it (local is the default)
```

This auto-sets: SQLite backend + sync disabled. Zero configuration needed.

### Prerequisites

- **Linux x64** (for the published binary)
- **.NET 10 SDK** ([download](https://dotnet.microsoft.com/download/dotnet/10.0)) — only needed to build from source
- Optional: **Docker** if you want PostgreSQL instead of SQLite
- No external runtime required in production — the binary is **self-contained**

> 💡 **Windows or macOS?** Change `-r linux-x64` to `-r win-x64` or `-r osx-x64` when publishing.

### Installation

```bash
# 1. Build
git clone https://github.com/efreet111/engram-dotnet
cd engram-dotnet
dotnet publish src/Engram.Cli -c Release -r linux-x64 --self-contained -o dist/

# 2. Start server
./dist/engram serve
```

### Verify

```bash
curl http://localhost:7437/health
# → {"status":"ok","service":"engram","version":"1.1.0","backend":"sqlite"}
```

### MCP Setup (any client)

Run the interactive wizard (recommended):

```bash
# Windows
.\scripts\setup.ps1

# Linux / macOS
./scripts/setup.sh
```

Or see [SETUP-WIZARD.md](SETUP-WIZARD.md) and [MCP-CONFIG.md](MCP-CONFIG.md).

### Ready 🎉

Your AI agent can now use `mem_save`, `mem_search`, `mem_context`, `mem_session_summary`, etc.

---

## 👥 Team Leader (2-5 people)

**Goal**: Shared PostgreSQL server with multi-user isolation. Each developer connects to the central server with their own identity. No offline-first sync.

### Profile

```
ENGRAM_PROFILE=remote-server   # ← auto-sets: PostgreSQL + sync disabled
```

This auto-sets `ENGRAM_DB_TYPE=postgres` and `ENGRAM_SYNC_ENABLED=false`. You only need to supply the connection string and user.

### Architecture

```
Dev 1 (ENGRAM_USER=victor)  ─┐
Dev 2 (ENGRAM_USER=juan)    ─┤── HTTP ──► PostgreSQL Server ──► Shared DB
Dev 3 (ENGRAM_USER=ana)     ─┘           (user isolation)
```

### Server Requirements

- Linux x64 server
- PostgreSQL installed and accessible
- .NET 10 SDK (only to build)

### 1. Setup PostgreSQL

```sql
CREATE DATABASE engram;
CREATE USER engram WITH PASSWORD 'REPLACE_ME';
GRANT ALL PRIVILEGES ON DATABASE engram TO engram;
```

### 2. Build & Start Server

```bash
# On the server
git clone https://github.com/efreet111/engram-dotnet
cd engram-dotnet
dotnet publish src/Engram.Cli -c Release -r linux-x64 --self-contained -o dist/

# Start with the remote-server profile
ENGRAM_PROFILE=remote-server \
ENGRAM_PG_CONNECTION="Host=localhost;Database=engram;Username=engram;Password=REPLACE_ME" \
ENGRAM_USER=admin \
./dist/engram serve
```

The `remote-server` profile auto-sets `ENGRAM_DB_TYPE=postgres` — no need to specify it manually.

### 3. Configure Each Developer

Each dev adds to their `opencode.json`:

```json
{
  "mcpServers": {
    "engram": {
      "command": "engram",
      "args": ["mcp"],
      "env": {
        "ENGRAM_URL": "http://192.168.1.100:7437",
        "ENGRAM_USER": "your-username"  // ← UNIQUE per developer
      }
    }
  }
}
```

### Verify Isolation

```bash
# Dev 1 saves a personal memory
curl -X POST http://server:7437/observations \
  -H "Content-Type: application/json" \
  -H "X-Engram-User: victor" \
  -d '{"session_id":"s1","title":"My note","content":"private","type":"manual","project":"team/mi-api"}'

# Dev 2 CANNOT see Dev 1's memory
curl -H "X-Engram-User: juan" http://server:7437/search?q=note
# → [] (empty)
```

---

## 🏢 IT Admin (5-20 people)

**Goal**: SQLite local + offline-first sync with enrollment, pause/resume, and automatic SyncManager. Developers work offline and sync when connected.

### Profile

```
ENGRAM_PROFILE=offline-first   # ← auto-sets: SQLite local + sync enabled + poll 30s + target cloud
```

This auto-sets `ENGRAM_DB_TYPE=sqlite`, `ENGRAM_SYNC_ENABLED=true`, `ENGRAM_SYNC_POLL_SECONDS=30`, and `ENGRAM_SYNC_TARGET=cloud`. You only need the server URL and user.

### Architecture

```
Each Developer:                 Server:
┌──────────────┐     push/pull ┌──────────────────┐
│ Local SQLite  │ ◄───HTTP───► │ PostgreSQL Server │
│ SyncManager   │   every 30s  │ cloud_mutations   │
│ pending_queue │              │ enrolled_projects │
└──────────────┘              └──────────────────┘
     │ offline-first                 │
     └── No connection = writes local│
     └── Connection = auto sync      │
```

> **Note**: The server uses PostgreSQL; each developer uses SQLite locally. SyncManager handles the bridge.

### Requirements

- Linux x64 server with PostgreSQL 15+
- **SyncManager** active on each developer machine (requires `ENGRAM_SYNC_ENABLED=true`)
- **Firewall**: Ensure port `7437` is open between developers and server

### 1. PostgreSQL (server side)

```sql
CREATE DATABASE engram;
-- Tables are created automatically when the server starts
```

### 2. Server (remote-server profile)

```bash
ENGRAM_PROFILE=remote-server \
ENGRAM_PG_CONNECTION="Host=localhost;Database=engram;Username=postgres;Password=REPLACE_ME" \
ENGRAM_USER=admin \
./engram serve
```

The `remote-server` profile auto-sets `ENGRAM_DB_TYPE=postgres` and `ENGRAM_SYNC_ENABLED=false` — the server does NOT run SyncManager, it just serves mutations.

### 3. Each Developer (offline-first profile)

Each developer runs the `offline-first` profile locally with SQLite:

```bash
ENGRAM_PROFILE=offline-first \
ENGRAM_SERVER_URL="http://server:7437" \
ENGRAM_USER=your-username \
./engram serve
```

The `offline-first` profile auto-sets `ENGRAM_DB_TYPE=sqlite`, `ENGRAM_SYNC_ENABLED=true`, `ENGRAM_SYNC_POLL_SECONDS=30`, and `ENGRAM_SYNC_TARGET=cloud` — no need to set those manually.

### 4. Each Developer (MCP config)

```json
{
  "mcpServers": {
    "engram": {
      "command": "engram",
      "args": ["mcp"],
      "env": {
        "ENGRAM_PROFILE": "offline-first",
        "ENGRAM_SERVER_URL": "http://your-server:7437",
        "ENGRAM_USER": "your-username",
        "ENGRAM_DATA_DIR": "~/.engram"
      }
    }
  }
}
```

> With `offline-first` profile, `ENGRAM_SYNC_ENABLED=true` and `ENGRAM_SYNC_TARGET=cloud` are auto-set. You only need `ENGRAM_SERVER_URL` and `ENGRAM_USER`.

### 5. Enroll Projects

```bash
curl -X POST http://localhost:7437/sync/enroll \
  -H "X-Engram-User: your-username" \
  -d '{"project":"team/mi-api"}'
```

### 6. Verify Sync

```bash
# Check sync status (from CLI)
engram sync status

# Check enrolled projects
curl -H "X-Engram-User: victor" http://localhost:7437/sync/enroll

# Check general health
curl http://localhost:7437/sync/status
```

### 7. Pause Sync (Admin)

```bash
# Pause (maintenance)
curl -X POST http://localhost:7437/sync/pause \
  -H "X-Engram-User: admin" \
  -d '{"project":"team/mi-api","reason":"DB migration"}'

# Resume
curl -X DELETE "http://localhost:7437/sync/pause?project=team/mi-api" \
  -H "X-Engram-User: admin"
```

---

## ⚙️ Mode Comparison

| Aspect | `local` | `remote-server` | `offline-first` | `desktop` |
|--------|---------|----------------|-----------------|-----------|
| **Backend** | SQLite | PostgreSQL | SQLite (local) + PostgreSQL (server) | PostgreSQL |
| **Sync** | ❌ No | ❌ No | ✅ Offline-First | ❌ No |
| **Multi-User** | ❌ No | ✅ Yes | ✅ Yes | ✅ Yes |
| **Enrollment** | ❌ No | ❌ No | ✅ Required | ❌ No |
| **Pause/Resume** | ❌ No | ❌ No | ✅ Admin | ❌ No |
| **Offline tolerance** | N/A | ❌ (needs connection) | ✅ Unlimited | ❌ (needs connection) |
| **Complexity** | Low | Medium | High | Medium |
| **Use case** | Solo dev | Shared server | Distributed team | Personal/shared workstation |

---

## 🔧 Troubleshooting by Profile

### `local` profile

```bash
# Error: Unable to load shared library 'e_sqlite3'
# Fix: The self-contained binary already includes native libs.
# Make sure to run ./dist/engram (not dotnet run)

# Error: Address already in use
# Fix: Another process is using the port.
fuser -k 7437/tcp
```

### `remote-server` / `desktop` profile

```bash
# Error: 28P01 (password authentication failed)
# Fix: PostgreSQL password is wrong. Check ENGRAM_PG_CONNECTION.

# Error: 42P01 (relation does not exist)
# Fix: Tables are created automatically on startup.
# Make sure the PostgreSQL user has CREATE permissions.
```

### `offline-first` profile

```bash
# Error: 42P10 (no unique constraint matching ON CONFLICT)
# Fix: Missing UNIQUE constraint on sync_enrolled_projects.
# The server creates it automatically in the latest version.

# Error: Sync disabled in /sync/status
# Fix: Set ENGRAM_SYNC_ENABLED=true (or use offline-first profile)

# Error: project not found in pull
# Fix: Enroll the project first with POST /sync/enroll
```

---

➜ **Next**: [📖 API Reference](API-REFERENCE.md) for all endpoints  
➜ **Next**: [🤖 Agent Protocol](AGENT-PROTOCOL.md) for how AI agents use it  
➜ **Next**: [📖 Full Sync Setup](SYNC-SETUP.md) for advanced configuration
