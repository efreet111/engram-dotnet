# MCP Configuration — engram-dotnet

> Cómo configurar engram-dotnet como servidor MCP en tu editor/agente.

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

```json
{
  "mcpServers": {
    "engram": {
      "type": "stdio",
      "command": "/ruta/a/engram",
      "args": ["mcp"],
      "env": {
        "ENGRAM_USER": "tu.email@ejemplo.com"
      }
    }
  }
}
```

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
| `ENGRAM_DATA_DIR` | ❌ No | Directorio local (default: `~/.engram`) |
| `ENGRAM_SERVER_URL` | ❌ No | URL del servidor remoto (sin esto = local) |
| `ENGRAM_USER` | ✅ Sí | Identidad del usuario (multi-user isolation) |
| `ENGRAM_PROJECT` | ❌ No | Proyecto por defecto para las tools |

> **Nota**: `ENGRAM_USER` es **obligatoria** para multi-user isolation. Sin ella, todas las memorias se guardan como `anonymous`.
