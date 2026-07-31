---
observation_id: 25
type: "discovery"
title: "ENG-453: installer missing ENGRAM_SERVER_URL prompt (FlowForge)"
created_at: "2026-07-08 03:54:53"
topic_key: "eng-453-installer-server-url"
project: "team/engram-dotnet"
scope: "team"
generated_at: "2026-07-21T22:00:59.5792504Z"
---

# ENG-453: installer missing ENGRAM_SERVER_URL prompt (FlowForge)

**What**: ENG-453 documented — FlowForge installer doesn't prompt for ENGRAM_SERVER_URL in sync mode, causing silent self-loop.

**Why**: When user installs with mode=sync, installer doesn't ask for server URL → SyncManager has no URL → detects self-loop (ENG-452) → disables. User thinks sync works but it doesn't.

**Where**: 
- FlowForge repo: src/FlowForge.Installer/InstallCommand.cs (lines 113-121, 201-202), EngramModule.cs (lines 204-205, 264-265)
- ADR-010: docs/decisions/ADR-010-installer-prompt-for-server-url.md (complete design, 235 lines, status: Proposed)
- Context map: .ai-work/eng-453-installer-server-url/context-map.md

**Learned**: 
- ADR-010 is complete (235 lines) with 3 bugs identified, new flows, test plan (7 tests), rollout plan
- Just needs implementation + status change to "Accepted"
- Hardcoded IP 192.168.0.178 must be removed entirely
- POST-INSTALL.md §3 documents manual workaround (to be removed after fix)
- Complementary: ADR-009 (flowforge sync connect command) for post-install URL changes
- Effort: S (1-2h), Priority: P1
