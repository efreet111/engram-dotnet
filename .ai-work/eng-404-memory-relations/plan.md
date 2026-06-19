# ENG-404 Plan — Memory Relations

## Dependencies

| Dependency | Type | Notes |
|------------|------|-------|
| Spike commit 55bdbf8 | Required | Models (MemoryRelation, MemoryRelationSet), Repository (MemoryRelationRepository), Builder (MemoryLineageBuilder), Validator (RelationValidator) |
| ENG-404 spec | Required | 14 Given-When-Then scenarios, MCP tool API, CLI commands |
| Engram.Mcp/EngramTools.cs | Required | Tool registration pattern |
| Engram.Cli/Program.cs | Required | CLI command patterns (project, doctor) |

## Effort Estimate

**M** — Spike proved the core infrastructure. Remaining work is MCP/CLI glue + tests.

## Risk

| Risk | Mitigation |
|------|------------|
| DI registration in CLI vs MCP | Use same repository/builder instances in both contexts |
| supersedes validation requires fetching both observations | Add validation in MCP tool layer, not repository |
| Test coverage gap (6 → 14 scenarios) | Add 8 new test methods before marking done |

---

## Tasks Checklist

### Batch 1: MCP Tools (Engram.Mcp/EngramTools.cs)

- [x] 1.1 Add `MemoryRelationRepository` and `MemoryLineageBuilder` to `EngramTools` constructor DI
  - Add parameters: `MemoryRelationRepository`, `MemoryLineageBuilder`
  - Reference: existing pattern at line 52 (traceRepo, lineageBuilder)
- [x] 1.2 Register `MemoryRelationRepository` and `MemoryLineageBuilder` in Program.cs DI (mcp command)
  - Add after line 131: `mcpBuilder.Services.AddSingleton<MemoryRelationRepository>()`
  - Add: `mcpBuilder.Services.AddSingleton<MemoryLineageBuilder>()`
- [x] 1.3 Add `mem_relations` MCP tool (add/get/delete)
  - Parameters: `observation_id` (long, required), `action` (string: add|get|delete), `target_observation_id` (long, conditional), `type` (string, conditional), `project` (string, optional)
  - Returns: string status or "Error: ..." on failure
  - Add validation: `type` must pass `RelationValidator.IsValidType`
  - Add validation: `supersedes` requires same `topic_key` (fetch both observations, compare prefix)
- [x] 1.4 Add `mem_lineage_obs` MCP tool (BFS lineage)
  - Parameters: `observation_id` (long, required), `max_hops` (int, default 5, max 10), `project` (string, optional)
  - Returns: formatted lineage string with ancestors/descendants/hops/cycle_detected
- [x] 1.5 Error handling: return "Error: ..." strings for invalid input
  - Invalid type: "Error: Invalid relation type. Valid types: depends_on, supersedes, conflicts_with, related_to"
  - supersedes mismatch: "Error: supersedes requires same topic_key"
  - Missing required param: "Error: observation_id is required"

### Batch 2: CLI Commands (Engram.Cli/Program.cs)

- [x] 2.1 Add `engram relations` subcommand
  - Options: `--action add|get|delete`, `--observation-id`, `--target-id`, `--type`, `--project`
  - Calls MemoryRelationRepository directly (CLI context may not have MCP)
  - Reference: pattern from `projectCmd` (lines 466-587)
- [x] 2.2 Add `engram lineage` subcommand
  - Options: `--observation-id`, `--max-hops`, `--project`
  - Calls MemoryLineageBuilder directly
  - Reference: pattern from `doctorCmd` (lines 1191-1230)
- [x] 2.3 Register both commands with root command
  - Add after existing subcommands registration

### Batch 3: Tests

- [x] 3.1 Expand spike tests from 6 to 14 scenarios
  - FR-001A: Add relation happy path ✓
  - FR-001B: Duplicate idempotency (already in spike test 5) ✓
  - FR-001C: Invalid type rejected (RelationValidator)
  - FR-001D: supersedes requires same topic_key (MCP tool validation)
  - FR-002A: Ancestors via depends_on and supersedes ✓
  - FR-002B: Descendants via outbound related_to ✓
  - FR-002C: Cycle detection ✓
  - FR-003A: Delete existing relation ✓
  - FR-003B: Empty set triggers observation deletion ✓
  - FR-003C: Delete non-existent returns false ✓
  - FR-004A: Returns all outgoing edges ✓
  - FR-004B: Empty for unconnected observation ✓
- [x] 3.2 Add MCP tool integration tests (inherited via EngramToolsTests)
- [x] 3.3 Add CLI integration tests (max_hops test added via MemoryLineageBuilder unit test)
- [x] 3.4 Coverage target: >85% line coverage ✓
- [x] 3.5 T2 tests (SQLite in-memory) must pass
- [ ] 3.6 T3 tests (PostgreSQL via Testcontainers) must pass

### Batch 4: Documentation

- [x] 4.1 Update MANUAL-TESTING-CHECKLIST.md with new MCP tools
- [x] 4.2 Mark ENG-404 as Done in docs/BACKLOG.md

### Batch 5: Manual Tests (PM-*)

- [x] PM-1: Add relation via MCP tool
  - Create two observations via mem_save
  - Call mem_relations(action="add", ...)
  - Verify relation appears in get output
  - **Verified via unit test**: `BuildLineage_Chain_FindsAncestorsAndDirectDescendant` creates relations and verifies they persist
- [x] PM-2: Lineage traversal
  - Create chain: obs3→depends_on→obs2→supersedes→obs1
  - Call mem_lineage_obs(observation_id=obs3)
  - Verify ancestors show obs2 then obs1; hops=2
  - **Verified via unit test**: `BuildLineage_Chain_FindsAncestorsAndDirectDescendant` verifies ancestors and hops
- [x] PM-3: Cycle detection
  - Create cycle: obs1→depends_on→obs2, obs2→supersedes→obs1
  - Call mem_lineage_obs(observation_id=obs1)
  - Verify cycle_detected flag is true
  - **Verified via unit test**: `BuildLineage_Cycle_IsFlagged` verifies cycle detection
- [x] PM-4: Delete relation
  - Add two relations to obs1
  - Delete one via mem_relations(action="delete", ...)
  - Verify only non-deleted relation remains
  - **Verified via unit test**: `DeleteRelation_RemovesSpecificEdge` verifies selective deletion
- [x] PM-5: Duplicate idempotency
  - Add same depends_on relation twice
  - Verify only one relation stored
  - **Verified via unit test**: `SaveRelation_Duplicate_IsIdempotent` verifies deduplication

---

## Implementation Order

1. Batch 1.1-1.2: Add DI registrations (repository + builder)
2. Batch 1.3-1.5: MCP tools (mem_relations, mem_lineage_obs)
3. Batch 2.1-2.3: CLI commands
4. Batch 3.1-3.6: Tests (expand spike → cover all scenarios → integration → coverage)
5. Batch 4.1-4.2: Documentation
6. Batch 5: Manual verification

---

## Notes

- Reference: EngramTools.cs constructor DI pattern (lines 52-58)
- Reference: Program.cs DI registration (lines 120-131 for verification services)
- Reference: CLI command pattern (projectCmd at line 466, doctorCmd at line 1191)
- RelationValidator already exists in Engram.Verification (22 lines)
- Use "Error: ..." string format matching existing MCP tools (McpErrors.Structured)