## Verification Report

**Change**: traceability
**Version**: N/A
**Mode**: Standard

---

### Completeness

| Metric | Value |
|--------|-------|
| Tasks total | 18 |
| Tasks complete (marked [x]) | 18 |
| Tasks actually implemented | 14 |
| Tasks NOT implemented despite [x] | 4 |

#### Incomplete Tasks (marked [x] but NO code exists)

| Task | Description | Evidence |
|------|-------------|----------|
| 3.1 | Add `mem_trace_source(rf_id, project?)` to `EngramTools.cs` | `grep mem_trace_source src/Engram.Mcp/` → zero results. No tool registered. |
| 3.2 | Add `mem_lineage(rf_id, project?)` to `EngramTools.cs` | `grep mem_lineage src/Engram.Mcp/` → zero results. No tool registered. |
| 3.3 | Extend `mem_traceability` with source validity, warning, superseded_by | `mem_traceability` tool (lines 721-770) builds only FTS5 coverage matrix. Does NOT use `TraceRepository`, emit `source_status`, `source_warning`, or `superseded_by`. |
| 4.4/4.5/4.6 | Integration tests for MCP surface (`mem_trace_source`, `mem_lineage`, extended `mem_traceability`) | Mcp.Tests project has zero tests referencing trace/lineage tools. No trace/lineage test files in either test project. |

---

### Build & Tests Execution

**Build (Engram.Verification)**: ✅ Passed
**Build (Engram.Mcp)**: ✅ Passed

**Tests (Engram.Verification.Tests)**: ✅ 28 passed / ❌ 0 failed / ⚠️ 0 skipped
```
Total: 28 — all passed in 142ms
```

**Tests (Engram.Mcp.Tests)**: Not relevant for traceability (no trace/lineage tests exist)

**Coverage**: ➖ Not available (coverage tool detected in csproj but not run — threshold not configured)

---

### Spec Compliance Matrix

| Requirement | Scenario | Test | Result |
|-------------|----------|------|--------|
| REQ-01: Traceability Section | Valid Traceability Section | `SpecParserTests.ParseTraceability_ValidEntry_ExtractsAllFields` | ✅ COMPLIANT |
| REQ-01: Traceability Section | Missing Optional Author Field | `SpecParserTests.ParseTraceability_MissingAuthorDate_StillParses` | ✅ COMPLIANT |
| REQ-01: Traceability Section | Missing Required Rationale | (none) | ❌ UNTESTED — Code does NOT reject entries without Rationale; `TraceSource.Rationale` is `string?` nullable, no validation exists |
| REQ-02: mem_trace_source Tool | Traced Requirement | (none) | ❌ UNTESTED — `mem_trace_source` MCP tool does NOT exist |
| REQ-02: mem_trace_source Tool | Untraced Requirement | (none) | ❌ UNTESTED — `mem_trace_source` MCP tool does NOT exist |
| REQ-02: mem_trace_source Tool | Project Not Provided | (none) | ❌ UNTESTED — `mem_trace_source` MCP tool does NOT exist |
| REQ-03: mem_lineage Tool | Simple Lineage | (none) | ❌ UNTESTED — `mem_lineage` MCP tool does NOT exist (backend `LineageBuilder` exists but unwired) |
| REQ-03: mem_lineage Tool | Lineage with Reworks | (none) | ❌ UNTESTED — `mem_lineage` MCP tool does NOT exist |
| REQ-03: mem_lineage Tool | Cyclic Relations Detected | `LineageTests.HasCycle_DirectCycle_Detected` (backend only, no MCP test) | ⚠️ PARTIAL — `TraceRepository.HasCycle` works, `LineageBuilder` tracks `CycleDetected` flag, but no `mem_lineage` MCP tool to surface it |
| REQ-03: mem_lineage Tool | Max Depth Reached | `LineageTests.BuildLineage_MaxHops_Truncates` (backend only) | ⚠️ PARTIAL — Backend truncates at 10 hops, but no MCP tool |
| REQ-04: Traceability Persistence | Persist New Traceability | `TraceIntegrationTests.SaveAndGetTrace_Roundtrip_Works` | ✅ COMPLIANT |
| REQ-04: Traceability Persistence | Update Existing Traceability | (none) | ❌ UNTESTED — `TraceRepository.SaveTraceAsync` always calls `AddObservationAsync` (creates new obs). No update-if-exists logic. Risk of duplication. |
| REQ-05: Relation Types | Valid Relation Syntax | `SpecParserTests.ParseRelations_ValidTypes_ParsesCorrectly` | ✅ COMPLIANT |
| REQ-05: Relation Types | Invalid Relation Type | `SpecParserTests.ParseRelations_InvalidType_SkipsEntry` | ⚠️ PARTIAL — Test verifies skip behavior, but spec says entry should be "marked as invalid with reason". Code silently skips, no error reporting. |
| REQ-05: Relation Types | Multiple Relations | `SpecParserTests.ParseRelations_ValidTypes_ParsesCorrectly` (4 types) | ✅ COMPLIANT |
| REQ-06: Cycle Detection | Direct Cycle | `LineageTests.HasCycle_DirectCycle_Detected` | ✅ COMPLIANT |
| REQ-06: Cycle Detection | Indirect Cycle | (none) | ❌ UNTESTED — No test for chain A→B→C→B. `HasCycle` test only covers A→B→A. |
| REQ-07: Source Validity Verification | Active Source | (none) | ❌ UNTESTED — `mem_traceability` tool not extended |
| REQ-07: Source Validity Verification | Inactive Source Warning | (none) | ❌ UNTESTED — `mem_traceability` tool not extended |
| REQ-07: Source Validity Verification | Source Without API Access | (none) | ❌ UNTESTED — `mem_traceability` tool not extended |
| REQ-07: Source Validity Verification | Superseded Requirement | (none) | ❌ UNTESTED — `mem_traceability` tool not extended |

