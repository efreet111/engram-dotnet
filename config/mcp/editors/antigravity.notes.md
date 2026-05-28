# Antigravity — notas MCP

Google Antigravity (y otros IDEs nuevos) suelen exponer MCP con formato similar a Cursor (`mcpServers` + stdio).

## Qué hacer

1. Corré el wizard: `.\scripts\setup.ps1` → generar en `config/mcp/generated/`.
2. Abrí la config MCP de Antigravity (menú Settings / MCP del producto).
3. Copiá el contenido de **`generated/cursor.mcp.json`** si usa la clave `mcpServers`.
4. Si la UI pide solo el bloque interno, copiá solo el objeto bajo `mcpServers.engram` desde `generated/engram.server.json` (si existe).

## Misma memoria que otros editores

Usá el mismo:

- `ENGRAM_DATA_DIR`
- `ENGRAM_USER`
- `ENGRAM_SERVER_URL` (si sync)

Así las memorias son las mismas al cambiar entre Antigravity, Cursor u OpenCode.
