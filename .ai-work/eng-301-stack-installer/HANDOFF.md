# Handoff — ENG-301 Stack Installer

**Estado al 2026-06-23 ~ 16:17 (UTC-5)**  
**Rama engram-dotnet:** `feat/eng-301-post-install-scripts` (commit local `2dcbf80`, pendiente push)  
**Rama FlowForge:** `feat/eng-301-stack-installer` (commit local `96b1b6b`, pendiente push)

---

## ¿Qué está hecho?

### engram-dotnet
- [x] `scripts/post-install.sh` — registra engram en `~/.engram/config.json` (Linux/macOS, idempotente)
- [x] `scripts/post-install.ps1` — ídem Windows
- [x] `docs/architecture/adr/ADR-004-post-install-registration.md` — decisión documentada
- [x] `.ai-work/eng-301-stack-installer/plan.md` + `spec.md`
- [x] Commit local listo: `feat/eng-301-post-install-scripts`

### FlowForge
- [x] `src/FlowForge.Installer/` — proyecto C# AOT completo
  - Commands: `install`, `update [--check]`, `uninstall`, `config`, `status`
  - Modules: `EngramModule`, `FlowForgeModule`, `FlowDocModule`
  - Infrastructure: `ConfigStore`, `GitHubReleasesClient`, `ManifestClient`, `InstallerLogger`, `PathHelper`
  - Models: `InstallerConfig`, `RemoteManifest`
- [x] `install/manifest.yaml` — leído en runtime desde GitHub (no embebido en el binario)
- [x] `install/install.sh` + `install/install.ps1` — bootstrap curl-pipe
- [x] `.github/workflows/release.yml` — publica binarios AOT al tagear `v*`
- [x] `docs/architecture/adr/ADR-001` + `ADR-002`
- [x] Commit local listo: `feat/eng-301-stack-installer`

---

## Pasos pendientes (en orden)

### 1. Push de las ramas (requiere SSH / terminal local)

```powershell
# engram-dotnet
cd E:\Proyectos\engram-dotnet
git push -u origin feat/eng-301-post-install-scripts

# FlowForge
cd E:\Proyectos\FlowForge
git push -u origin feat/eng-301-stack-installer
```

### 2. Tags para activar los GitHub Actions

```powershell
# engram-dotnet → genera binario self-contained v0.3.0
cd E:\Proyectos\engram-dotnet
git tag -a v0.3.0 -m "v0.3.0: pre-release for ENG-301 stack installer testing"
git push origin v0.3.0

# FlowForge → genera flowforge-linux-x64 y flowforge-win-x64.exe (~3-5 min)
cd E:\Proyectos\FlowForge
git tag -a v0.1.0-alpha.1 -m "v0.1.0-alpha.1: initial FlowForge stack installer"
git push origin v0.1.0-alpha.1
```

### 3. Verificar GitHub Actions

- `engram-dotnet` Actions: https://github.com/efreet111/engram-dotnet/actions
- `FlowForge` Actions: https://github.com/efreet111/FlowForge/actions

Assets esperados en la release de FlowForge:
```
flowforge-linux-x64
flowforge-linux-x64.sha256
flowforge-win-x64.exe
flowforge-win-x64.exe.sha256
```

### 4. Prueba de usuario real (smoke test)

```bash
# Linux/macOS — fresh install
curl -fsSL https://raw.githubusercontent.com/efreet111/FlowForge/main/install/install.sh | bash

# Windows PowerShell
iwr -useb https://raw.githubusercontent.com/efreet111/FlowForge/main/install/install.ps1 | iex
```

Verificar:
- [ ] `flowforge` aparece en PATH
- [ ] `flowforge` (sin args) muestra status
- [ ] `flowforge install` arranca el wizard interactivo
- [ ] `flowforge update --check` detecta versiones correctamente
- [ ] `~/.engram/config.json` se crea con las entradas correctas

### 5. (Opcional) Merge de las ramas a main

Una vez pasados los smoke tests, mergear ambas ramas y crear PRs.

---

## Arquitectura — resumen de decisiones clave

| Decisión | Qué se eligió | ADR |
|----------|--------------|-----|
| Tecnología del installer | C# AOT (no bash/PS1) | FlowForge ADR-001 |
| Compatibilidad entre versiones | `manifest.yaml` leído de GitHub en runtime | FlowForge ADR-002 |
| Registro post-instalación | Scripts `post-install.sh/.ps1` en engram-dotnet | engram ADR-004 |
| CLI routing (AOT-safe) | `ConsoleAppFramework` (no `Spectre.Console.Cli`) | FlowForge ADR-001 |

## Config generada tras instalación

`~/.engram/config.json`:
```json
{
  "version": "0.1.0",
  "channel": "stable",
  "auto_update": false,
  "flowdoc": { "enabled": true },
  "components": {
    "engram_dotnet": { "installed": true, "version": "0.3.0", "binary": "..." },
    "flowforge":     { "installed": true, "version": "0.1.0-alpha.1", "ides": [...] }
  }
}
```

---

## Contexto de sesión anterior

Esta sesión se inició como [ENG-301 Stack Installer](c1c3fd69-d5b0-40a1-8cc7-0aa955eee3c6).

---

## Status update (2026-06-23 ~ evening)

**As of this update, ENG-301 has been COMPLETED in FlowForge:**

- ✅ **FlowForge:** `feat/eng-301-stack-installer` merged to `main` via merge commit `a3ff937`. Released `v0.1.0-alpha.2` with all assets on GitHub Releases. PM-1 ✅, PM-2 ✅, PM-3..5 deferred with rationale. Branch deleted (local + remote). CKP-4 closure committed at `c3b60ae`. Reference: `FlowForge/.ai-work/stack-installer/summary.md` (canonical closure doc).

- ✅ **engram-dotnet:** `feat/eng-301-post-install-scripts` (commit `2dcbf80`) — **local, NOT yet pushed**. Scripts `post-install.sh` + `post-install.ps1` were tested manually during FlowForge's PM-1 (full bootstrap install) in the user's pop-os machine.

### What's pending for engram-dotnet to fully close ENG-301

1. Push `feat/eng-301-post-install-scripts` to remote: `git push -u origin feat/eng-301-post-install-scripts`
2. Merge to `main` (directly or via PR)
3. Optionally tag a release that bundles the post-install scripts (e.g. `v1.3.0`)
4. ENG-301 already marked Done in `BACKLOG.md` and `ROADMAP.md` (this session)
5. Close this work item via orchestrator's CKP-4 flow or equivalent

### Reading guide for the future agent that closes ENG-301

1. **Start here:** `.ai-work/eng-301-stack-installer/summary.md` (in this directory) — cross-repo resolution context
2. **Original handoff:** `.ai-work/eng-301-stack-installer/HANDOFF.md` (this file) — pre-completion state
3. **Implementation details:** `FlowForge/.ai-work/stack-installer/summary.md` — canonical closure doc with all commit hashes, defects found and fixed, test results
4. **Decide** whether to merge `feat/eng-301-post-install-scripts` (the work is done; mostly housekeeping) and close ENG-301 per the orchestrator flow
