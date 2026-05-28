# MCP — configuración centralizada

**Guía principal:** [INSTALL.md](INSTALL.md) — varios editores, modos local/sync, OpenRouter vs MCP.

## Contenido

| Ruta | Uso |
|------|-----|
| [INSTALL.md](INSTALL.md) | Instalación y copia a Cursor, OpenCode, Claude, VS Code, Antigravity |
| [editors/](editors/) | Plantillas estáticas con placeholders |
| [engram.local.json](engram.local.json) | Bloque servidor — solo local |
| [engram.offline-sync.json](engram.offline-sync.json) | Bloque servidor — local + sync |
| `generated/` | Salida del wizard (tus rutas reales; no se commitea) |

## Wizard

```powershell
.\scripts\setup.ps1    # Windows
./scripts/setup.sh     # Linux / macOS
```

Genera **todos** los JSON en `generated/` para que elijas el editor que uses hoy (y otro mañana sin reconfigurar Engram).

## Docs

- [docs/SETUP-WIZARD.md](../../docs/SETUP-WIZARD.md)
- [docs/MCP-CONFIG.md](../../docs/MCP-CONFIG.md)
