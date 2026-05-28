# MCP Configuration — engram-dotnet

> Configuración del servidor MCP `engram mcp` para **cualquier cliente** compatible (Cursor, Claude Desktop, OpenCode, VS Code, etc.).

---

## Inicio rápido (recomendado)

Tras clonar el repositorio:

| SO | Comando |
|----|---------|
| Windows | `.\scripts\setup.ps1` |
| Linux / macOS | `./scripts/setup.sh` |

El wizard pregunta **solo local** vs **offline-first sync**, compila el binario y escribe `mcp.json` en el editor elegido.

Guía completa: [SETUP-WIZARD.md](SETUP-WIZARD.md)  
**Varios editores:** [`config/mcp/INSTALL.md`](../config/mcp/INSTALL.md)  
Plantillas: [`config/mcp/editors/`](../config/mcp/editors/)

---

## Tres modos de uso

| Modo | Variables clave | Comportamiento |
|------|-----------------|----------------|
| **Solo local** | `ENGRAM_DATA_DIR`, `ENGRAM_SYNC_ENABLED=false` | SQLite en tu máquina. Sin sync. |
| **Solo remoto** | `ENGRAM_URL` | El MCP habla directo al servidor HTTP (sin SQLite local). |
| **Local + sync** (equipos) | `ENGRAM_DATA_DIR` + `ENGRAM_SERVER_URL` + `ENGRAM_SYNC_ENABLED=true` | SQLite local + push/pull al servidor en background. |

> **Importante**: Para sync offline-first usá `ENGRAM_SERVER_URL`, **no** `ENGRAM_URL`.  
> `ENGRAM_URL` activa modo remoto puro (HttpStore) y desactiva el journal local de sync.

---

## Bloque MCP estándar (agnóstico de editor)

El cliente solo necesita lanzar `engram mcp` por stdio:

```json
{
  "command": "engram",
  "args": ["mcp"],
  "env": {
    "ENGRAM_DATA_DIR": "~/.engram",
    "ENGRAM_USER": "tu.email@ejemplo.com",
    "ENGRAM_SYNC_ENABLED": "true",
    "ENGRAM_SERVER_URL": "http://192.168.0.178:7437"
  }
}
```

Envolvé ese bloque en la clave que use tu editor, por ejemplo:

```json
{
  "mcpServers": {
    "engram": {
      "type": "stdio",
      "...": "mismo bloque de arriba"
    }
  }
}
```

---

## Dónde guardar el JSON (por editor)

| Cliente | Archivo | Clave raíz |
|---------|---------|------------|
| **Cursor** | `~/.cursor/mcp.json` | `mcpServers` |
| **Claude Desktop** | `~/Library/Application Support/Claude/claude_desktop_config.json` (macOS) o `%APPDATA%\Claude\` (Windows) | `mcpServers` |
| **OpenCode** | Ver documentación del proyecto | `mcpServers` |
| **VS Code** (ext. MCP) | Según extensión | `servers` o `mcpServers` |

Ejemplos de referencia en el repo (copiar y ajustar):

- [`config/cursor/mcp.json`](../config/cursor/mcp.json)
- [`config/vscode/mcp.json`](../config/vscode/mcp.json)

---

## Desarrollo desde el repo (`dotnet run`)

Mientras modificás el código, podés apuntar el `command` a:

```json
{
  "command": "dotnet",
  "args": [
    "run",
    "--project",
    "RUTA_AL_REPO/src/Engram.Cli/Engram.Cli.csproj",
    "--",
    "mcp"
  ]
}
```

En Windows, rutas con espacios en `dotnet.exe` deben ir entre comillas al probar en terminal.

---

## Producción (`engram` en PATH)

Tras `dotnet publish` o descargar el release:

```json
{
  "command": "engram",
  "args": ["mcp"]
}
```

O ruta absoluta al `.exe` / binario publicado en `dist/`.

---

## Variables de entorno

| Variable | Obligatoria | Descripción |
|----------|-------------|-------------|
| `ENGRAM_DATA_DIR` | No | Directorio local SQLite (default: `~/.engram`) |
| `ENGRAM_URL` | No | Modo remoto puro (HttpStore) |
| `ENGRAM_SERVER_URL` | No* | Servidor para **sync** (push/pull) |
| `ENGRAM_SYNC_ENABLED` | No | `false` desactiva SyncManager; default efectivo `true` en SQLite |
| `ENGRAM_USER` | Sí (equipos) | Identidad para aislamiento multi-usuario |
| `ENGRAM_PROJECT` | No | Override de proyecto (normalmente se auto-detecta) |

---

## Verificar

```bash
curl http://TU_SERVIDOR:7437/health
# → {"status":"ok","service":"engram",...}
```

Probar MCP en terminal (debe quedar esperando, sin salir):

```powershell
$env:ENGRAM_DATA_DIR = "$env:USERPROFILE\.engram"
$env:ENGRAM_USER = "tu@ejemplo.com"
$env:ENGRAM_SYNC_ENABLED = "true"
$env:ENGRAM_SERVER_URL = "http://192.168.0.178:7437"
& "RUTA\engram.exe" mcp
```

---

## Troubleshooting

1. **Check rojo en Cursor** — Probá el comando manual arriba. Si crashea, revisá que no mezcles `ENGRAM_URL` con modo sync.
2. **Sync sin push** — Proyecto no enrollado: [SYNC-SETUP.md](SYNC-SETUP.md).
3. **`.exe` bloqueado al publicar** — Cerrá el editor (MCP puede tener el archivo abierto) y publicá a otra carpeta (`dist/win-x64-fixed`).

---

## Referencias

- [SETUP-WIZARD.md](SETUP-WIZARD.md) — paso a paso post-clone  
- [OFFLINE-FIRST-SYNC.md](OFFLINE-FIRST-SYNC.md) — arquitectura  
- [SYNC-SETUP.md](SYNC-SETUP.md) — servidor y enroll
