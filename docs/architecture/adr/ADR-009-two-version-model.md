# ADR-009: Two-version model — product version vs API/schema version

**Status:** Accepted
**Date:** 2026-07-06
**Deciders:** victor
**Related:** ENG-437, AD-1, AD-2

## Context

engram-dotnet suffered from a four-way version string conflict:

| Location | Version | Problem |
|----------|---------|---------|
| Git tags | `v1.0.0` → `v1.2.1` | CHANGELOG didn't match |
| CHANGELOG headers | `[0.1.0]` → `[0.3.0]` | No corresponding git tags |
| `Program.cs` (`engram version`) | `"0.3.0"` | Contradicted `/health` |
| `/health` endpoint | `"1.1.0"` | Contradicted CLI |

Additionally, the `EngramServer.cs`, `Models.cs`, `SqliteStore.cs`, and `PostgresStore.cs` files all hardcode `"1.1.0"` as a format/schema version marker in `/health` responses and `ExportData` serialization — roles fundamentally different from the product version.

Attempting to unify all of these into a single version number would be incorrect, because:
- The product version tracks releases and git tags
- The API/schema version tracks wire format compatibility
- These are orthogonal concerns that evolve at different rates

## Decision

Adopt a **two-version model**:

### Product version
- Source of truth: `src/Engram.Cli/Program.cs` line 35 (`const string Version`)
- Tracks git tags (`v1.x` scheme)
- User-facing: `engram version`, `engram --version`, Docker images
- Updated on every release (TIPO A files)
- Current: `1.3.0`

### API/schema version
- Source of truth: `"1.1.0"` hardcoded in 4 files (6 total occurrences)
- Tracks wire format and data export schema compatibility
- Used in `/health` response and `ExportData.Version`
- **Never** changed during product version bumps
- Only updated when the API contract or export format actually changes
- Current: `"1.1.0"`

### File classification

| Type | Content | Update on release? |
|------|---------|-------------------|
| **TIPO A** | Product version — `Program.cs`, Dockerfiles, scripts, live docs | **Yes** |
| **TIPO B** | API/schema version — `EngramServer.cs`, `Models.cs`, `SqliteStore.cs`, `PostgresStore.cs` | **Never** |
| **TIPO C** | Docs live — quick-start, setup guides, READMEs | Yes, if they show version numbers |

## Consequences

### Positive
1. **Clarity**: Engineers now know which files to touch on release and which to leave alone
2. **API stability**: Consumers of `/health` see a stable version string that only changes when the API contract changes
3. **Traceability**: Product version maps 1:1 to git tags and CHANGELOG entries
4. **No more confusion**: `engram version` and `/health` can legitimately differ

### Negative
1. **Cognitive overhead**: New contributors need to learn the two-version model
2. **Release checklist complexity**: Must verify TIPO B files are NOT modified as part of release process

### Mitigations
1. This ADR serves as the canonical reference for the two-version model
2. Future work (ENG-304) may introduce centralized versioning via `Directory.Build.props`, but the distinction between product and API version must remain
3. Release procedure in GIT-WORKFLOW.md updated to use generic `vX.Y.Z` placeholders

## Compliance

- **ENG-437**: Verified all 19 requirements pass. TIPO B files confirmed untouched via `git diff`. All 6 occurrences of `"1.1.0"` preserved.
- **Verification grep**: `grep -rn '"1\.1\.0"' src/Engram.Server/EngramServer.cs` → 1; `grep -rn '"1\.1\.0"' src/Engram.Store/` → 5. Total: 6.
