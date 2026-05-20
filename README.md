# engram-dotnet

> **engram** `/ˈen.ɡræm/` — *neurociencia*: la huella física de un recuerdo en el cerebro.

Memoria persistente para agentes de IA. Fork a **.NET 10 C#** del proyecto original [engram](https://github.com/Gentleman-Programming/engram).

Compatible con Claude Code, OpenCode, Gemini CLI, Cursor, Codex — solo se cambia `ENGRAM_URL`.

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
ENGRAM_SYNC_ENABLED=true ENGRAM_SYNC_TARGET_KEY=cloud ./engram serve
```

> **Resultado**: Sync bidireccional, trabajo offline, enroll de proyectos, pause/resume admin.

---

## ⚡ Features

| Feature | Estado | Docs |
|---------|--------|------|
| **REST API** (30 endpoints) | ✅ Complete | [API Reference](docs/API-REFERENCE.md) |
| **MCP Server** (19 tools) | ✅ Complete | — |
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

---

## 🙏 Créditos

Este proyecto es un port a .NET 10 C# del original [engram](https://github.com/Gentleman-Programming/engram) por [Alan Buscaglia](https://github.com/Gentleman-Programming). **Todo el mérito del diseño y la idea pertenece al proyecto original.** Licencia MIT.

---

## 🚀 Siguiente

➜ **[docs/01-QUICK-START.md](docs/01-QUICK-START.md)** — Elegí tu perfil y empezá.
