---
observation_id: 20
type: "decision"
title: "AD-3: Historical docs are immutable records"
created_at: "2026-07-08 00:55:44"
topic_key: "documentation/historical-immutability"
project: "engram-dotnet"
scope: "team"
generated_at: "2026-07-21T22:00:59.5826053Z"
---

# AD-3: Historical docs are immutable records

**What**: Historical documentation files (MIGRATION.md, SYNC-SETUP.md, ADR-004) are immutable records of "what was true at the time" and must never be modified by version bumps or retroactive edits.

**Why**: These docs serve as historical reference — MIGRATION.md documents the v0.3.0→v1.x migration path, SYNC-SETUP.md documents the minimum version requirements at the time, ADR-004 is an immutable decision record. Changing them would break the historical trace and mislead readers about past states.

**Where**: docs/MIGRATION.md (still contains "v0.3.0" references — intentional), docs/SYNC-SETUP.md (still contains "v0.3.0" — intentional), docs/architecture/adr/ADR-004-post-install-registration.md (ADR — immutable by definition)

**Learned**: 
- Do NOT grep for "v0.3.0" and remove it blindly — check file category first
- Live docs (01-QUICK-START.md, POSTGRES-SETUP.md, docker/README.md) → UPDATE
- Historical docs (MIGRATION.md, SYNC-SETUP.md) → PRESERVE
- ADRs (ADR-004 and all others) → NEVER modify
- The distinction: live docs reflect current reality, historical docs reflect past reality
