# engram-dotnet

> **engram** `/ˈen.ɡræm/` — *neuroscience*: the physical trace of a memory in the brain.

Persistent memory for AI coding agents. A **.NET 10 C#** port of the original [engram](https://github.com/Gentleman-Programming/engram) by [Alan Buscaglia](https://github.com/Gentleman-Programming).

**Why .NET 10?** Strong typing, native performance (AOT-ready), easy deployment in enterprise Windows/Linux environments, and a mature ecosystem for teams already on .NET. Same API as the original — just change `ENGRAM_URL`.

Compatible with Claude Code, OpenCode, Gemini CLI, Cursor, Codex.

---

## 🛠️ Design Philosophy: Pragmatic & Lean

This project intentionally rejects corporate over-engineering.

* **No MediatR.** No hidden magic pipelines or reflection-heavy message buses.
* **No CQRS.** No artificial separation of reads and writes when simple, well-indexed state is what matters.
* **No Clean Architecture abstractions.** No endless onion layers or boilerplate mapping interfaces.

**Just compiled C# 10, Minimal APIs, and Dependency Injection.** Built for raw performance, explicit execution paths, and zero boilerplate.

---

## 🏗️ Architecture

```
AI AGENT                       ENGRAM-DOTNET                STORAGE
(Claude/OpenCode/Cursor)                                    
       │                                                     
       ├── MCP stdio ──► engram mcp ───────►┐               
       │                                     │               
       │                          ┌──────────┴──────────┐    
       │                          │  EngramServer (.NET) │    
       └── HTTP REST ────────────►│  30 REST endpoints   │    
                                  │  24 MCP tools        │    
                                  └──────────┬──────────┘    
                                             │                
                          ┌──────────────────┼──────────────────┐
                          ▼                  ▼                  ▼
                    ┌──────────┐      ┌────────────┐     ┌──────────┐
                    │ SQLite   │      │ PostgreSQL │     │ Remote   │
                    │ Local    │◄────►│ Server     │     │ Server   │
                    │ (default)│ Sync │ (team)     │     │ (HttpStore)
                    └──────────┘      └────────────┘     └──────────┘
```

**Note on architecture**: engram-dotnet uses a **simple Strategy Pattern** (`IStore` interface with `SqliteStore`, `PostgresStore`, `HttpStore` implementations). No MediatR, no CQRS, no Clean Architecture — just minimal APIs + dependency injection. The complexity is in the features, not the framework.

### What does a "memory" look like?

```json
{
  "id": 1, "title": "Decision: use PostgreSQL", 
  "content": "**What**: ... **Why**: ... **Where**: ... **Learned**: ...",
  "type": "decision", "project": "team/mi-api", "scope": "team",
  "topic_key": "architecture/db-choice", "created_at": "2026-05-20T..."
}
```

That's what the agent saves when it calls `mem_save`. Later it finds it with `mem_search`. Simple.

---

## 👤 Who are you?

Choose your profile and follow the guide:

### 🧑 Solo Developer
[➜ Quick start for solo developer](docs/01-QUICK-START.md#-solo-developer)

```bash
# Minimum to get started
git clone https://github.com/efreet111/engram-dotnet
dotnet publish src/Engram.Cli -c Release -r linux-x64 --self-contained -o dist/
./dist/engram serve
```

> **Result**: Local SQLite server, ready to connect your agent.

### 👥 Team Leader (2-5 people)
[➜ Quick start for shared server team](docs/01-QUICK-START.md#-team-leader)

```bash
# Centralized server + multi-user isolation
ENGRAM_DB_TYPE=postgres ENGRAM_PG_CONNECTION="..." ./engram serve
```

> **Result**: Shared server, each dev has identity (`ENGRAM_USER`), isolated memories.

### 🏢 IT Admin (5-20 people)
[➜ Quick start for full offline-first sync](docs/01-QUICK-START.md#-it-admin)

```bash
# PostgreSQL + offline-first sync + enrollment
ENGRAM_SYNC_ENABLED=true ENGRAM_SYNC_TARGET=cloud ./engram serve
```

> **Result**: Bidirectional sync, offline work, project enrollment, admin pause/resume.

---

## ⚡ Features

| Feature | Status | Docs |
|---------|--------|------|
| **REST API** (41 endpoints) | ✅ Complete | [API Reference](docs/API-REFERENCE.md) |
| **MCP Server** (26 tools) | ✅ Complete | [MCP Config](docs/MCP-CONFIG.md) |
| **Offline-First Sync** | ✅ Complete (4 phases) | [Sync Setup](docs/SYNC-SETUP.md) |
| **Multi-User Isolation** | ✅ RFC-002 | [Multi-User](docs/MULTI-USER.md) |
| **TTL Configurable** | ✅ Archived | — |
| **Doctor Diagnostic** | ✅ Archived | — |
| **Obsidian Export** | ✅ Complete | — |

---

## 📚 Documentation

| Doc | Audience |
|-----|----------|
| [📖 Quick Start by Persona](docs/01-QUICK-START.md) | Everyone |
| [📖 API Reference](docs/API-REFERENCE.md) | Humans (curl, parameters, responses) |
| [🤖 Agent Protocol](docs/AGENT-PROTOCOL.md) | AI agents (tools, scope, sync) |
| [📖 Sync Setup](docs/SYNC-SETUP.md) | SysAdmins (PostgreSQL, env vars) |
| [📖 Multi-User](docs/MULTI-USER.md) | Team leads (identity, isolation) |
| [📖 MCP Config](docs/MCP-CONFIG.md) | AI agent setup (any MCP client) |
| [📖 Setup Wizard](docs/SETUP-WIZARD.md) | Clone repo → local or offline-first sync (`scripts/setup.ps1`) |
| [📖 Backlog](docs/BACKLOG.md) | Cola de trabajo ordenada (ENG-xxx) — qué hacer ahora |
| [📖 Git workflow](docs/GIT-WORKFLOW.md) | Ramas, PRs, commits y releases (`v*` tags) |

---

## 📖 Spanish version

➜ **[README.es.md](README.es.md)** — Documentación en español.

---

## 🙏 Credits

This is a .NET 10 C# port of the original [engram](https://github.com/Gentleman-Programming/engram) by [Alan Buscaglia](https://github.com/Gentleman-Programming). **All design credit belongs to the original project.** MIT License.

---

## 🚀 Next

➜ **[docs/01-QUICK-START.md](docs/01-QUICK-START.md)** — Pick your profile and start.
