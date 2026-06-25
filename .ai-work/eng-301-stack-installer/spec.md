---
capability_matrix:
  ai_reasoning:
    - Installer UX/wording and interactive prompts
    - IDE selection defaults (all checked vs none checked)
    - Color/theme of CLI wizard
    - Error message tone and helpfulness
    - FlowDoc template content structure
  deterministic:
    - Binary location: ~/.local/bin (Linux/macOS), %LOCALAPPDATA%\Programs (Windows)
    - Config file location: ~/.engram/config.json
    - Log file: ~/.engram/install.log
    - Channel mapping: main→stable, dev→beta, daily builds→nightly
    - GitHub Releases API filtering for stable vs pre-releases
    - Auto-update OFF by default (notification only on run)
    - FlowDoc opt-in ON by default
    - Single binary per platform (no per-component binaries)
    - Installer manifest version: installer_version in manifest.yaml
    - engram-dotnet minimum version: >=1.2.0
    - Uninstall removes: binary from PATH, ~/.engram/, IDE skills
    - Idempotent: re-running install does not break existing setup
---

# Spec: ENG-301 — FlowForge Stack Installer

## 1. Objective and Scope

### Problem
Today, installing the full stack (engram-dotnet + FlowForge + FlowDocs) requires:
- .NET SDK 10 installed
- Clone 3 repos manually
- Build each one
- Configure MCP for each editor manually
- Copy skills to each IDE manually

This is a 2-hour setup for someone who knows the stack. New users abandon.

### Objective
A user with zero context can install the full stack in <10 minutes with one command.

### Scope

**Components offered:**
- engram-dotnet (backend, standalone binary)
- FlowForge (skills for IDEs: OpenCode, Cursor, Antigravity, VS Code)
- FlowDocs (docs template for current project)

**In scope (v1):**
- `flowforge install` — fresh install (interactive wizard)
- `flowforge update` — update existing install
- `flowforge update --check` — check for updates only (notification only)
- `flowforge uninstall` — remove everything
- `flowforge` (no args) — show status + version + update notification
- Curl-pipe distribution for Linux/macOS
- IWR-pipe distribution for Windows
- GitHub Releases binaries (self-contained, no .NET required)
- FlowDoc opt-in via config file (`~/.engram/config.json`)
- Channel selection: stable / beta / nightly

