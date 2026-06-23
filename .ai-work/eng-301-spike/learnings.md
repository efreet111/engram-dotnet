# ENG-301 Spike — Installer landscape exploration

**Date:** 2026-06-22
**Status:** ✅ SUFFICIENT INFO GATHERED

---

## ¿Qué existe HOY?

### 1. Pipeline de release (`.github/workflows/release.yml`)

Self-contained binaries ya se publican para `linux-x64` y `win-x64` al pushear un tag `v*`. **No requiere .NET instalado**. Los assets se suben a GitHub Releases con instrucciones de descarga curl.

### 2. Scripts de setup (existentes)

| Script | Plataforma | Qué hace |
|--------|-----------|----------|
| `scripts/setup.sh` | Linux/macOS | Wizard CLI: modo (local/sync), user, data dir, compila, genera MCP configs para 5 editores, opcional instalar en Cursor |
| `scripts/setup.ps1` | Windows | Idéntico al de Linux, PowerShell nativo |

Ambos ya cubren el flujo core. El instalador debe **mejorar**, no reescribir.

### 3. Configuración MCP (5 editores soportados)

| Editor | Path destino (Win) | Path destino (Linux) |
|--------|-------------------|---------------------|
| Cursor | `%USERPROFILE%\.cursor\mcp.json` | `~/.cursor/mcp.json` |
| Claude Desktop | `%APPDATA%\Claude\claude_desktop_config.json` | `~/Library/Application Support/Claude/...` |
| VS Code | `.vscode/mcp.json` (workspace) | Igual |
| OpenCode | `~/.config/opencode/opencode.json` | Igual |
| Antigravity | (notas especiales) | Igual |

### 4. Documentación existente

- `config/mcp/INSTALL.md` — fuente única, explica estructura y dónde copiar
- `docs/SETUP-WIZARD.md` — flujo completo post-clone
- `docs/SYNC-SETUP.md` — enroll de proyectos en sync mode
- `docs/POSTGRES-SETUP.md` — backend Postgres (opcional)

---

## Lo que FALTA (gap real)

### 🔴 Critical

1. **No hay binarios pre-construidos accesibles al usuario final**. Hoy hay que clonar el repo, instalar .NET SDK, y ejecutar scripts de build. Eso es exactamente lo que ENG-301 debe resolver.
2. **El wizard actual requiere repo clonado**. El usuario final no quiere clonar — quiere descargar un binario y correrlo.

### 🟡 Importante

3. **No hay release vX.Y.Z publicado todavía** (no hay tags en el repo según el release.yml que se activa con tags). El pipeline existe pero nunca se ejecutó.
4. **El wizard solo soporta compilar desde fuente**. No soporta "ya tengo el binario en PATH, solo config".
5. **No hay uninstall / cleanup**.

### 🟢 Nice-to-have

6. Instalación en PATH (no solo en `dist/`)
7. GUI wizard (ENG-302 — feature separada, depende de ENG-301)
8. Auto-update check
9. Homebrew/Scoop/Chocolatey taps
10. Paquetes DEB/RPM/MSI nativos

---

## Diseño propuesto (alto nivel)

### Stack Installer — un binario, tres componentes

**Comando**: `flowforge install` (o `flowforge setup`)

El installer es un único binario/wizard que pregunta qué instalar:

