# Quick Start — engram-dotnet by Persona

---

## 🧑 Solo Developer

**Goal**: Use Engram locally with SQLite, no shared server. Ideal for a single developer working with their AI agent.

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

### MCP Setup (OpenCode)

```json
{
  "mcpServers": {
    "engram": {
      "command": "engram",
      "args": ["mcp"],
      "env": {
        "ENGRAM_DATA_DIR": "~/.engram"
      }
    }
  }
}
```

### Ready 🎉

Your AI agent can now use `mem_save`, `mem_search`, `mem_context`, `mem_session_summary`, etc.

---

## 👥 Team Leader (2-5 people)

**Goal**: Shared PostgreSQL server with multi-user isolation. Each developer connects to the central server with their own identity. No offline-first sync.

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
CREATE USER engram WITH PASSWORD 'supersecret';
GRANT ALL PRIVILEGES ON DATABASE engram TO engram;
```

### 2. Build & Start Server

```bash
# On the server
git clone https://github.com/efreet111/engram-dotnet
cd engram-dotnet
dotnet publish src/Engram.Cli -c Release -r linux-x64 --self-contained -o dist/

# Start with PostgreSQL
ENGRAM_DB_TYPE=postgres \
ENGRAM_PG_CONNECTION="Host=localhost;Database=engram;Username=engram;Password=supersecret" \
./dist/engram serve
```

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
        "ENGRAM_USER": "victor.silgado"  // ← UNIQUE per developer
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

**Goal**: PostgreSQL + offline-first sync with enrollment, pause/resume, and automatic SyncManager. Developers work offline and sync when connected.

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

### Requirements

Same as Team Leader, plus:
- **SyncManager** active (requires `ENGRAM_SYNC_ENABLED=true`)
- **Firewall**: Ensure port `7437` is open between developers and server

### 1. PostgreSQL

```sql
CREATE DATABASE engram;
-- Tables are created automatically when the server starts
```

### 2. Server

```bash
ENGRAM_DB_TYPE=postgres \
ENGRAM_PG_CONNECTION="Host=localhost;Database=engram;Username=postgres;Password=NoAdmin.210725" \
ENGRAM_SYNC_ENABLED=true \
ENGRAM_SYNC_TARGET=cloud \
ENGRAM_SYNC_POLL_SECONDS=30 \
./engram serve
```

### 3. Each Developer

```json
{
  "mcpServers": {
    "engram": {
      "command": "engram",
      "args": ["mcp"],
      "env": {
        "ENGRAM_SERVER_URL": "http://192.168.0.178:7437",
        "ENGRAM_USER": "victor.silgado",
        "ENGRAM_SYNC_ENABLED": "true",
        "ENGRAM_SYNC_TARGET": "cloud",
        "ENGRAM_DATA_DIR": "~/.engram"
      }
    }
  }
}
```

### 4. Enroll Projects

```bash
curl -X POST http://192.168.0.178:7437/sync/enroll \
  -H "X-Engram-User: victor.silgado" \
  -d '{"project":"team/mi-api"}'
```

### 5. Verify Sync

```bash
# Check sync status (from CLI)
engram sync status

# Check enrolled projects
curl -H "X-Engram-User: victor" http://192.168.0.178:7437/sync/enroll

# Check general health
curl http://192.168.0.178:7437/sync/status
```

### Pause Sync (Admin)

```bash
# Pause (maintenance)
curl -X POST http://192.168.0.178:7437/sync/pause \
  -H "X-Engram-User: admin" \
  -d '{"project":"team/mi-api","reason":"DB migration"}'

# Resume
curl -X DELETE "http://192.168.0.178:7437/sync/pause?project=team/mi-api" \
  -H "X-Engram-User: admin"
```

---

## ⚙️ Mode Comparison

| Aspect | Solo Developer | Team Leader | IT Admin |
|--------|---------------|-------------|----------|
| **Backend** | Local SQLite | PostgreSQL | PostgreSQL |
| **Sync** | ❌ No | ❌ No | ✅ Offline-First |
| **Multi-User** | ❌ No | ✅ RFC-002 | ✅ RFC-002 |
| **Enrollment** | ❌ No | ❌ No | ✅ Required |
| **Pause/Resume** | ❌ No | ❌ No | ✅ Admin |
| **Offline tolerance** | N/A | ❌ (needs connection) | ✅ Unlimited |
| **Complexity** | Low | Medium | High |

---

## 🔧 Troubleshooting by Persona

### Solo Developer

```bash
# Error: Unable to load shared library 'e_sqlite3'
# Fix: The self-contained binary already includes native libs.
# Make sure to run ./dist/engram (not dotnet run)

# Error: Address already in use
# Fix: Another process is using the port.
fuser -k 7437/tcp
```

### Team Leader

```bash
# Error: 28P01 (password authentication failed)
# Fix: PostgreSQL password is wrong. Check ENGRAM_PG_CONNECTION.

# Error: 42P01 (relation does not exist)
# Fix: Tables are created automatically on startup.
# Make sure the PostgreSQL user has CREATE permissions.
```

### IT Admin

```bash
# Error: 42P10 (no unique constraint matching ON CONFLICT)
# Fix: Missing UNIQUE constraint on sync_enrolled_projects.
# The server creates it automatically in the latest version.

# Error: Sync disabled in /sync/status
# Fix: Set ENGRAM_SYNC_ENABLED=true

# Error: project not found in pull
# Fix: Enroll the project first with POST /sync/enroll
```

---

➜ **Next**: [📖 API Reference](API-REFERENCE.md) for all endpoints  
➜ **Next**: [🤖 Agent Protocol](AGENT-PROTOCOL.md) for how AI agents use it  
➜ **Next**: [📖 Full Sync Setup](SYNC-SETUP.md) for advanced configuration
