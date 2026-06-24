# Summary: ENG-301 Stack Installer (cross-repo resolution)

**Date:** 2026-06-23
**Status:** ✅ Done (resolved across repos)

---

## What was done

ENG-301 was completed in TWO repos:

### FlowForge repo (`efreet111/FlowForge`)

- **Branch:** `feat/eng-301-stack-installer` (merged to `main`, then deleted)
- **Final tag:** `v0.1.0-alpha.2` (published 2026-06-23)
- **Release assets:** `flowforge-linux-x64`, `flowforge-linux-x64.sha256`, `flowforge-win-x64.exe`, `flowforge-win-x64.exe.sha256`
- **Implementation:** ~3,300 LOC across C# .NET 10 AOT project + bootstrap scripts + GitHub Actions matrix workflow
- **Verification:** PM-1 (full bootstrap install) ✅, PM-2 (offline wizard) ✅, PM-3..5 deferred with rationale
- **Canonical closure doc:** `FlowForge/.ai-work/stack-installer/summary.md`

### engram-dotnet repo (this repo)

- **Branch:** `feat/eng-301-post-install-scripts` (commit `2dcbf80`, **NOT yet pushed**)
- **Implementation:** `scripts/post-install.sh` + `scripts/post-install.ps1` (register engram version in `~/.engram/config.json` after engram binary is installed)
- **Tests:** Manual idempotency check during PM-1 (no automated test suite — FU-7)

---

## Why two repos?

- The **installer** (C# AOT binary + bootstrap scripts + release workflow) belongs in **FlowForge** because it depends on FlowForge skills/agents to install.
- The **post-install registration scripts** belong in **engram-dotnet** because they run after engram is installed and write engram's own version to the shared `~/.engram/config.json`.

This split was decided upfront in the [spike learnings](../eng-301-spike/learnings.md) and [ADR-004-post-install-registration.md](../../docs/architecture/adr/ADR-004-post-install-registration.md).

---

## What's pending for engram-dotnet to fully close ENG-301

1. Push `feat/eng-301-post-install-scripts` to remote:
   ```bash
   git push -u origin feat/eng-301-post-install-scripts
   ```
2. Merge to `main` (directly or via PR).
3. Optionally tag a release that bundles the post-install scripts (e.g. `v1.3.0`):
   ```bash
   git tag -a v1.3.0 -m "v1.3.0: post-install registration scripts for FlowForge installer"
   git push origin v1.3.0
   ```
4. ENG-301 already marked **Done** in `BACKLOG.md` and `ROADMAP.md` (this session, 2026-06-23).
5. Close this work item via the orchestrator's CKP-4 flow or equivalent.

---

## Reading guide for the future agent

The agent that closes ENG-301 in this repo should:

1. **Start here:** this `summary.md` — cross-repo resolution context.
2. **Original handoff:** `HANDOFF.md` — pre-completion state (2026-06-23 16:17 UTC-5).
3. **Implementation details:** `FlowForge/.ai-work/stack-installer/summary.md` — canonical closure doc with all commit hashes, defects found and fixed, test results, FU list.
4. **Decide** whether to merge `feat/eng-301-post-install-scripts` (the work is done; mostly housekeeping) and close ENG-301 per the orchestrator flow.

---

## Related artifacts

### engram-dotnet docs (this repo)

- [`docs/BACKLOG.md`](../../docs/BACKLOG.md) — ENG-301 entry (updated to Done 2026-06-23)
- [`docs/ROADMAP.md`](../../docs/ROADMAP.md) — ENG-301 status (updated 2026-06-23)
- [`docs/architecture/adr/ADR-004-post-install-registration.md`](../../docs/architecture/adr/ADR-004-post-install-registration.md) — engram-side ADR for post-install scripts
- [`.ai-work/eng-301-spike/learnings.md`](../eng-301-spike/learnings.md) — original design spike

### engram-dotnet `.ai-work/eng-301-stack-installer/` (this dir)

- [`spec.md`](spec.md) — original spec
- [`plan.md`](plan.md) — checklist (updated 2026-06-23 — impl items checked, tests deferred to FU-7)
- [`HANDOFF.md`](HANDOFF.md) — historical handoff + status update section
- `summary.md` — this file

### FlowForge `.ai-work/stack-installer/` (cross-repo)

- `summary.md` — canonical closure doc (350+ lines)
- `verify-report.md`, `rework_ticket*.md`, `spec.md`, `plan.md`
- See: https://github.com/efreet111/FlowForge/tree/main/.ai-work/stack-installer

---

## How to verify "this is done" from engram-dotnet

After merging and tagging, run:
```bash
ls scripts/post-install.sh scripts/post-install.ps1
# Should show both files exist

git tag -l | grep -E "v1\.3\.0|v0\.3\.0"
# Should show the new tag

grep -A 2 "ENG-301" docs/BACKLOG.md
# Should show "Done" status
```

For the actual install verification:
```bash
# Download FlowForge installer
curl -fsSL https://raw.githubusercontent.com/efreet111/FlowForge/main/install/install.sh | bash

# During install wizard, when prompted, engram-dotnet's post-install scripts run
# They should register the installed engram version in ~/.engram/config.json

cat ~/.engram/config.json
# Should show engram_dotnet.installed = true with version
```

