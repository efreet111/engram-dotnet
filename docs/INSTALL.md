# Installation Guide — engram-dotnet

> **Single entry point** for all installation methods. Choose your path based on your needs.

---

## 🚀 Quick Decision

| You want to... | Use this method |
|----------------|-----------------|
| **Try it in 5 minutes** | [FlowForge Installer](#method-1-flowforge-installer-recommended) |
| **Install from source** | [Build from Git](#method-2-build-from-git) |
| **Run in Docker** | [Docker](#method-3-docker) |
| **Configure MCP for your IDE** | [MCP Setup](#4-mcp-setup) |

---

## Method 1: FlowForge Installer (Recommended)

**Best for**: Most users. One command installs everything.

```bash
# Linux / macOS
curl -fsSL https://raw.githubusercontent.com/efreet111/FlowForge/main/install/install.sh | bash

# Windows (PowerShell)
irm https://raw.githubusercontent.com/efreet111/FlowForge/main/install/install.ps1 | iex
```

**What it does**:
- Downloads the latest engram-dotnet binary
- Installs FlowForge skills for your IDE
- Configures MCP automatically
- Sets up `~/.engram/` data directory

**Requirements**:
- Linux x64, macOS, or Windows x64
- No .NET SDK required (binary is self-contained)

**Next**: Skip to [MCP Setup](#4-mcp-setup) to configure your IDE.

---

## Method 2: Build from Git

**Best for**: Developers who want the latest code or want to contribute.

### Prerequisites

- **.NET 10 SDK** — [Download](https://dotnet.microsoft.com/download/dotnet/10.0)
- **Git**

### Steps

```bash
# 1. Clone the repository
git clone https://github.com/efreet111/engram-dotnet.git
cd engram-dotnet

# 2. Build the binary
dotnet publish src/Engram.Cli -c Release -r linux-x64 --self-contained -o dist/

# 3. Verify
./dist/engram version
# → engram 1.3.0

# 4. Start the server
./dist/engram serve
```

**Platform-specific builds**:
- Linux: `-r linux-x64`
- macOS: `-r osx-x64`
- Windows: `-r win-x64`

### Run the Setup Wizard

After building, configure MCP for your IDE:

```bash
# Linux / macOS
./scripts/setup.sh

# Windows (PowerShell)
.\scripts\setup.ps1
```

The wizard will:
- Ask for your mode (local or sync)
- Configure your IDE (Cursor, VS Code, OpenCode, etc.)
- Generate MCP config files

**Next**: Skip to [Verify Installation](#5-verify-installation).

---

## Method 3: Docker

**Best for**: Teams running a shared server, or isolated environments.

### Quick Start

```bash
# Pull the image
docker pull ghcr.io/efreet111/engram-dotnet:latest

# Run with SQLite (local mode)
docker run -d \
  --name engram \
  -p 7437:7437 \
  -v engram-data:/data \
  ghcr.io/efreet111/engram-dotnet:latest

# Verify
curl http://localhost:7437/health
```

### Docker Compose (with PostgreSQL)

For teams, use the provided `docker-compose.yml`:

```bash
cd docker/
cp .env.example .env
# Edit .env with your PostgreSQL credentials
docker compose up -d
```

**See also**: [Docker README](../docker/README.md) for advanced configuration.

---

## 4. MCP Setup

After installation, configure your IDE to use Engram.

### Automatic (Recommended)

Run the setup wizard:

```bash
# Linux / macOS
./scripts/setup.sh

# Windows (PowerShell)
.\scripts\setup.ps1
```

### Manual Configuration

Add to your IDE's MCP config:

**OpenCode** (`~/.config/opencode/opencode.json`):
```json
{
  "mcpServers": {
    "engram": {
      "command": "engram",
      "args": ["mcp"],
      "env": {
        "ENGRAM_DATA_DIR": "~/.engram",
        "ENGRAM_USER": "your-username"
      }
    }
  }
}
```

**Cursor** (`~/.cursor/mcp.json`):
```json
{
  "mcpServers": {
    "engram": {
      "command": "engram",
      "args": ["mcp"],
      "env": {
        "ENGRAM_DATA_DIR": "~/.engram",
        "ENGRAM_USER": "your-username"
      }
    }
  }
}
```

**VS Code** (`.vscode/mcp.json` in your project):
```json
{
  "servers": {
    "engram": {
      "command": "engram",
      "args": ["mcp"],
      "env": {
        "ENGRAM_DATA_DIR": "~/.engram",
        "ENGRAM_USER": "your-username"
      }
    }
  }
}
```

### Deployment Profiles

Instead of setting 10+ variables manually, use `ENGRAM_PROFILE` to pick your deployment mode in one line:

| Profile | Who it's for | Backend | Sync |
|---------|-------------|---------|------|
| `local` (default) | Solo developer | SQLite | ❌ |
| `remote-server` | Small team, shared DB | PostgreSQL | ❌ |
| `offline-first` | Large team, offline-first | SQLite (local) + PostgreSQL (server) | ✅ |
| `desktop` | Personal/shared workstation | PostgreSQL | ❌ |

```json
// OpenCode example — just set ENGRAM_PROFILE:
{
  "mcpServers": {
    "engram": {
      "command": "engram",
      "args": ["mcp"],
      "env": {
        "ENGRAM_PROFILE": "local",
        "ENGRAM_USER": "your-username"
      }
    }
  }
}
```

Each profile sets sensible defaults (`ENGRAM_DB_TYPE`, `ENGRAM_SYNC_ENABLED`, etc.) that you can override individually. Required vars per profile are validated at startup so you catch misconfiguration early.

### Environment Variables

| Variable | Description | Default |
|----------|-------------|---------|
| `ENGRAM_PROFILE` | Deployment profile: `local`, `remote-server`, `offline-first`, `desktop` | `local` |
| `ENGRAM_DATA_DIR` | Data directory | `~/.engram` |
| `ENGRAM_USER` | Your identity (required for `remote-server`, `offline-first`, `desktop`) | System user |
| `ENGRAM_SERVER_URL` | Server URL (required for `offline-first`) | — |
| `ENGRAM_SYNC_ENABLED` | Enable sync (auto-set by profile) | `false` |
| `ENGRAM_DB_TYPE` | Backend: `sqlite` or `postgres` (auto-set by profile) | `sqlite` |

**See also**: [MCP Configuration Guide](MCP-CONFIG.md) for all options.

---

## 5. Verify Installation

### Check the server

```bash
curl http://localhost:7437/health
# → {"status":"ok","service":"engram","version":"1.3.0","backend":"sqlite"}
```

### Check MCP connection

In your IDE, try using an Engram tool:

```
Use mem_save to save a test memory
```

Or from CLI:

```bash
engram search "test" --project default
```

### Check sync status (if enabled)

```bash
engram sync status
```

---

## 6. Next Steps

### Choose your profile

Set `ENGRAM_PROFILE` and the required variables for your use case:

| Profile | Use case | Required vars |
|---------|----------|---------------|
| **`local`** (default) | Solo developer, no sharing | *(none)* |
| **`remote-server`** | Shared server, no offline | `ENGRAM_PG_CONNECTION`, `ENGRAM_USER` |
| **`offline-first`** | Offline-first, multi-device | `ENGRAM_SERVER_URL`, `ENGRAM_USER` |
| **`desktop`** | Personal/shared workstation | `ENGRAM_PG_CONNECTION`, `ENGRAM_USER` |

### Profile: `local`

```bash
# Profile is "local" by default — no extra vars needed
./engram serve
# → SQLite backend, sync disabled
```

### Profile: `remote-server`

For shared PostgreSQL server (team leader):

```bash
# Server
ENGRAM_PROFILE=remote-server \
ENGRAM_PG_CONNECTION="Host=localhost;Database=engram;Username=engram;Password=REPLACE_ME" \
ENGRAM_USER=admin \
./engram serve

# Each developer (MCP client — uses remote URL, not profile)
export ENGRAM_URL="http://server:7437"
export ENGRAM_USER="your-username"
```

The `remote-server` profile auto-sets `ENGRAM_DB_TYPE=postgres` and `ENGRAM_SYNC_ENABLED=false`. You only need to supply the connection string and user.

**See also**: [01-QUICK-START.md](01-QUICK-START.md) for detailed team setup.

### Profile: `offline-first`

For offline-first multi-device sync (IT Admin):

```bash
# On the server (remote-server profile):
ENGRAM_PROFILE=remote-server \
ENGRAM_PG_CONNECTION="Host=localhost;Database=engram;Username=engram;Password=REPLACE_ME" \
ENGRAM_USER=admin \
./engram serve

# On each developer machine (offline-first profile):
ENGRAM_PROFILE=offline-first \
ENGRAM_SERVER_URL="http://server:7437" \
ENGRAM_USER=your-username \
./engram serve

# Enroll your project
curl -X POST http://server:7437/sync/enroll \
  -H "X-Engram-User: admin" \
  -d '{"project":"your-project"}'
```

The `offline-first` profile auto-sets `ENGRAM_DB_TYPE=sqlite`, `ENGRAM_SYNC_ENABLED=true`, `ENGRAM_SYNC_POLL_SECONDS=30`, and `ENGRAM_SYNC_TARGET=cloud`.

> **Note**: `offline-first` uses SQLite locally on each developer machine, NOT PostgreSQL. The server runs `remote-server` profile with PostgreSQL.

**See also**: [SYNC-SETUP.md](SYNC-SETUP.md) for full sync documentation.

### Profile: `desktop`

For personal use or shared workstation with PostgreSQL:

```bash
ENGRAM_PROFILE=desktop \
ENGRAM_PG_CONNECTION="Host=localhost;Database=engram;Username=engram;Password=REPLACE_ME" \
ENGRAM_USER=your-username \
./engram serve
```

The `desktop` profile auto-sets `ENGRAM_DB_TYPE=postgres` and `ENGRAM_SYNC_ENABLED=false`. Allows both local and external connections.

> **Backward compatible**: All existing env vars still work. If you don't set `ENGRAM_PROFILE`, the system behaves exactly as before — no migration needed.

---

## 7. Troubleshooting

### Common Issues

**Port already in use**
```bash
fuser -k 7437/tcp  # Linux/macOS
netstat -ano | findstr :7437  # Windows, then kill the PID
```

**SQLite library not found**
- The self-contained binary includes native libs
- Make sure you're running `./dist/engram` (not `dotnet run`)

**Permission denied**
```bash
chmod +x ./dist/engram
```

**Cannot connect to server**
- Check firewall allows port 7437
- Verify server is running: `curl http://localhost:7437/health`

### Getting Help

- [API Reference](API-REFERENCE.md) — all endpoints
- [Agent Protocol](AGENT-PROTOCOL.md) — how AI agents use Engram
- [GitHub Issues](https://github.com/efreet111/engram-dotnet/issues) — report bugs
- [Discussions](https://github.com/efreet111/engram-dotnet/discussions) — ask questions

---

## 8. Uninstallation

### FlowForge Installer

```bash
# Linux / macOS
curl -fsSL https://raw.githubusercontent.com/efreet111/FlowForge/main/install/uninstall.sh | bash

# Windows (PowerShell)
irm https://raw.githubusercontent.com/efreet111/FlowForge/main/install/uninstall.ps1 | iex
```

### Manual Removal

```bash
# Remove binary
rm -rf dist/

# Remove data (optional)
rm -rf ~/.engram/

# Remove IDE config
rm ~/.cursor/mcp.json  # Cursor
rm ~/.config/opencode/opencode.json  # OpenCode
```

---

**Version**: 1.3.0 (2026-07-06)  
**Last updated**: 2026-07-06
