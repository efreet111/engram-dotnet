# Setup Wizard — clonar repo y configurar MCP

Guía para instalar engram-dotnet en una máquina de desarrollo y conectar cualquier cliente MCP (Cursor, Claude Desktop, OpenCode, etc.).

> **Objetivo a finales de junio**: un instalador gráfico reemplazará parte de estos pasos; hoy el wizard es `scripts/setup.ps1` / `scripts/setup.sh`.

---

## Requisitos

| Componente | Solo local | Offline-first sync |
|------------|------------|-------------------|
| .NET 10 SDK | Para compilar desde el repo | Igual |
| Cliente MCP | Cursor, Claude, etc. | Igual |
| Servidor engram | No | Sí (`http://host:7437/health` → OK) |
| `ENGRAM_USER` | Recomendado | Obligatorio en equipos |

---

## Paso 1 — Clonar y compilar

```bash
git clone https://github.com/efreet111/engram-dotnet.git
cd engram-dotnet
```

**Windows (PowerShell):**

```powershell
.\scripts\setup.ps1
```

**Linux / macOS:**

```bash
chmod +x scripts/setup.sh
./scripts/setup.sh
```

El wizard pregunta:

1. **Modo** — solo local o offline-first sync  
2. **Usuario** — `ENGRAM_USER` (tu identidad en el servidor compartido)  
3. **Datos** — carpeta SQLite (default `~/.engram`)  
4. **Servidor** — solo si elegiste sync (`ENGRAM_SERVER_URL`)  
5. **Compilar** — publica `engram` en `dist/`  
6. **Salida** — escribe **todos** los editores en [`config/mcp/generated/`](../config/mcp/generated/) (ver README ahí)  
7. **Opcional** — instalar además en Cursor o Claude con un solo clic

> **Varios IDEs:** no estás limitado a uno. Usá `generated/cursor.mcp.json` hoy y `generated/opencode.mcp.json` mañana. Guía: [`config/mcp/INSTALL.md`](../config/mcp/INSTALL.md).

---

## Paso 2 — Elegir modo

### A) Solo local

- Memorias solo en `ENGRAM_DATA_DIR/engram.db`
- No requiere servidor
- Ideal: un desarrollador, pruebas, sin red

Variables generadas:

```env
ENGRAM_DATA_DIR=~/.engram
ENGRAM_USER=tu@ejemplo.com
ENGRAM_SYNC_ENABLED=false
```

### B) Offline-first sync (equipos)

- Memorias en SQLite local (rápido, funciona offline)
- `SyncManager` empuja/pull hacia el servidor PostgreSQL
- Misma identidad en varias máquinas vía `ENGRAM_USER`

Variables generadas:

```env
ENGRAM_DATA_DIR=~/.engram
ENGRAM_USER=tu@ejemplo.com
ENGRAM_SYNC_ENABLED=true
ENGRAM_SERVER_URL=http://192.168.0.178:7437
```

> No uses `ENGRAM_URL` en este modo: activa cliente HTTP puro y **no** el journal de sync local. Ver [MCP-CONFIG.md](MCP-CONFIG.md).

---

## Paso 3 — Verificar MCP manualmente (opcional)

**Windows:**

```powershell
$env:ENGRAM_DATA_DIR = "$env:USERPROFILE\.engram"
$env:ENGRAM_USER = "tu@ejemplo.com"
$env:ENGRAM_SYNC_ENABLED = "true"
$env:ENGRAM_SERVER_URL = "http://192.168.0.178:7437"
& "E:\ruta\al\repo\dist\win-x64-fixed\engram.exe" mcp
```

Debe quedar esperando (sin crash). `Ctrl+C` para salir.

**Health del servidor:**

```bash
curl http://192.168.0.178:7437/health
```

---

## Paso 4 — Recargar el editor

| Editor | Acción |
|--------|--------|
| Cursor | `Developer: Reload Window` |
| Claude Desktop | Reiniciar la app |

En Cursor: **Settings → Features → MCP Servers** → `engram` en verde.

---

## Paso 5 — Sync: enroll del proyecto (solo modo B)

Si el push queda bloqueado por proyectos no inscritos, enrollá el proyecto en el servidor local:

```bash
curl -X POST http://localhost:7437/sync/enroll \
  -H "X-Engram-User: TU_USUARIO" \
  -H "Content-Type: application/json" \
  -d '{"project":"team/mi-proyecto"}'
```

Detalle: [SYNC-SETUP.md](SYNC-SETUP.md).

---

## Configuración manual (sin wizard)

Ver [`config/mcp/INSTALL.md`](../config/mcp/INSTALL.md) y plantillas en [`config/mcp/editors/`](../config/mcp/editors/).

---

## Roadmap del instalador

| Fase | Entregable |
|------|------------|
| **Hoy** | `scripts/setup.ps1` / `setup.sh` + plantillas en `config/mcp/` |
| **Próximo** | MSI / script que instale `engram` en PATH y ejecute el wizard |
| **Junio** | Wizard gráfico: local vs sync, test de health, enroll opcional |

---

## Referencias

- [MCP-CONFIG.md](MCP-CONFIG.md) — variables y troubleshooting  
- [01-QUICK-START.md](01-QUICK-START.md) — perfiles solo / team / admin  
- [OFFLINE-FIRST-SYNC.md](OFFLINE-FIRST-SYNC.md) — arquitectura sync
