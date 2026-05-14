# Proposal: Verification Tools — Artifact Compliance & Traceability

## Intent

Currently engram-dotnet only persists and retrieves memory. It does not understand development context: what is a spec.md, a plan.md, or how to verify code against requirements.

To enable EngramFlow to have a Verify Agent that validates automatically that code matches the requirements (and writes a rework_ticket.md when not), engram-dotnet needs tools that understand the semantics of development artifacts.

This proposal introduces MCP tools that allow verifying code against a spec, tracing RF/RNF, and generating structured verification reports.

## Scope

### In Scope
- MCP tool mem_verify_artifact: takes spec.md + plan.md + diff → structured compliance report
- MCP tool mem_traceability: takes spec.md with RF/RNF list → checks coverage vs code
- Structured report format (JSON + markdown)
- Integration with cycle count for rework_ticket generation
- Basic support for spec.md with identifiable RF/RNF sections

### Out of Scope
- Deep semantic parsing of arbitrary spec.md (only canonical EngramFlow format)
- Auto-fix of code based on reports (Dev Agent does this, not engram-dotnet)
- UI visuals for reports (JSON + markdown only)

## Capabilities

### New Capabilities
- `artifact-verification`: Ability to verify code against spec.md, identify missing RF/RNF coverage, and generate structured verification reports in a canonical rework_ticket.md format.

### Modified Capabilities
- None (no changes to memory semantics)

## Approach

1. Define a canonical spec.md format that the tool can parse:
   - `## Objetivo` / `## Objective`
   - `## Functional Requirements` with items like `- RF-NNN:`
   - `## Non-Functional Requirements` with items like `- RNF-NNN:`
2. Implement `mem_verify_artifact` that:
   a. Reads spec.md and extracts RF/RNF
   b. Reads plan.md and extracts task lists
   c. Takes diff of changes (or accepts a file list)
   d. For each RF/RNF, evaluates whether the code covers it (via LLM-as-Judge with Sonnet)
   e. Generates a structured report: `{ passed: [...], failed: [...], untested: [...], coverage_pct }`
3. Implement `mem_traceability` that:
   a. From spec.md, builds a RF/RNF → code matrix via store searches
   b. Identifies RF/RNF without test coverage
   c. Output: structured traceability matrix
4. Canonical rework_ticket.md format:
```markdown
# Rework Ticket — Cycle {N}/{MAX}

## Failed Items
- [ ] {RF-NNN}: {reason}
- [ ] {RNF-NNNN}: {reason}

## Instructions
{Verify Agent → Dev Agent: what to change and why}
```
5. Cycle count persistence: increment on failed verification; escalate to human after max cycles.

## Affected Areas

| Area | Impact | Description |
|------|--------|-------------|

## Risks

- LLM misclassifications: mitigate with reasoning and confidence fields
- Non-canonical spec inputs: parser tolerance
- Cycle counts and persistence: ensure survive restarts

## Success Criteria

- mem_verify_artifact detects unimplemented RFs
- mem_traceability provides coverage matrix
- Report includes confidence per item
- Cycle escalation after max cycles

(End of file)
