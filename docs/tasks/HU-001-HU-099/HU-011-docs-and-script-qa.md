# HU-011 — Documentation & Script QA

**As**: Developer maintaining engram-dotnet  
**I want**: All documentation to follow FlowDoc conventions and scripts to be validated automatically  
**To**: Ensure quality, consistency, and that documentation stays aligned with the actual implementation

---

## Acceptance Criteria

- [ ] All HU documents follow the FlowDoc HU template structure
- [ ] HU documents have no broken internal cross-references
- [ ] `docs/architecture/adr/INDEX.md` exists and is up-to-date with all ADRs
- [ ] `scripts/deploy.sh` passes `shellcheck` validation with no errors
- [ ] `scripts/backup.sh` passes `shellcheck` validation with no errors
- [ ] `scripts/*.sh` in root and `scripts/` directory are validated
- [ ] Documentation update for HU-010 covers all affected docs: `INSTALL.md`, `01-QUICK-START.md`, `DOCKER-VANILLA.md`, `docker/README.md`

---

## Tasks (Implementation)

### Documentation Validation & Fixes

- [ ] Scan all HU documents in `docs/tasks/HU-001-HU-099/` for template compliance
- [ ] Fix missing required sections in any HU that doesn't follow the template
- [ ] Verify all internal links in HU documents resolve to existing files
- [ ] Verify `docs/architecture/adr/INDEX.md` exists and lists all ADRs
- [ ] Create `docs/architecture/adr/INDEX.md` if missing
- [ ] Document MCP client configuration per profile in `docs/SYNC-SETUP.md` or new `docs/MCP-PROFILES.md`

### Script QA

- [ ] Add `shellcheck` to CI pipeline (`.github/workflows/shellcheck.yml`)
- [ ] Run `shellcheck` on all `*.sh` files in `scripts/` and repo root
- [ ] Fix all `shellcheck` errors before merge
- [ ] Document `shellcheck` requirement in `CONTRIBUTING.md`

---

## Current State (Audit Results)

### Documentation Issues Found

| Issue | Severity | Status |
|-------|----------|--------|
| `docs/templates/` exists but **ALL template files are EMPTY** | Error | Open |
| `docs/architecture/adr/INDEX.md` **does not exist** | Error | Open |
| ADR numbering gaps: 002, 003, 005, 006 missing | Warning | Open |
| HU-008 missing (gap in sequence) | Warning | Open |
| `PRD-001-postgresql-backend.md` misplaced in `rfc/` instead of `PRD/` | Warning | Open |
| HU-002 through HU-006 missing `Acceptance Criteria`, `Tasks`, `Scenarios` sections | Error | Open |
| HU-010 and HU-011 properly formatted | — | OK |

### Files Needing Fixes

| File | Issue |
|------|-------|
| `docs/templates/user-stories/template-user-story.md` | Empty — needs template content |
| `docs/templates/architecture/ADR_template.md` | Empty — needs template content |
| `docs/templates/architecture/RFC_template.md` | Empty — needs template content |
| `docs/architecture/adr/INDEX.md` | Missing — needs creation |
| `docs/architecture/rfc/PRD-001-postgresql-backend.md` | Misplaced — should move to `docs/PRD/` |

---

## Notes

The MCP client (`mcp.json`) configuration should be documented per deployment profile:

**Profile: `local`** (standalone SQLite)
```json
{
  "mcpServers": {
    "engram": {
      "command": "engram",
      "args": ["mcp"],
      "env": {
        "ENGRAM_DATA_DIR": "~/.engram"
      }
    }
  }
}
```

**Profile: `server`** (PostgreSQL, no sync)
```json
{
  "mcpServers": {
    "engram": {
      "command": "engram",
      "args": ["mcp"],
      "env": {
        "ENGRAM_URL": "http://your-server:7437",
        "ENGRAM_USER": "your-username"
      }
    }
  }
}
```

**Profile: `sync`** (offline-first with SyncManager)
```json
{
  "mcpServers": {
    "engram": {
      "command": "engram",
      "args": ["mcp"],
      "env": {
        "ENGRAM_SERVER_URL": "http://your-server:7437",
        "ENGRAM_SYNC_ENABLED": "true",
        "ENGRAM_USER": "your-username",
        "ENGRAM_DATA_DIR": "~/.engram"
      }
    }
  }
}
```

### ShellCheck CI Example

```yaml
# .github/workflows/shellcheck.yml
name: ShellCheck

on: [push, pull_request]

jobs:
  shellcheck:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - name: Run ShellCheck
        uses: ludwanpierre/shellcheck-github-action@v1
        with:
          scflags: "-x -S error"
          include: "scripts/*.sh *.sh"
```

### Existing Scripts to Validate

| Script | Purpose |
|---------|---------|
| `scripts/setup.sh` | MCP setup wizard |
| `scripts/post-install.sh` | Post-install registration |
| `scripts/dev-test.sh` | Local integration tests |
| `scripts/test-offline-reconnect.sh` | Offline sync test |
| `scripts/test-2client-pull.sh` | Multi-client sync test |
| `scripts/regression-test.sh` | Regression tests |

---

## Notes

- **Priority**: Documentation fixes should be done BEFORE implementing HU-010 so the docs reflect the new profile system correctly.
- **ADR Index**: If `INDEX.md` doesn't exist, creating it is a prerequisite for ADR validation.
- **ShellCheck severity**: Only fix `error` level issues in CI. Warnings can be tracked separately.
