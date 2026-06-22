# ⚠️ DEPRECATED — Openspec Directory

**Migration Date**: 2026-06-01

This directory was an experimental artifact store that was never fully adopted. It has been **superseded by the FlowDoc structure** in `docs/`.

## Contents

Only `openspec/RFC-002/` contained artifacts (proposal.md, spec.md, tasks.md). These have been migrated to `docs/architecture/rfc/RFC-002-multi-user-isolation.md`.

## Current Location for RFCs

RFCs are now stored in `docs/architecture/rfc/`.

## Migration Mapping

| Original | New Location |
|----------|--------------|
| openspec/RFC-002/proposal.md | docs/architecture/rfc/RFC-002-multi-user-isolation.md |
| openspec/RFC-002/spec.md | (merged into above) |
| openspec/RFC-002/tasks.md | (merged into above) |

## Deprecation Note

openspec was a file-based SDD artifact store. FlowDoc uses `docs/` as the single source of truth for all documentation.

Do not create new artifacts in this directory.

**Last updated**: 2026-06-01