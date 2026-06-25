# Plan: ENG-301 — FlowForge Stack Installer

**Spec:** [spec.md](spec.md)
**Spike:** [spike learnings](../eng-301-spike/learnings.md)
**Decisión de arquitectura:** C# AOT binary (actualiza spec que mencionaba bash/PowerShell)

---

## 1. Componentes y dependencias

| Aspecto | Detalle |
|---------|---------|
| **Repos afectados** | `FlowForge` (90% del trabajo) + `engram-dotnet` (post-install scripts) |
| **Nuevo proyecto** | `FlowForge/src/FlowForge.Installer/` — C# console, net10.0, AOT, ConsoleAppFramework + Spectre.Console (core) |
| **Distribución** | Bootstrap scripts (bash/PS1 minimalistas) + binario AOT via GitHub Releases |
| **Plataformas** | linux-x64, win-x64 |
| **Dependencias externas** | GitHub Releases API (versiones), ConsoleAppFramework (subcomandos AOT-safe), Spectre.Console core (visual output, prompts) |

### ¿Por qué C# AOT en lugar de bash/PowerShell?

| Criterio | Bash+PS1 | C# AOT |
|----------|----------|--------|
| Codebase | Dos lenguajes | **Un solo codebase** |
| Stack del proyecto | Distinto | **Mismo stack — reutiliza conocimiento** |
| Wizard interactivo | Básico | **Spectre.Console core (checkboxes, prompts) + ConsoleAppFramework (routing)** |
| Sin runtime requerido | N/A | **Self-contained, sin .NET en el host** |
| Release pipeline | Nuevo | **Reutiliza `release.yml` de engram-dotnet** |
| Error handling | Difícil | **Stacktraces, logs tipados** |

El bootstrap script (bash/PS1) solo descarga el binario y lo ejecuta — lógica mínima.

---

## 2. Estructura de archivos (actualizada desde spec)

### FlowForge repo

```
FlowForge/
├── src/
│   └── FlowForge.Installer/
│       ├── FlowForge.Installer.csproj   (AOT, net10.0, Spectre.Console)
│       ├── Program.cs                   (entry point, subcomandos)
│       ├── Commands/
│       │   ├── InstallCommand.cs        (flowforge install)
│       │   ├── UpdateCommand.cs         (flowforge update [--check])
│       │   ├── UninstallCommand.cs      (flowforge uninstall)
│       │   └── ConfigCommand.cs         (flowforge config set ...)
│       ├── Modules/
│       │   ├── EngramModule.cs          (descarga binario, PATH, MCP config)
│       │   ├── FlowForgeModule.cs       (copia skills a IDEs)
│       │   └── FlowDocModule.cs         (template docs/ + AGENTS.md toggle)
│       ├── Infrastructure/
│       │   ├── GitHubReleasesClient.cs  (download + checksum)
│       │   ├── ConfigStore.cs           (~/.engram/config.json read/write)
│       │   ├── PathHelper.cs            (rutas cross-platform)
│       │   └── Logger.cs                (~/.engram/install.log)
│       └── Models/
│           ├── InstallerConfig.cs       (config.json model)
│           └── ReleaseManifest.cs       (manifest.yaml model)
├── install/
│   ├── install.sh        (bootstrap Linux/macOS: descarga binario + ejecuta)
│   ├── install.ps1       (bootstrap Windows: descarga binario + ejecuta)
│   └── manifest.yaml     (versiones mínimas requeridas por componente)
└── .github/
    └── workflows/
        └── release.yml   (publica binarios AOT al tagear v*)
```

### engram-dotnet repo (este repo)

```
engram-dotnet/
└── scripts/
    ├── post-install.sh   (registra engram con el manifest del installer)
    └── post-install.ps1  (ídem Windows)
```

---

## 3. Contratos y modelos clave

### Config file: `~/.engram/config.json`

```json
{
  "version": "0.1.0",
  "channel": "stable",
  "auto_update": false,
  "flowdoc": {
    "enabled": true
  },
  "components": {
    "engram_dotnet": { "installed": true, "version": "1.2.0" },
    "flowforge": { "installed": true, "version": "0.1.0", "ides": ["cursor", "opencode"] }
  }
}
```

### Manifest: `FlowForge/install/manifest.yaml`

```yaml
installer_version: 0.1.0
requires:
  engram-dotnet: ">=1.2.0"
channels: [stable, beta, nightly]
default_channel: stable
```

### Subcomandos del binario

