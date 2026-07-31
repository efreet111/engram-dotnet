---
observation_id: 34
type: "session_summary"
title: "Session close 2026-07-13: FlowDoc v2.0 alignment + ENG-454/455/456/457 deferred"
created_at: "2026-07-13 22:54:07"
project: "team/flowforge"
scope: "personal"
generated_at: "2026-07-21T22:00:59.5751251Z"
---

# Session close 2026-07-13: FlowDoc v2.0 alignment + ENG-454/455/456/457 deferred

## Goal
Close the 2026-07-13 session covering: (1) verification of ENG-454/455/456/457 status in engram-dotnet, (2) discovery and partial fix of FlowDoc v1.1→v2.0 documentation drift in FlowForge, (3) consolidated gap capture and clean session closure.

## Instructions
- Use Spanish for user-facing observations (user preference)
- Memory Curation Protocol (ADR-001): always include what/why/where/learned
- Traceability via topic_key when saving related observations
- Sync project name: 'team/flowforge' (configured in .flowforge.json)

## Discoveries
- FlowDoc v2.0 was absorbed into FlowForge main on 2026-07-08 (commits d6a2288 + 0f3b872) but ~32 references to v1.1 remained in the codebase, including the pin in .flowforge.json
- The obs #27 (team/flowforge, 2026-07-09) proposed ENG-454, ENG-455, ENG-456, ENG-457 for engram-dotnet inspired by FlowDoc v2.0 patterns, but NO formal tickets were ever created in BACKLOG.md, ROADMAP.md or engram memory — only the proposal observation existed (anti-pattern per ADR-001)
- engram sync has 3 known bugs: CLI doesn't inherit env vars from opencode.json (FR-006), server GET /sync/enroll filter returns empty despite duplicate POST (server-side bug), and total_pulled counter doesn't increment even when data arrives (server-side counter bug)
- engram MCP tools (engram_mem_*) are not responding in this OpenCode session despite the MCP server process running — 3 stale mcp processes were detected

## Accomplished
- Verified ENG-454/455/456/457 status in engram-dotnet docs (BACKLOG.md ends at ENG-450 + 434a/b/c, no v2.0 FlowDoc tickets created). Created engram-dotnet/.ai-work/eng-454-457-deferred/summary.md (187 lines) consolidating findings and deferring to v1.1
- Added 'docs_framework: flowdoc@2.0' to .flowforge.json (was missing — ADR-004 requirement)
- Added FlowDoc v2.0 adopter callout to AGENTS.md
- Added '## FlowDoc Integration' section to ide/opencode/AGENTS.md
- Updated v1.1→v2.0 in 4 examples/docs: QUICKSTART.md, QUICKSTART.es.md, skills/forge-discovery/SKILL.md, ide/cursor/agents/forge-discovery.md
- Created .ai-work/flowdoc-v2-doc-drift/summary.md documenting remaining drift in templates/, docs/20-flowdoc-ecosystem.md, ADR-004 (preserved as historical)
- Committed 8 files (207 insertions, 15 deletions) to new branch flowdoc-v2-cleanup and pushed to origin

## Next Steps
- Create ADR-005 documenting FlowDoc v1.1→v2.0 migration decision (currently no ADR covers this — only ADR-004 covers original v1.1 adoption)
- Run /flow-start flowdoc-v2-doc-rewrite to: re-write docs/20-flowdoc-ecosystem.md for v2.0 features (Discovery block ADR-009, 45-line switch rule, R2 HU propagation, L1→L3 staged adoption)
- Sync 5 templates (PRD.md, HU-template.md, rfc-template.md, adr-template.md, HU-001-example.md) — requires own /flow-start cycle because affects downstream flow-init output
- Investigate why engram_mem_* MCP tools fail in OpenCode despite process running — possibly 3 stale mcp processes blocking the new one

## Relevant Files
- .flowforge.json (added docs_framework: flowdoc@2.0)
- AGENTS.md, ide/opencode/AGENTS.md (added FlowDoc v2.0 references)
- QUICKSTART.md, QUICKSTART.es.md (updated v1.1→v2.0 in examples)
- skills/forge-discovery/SKILL.md, ide/cursor/agents/forge-discovery.md (updated precondition example)
- .ai-work/flowdoc-v2-doc-drift/summary.md (new gap-capture document)
- /home/victor/Documentos/Proyectos/Desarrollo Personal/engram-dotnet/.ai-work/eng-454-457-deferred/summary.md (consolidated gap-capture for deferred ENG)
