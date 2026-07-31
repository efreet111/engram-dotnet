---
observation_id: 23
type: "discovery"
title: "Memory Signal — ENG-437 closure (forge-memory)"
created_at: "2026-07-08 00:57:14"
topic_key: "memory-signal/eng-437-closure"
project: "engram-dotnet"
scope: "team"
generated_at: "2026-07-21T22:00:59.5815101Z"
---

# Memory Signal — ENG-437 closure (forge-memory)

## Memory Signal — Phase 4 closure

**Session**: ENG-437 release v1.3.0
**Commit**: 61f839f
**Tag**: v1.3.0 (local, not pushed)
**Verdict**: PASS (all 19 requirements)

### Decisions persisted (5 observations)
1. **Two-version model**: Product version (git tags) vs API/schema version ("1.1.0") — independent
2. **CHANGELOG-git tag mapping**: [0.1.0]→[1.0.0], [0.2.0]→[1.1.0], [0.3.0]→[1.2.1], new→[1.3.0]
3. **AD-2**: API version "1.1.0" in TIPO B files is independent — never touch during product bumps
4. **AD-3**: Historical docs (MIGRATION.md, SYNC-SETUP.md, ADR-004) are immutable
5. **ENG-437 implementation summary**: 17 files, 615 tests passed, 0 errors

### ADRs created (2 new)
- **ADR-009**: Two-version model — product vs API/schema version
- **ADR-010**: Historical documentation immutability policy

### Block closure check
- PM-* items in spec.md: none found → ✅ No blocking

### Project name drift detected
- Observations exist under: `engram-dotnet`, `team/engram-dotnet`, and `team/team/engram-dotnet`
- Awaiting human decision on merge (destructive operation)

### Session summary
- `engram_mem_session_summary` saved with full Goal/Discoveries/Accomplished/Files
