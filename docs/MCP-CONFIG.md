# MCP Configuration — engram-dotnet

> Cómo configurar engram-dotnet como servidor MCP en tu editor/agente.

---

## Tres modos de uso

| Modo | Variables clave | Comportamiento |
|------|-----------------|----------------|
| **Solo local** | `ENGRAM_DATA_DIR` (sin `ENGRAM_SERVER_URL`) | SQLite en tu máquina. Sin sync. |
| **Solo remoto** | `ENGRAM_URL` | El MCP habla directo al servidor HTTP (sin SQLite local). |
| **Local + sync** (recomendado para equipos) | `ENGRAM_DATA_DIR` + `ENGRAM_SERVER_URL` + `ENGRAM_SYNC_ENABLED=true` | SQLite local + push/pull al servidor en background. |

> **Importante**: Para sync offline-first usá `ENGRAM_SERVER_URL`, **no** `ENGRAM_URL`.  
> `ENGRAM_URL` activa modo remoto puro (HttpStore) y desactiva el journal local de sync.

---

## OpenCode

```json
{
  "mcpServers": {
    "engram": {
      "command": "engram",
      "args": ["mcp"],
      "env": {
        "ENGRAM_DATA_DIR": "~/.engram",
        "ENGRAM_SERVER_URL": "http://192.168.0.178:7437",
        "ENGRAM_USER": "tu.email@ejemplo.com"
      }
    }
  }
}
```

## Cursor

Archivo: `%USERPROFILE%\.cursor\mcp.json` (Windows) o `~/.cursor/mcp.json` (Linux/macOS).

### Desarrollo (Windows) — `dotnet run`

```json
{
  "mcpServers": {
    "engram": {
      "type": "stdio",
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "E:\\Proyectos\\engram-dotnet\\src\\Engram.Cli\\Engram.Cli.csproj",
        "--",
        "mcp"
      ],
      "env": {
        "ENGRAM_DATA_DIR": "C:\\Users\\TU_USUARIO\\.engram",
        "ENGRAM_USER": "tu.email@ejemplo.com",
        "ENGRAM_PROJECT": "engram-dotnet",
        "ENGRAM_SYNC_ENABLED": "true",
        "ENGRAM_SERVER_URL": "http://192.168.0.178:7437"
      }
    }
  }
}
```

### Producción — ejecutable compilado

```json
{
  "mcpServers": {
    "engram": {
      "type": "stdio",
      "command": "E:\\ruta\\engram.exe",
      "args": ["mcp"],
      "env": {
        "ENGRAM_DATA_DIR": "C:\\Users\\TU_USUARIO\\.engram",
        "ENGRAM_USER": "tu.email@ejemplo.com",
        "ENGRAM_SYNC_ENABLED": "true",
        "ENGRAM_SERVER_URL": "http://192.168.0.178:7437"
      }
    }
  }
}
```

Después de editar: **Developer: Reload Window** en Cursor.

## Claude Desktop

```json
{
  "mcpServers": {
    "engram": {
      "command": "/ruta/a/engram",
      "args": ["mcp"],
      "env": {
        "ENGRAM_USER": "tu.email@ejemplo.com"
      }
    }
  }
}
```

## GitHub Copilot / Codex

Usan el `ENGRAM_URL` standard:
```bash
export ENGRAM_URL=http://tu-servidor:7437
```

## Variables de Entorno para MCP

| Variable | Obligatoria | Descripción |
|----------|-------------|-------------|
| `ENGRAM_DATA_DIR` | ❌ No | Directorio local SQLite (default: `~/.engram`) |
| `ENGRAM_URL` | ❌ No | Modo remoto puro: todas las tools van al servidor HTTP |
| `ENGRAM_SERVER_URL` | ❌ No* | Servidor para **sync** (push/pull). Requerida en modo local+sync |
| `ENGRAM_SYNC_ENABLED` | ❌ No | `true` (default) activa SyncManager en SQLite local |
| `ENGRAM_USER` | ✅ Sí | Identidad del usuario (multi-user isolation) |
| `ENGRAM_PROJECT` | ❌ No | Proyecto por defecto para las tools |

> **Nota**: `ENGRAM_USER` es **obligatoria** para multi-user isolation. Sin ella, todas las memorias se guardan como `anonymous`.

---

## Verificar que el servidor responde

```bash
curl http://192.168.0.178:7437/health
# → {"status":"ok","service":"engram",...}
```

## Troubleshooting (Cursor en rojo)

1. **Probar el comando a mano** (PowerShell — rutas con espacios entre comillas):
   ```powershell
   $env:ENGRAM_DATA_DIR = "C:\Users\efree\.engram"
   $env:ENGRAM_USER = "efree@local.dev"
   $env:ENGRAM_SYNC_ENABLED = "true"
   $env:ENGRAM_SERVER_URL = "http://192.168.0.178:7437"
   dotnet run --project "E:\Proyectos\engram-dotnet\src\Engram.Cli\Engram.Cli.csproj" -- mcp
   ```
   Si arranca y queda esperando (sin crash), el MCP está bien; recargá Cursor.

2. **`ENGRAM_SYNC_ENABLED=true` sin SQLite local**: el proceso puede fallar al iniciar. Usá modo local+sync (sin `ENGRAM_URL`) o desactivá sync temporalmente.

3. **Ejecutable bloqueado**: si `dotnet publish` falla con *Access denied* en `engram.exe`, cerrá Cursor (el MCP puede tener el archivo abierto) y volvé a publicar.
