---
cycle_count: 1
max_cycles: 3
status: "open"
severity: P2
---
# Rework ticket — deep-audit-fixes

## 1. Failure Reason

**FR-004 spec violation — ARCHITECTURE.md tool listing incomplete.**

The spec explicitly required: `docs/ARCHITECTURE.md | Tool count 26 → 28 (and add mem_relations, mem_lineage_obs to tool list)`.

The header numbers were updated (26 → 28 in headings), but the enumerated MCP tool listing still contains only 26 tool names. The two new tools — `mem_relations` and `mem_lineage_obs` — exist in the source code (`EngramTools.cs:1000` and `:1076`) but are absent from the ARCHITECTURE.md documentation.

Additionally, `EngramTools.cs:47` has a latent stale comment (`/// All 26 Engram MCP tools`) that wasn't in the spec's file table but should be corrected for consistency.

Classification: **Spec deviation** (documentation implementation incomplete).

## 2. Affected Files

- `docs/ARCHITECTURE.md` (lines 178-201 — MCP tool listing needs `mem_relations` and `mem_lineage_obs` added)
- `src/Engram.Mcp/EngramTools.cs` (line 47 — class comment says 26, should say 28)

## 3. Correction Instruction

**ARCHITECTURE.md**: Add a "Relations" group (or extend "Verification") to the MCP tool listing that includes:
```
Relations:
  mem_relations, mem_lineage_obs
```

The total groups should enumerate all 28 tools. Suggested grouping:
```
Verification:
  mem_verify_artifact, mem_traceability, mem_trace_source, mem_lineage

Relations:
  mem_relations, mem_lineage_obs
```

**EngramTools.cs:47**: Change `/// All 26 Engram MCP tools` to `/// All 28 Engram MCP tools`.

## 4. Close Criteria

- [ ] ARCHITECTURE.md tool listing includes `mem_relations` and `mem_lineage_obs` (total 28 names enumerated under 28-tool heading)
- [ ] EngramTools.cs:47 class comment updated from 26 → 28
- [ ] Verify: count tool names in ARCHITECTURE.md listing = 28
- [ ] Verify: `grep -c "\[McpServerTool(" src/Engram.Mcp/EngramTools.cs` = 28 (no change expected, confirmation only)