**Compliance summary**: 7/21 scenarios fully COMPLIANT, 3/21 PARTIAL, 11/21 UNTESTED

---

### Correctness (Static — Structural Evidence)

| Requirement | Status | Notes |
|------------|--------|-------|
| REQ-01: Traceability Section Parsing | ⚠️ Partial | Parser exists (`SpecParser.cs`, `ParseTraceability()`, `TryParseTraceability()`) with field parsing and relation extraction. But: does NOT reject entries without Rationale (spec MUST requirement). `TraceSource.Rationale` is nullable with no validation. |
| REQ-02: mem_trace_source Tool | ❌ Missing | Backend `TraceRepository.GetTraceAsync()` exists with `topic_key: trace/{project}/{rf-id}` lookup. But no MCP tool registered. `grep MemTraceSource src/Engram.Mcp/` → 0 results. |
| REQ-03: mem_lineage Tool | ❌ Missing | Backend `LineageBuilder.BuildLineageAsync()` exists with BFS, `HashSet visited`, 10-hop limit, cycle detection. But no MCP tool registered. `grep MemLineage src/Engram.Mcp/` → 0 results. |
| REQ-04: Traceability Persistence | ⚠️ Partial | `TraceRepository.SaveTraceAsync()` uses `topic_key: trace/{project}/{rf-id}` via `AddObservationAsync`. But always creates new observations — no update-if-exists. Update scenario (spec says "updated, not duplicated") is not implemented. |
| REQ-05: Relation Types | ⚠️ Partial | `SpecParser.ParseRelations()` handles 4 types (`depends_on`, `supersedes`, `conflicts_with`, `related_to`) with case-insensitive matching. `RelationValidator` validates types. But: invalid types silently skipped instead of reporting error as spec requires. |
| REQ-06: Cycle Detection | ⚠️ Partial | `LineageBuilder` has BFS with visited set and 10-hop limit. `TraceRepository.HasCycle()` uses DFS with recursion stack. Direct cycle tested. Indirect cycle untested. |
| REQ-07: Source Validity Verification | ❌ Missing | `TraceabilityEntry` model has `SourceStatus`, `SourceWarning`, `SupersededBy` fields declared. But NO code populates these fields. `mem_traceability` tool (lines 721-770) builds FTS5-based matrix only — never queries `TraceRepository`. |

---

### Coherence (Design vs Implementation)

