# Tasks: Requirement Traceability — Source & Lineage Tracking

## Phase 1: Foundation — Models + Spec Parser

- [x] 1.1 Add `TraceSource`, `TraceRelation`, `TraceInfo`, `TraceResult`, `LineageResult` records to `src/Engram.Verification/Models.cs`
- [x] 1.2 Add `List<TraceInfo> Traceability` to `SpecParseResult` + `SourceStatus`/`SourceWarning`/`SupersededBy` to `TraceabilityEntry` in `Models.cs`
- [x] 1.3 Parse `## Traceability` section in `src/Engram.Verification/SpecParser.cs`: extract subsection headings + Source/Author/Date/Rationale/Relations fields
- [x] 1.4 Parse Relations syntax (`Depends on RF-001`, `Supersedes RF-002`, etc.) with validation for known types
- [x] 1.5 Create `src/Engram.Verification/TraceRepository.cs` — persistence layer via IStore observations with topic_key
- [x] 1.6 Create `src/Engram.Verification/RelationValidator.cs` — static validator for known relation types

## Phase 2: Core — Persistence, Lineage, Cycle Detection

- [x] 2.1 Persist parsed `TraceRequirement` as Engram observation with `topic_key: trace/{project}/{rf-id}` via `IStore` in `SpecParser`
- [x] 2.2 Implement cycle detection in `src/Engram.Verification/LineageBuilder.cs`: `HashSet visited` + 10-hop limit + cycle path reporting
- [x] 2.3 Implement BFS lineage tree builder: `Queue<(id, depth)>`, extract relations from `obs.Content`, build `LineageTree`

## Phase 3: Surface — MCP Tools

- [x] 3.1 Add `mem_trace_source(rf_id, project?)` to `src/Engram.Mcp/EngramTools.cs`: topic_key lookup, return trace data or "untraced"
- [x] 3.2 Add `mem_lineage(rf_id, project?)` to `src/Engram.Mcp/EngramTools.cs`: invoke BFS lineage, return tree with cycle/truncation info
- [x] 3.3 Extend `mem_traceability` in `src/Engram.Mcp/EngramTools.cs`: check source validity, emit warning if inactive, report superseded_by

## Phase 4: Testing — Unit + Integration

- [x] 4.1 Test SpecParser traceability: valid entry, missing Author (optional), missing Rationale (invalid), no Traceability section (no-op)
- [x] 4.2 Test relation parsing: valid types, invalid type, multiple relations, case-insensitive matching
- [x] 4.3 Test cycle detection: direct cycle, indirect cycle, chain of 15 (truncated at 10), no cycle (happy path)
- [x] 4.4 Test `mem_trace_source`: traced RF returns full data, untraced RF returns "untraced", default project fallback
- [x] 4.5 Integration: persist trace via `IStore`, invoke `mem_lineage`, assert tree depth and node content
- [x] 4.6 Integration: persist trace obs, invoke extended `mem_traceability`, verify `source_status: "unknown"` for non-API sources