| Comando | Descripción |
|---------|-------------|
| `flowforge` | Status + versiones + notificación de update |
| `flowforge install` | Wizard interactivo multi-componente |
| `flowforge update` | Actualiza a última versión del canal |
| `flowforge update --check` | Solo verifica, no instala |
| `flowforge uninstall` | Elimina todo limpiamente |
| `flowforge config set <key> <value>` | Edita config.json |

---

## 4. Checklist de implementación

### Fase 0 — Prerequisito (engram-dotnet)

 - [x] impl: Crear `scripts/post-install.sh` — escribe versión engram en `~/.engram/config.json`
 - [x] impl: Crear `scripts/post-install.ps1` — ídem Windows
 - [ ] test: (FU-7 — deferred to v0.2.0 automated test suite) Verificar idempotencia: correr post-install dos veces no corrompe config

### Fase 1 — Proyecto FlowForge.Installer

 - [x] impl: Crear `FlowForge/src/FlowForge.Installer/FlowForge.Installer.csproj` (net10.0, AOT, Spectre.Console)
 - [x] impl: Configurar `PublishAot`, `RuntimeIdentifiers` (linux-x64, win-x64), `SelfContained`
 - [x] impl: `Program.cs` con subcomandos (install / update / uninstall / config)
 - [ ] test: (FU-7 — deferred to v0.2.0 automated test suite) `dotnet build` pasa limpio en ambas plataformas

