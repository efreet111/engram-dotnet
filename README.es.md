# engram-dotnet

[![CI](https://github.com/efreet111/engram-dotnet/actions/workflows/ci.yml/badge.svg)](https://github.com/efreet111/engram-dotnet/actions/workflows/ci.yml)

> **engram** `/ˈen.ɡræm/` — *neurociencia*: la huella física de un recuerdo en el cerebro.

Memoria persistente para agentes de IA. Una reimplementación a **.NET 10 C#** del proyecto original [engram](https://github.com/Gentleman-Programming/engram) por [Alan Buscaglia](https://github.com/Gentleman-Programming).

**¿Por qué .NET 10?** Tipos fuertes, rendimiento nativo (AOT-ready), facilidad de despliegue en entornos enterprise Windows/Linux, y un ecosistema maduro para equipos que ya usan .NET. Misma API que el original — solo cambia `ENGRAM_URL`.

Compatible con Claude Code, OpenCode, Gemini CLI, Cursor, Codex.

---

## 🛠️ Filosofía de Diseño: Pragmático y Simple

Este proyecto rechaza intencionalmente el over-engineering corporativo.

* **No MediatR.** Sin pipelines mágicos ocultos ni message buses basados en reflection.
* **No CQRS.** Sin separación artificial de lecturas y escrituras cuando un estado simple con buenos índices es suficiente.
* **No Clean Architecture.** Sin capas interminables de cebolla ni interfaces de mapeo repetitivas.

**Solo C# 10 compilado, Minimal APIs, e Inyección de Dependencias.** Construido para rendimiento bruto, rutas de ejecución explícitas, y cero boilerplate.

---

## 🏗️ Arquitectura

```
AGENTE DE IA                   ENGRAM-DOTNET                PERSISTENCIA
(Claude/OpenCode/Cursor)                                    
       │                                                     
       ├── MCP stdio ──► engram mcp ───────►┐               
       │                                     │               
       │                          ┌──────────┴──────────┐    
       │                          │  EngramServer (.NET) │    
       └── HTTP REST ────────────►│  30 endpoints REST   │    
                                  │  24 herramientas MCP │    
                                  └──────────┬──────────┘    
                                             │                
                          ┌──────────────────┼──────────────────┐
                          ▼                  ▼                  ▼
                    ┌──────────┐      ┌────────────┐     ┌──────────┐
                    │ SQLite   │      │ PostgreSQL │     │ Servidor │
                    │ Local    │◄────►│ Remoto     │     │ Remoto   │
                    │ (default)│ Sync │ (equipo)   │     │ (HttpStore)
                    └──────────┘      └────────────┘     └──────────┘
```

**Nota sobre arquitectura**: Engram-dotnet usa un **Strategy Pattern** simple (interfaz `IStore` con implementaciones `SqliteStore`, `PostgresStore`, `HttpStore`). **No usa MediatR, CQRS, ni Clean Architecture** — solo minimal APIs + inyección de dependencias. La complejidad está en las features, no en el framework.

### ¿Cómo se ve una "memoria"?

```json
{
  "id": 1, "title": "Decisión: usar PostgreSQL", 
  "content": "**What**: ... **Why**: ... **Where**: ... **Learned**: ...",
  "type": "decision", "project": "team/mi-api", "scope": "team",
  "topic_key": "architecture/db-choice", "created_at": "2026-05-20T..."
}
```

Eso guarda el agente cuando llama `mem_save`. Después lo busca con `mem_search`. Simple.

---

## 👤 ¿Quién sos?

Elegí tu perfil y seguí las instrucciones correspondientes:

### 🧑 Solo Developer
[➜ Guía rápida para usar engram solo](docs/01-QUICK-START.md#-solo-developer)

```bash
# Lo mínimo para arrancar
git clone https://github.com/efreet111/engram-dotnet
dotnet publish src/Engram.Cli -c Release -r linux-x64 --self-contained -o dist/
./dist/engram serve
```

> **Resultado**: Servidor local con SQLite, listo para conectar tu agente.

### 👥 Team Leader (2-5 personas)
[➜ Guía rápida para equipo con servidor compartido](docs/01-QUICK-START.md#-team-leader)

```bash
# Servidor centralizado + multi-user isolation
ENGRAM_DB_TYPE=postgres ENGRAM_PG_CONNECTION="..." ./engram serve
```

> **Resultado**: Servidor compartido, cada dev con su identidad (`ENGRAM_USER`), memorias aisladas.

### 🏢 IT Admin (5-20 personas)
[➜ Guía rápida para equipo completo con sync offline-first](docs/01-QUICK-START.md#-it-admin)

```bash
# PostgreSQL + offline-first sync + enrollment
ENGRAM_SYNC_ENABLED=true ENGRAM_SYNC_TARGET=cloud ./engram serve
```

> **Resultado**: Sync bidireccional, trabajo offline, enroll de proyectos, pause/resume admin.

---

## ⚡ Features

| Feature | Estado | Docs |
|---------|--------|------|
| **REST API** (41 endpoints) | ✅ Complete | [API Reference](docs/API-REFERENCE.md) |
| **MCP Server** (28 tools) | ✅ Complete | [MCP Config](docs/MCP-CONFIG.md) |
| **Offline-First Sync** | ✅ Complete (4 phases) | [Sync Setup](docs/SYNC-SETUP.md) |
| **Multi-User Isolation** | ✅ RFC-002 | [Multi-User](docs/MULTI-USER.md) |
| **TTL Configurable** | ✅ Archived | — |
| **Doctor Diagnostic** | ✅ Archived | — |
| **Obsidian Export** | ✅ Complete | — |

---

## 📚 Documentación

| Documento | Para quién |
|-----------|-----------|
| [📖 Guía Rápida por Persona](docs/01-QUICK-START.md) | Todos |
| [📖 API Reference](docs/API-REFERENCE.md) | Humanos (curl, parámetros, respuestas) |
| [🤖 Agent Protocol](docs/AGENT-PROTOCOL.md) | Agentes IA (tools, scope, sync) |
| [📖 Sync Setup](docs/SYNC-SETUP.md) | SysAdmins (PostgreSQL, env vars) |
| [📖 Multi-User](docs/MULTI-USER.md) | Team leads (identidad, isolation) |
| [📖 MCP Config](docs/MCP-CONFIG.md) | Configuración MCP (cualquier cliente) |
| [📖 Setup Wizard](docs/SETUP-WIZARD.md) | Clonar repo → local o sync (`scripts/setup.ps1`) |
| [📖 Backlog](docs/BACKLOG.md) | Cola de trabajo ordenada (ENG-xxx) — qué hacer ahora |
| [📖 Git workflow](docs/GIT-WORKFLOW.md) | Ramas, PRs, commits y releases (tags `v*`) |

---

## 🙏 Créditos

Este proyecto es un port a .NET 10 C# del original [engram](https://github.com/Gentleman-Programming/engram) por [Alan Buscaglia](https://github.com/Gentleman-Programming). **Todo el mérito del diseño y la idea pertenece al proyecto original.** Licencia MIT.

---

## 🤝 Contribuir

| Documento | Propósito |
|-----------|-----------|
| [CONTRIBUTING.md](CONTRIBUTING.md) | Cómo contribuir, compilar y enviar PRs |
| [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md) | Estándares de la comunidad |
| [SECURITY.md](SECURITY.md) | Reportar vulnerabilidades |
| [CONTRIBUTORS.md](CONTRIBUTORS.md) | Quiénes construyen esto |

---

## 🚀 Siguiente

➜ **[docs/01-QUICK-START.md](docs/01-QUICK-START.md)** — Elegí tu perfil y empezá.
