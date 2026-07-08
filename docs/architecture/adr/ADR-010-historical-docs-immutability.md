# ADR-010: Historical documentation immutability policy

**Status:** Accepted
**Date:** 2026-07-06
**Deciders:** victor
**Related:** ENG-437, AD-3

## Context

During ENG-437 (version string unification), we identified documentation files that contain outdated version references (e.g., `v0.3.0`). Some of these are "live" docs that should be updated to reflect current reality. Others are "historical" docs that intentionally preserve the state of the world at the time they were written.

The question: which docs should be updated and which should be left untouched?

### Files in question

| File | Contains | Should update? |
|------|----------|---------------|
| `docs/01-QUICK-START.md` | `"version":"0.3.0"` in `/health` example | **Yes** — live doc, user-facing |
| `docs/POSTGRES-SETUP.md` | `"version":"0.3.0"` in `/health` example | **Yes** — live doc, user-facing |
| `docker/README.md` | `"version":"0.3.0"` in `/health` example | **Yes** — live doc, user-facing |
| `docs/GIT-WORKFLOW.md` | `v0.3.0` tag reference, `v0.4.0` example | **Yes** — process doc, should reflect current practice |
| `docs/ROADMAP.md` | `Version 0.3.0` in table | **Yes** — vision doc, should show current version |
| `docs/MIGRATION.md` | `v0.3.0` references (migration from) | **No** — historical record of migration path |
| `docs/SYNC-SETUP.md` | `v0.3.0` minimum requirement | **No** — historical minimum version snapshot |
| `docs/architecture/adr/ADR-004-*.md` | (any old version references) | **No** — ADRs are immutable by definition |

## Decision

Establish a clear policy for documentation updates:

### Live docs (UPDATE on version changes)
Docs that describe current behavior, provide setup instructions, or serve as user-facing reference. These must reflect the current product version.

- `docs/01-QUICK-START.md`
- `docs/POSTGRES-SETUP.md`
- `docker/README.md`
- `docs/GIT-WORKFLOW.md` (process docs)
- `docs/ROADMAP.md`
- `docs/API-REFERENCE.md`
- `docs/INSTALLER-TROUBLESHOOTING.md` (only if the version reference is functional, not illustrative)

### Historical docs (NEVER modify)
Docs that serve as records of past states, migration paths, or minimum requirements at a point in time. These are intentionally preserved as-is.

- `docs/MIGRATION.md` — records the upgrade path from v0.3.0 to current
- `docs/SYNC-SETUP.md` — records the minimum version requirement at time of writing
- All ADR files (`docs/architecture/adr/ADR-*.md`) — immutable decision records
- Session reports (`docs/SESSION-REPORT-*.md`) — historical session records
- FlowForge artifacts (`.ai-work/`, `sdd/`) — never modified after creation

### Test for classification
When evaluating a doc for update:

1. **Is it documenting something that WAS true?** → Historical, leave alone
2. **Is it documenting something that IS true / should be true?** → Live, update
3. **Is it an ADR or decision record?** → Immutable, never modify

## Consequences

### Positive
1. **Traceability**: Historical docs retain accurate records of past states
2. **Safety**: No risk of breaking migration guides by retroactively editing version numbers
3. **Clarity**: Release checklist can specify exactly which docs to update

### Negative
1. **Stale references**: Historical docs will contain outdated version strings — this is intentional
2. **Search noise**: `grep` for old version numbers will still hit historical docs — must filter by category

### Mitigations
1. Release verification should check historical docs are NOT modified (via `git diff`)
2. Live docs should be listed explicitly in each release spec
3. ADR files are never in scope for version bumps — enforce via spec

## Compliance

- **ENG-437**: C4 (MIGRATION.md), C5 (SYNC-SETUP.md), C9 (ADR-004) confirmed untouched via `git diff`. `v0.3.0` still present in MIGRATION.md (1) and SYNC-SETUP.md (1) — intentional.
