# Instalación MCP — varios editores desde un solo lugar

Esta carpeta es la **fuente única** para configurar Engram en cualquier cliente MCP. No estás limitado a un IDE: podés usar Cursor hoy, OpenCode mañana, Claude Desktop otro día — **misma memoria**, mismas variables.

---

## Inicio rápido (recomendado)

Desde la raíz del repo:

```powershell
# Windows
.\scripts\setup.ps1
```

```bash
# Linux / macOS
./scripts/setup.sh
```

Elegí **generar todas las configuraciones** → el wizard escribe en `config/mcp/generated/` un archivo **por editor**, listo para copiar.

También guarda referencia en `%USERPROFILE%\.engram\mcp.config.json` (o `~/.engram/mcp.config.json`).

---

## Estructura de esta carpeta

```
config/mcp/
├── INSTALL.md              ← esta guía
├── README.md               ← índice corto
├── engram.local.json       ← bloque servidor (solo local)
├── engram.offline-sync.json← bloque servidor (local + sync)
├── editors/                ← plantillas estáticas (placeholders)
│   ├── cursor.mcp.json
│   ├── claude-desktop.mcp.json
│   ├── vscode.mcp.json
│   ├── opencode.mcp.json
│   └── antigravity.notes.md
└── generated/              ← salida del wizard (tus rutas reales; en .gitignore)
    ├── README.md
    ├── cursor.mcp.json
    ├── claude-desktop.mcp.json
    └── ...
```

---

## ¿Un solo IDE o varios?

| Pregunta | Respuesta |
|----------|-----------|
| ¿Solo puedo elegir un editor? | **No.** Cada app tiene su propio archivo JSON. |
| ¿Comparten memorias? | **Sí**, si usan el mismo `ENGRAM_DATA_DIR` y `ENGRAM_USER`. Sin `ENGRAM_USER` en equipos, el SO puede asignar la misma identidad a varias personas — ver [SYNC-SETUP.md](../../docs/SYNC-SETUP.md). |
| ¿Debo reconfigurar Engram al cambiar de IDE? | **No.** Copiás el JSON generado al editor nuevo (o volvés a correr el wizard). |

---

## Dónde copiar cada archivo generado

| Editor | Archivo destino (Windows) | Archivo destino (Linux/macOS) |
|--------|---------------------------|-------------------------------|
| **Cursor** | `%USERPROFILE%\.cursor\mcp.json` | `~/.cursor/mcp.json` |
| **Claude Desktop** | `%APPDATA%\Claude\claude_desktop_config.json` | `~/Library/Application Support/Claude/claude_desktop_config.json` |
| **VS Code** (ext. MCP) | Según extensión; a menudo `.vscode/mcp.json` en el proyecto | Igual |
| **OpenCode** | Ver [opencode.ai](https://opencode.ai) — suele ser `~/.config/opencode/opencode.json` (clave `mcp`) | `~/.config/opencode/opencode.json` |
| **Antigravity** | Ver notas en [`editors/antigravity.notes.md`](editors/antigravity.notes.md) | Igual |

Después de copiar: **reiniciá o recargá** el editor (Cursor: `Developer: Reload Window`).

---

## Modos: qué plantilla usar

| Si querés… | Plantilla / wizard |
|------------|-------------------|
| Solo SQLite en tu PC | Modo **1** local → `engram.local.json` |
| SQLite + servidor del equipo (sync) | Modo **2** offline-sync → `engram.offline-sync.json` |
| Todo directo al servidor (sin SQLite local) | No uses estas plantillas; usá `ENGRAM_URL` en [MCP-CONFIG.md](../../docs/MCP-CONFIG.md) |

---

## OpenRouter, modelos y MCP (no confundir)

| Capa | Qué configurás | Dónde |
|------|----------------|-------|
| **Modelo LLM** (OpenRouter, OpenAI, etc.) | API key, modelo, proveedor | Config del **editor** (OpenCode, Cursor, …) |
| **Memoria Engram** | `engram mcp` | JSON MCP del **editor** (esta guía) |

OpenRouter **no reemplaza** la config de Engram. Configurás el modelo en OpenCode (o el IDE que uses) **y**, aparte, el bloque `engram` en MCP.

---

## Instalación manual (sin wizard)

1. Elegí plantilla en `engram.local.json` o `engram.offline-sync.json`.
2. Reemplazá placeholders (`{{ENGRAM_COMMAND}}`, etc.).
3. Insertá el bloque en el JSON del editor (ver tabla arriba).
4. O copiá un archivo completo desde `editors/` y ajustá rutas.

---

## Verificar

```bash
curl http://TU_SERVIDOR:7437/health
```

Probar MCP (debe quedar en espera, sin cerrarse):

```powershell
& "RUTA\A\engram.exe" mcp
```

---

## Más documentación

- [docs/SETUP-WIZARD.md](../../docs/SETUP-WIZARD.md) — flujo completo post-clone
- [docs/MCP-CONFIG.md](../../docs/MCP-CONFIG.md) — variables y troubleshooting
- [docs/SYNC-SETUP.md](../../docs/SYNC-SETUP.md) — enroll de proyectos en modo sync