| Decision | Followed? | Notes |
|----------|-----------|-------|
| Storage: `topic_key` observations | ✅ Yes | `TraceRepository` uses `trace/{project}/{requirementId}` |
| Relations: Text in content | ✅ Yes | Serialized JSON via `JsonSerializer.Serialize(trace)` |
| Cycle detection: HashSet + 10 hops | ✅ Yes | `LineageBuilder.MaxHops = 10`, `HashSet visited` in BFS |
| SpecParser: Unified parser | ✅ Yes | `SpecParseResult` includes `Traceability: List<TraceInfo>` |
| Source format: String libre | ✅ Yes | `TraceSource.Source` is `string`, not enum |
| Source validity API: "unknown" + note | ❌ Not implemented | Fields exist on `TraceabilityEntry` model but never populated |
| Design model: `TraceRequirement` | ⚠️ Renamed | Implemented as `TraceInfo` — fields differ slightly (no `IsValid`/`InvalidReason` on actual model) |
| Design model: `LineageNode`/`LineageTree` | ⚠️ Renamed | Implemented as single `LineageResult` with `Ancestors`/`Descendants` lists instead of tree |
| MCP Tools surface | ❌ Not implemented | Design specifies `mem_trace_source`, `mem_lineage`, extended `mem_traceability` — none wired |
| Data Flow: `spec.md → SpecParser → mem_trace_source/mem_lineage/mem_traceability` | ❌ Broken | Backend exists but MCP surface is the missing link |

---

### Issues Found

**CRITICAL** (must fix before archive):

1. **`mem_trace_source` MCP tool missing** — Tasks 3.1 marked [x] but zero implementation. Backend `TraceRepository.GetTraceAsync()` exists but no tool exposes it. Breaks REQ-02 entirely (3 scenarios untested).

2. **`mem_lineage` MCP tool missing** — Task 3.2 marked [x] but zero implementation. Backend `LineageBuilder` exists but no tool. Breaks REQ-03 entirely (4 scenarios untested).

3. **`mem_traceability` not extended with source validity** — Task 3.3 marked [x] but the tool (lines 721-770) only builds FTS5 coverage matrix. No `TraceRepository` usage, no `source_status`/`source_warning`/`superseded_by` output. Breaks REQ-07 entirely (4 scenarios untested).

4. **Spec parser does NOT reject entries without Rationale** — `ParseTraceSubsection()` creates `TraceSource` whenever `sourceValue != null`, ignoring Rationale absence. Spec REQUIRES rejection of entries without Rationale. `TraceSource.Rationale` is `string?` nullable — violates MUST requirement.

5. **TraceRepository always creates, never updates** — `SaveTraceAsync()` calls `AddObservationAsync` without checking for existing observation. Spec scenario "Update Existing Traceability" requires update (not duplication). Risk: duplicate trace observations accumulate.

**WARNING** (should fix):

6. **Invalid relations silently skipped** — `SpecParser.ParseRelations()` silently discards invalid types (regex doesn't match). Spec says entry should be "marked as invalid with reason 'Unknown relation type'". No error reporting.

7. **Design model mismatch** — Design specifies `TraceRequirement` with `IsValid`/`InvalidReason`; implementation uses `TraceInfo` without validation fields. Design specifies `LineageNode`/`LineageTree`; implementation uses `LineageResult` with flat lists instead of tree structure.

8. **No indirect cycle test** — `LineageTests.HasCycle_DirectCycle_Detected` only tests A→B→A. No test for chain A→B→C→B (indirect cycle at depth 3). Spec scenario exists but untested.

9. **No MCP integration tests for trace tools** — `Engram.Mcp.Tests` has zero tests referencing traceability. All traceability tests are in `Engram.Verification.Tests` but only test the backend layer.

10. **Spec scenario count inconsistency** — Summary says "19 scenarios" but actual count is 21 (3+3+4+2+3+2+4). Minor documentation bug.

**SUGGESTION** (nice to have):

11. Add coverage tooling to verification pipeline (`coverlet.collector` already in csproj, just not executed).
12. Consider wiring `SpecParser` to call `TraceRepository.SaveTraceFromSpecAsync` automatically (task 2.1 says "Persist... in SpecParser" but persistence is separate).
13. Add `IsValid`/`InvalidReason` to `TraceInfo` record to support the spec's "marked as invalid" requirement.

---

### Verdict

**FAIL** — 4 CRITICAL issues. The backend infrastructure (Models, SpecParser, TraceRepository, LineageBuilder, RelationValidator) is solid, but the MCP tool surface (tasks 3.1, 3.2, 3.3) is completely missing. 11 of 21 spec scenarios are UNTESTED because the tools that would cover them don't exist. Additionally, the parser doesn't enforce the MUST requirement for Rationale (REQ-01 scenario 3), and persistence always creates new observations instead of updating existing ones.

The 3 MCP tools need to be added to `EngramTools.cs` using the existing `TraceRepository` and `LineageBuilder` backends before this change can be archived.
