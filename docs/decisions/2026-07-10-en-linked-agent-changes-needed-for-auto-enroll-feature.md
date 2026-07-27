---
observation_id: 32
type: "pattern"
title: "EN linked: agent changes needed for auto-enroll feature"
created_at: "2026-07-10 00:04:12"
topic_key: "flowforge/auto-enroll-agent-changes"
project: "team/flowforge"
scope: "team"
generated_at: "2026-07-21T22:00:59.5759721Z"
---

# EN linked: agent changes needed for auto-enroll feature

**What**: Cross-link to the EN `engram-dotnet/auto-enroll-on-first-save` (saved 2026-07-09 in engram-dotnet project). This observation tracks the FlowForge-side agent changes that the EN will require.

**Why**: The EN is the canonical spec. This observation exists so that when an agent runs `mem_search` filtered to `team/flowforge` (not `team/engram-dotnet`), they find the agent-side follow-ups.

**Where**:
- EN: `engram-dotnet/auto-enroll-on-first-save` (in engram-dotnet project)
- This observation: in team/flowforge project (linked to the EN)

**Learned**:

If `engram-dotnet` ships the auto-enroll feature, the following FlowForge agent updates are needed (per the EN's "What the agents need to change" section):

| Agent | File | Change |
|-------|------|--------|
| `forge-memory` | `skills/forge-memory/SKILL.md` | Add `## Sync Status` block to session summary output (enrolled: yes/no, pending push, last sync) |
| `forge-verify` | `skills/forge-verify/SKILL.md` | Add REWORK check: sync enabled but not enrolled → issue ticket with remediation message |
| `forge-discovery` | `skills/forge-discovery/SKILL.md` | Add `## Sync Status` block to Context Map output (visible at start of every feature) |
| All agents | `ide/{cursor,opencode,vscode,antigravity}/...` | Mirror the SKILL.md changes per the parity contract (`ide/shared/workflow-orchestrator-parity.md`) |

**Implementation order** (after engram-dotnet ships the auto-enroll):
1. Update `skills/forge-memory/SKILL.md` first (most user-facing)
2. Update `skills/forge-verify/SKILL.md` (mechanical check)
3. Update `skills/forge-discovery/SKILL.md` (Context Map enhancement)
4. Mirror to IDE-specific agents for parity

**Tracking**:
- EN ID: `engram-dotnet/auto-enroll-on-first-save`
- Implementation owner: user (Victor) — will work on engram-dotnet first
- This FlowForge-side work is blocked on the engram-dotnet release
- Estimated effort: 30-45 min once engram-dotnet supports it

**Reference ADR**: When the EN becomes a real implementation, write an ADR in `FlowForge/docs/decisions/ADR-NNN-auto-enroll-agent-changes.md` documenting the agent-side changes. Suggested title: "ADR-011 — Agent awareness of engram sync enrollment status".