**Out of scope (v1):**
- systemd / Windows Service
- Package managers (apt/brew/scoop/chocolatey/winget)
- Auto-update background daemon
- GUI wizard (ENG-302 — separate feature)
- Custom install paths (always use defaults: `~/.local/bin/` or `%LOCALAPPDATA%\Programs\`)
- Per-component uninstall

### Component Manifest

```yaml
installer_version: 0.1.0
requires:
  engram-dotnet: ">=1.2.0"
channels: [stable, beta, nightly]
default_channel: stable
```

---

## 2. Functional Requirements (FR)

### FR-001: Fresh install on Linux/macOS via curl-pipe

User runs `curl -fsSL https://get.flowforge.dev | bash`. The installer:
- Detects platform (Linux/macOS)
- Shows banner with version
- Runs interactive wizard (multi-select components)
- Installs binaries to `~/.local/bin/`
- Adds to PATH if needed
- Configures selected IDEs with MCP settings
- Creates `~/.engram/config.json`
- Logs all actions to `~/.engram/install.log`

*Scenario A — Full stack install:*
- **Given** a clean Linux machine with no engram-dotnet installed
- **When** the user runs `curl -fsSL https://get.flowforge.dev | bash` and selects all components (engram-dotnet, FlowForge, FlowDocs), OpenCode + Cursor IDEs, local SQLite mode
- **Then** the installer downloads the latest stable binary to `~/.local/bin/flowforge`, creates `~/.engram/config.json` with all options, installs engram-dotnet binary, copies FlowForge skills to `~/.config/opencode/` and `~/.cursor/`, prompts to create `docs/` from FlowDoc template, and logs every step to `~/.engram/install.log`

*Scenario B — Partial install (skip engram-dotnet):*
- **Given** a clean Linux machine
- **When** the user runs the curl-pipe install and deselects engram-dotnet but selects FlowForge only
- **Then** only FlowForge skills are installed to the selected IDEs; no engram binary is downloaded; `~/.engram/config.json` is created with `engram.enabled = false`; no MCP config for engram is written

---

### FR-002: Fresh install on Windows via IWR-pipe

User runs `iwr -useb https://get.flowforge.dev/install.ps1 | iex`. The installer:
- Detects Windows platform
- Shows banner with version
- Runs interactive wizard (multi-select components)
- Installs binaries to `%LOCALAPPDATA%\Programs\FlowForge\`
- Adds to user PATH via `setx` or registry update
- Configures selected IDEs with MCP settings
- Creates `~\.engram\config.json`
- Logs all actions to `~\.engram\install.log`

*Scenario A — Full Windows install:*
- **Given** a clean Windows 10/11 machine
- **When** the user runs `iwr -useb https://get.flowforge.dev/install.ps1 | iex` and selects all components with VS Code IDE
- **Then** the installer downloads the Windows binary to `%LOCALAPPDATA%\Programs\FlowForge\flowforge.exe`, creates `~\.engram\config.json`, installs FlowForge skills to `%USERPROFILE%\.vscode\`, and logs to `~\.engram\install.log`

*Scenario B — Windows install with FlowDoc disabled:*
- **Given** a clean Windows machine
- **When** the user opts out of FlowDoc during the wizard
- **Then** no `docs/` template is created; `~\.engram\config.json` contains `flowdoc.enabled = false`; existing `AGENTS.md` is not modified

---

### FR-003: Update existing install

User runs `flowforge update`. The installer:
- Reads current version from `~/.engram/config.json`
- Calls GitHub Releases API to fetch latest version for the selected channel
- If newer version available, shows changelog summary
- Confirms with user (unless `auto_update = true` in config)
- Downloads new binary to temp location
- Replaces old binary atomically
- Retains config file and MCP settings

*Scenario A — Update available and confirmed:*
- **Given** user has flowforge v0.1.0 installed on Linux with channel=stable
- **When** the user runs `flowforge update` and confirms the prompt
- **Then** the installer downloads v0.2.0 from GitHub Releases, replaces `~/.local/bin/flowforge`, preserves `~/.engram/config.json`, and prints "Successfully updated to v0.2.0"

*Scenario B — No update available:*
- **Given** user has flowforge v0.2.0 installed and latest GitHub Release is also v0.2.0
- **When** the user runs `flowforge update`
- **Then** the installer prints "You are on the latest version (v0.2.0)" and exits with code 0

---

### FR-004: Update check on run (no auto-install)

User runs `flowforge` with no args. The installer:
- Shows current version and install status
- Checks GitHub Releases for newer version (in background or fast path)
- If newer version exists, shows warning banner: "⚠️ v0.2.0 available"
- Does NOT download or install unless user explicitly runs `flowforge update`

*Scenario A — Update available shown on run:*
- **Given** user has v0.1.0 installed with auto_update=false
- **When** the user runs `flowforge`
- **Then** the output includes "FlowForge v0.1.0 (current) | ⚠️ v0.2.0 available — run `flowforge update` to upgrade"

*Scenario B — No update, clean run:*
- **Given** user has latest version installed
- **When** the user runs `flowforge`
- **Then** the output shows "FlowForge v0.2.0 (latest)" with no warning

---

### FR-005: Auto-update opt-in via config

User runs `flowforge config set auto_update true`. On subsequent runs:
- `flowforge` detects new version
- Automatically downloads and installs without confirmation prompt
- Logs the auto-update to `~/.engram/install.log`

*Scenario A — Auto-update triggers on run:*
- **Given** user has auto_update=true in config, current version v0.1.0, latest v0.2.0
- **When** the user runs `flowforge`
- **Then** the installer automatically downloads v0.2.0, replaces the binary, and prints "Auto-updated to v0.2.0"

*Scenario B — Auto-update disabled restores prompt:*
- **Given** user had auto_update=true but now runs `flowforge config set auto_update false`
- **When** the user runs `flowforge update`
- **Then** the installer asks for confirmation before downloading

---

### FR-006: FlowDoc opt-out via config file

User creates/edits `~/.engram/config.json` with `flowdoc.enabled = false`. Effects:
- Installer skips FlowDoc template prompt
- No `docs/` directory created by installer
- `AGENTS.md` is not copied or modified

*Scenario A — Config opt-out respected on fresh install:*
- **Given** user has pre-existing `~/.engram/config.json` with `flowdoc.enabled = false`
- **When** the user runs a fresh install with FlowDocs component selected
- **Then** the installer skips FlowDoc entirely, prints "FlowDoc disabled in config — skipping"

*Scenario B — AGENTS.md not touched when opt-ed out:*
- **Given** project already has an `AGENTS.md` file
- **When** FlowDoc is disabled in config and installer runs
- **Then** the existing `AGENTS.md` is not modified, no backup created

---

### FR-007: Uninstall removes everything

User runs `flowforge uninstall`. The uninstaller:
- Confirms with user
- Removes binary from `~/.local/bin/` or `%LOCALAPPDATA%\Programs\FlowForge\`
- Removes `~/.engram/` directory entirely (config, logs, state)
- Removes IDE skill directories: `~/.config/opencode/skills/flowforge/`, `~/.cursor/extensions/flowforge/`, etc.
- Removes MCP config entries for engram from IDE config files (edits in place, does not corrupt)

*Scenario A — Full uninstall Linux:*
- **Given** flowforge was installed on Linux with all components and all IDEs
- **When** the user runs `flowforge uninstall` and confirms
- **Then** `~/.local/bin/flowforge` is deleted, `~/.engram/` is removed, `~/.config/opencode/skills/flowforge/` is removed, `~/.cursor/mcp.json` has engram entry removed, `~/.config/opencode/opencode.json` has flowforge skills removed; installer prints "Uninstall complete. Thanks for trying FlowForge!"

*Scenario B — Uninstall with partial install:*
- **Given** only FlowForge (no engram-dotnet) was installed previously
- **When** the user runs `flowforge uninstall`
- **Then** the uninstaller removes only FlowForge skills and config; no engram-dotnet binary or config is touched (because it was never installed)

---

### FR-008: Channel switch via config

User runs `flowforge config set channel beta`. The installer:
- Updates `~/.engram/config.json` with `channel = "beta"`
- On next `flowforge update`, pulls from beta channel (GitHub pre-releases)
- Shows current channel in `flowforge` status output

*Scenario A — Beta channel update:*
- **Given** user has channel=beta in config, current v0.1.0 stable installed
- **When** the user runs `flowforge update`
- **Then** the installer fetches from GitHub Releases pre-releases, finds v0.2.0-beta.1, and offers to install it

*Scenario B — Channel revert to stable:*
- **Given** user has channel=beta in config
- **When** the user runs `flowforge config set channel stable`
- **Then** next update pulls from stable releases only (ignoring pre-releases)

---

### FR-009: Component selection in wizard

Installer wizard presents multi-select for components:
- engram-dotnet (default: selected)
- FlowForge (default: selected)
- FlowDocs (default: selected)

*Scenario A — All components selected by default:*
- **Given** a clean install
- **When** the user hits Enter at the component selection prompt without changing anything
- **Then** all three components are selected for installation

*Scenario B — User deselects engram-dotnet:*
- **Given** a clean install
- **When** the user spacebar-deselects engram-dotnet but keeps FlowForge and FlowDocs
- **Then** only FlowForge skills and FlowDoc template are installed; installer proceeds without asking about engram mode

---

### FR-010: Incompatible version detection

Installer checks minimum version requirements from manifest:
- If installed engram-dotnet version < required minimum, block with error
- Provide clear message: "engram-dotnet v1.0.0 detected but v1.2.0+ is required. Please update engram-dotnet first."

*Scenario A — Incompatible engram-dotnet version blocks update:*
- **Given** user has engram-dotnet v1.0.0 installed and manifest requires >=1.2.0
- **When** the user runs `flowforge update`
- **Then** the installer prints "ERROR: engram-dotnet v1.0.0 is installed but flowforge requires v1.2.0 or higher. Please update engram-dotnet separately." and exits with code 1

*Scenario B — Compatible version proceeds:*
- **Given** user has engram-dotnet v1.3.0 installed
- **When** the installer checks the version
- **Then** it passes the compatibility check and proceeds normally

---

## 3. Non-Functional Requirements (NFR)

### Performance
- RNF-PERF-001: Install completes in <10 min on a clean machine (excluding download time)
- RNF-PERF-002: Binary size < 50 MB self-contained per platform
- RNF-PERF-003: `flowforge update --check` completes in < 2 seconds (cached or fast-fail)

### Distribution & Reliability
- RNF-DIST-001: Works offline after install (only updates require network)
- RNF-DIST-002: Idempotent — running install twice does not break existing setup
- RNF-DIST-003: Respects existing PATH (does not override user PATH; uses ~/.local/bin/ which is already in most Linux distros)
- RNF-DIST-004: Curl-pipe URL: `https://get.flowforge.dev` with GitHub raw fallback
- RNF-DIST-005: Windows IWR URL: `https://get.flowforge.dev/install.ps1`

### Logging & Diagnostics
- RNF-LOG-001: All install/update/uninstall actions logged to `~/.engram/install.log`
- RNF-LOG-002: Log format: `[YYYY-MM-DD HH:MM:SS] [LEVEL] message`
- RNF-LOG-003: Log includes: timestamp, action, component affected, result (success/failure), error details if any

### Security (STRIDE)
- RNF-SEC-SPOOF: Installer verifies binary authenticity via SHA-256 checksum published on GitHub Releases (not just HTTPS)
- RNF-SEC-TAMPER: Downloads are validated against checksum before execution; checksum mismatch aborts install with clear error
- RNF-SEC-REPUDIATION: All state-changing operations log actor ID, timestamp, component
- RNF-SEC-DISCLOSURE: No secrets, tokens, or PII written to log files
- RNF-SEC-DOS: Rate limit proof-of-concept: network timeout on GitHub API call is 5 seconds; no retry storm
- RNF-SEC-ELEVATION: Installer runs with user-level permissions only; no sudo/elevated required for default install path

### Update Channels
- RNF-CHAN-001: Channel mapping: `main` branch releases → `stable`, `dev` branch → `beta`, daily/nightly builds → `nightly`
- RNF-CHAN-002: GitHub Releases API filters: stable = not pre-release, beta = pre-release tagged, nightly = draft or pre-release with `nightly` in tag
- RNF-CHAN-003: Default channel: `stable`
- RNF-CHAN-004: Auto-check OFF by default; notification shown on `flowforge` run if newer version exists

### Installer Architecture
- RNF-ARCH-001: Single binary per platform (one `flowforge` binary, not per-component)
- RNF-ARCH-002: Installer binary is a thin wrapper around bash/PowerShell — no custom language or runtime
- RNF-ARCH-003: Channel filtering via GitHub Releases API (not tag parsing)
- RNF-ARCH-004: FlowDoc opt-in default: ON; config file at `~/.engram/config.json`
- RNF-ARCH-005: Uninstall removes everything by default (no per-component uninstall in v1)

---

## 4. Developer Manual Tests (PM-*) — Required for CKP-4

These tests are run by a **human developer** before `/flow-close`. forge-memory blocks CKP-4 if any PM lacks `[x]`.

| ID | Case / Flow | Steps (Summary) | Expected Result | [x] |
|----|-------------|-----------------|-----------------|-----|
| PM-1 | Fresh install on clean Linux machine | 1. Spin up clean Ubuntu VM or container<br>2. Run `curl -fsSL https://get.flowforge.dev \| bash`<br>3. Select all components, all IDEs, local SQLite mode<br>4. Verify binary in `~/.local/bin/`<br>5. Verify `~/.engram/config.json`<br>6. Run `flowforge` to see status | Banner shows version, status shows "installed", no errors | [ ] |
| PM-2 | Fresh install on clean Windows machine | 1. Spin up clean Windows 10/11 VM<br>2. Run `iwr -useb https://get.flowforge.dev/install.ps1 \| iex`<br>3. Select all components, VS Code IDE<br>4. Verify binary in `%LOCALAPPDATA%\Programs\FlowForge\`<br>5. Run `flowforge` | Status shows installed with correct version | [ ] |
| PM-3 | Update from v0.1.0 to v0.2.0 | 1. Have v0.1.0 installed (simulate by placing old binary)<br>2. Run `flowforge update`<br>3. Confirm prompt<br>4. Verify new binary version | Binary replaced, config preserved, version incremented | [ ] |
| PM-4 | Uninstall removes everything cleanly | 1. Have full install with all components<br>2. Run `flowforge uninstall`<br>3. Confirm prompt<br>4. Check binary gone, `~/.engram/` gone, IDE configs cleaned | Clean removal, no orphaned files or configs | [ ] |
| PM-5 | FlowDoc opt-out via config file | 1. Pre-create `~/.engram/config.json` with `flowdoc.enabled = false`<br>2. Run install selecting FlowDocs component<br>3. Verify `docs/` was NOT created<br>4. Verify `AGENTS.md` was NOT modified | FlowDoc skipped, config respected | [ ] |

---

## 5. Files Affected

| File | Action | Purpose |
|------|--------|---------|
| `FlowForge/install/install.sh` | New (~200 lines) | Linux/macOS installer entry point + wizard |
| `FlowForge/install/install.ps1` | New (~200 lines) | Windows installer entry point + wizard |
| `FlowForge/install/uninstall.sh` | New (~50 lines) | Linux/macOS uninstaller |
| `FlowForge/install/uninstall.ps1` | New (~50 lines) | Windows uninstaller |
| `FlowForge/install/manifest.yaml` | New (~20 lines) | Version + component requirements |
| `FlowForge/install/flowforge` | New (bash wrapper or thin binary) | Main installer binary |
| `FlowForge/install/lib/common.sh` | New | Shared functions (logging, download, checksum) |
| `FlowForge/install/lib/engram.sh` | New | engram-dotnet binary installer + MCP config |
| `FlowForge/install/lib/flowforge.sh` | New | FlowForge IDE skills installer |
| `FlowForge/install/lib/flowdoc.sh` | New | FlowDoc template installer |
| `FlowForge/install/README.md` | New | Public-facing install docs |
| `FlowForge/.github/workflows/release.yml` | New | Release workflow for installer binaries |
| `engram-dotnet/scripts/post-install.ps1` | New | Register engram with installer manifest |
| `engram-dotnet/scripts/post-install.sh` | New | Register engram with installer manifest |
| `engram-dotnet/.engram-id` | New | Template for new projects |

---

## 6. Effort Re-estimate

**M (3 installers in one)** — updated from L because the spike proved the scope is manageable. The spike showed that:
- Self-contained binaries already exist in the release pipeline
- Setup scripts already cover the core wizard flow
- The gap is primarily distribution (curl-pipe + releases) + uninstall + update mechanism

---

## 7. Open Questions

No open questions. All decisions were resolved by the user (see decision table in the brief).

---

## Memory Signal
- type: architecture
- significance: high
- summary: "ENG-301 Stack Installer: single multi-mode installer (flowforge) in FlowForge repo, curl-pipe distribution via get.flowforge.dev, GitHub Releases for binaries, channel-based update (stable/beta/nightly), auto-update OFF by default, FlowDoc opt-in ON by default, uninstall from day 1"