### Fase 2 — Infraestructura core

 - [x] impl: `ConfigStore.cs` — leer/escribir `~/.engram/config.json` (crear si no existe)
 - [x] impl: `Logger.cs` — escribir a `~/.engram/install.log` con formato `[YYYY-MM-DD HH:MM:SS] [LEVEL] msg`
 - [x] impl: `PathHelper.cs` — rutas cross-platform (`~/.local/bin` vs `%LOCALAPPDATA%\Programs\FlowForge\`)
 - [x] impl: `GitHubReleasesClient.cs` — listar releases por canal, descargar binario, verificar SHA-256
 - [ ] test: (FU-7 — deferred to v0.2.0 automated test suite) Mock de GitHubReleasesClient para tests sin red

### Fase 3 — Módulo engram-dotnet

 - [x] impl: `EngramModule.cs`
  - Descarga binario self-contained desde GitHub Releases (engram-dotnet)
  - Instala en path correcto según plataforma
  - Agrega al PATH si no está
  - Escribe MCP config para IDEs seleccionados (reutiliza lógica de `setup.sh`)
  - Verifica versión mínima (manifest `requires.engram-dotnet`)
 - [ ] test: (FU-7 — deferred to v0.2.0 automated test suite) Simular descarga con binario de prueba
 - [ ] test: (FU-7 — deferred to v0.2.0 automated test suite) Verificar que MCP config se escribe correctamente en cada editor soportado

### Fase 4 — Módulo FlowForge (skills)

 - [x] impl: `FlowForgeModule.cs`
  - Copia skills a paths de IDEs seleccionados (Cursor, OpenCode, VS Code, Antigravity, Claude Desktop)
  - Paths definidos en `PathHelper`
 - [ ] test: (FU-7 — deferred to v0.2.0 automated test suite) Verificar copy a directorio mock para cada IDE

### Fase 5 — Módulo FlowDoc

 - [x] impl: `FlowDocModule.cs`
  - Lee `flowdoc.enabled` de `~/.engram/config.json`
  - Si enabled: crea `docs/` en CWD desde template embebido en el binario
  - Si `docs/` ya existe: pregunta antes de sobrescribir
  - Copia/crea `AGENTS.md` con sección FlowDoc opt-in/opt-out
 - [ ] test: (FU-7 — deferred to v0.2.0 automated test suite) Verificar que FlowDoc se salta cuando `flowdoc.enabled = false`
 - [ ] test: (FU-7 — deferred to v0.2.0 automated test suite) Verificar que AGENTS.md existente no se modifica cuando opt-out

### Fase 6 — Wizard interactivo (InstallCommand)

 - [x] impl: `InstallCommand.cs` — Spectre.Console multi-select:
  1. Multi-select componentes (engram-dotnet ✓, FlowForge ✓, FlowDocs ✓)
  2. Si engram seleccionado: modo (local SQLite / local+sync)
  3. Si FlowForge seleccionado: IDEs (multi-select: Cursor, OpenCode, VS Code, Antigravity, Claude Desktop)
  4. Si FlowDocs seleccionado: FlowDoc opt-in/out (default ON)
  5. Resumen + confirmación antes de instalar
  6. Progreso durante instalación

### Fase 7 — Update, UpdateCheck, Uninstall, Config

 - [x] impl: `UpdateCommand.cs` — detecta versión actual, consulta GitHub, descarga si hay update (con confirmación)
 - [x] impl: `UpdateCommand.cs --check` — solo muestra si hay update disponible, exit 0
 - [x] impl: `UninstallCommand.cs` — elimina binario, `~/.engram/`, skills de IDEs, MCP entries (con confirmación)
 - [x] impl: `ConfigCommand.cs` — `flowforge config set channel beta`, `flowforge config set auto_update true`, etc.
 - [x] impl: `Program.cs` (no args) — muestra status, versión, notificación de update si hay

### Fase 8 — Bootstrap scripts (thin)

 - [x] impl: `FlowForge/install/install.sh` (~30 líneas) — detecta OS/arch, descarga binario AOT desde GitHub Releases, ejecuta
 - [x] impl: `FlowForge/install/install.ps1` (~30 líneas) — ídem Windows
 - [ ] test: (FU-7 — deferred to v0.2.0 automated test suite) Verificar script en Linux limpio (Ubuntu 22.04)
 - [ ] test: (FU-7 — deferred to v0.2.0 automated test suite) Verificar script en Windows 11

### Fase 9 — Release pipeline (FlowForge)

 - [x] impl: `FlowForge/.github/workflows/release.yml` — al tagear `v*`: publica binarios linux-x64 y win-x64 con checksums SHA-256 en GitHub Releases
 - [x] impl: `FlowForge/install/manifest.yaml` con versión inicial `0.1.0`
- [ ] manual: Tagear `v0.1.0` y verificar que el release pipeline produce los assets

### Fase 10 — Verificación (smoke tests PM-1 a PM-5)

- [x] manual PM-1: Fresh install Ubuntu limpio (VM/container) — curl-pipe + all components
- [x] manual PM-2: Fresh install Windows 11 — IWR-pipe + VS Code IDE
- [ ] manual PM-3: (deferred — FU-7) Update de v0.1.0 a v0.2.0 (simular con binario viejo)
- [ ] manual PM-4: (deferred — FU-7) Uninstall completo — verificar que no queda nada
- [ ] manual PM-5: (deferred — FU-7) FlowDoc opt-out via config.json previo

---

## 5. Orden de ejecución entre repos

| Paso | Repo | Justificación |
|------|------|---------------|
| 1. post-install scripts | engram-dotnet | Prerequisito para que EngramModule sepa qué versión está instalada |
| 2. FlowForge.Installer proyecto | FlowForge | Base de todo lo demás |
| 3. Core infra (ConfigStore, Logger, Path, GH Client) | FlowForge | Bloqueante para módulos |
| 4-5. Módulos Engram + FlowForge | FlowForge | Paralelo entre sí |
| 6. Módulo FlowDoc | FlowForge | Independiente de 4-5 |
| 7. Wizard + Update/Uninstall | FlowForge | Depende de módulos |
| 8. Bootstrap scripts | FlowForge | Depende de que el binario exista |
| 9. Release pipeline | FlowForge | Último paso previo a verify |
| 10. Smoke tests | Ambos | Solo cuando release está listo |

---

## 6. Riesgos

| Riesgo | Probabilidad | Mitigación |
|--------|-------------|------------|
| Spectre.Console.Cli no es AOT-compatible | ✅ Resuelto | NO usar `Spectre.Console.Cli`. Usar `ConsoleAppFramework` para routing + `Spectre.Console` core para UI visual |
| PATH manipulation en Windows requiere restart de terminal | Alta | Documentarlo; usar `setx` + mostrar instrucción al usuario |
| get.flowforge.dev no existe todavía | Alta | Usar GitHub raw como fallback en bootstrap script |
| Tamaño del binario AOT > 50 MB (NFR) | Media | Trimming agresivo + medir en CI |

---

## 7. No en scope v1

- Auto-update background daemon
- GUI wizard (ENG-302)
- Paquetes nativos (apt/brew/scoop/winget)
- systemd / Windows Service
- Per-component uninstall
- macOS (arm64 / x64) — se agrega en v1.1 si hay demanda

---

## 8. Rollback

| Riesgo | Bajo |
|--------|------|
| Rollback | Ningún código existente se modifica — todo es archivos nuevos |
| Data loss | Ninguno — el installer no toca datos de engram existentes |
| Recovery | Borrar binario + `~/.engram/` (que es lo mismo que `flowforge uninstall`) |