```
┌─────────────────────────────────────────────────────────────┐
│  FlowForge Stack Installer v0.1.0                           │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  ¿Qué componentes querés instalar? (multi-select)          │
│                                                             │
│  [✓] engram-dotnet (backend de memoria persistente)         │
│  [ ] FlowForge (skills + agents para IDEs)                  │
│  [ ] FlowDocs (template docs/ en proyecto actual)           │
│                                                             │
│  Modo de uso de engram: (si engram-dotnet seleccionado)      │
│  [1] Solo local (SQLite)                                   │
│  [2] Local + sync con servidor                              │
│                                                             │
│  ¿Usar estructura FlowDoc? (si FlowDocs seleccionado)       │
│  [✓] Sí  [ ] No (usar mi estructura custom)                  │
│                                                             │
│  ¿Dónde instalar los skills de FlowForge?                   │
│  [✓] OpenCode  [✓] Cursor  [ ] Antigravity  [ ] VS Code    │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

### Estructura del installer (en FlowForge repo)

```
FlowForge/
├── install/
│   ├── install.sh           # Linux/macOS installer
│   ├── install.ps1          # Windows installer
│   ├── uninstall.sh         # Linux/macOS uninstall
│   ├── uninstall.ps1        # Windows uninstall
│   ├── lib/
│   │   ├── common.sh        # Shared functions
│   │   ├── flowdoc.sh       # FlowDoc template installer
│   │   ├── engram.sh        # engram-dotnet binary + MCP config
│   │   └── flowforge.sh     # IDE skills installer
│   └── README.md
├── docs/
│   └── INSTALLER.md         # Public-facing docs
```

### Distribución

El installer se distribuye vía:
- **GitHub Releases** de FlowForge (binarios self-contained para linux-x64, win-x64)
- **Curl-pipe** para instalación sin descarga previa:
  ```bash
  curl -fsSL https://flowforge.dev/install.sh | bash
  iwr -useb https://flowforge.dev/install.ps1 | iex
  ```
- **Package managers** (futuro, no en v1): brew, scoop, choco, apt

---

## Recomendación

**Opción A (script installer) primero** — es lo que el usuario final necesita AHORA:
- Cero fricción: curl + bash o iwr + ps1
- Funciona en Windows + Linux con el mismo flujo
- No requiere code signing certificates
- Construye sobre lo que ya existe (scripts/setup.sh + setup.ps1)

**Opción B/C después del launch** — para cuando tengamos usuarios empresariales.

---

## Effort re-estimate (stack installer)

| Tarea | Effort |
|-------|--------|
| Installer framework (lib/, common functions) | M |
| `install.sh` — Linux/macOS (multi-component wizard) | M |
| `install.ps1` — Windows (multi-component wizard) | M |
| engram-dotnet installer module (binario + PATH + MCP config) | S |
| FlowForge installer module (IDE skills placement) | M |
| FlowDocs installer module (template docs/ + AGENTS.md toggle) | M |
| Uninstall scripts | S |
| Curl-pipe URLs (flowforge.dev/install.sh) | S |
| Primer release FlowForge `v0.1.0` | S |
| Documentación pública (INSTALLER.md) | S |
| Smoke test end-to-end | S |
| **Total** | **L** (ahora sí, porque son 3 installers en uno) |

---

## Decisiones pendientes (para spec)

1. **PATH location**: `~/.local/bin` (Linux/macOS XDG) vs `/usr/local/bin` (requiere sudo)?
2. **Auto-config**: ¿el installer corre el wizard automáticamente o solo instala y el usuario corre `engram setup` después?
3. **Versioning**: ¿`v0.1.0` o `v1.0.0` para el primer release público?
4. **Backwards compat**: ¿los binarios anteriores siguen funcionando o es breaking?
5. **Pre-releases**: ¿usamos semver pre-release tags (`v0.1.0-beta.1`)?

---

## Decisiones del usuario (2026-06-22)

| # | Pregunta | Decisión |
|---|----------|----------|
| 1 | ¿Installer único o 3 separados? | **1 único** que pregunta qué instalar |
| 2 | ¿En qué repo vive? | **FlowForge** — accesible vía `apt install flowforge`-style (curl-pipe o paquete nativo) |
| 3 | ¿FlowDoc opt-in? | **Config file** + pregunta en installer + opción en AGENTS.md (default ON para nuevos) |
| 4 | ¿Modo "server" como Service? | **No** — solo instrucciones para Docker/manual |
| 5 | ¿Uninstall desde el inicio? | **Sí** |

## Próximo paso

Abrir ENG-301 como feature real con flow completo:
- **forge-arch**: spec con las 5 decisiones respondidas
- **forge-plan**: checklist de ~5-7 items
- **forge-dev**: implementar install.sh, install.ps1, primer tag
- **forge-verify**: smoke test end-to-end