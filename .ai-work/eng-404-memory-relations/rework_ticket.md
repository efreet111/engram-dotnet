---
cycle_count: 2
max_cycles: 3
status: "resolved"
---
# Rework Ticket — ENG-404

## 1. Failure Reason

**DEAD PARAMETER: `max_hops` accepted but never used** (False Green).

Both the MCP tool `mem_lineage_obs` (`EngramTools.cs:1085`) and the CLI command `engram lineage` (`Program.cs:1306`) accept a `max_hops` parameter, validate it with `Math.Clamp(max_hops, 1, 10)`, but **NEVER pass it** to `MemoryLineageBuilder.BuildLineageAsync()`. The builder method signature has no `maxHops` parameter — it uses the hardcoded constant `MaxHops = 10` for all traversals.

### Impact
- Spec FR-002 requires `max_hops` to default to **5** and max at **10**. Currently, BFS always traverses up to 10 hops regardless of what the user provides.
- `mem_lineage_obs(observation_id=3, max_hops=2)` would still traverse to depth 10.
- The `Hops` value in the output reflects actual depth reached (up to 10), not the requested limit.

### Evidence
- `EngramTools.cs` line 1085: `var clampedHops = Math.Clamp(max_hops, 1, 10);` — computed but unused.
- `EngramTools.cs` line 1088: `var result = await _memLineageBuilder.BuildLineageAsync(resolvedProject, observation_id);` — no `maxHops` passed.
- `Program.cs` line 1306: `var clampedHops = Math.Clamp(maxHops, 1, 10);` — computed but unused.
- `Program.cs` line 1312: `var result = await builder.BuildLineageAsync(project, obsId);` — no `maxHops` passed.
- `MemoryLineageBuilder.cs` line 48: `public const int MaxHops = 10;` — hardcoded, no parameter override.
- `MemoryLineageBuilder.cs` line 74: `while (queue.Count > 0 && hops < MaxHops)` — uses constant, not parameter.

### Classification
**False Green** — the parameter parses and validates correctly, giving the illusion of functionality, but has zero effect on behavior. Tests pass because none verify that `max_hops` limits traversal depth.

## 2. Affected Files

- `src/Engram.Verification/MemoryLineageBuilder.cs` — `BuildLineageAsync` must accept `maxHops` parameter
- `src/Engram.Mcp/EngramTools.cs` — `MemLineageObs` must pass `clampedHops` to builder
- `src/Engram.Cli/Program.cs` — `lineageCmd` handler must pass `clampedHops` to builder
- `tests/Engram.Verification.Tests/MemoryRelationsSpikeTests.cs` — add test: `max_hops=1` should only traverse 1 hop

## 3. Correction Instruction

1. **Add `maxHops` parameter to `MemoryLineageBuilder.BuildLineageAsync`**:
   ```csharp
   public async Task<MemoryLineageResult> BuildLineageAsync(string project, long rootObservationId, int maxHops = 5)
   ```
   - Clamp internally: `int effectiveMaxHops = Math.Clamp(maxHops, 1, MaxHops);`
   - Use `effectiveMaxHops` in the while-loop condition instead of the constant `MaxHops`.
   - Keep `MaxHops = 10` as hard ceiling.

2. **Pass `clampedHops` from MCP tool** (`EngramTools.cs:1088`):
   ```csharp
   var result = await _memLineageBuilder.BuildLineageAsync(resolvedProject, observation_id, clampedHops);
   ```

3. **Pass `clampedHops` from CLI** (`Program.cs:1312`):
   ```csharp
   var result = await builder.BuildLineageAsync(project, obsId, clampedHops);
   ```

4. **Add test**: Create a chain of 3 observations and verify that `max_hops=1` returns only 1 hop (not the full chain).

---

## ✅ Verification Confirmed (2026-06-18)

All 4 fix points verified by `forge-verify` (cycle 2):

| # | Fix | File | Status |
|---|-----|------|--------|
| 1 | `maxHops` param added to `BuildLineageAsync` | `MemoryLineageBuilder.cs:62` | ✅ |
| 2 | `clampedHops` passed from MCP | `EngramTools.cs:1088` | ✅ |
| 3 | `clampedHops` passed from CLI | `Program.cs:1312` | ✅ |
| 4 | `BuildLineage_MaxHops_LimitsTraversalDepth` test | `MemoryRelationsSpikeTests.cs:295` | ✅ |

**T2 tests**: 13/13 pass, 0 failures. **Verdict: PASS.**
