# ENG-404 Spike — Learnings

**Date:** 2026-06-18  
**Verdict:** ✅ GO — proceed to feature ENG-404

---

## Summary

The spike validated that **cloning the existing `Engram.Verification` pattern
(TraceRepository + LineageBuilder) for general memory observations works in
M effort**, not the XL the backlog estimated. Zero schema changes needed.

## What was built

| File | Lines | Purpose |
|------|------:|---------|
| `src/Engram.Verification/MemoryRelation.cs` | 22 | Models: `MemoryRelation` + `MemoryRelationSet` |
| `src/Engram.Verification/MemoryRelationRepository.cs` | 121 | Save/Get/Delete via `IStore` topic_key upsert |
| `src/Engram.Verification/MemoryLineageBuilder.cs` | 148 | BFS with MaxHops=10, cycle detection |
| `tests/Engram.Verification.Tests/MemoryRelationsSpikeTests.cs` | 183 | 6 smoke tests (all pass) |
| **Total** | **474** | (291 src + 183 tests) |

## What was confirmed

1. **Pattern reusability**: `TraceRepository` save/get/upsert logic cloned
   cleanly with `memrel/{project}/{observationId}` topic_key.
2. **Zero schema changes**: relations persist as Engram observations
   themselves (via topic_key upsert at `SqliteStore.cs:583-621`). No new
   tables, no migration, no impact on existing data.
3. **BFS works**: cycle detection + MaxHops ceiling + visited set
   algorithm ported from `LineageBuilder.cs:33-95` translates to
   `long` observation IDs without modification.
4. **Validator is shared**: `RelationValidator.IsValidType` works for
   both `TraceRelation` and `MemoryRelation` — same 4 valid types.
5. **T1 loop is fast**: build + 6 tests = 0.8s, no Docker required.

## What surprised us

- **BFS direction matters**: `MemoryLineageBuilder` only follows
  **outgoing** edges (same as `LineageBuilder`). The original test case
  had inbound `related_to` edges, which the BFS correctly did NOT
  traverse. For the real feature, either:
  - Document "follows outgoing edges only" in the API
  - Add inverse traversal as a follow-up `M` task (scan all observations
    in the project for inbound edges)
- **Over by 45% on line count**: 291 src vs ~200 target. The extra
  ~90 lines come from XML doc comments (per `ADR-003`), `DeleteRelationAsync`
  (which the plan listed but didn't size), and `ToMemoryTraceNode` (fetches
  observation titles via `IStore`). Still well within M effort.
- **`Normalizers.NormalizeTopicKey` is case-insensitive**: `memrel/MyProj/42`
  round-trips to `memrel/myproj/42`. The FTS5 path (with `unicode61` tokenizer)
  handles this, so we didn't need explicit case handling.

## Design decisions for the real feature

These were deferred from the spike — surface them at spec time:

1. **Inverse traversal** (find descendants via inbound `related_to`)
   — `M` effort follow-up
2. **MCP tool API shape**:
   - `mem_relations(observation_id, action="add"|"get"|"delete")`
   - `mem_lineage_obs(observation_id, max_hops=10)`
3. **Sync semantics**: relations are observations → they sync naturally
   via `SyncManager`. But should `conflicts_with` cause sync conflict flags?
4. **Validation rules on insert**:
   - `supersedes` requires same `topic_key` (otherwise it's not a supersedence)
   - `conflicts_with` requires high semantic similarity (out of scope for spike)
5. **Retention**: should `memrel/{project}/*` observations count toward
   the project retention budget? (Probably yes — they're just observations.)

## Recommendation for next steps

1. Open ENG-404 as a real feature in the backlog (move from Icebox → Ready)
2. Use the flow: `forge-arch` for spec → `forge-plan` for plan → `forge-dev` for implementation
3. Spec should include:
   - 4 relation types (canonical, same as `RelationValidator`)
   - BFS algorithm (mirror `LineageBuilder` exactly)
   - Inverse traversal decision (M follow-up or v1 blocker?)
   - MCP tool API surface
   - Test coverage target: >85% (matches project policy)
4. Plan should include the `DeleteRelationAsync` UX (CLI command? MCP tool?)
5. The spike code in `src/Engram.Verification/` becomes the implementation
   starting point — refactor in place, don't move to `Engram.Store`

## Effort re-estimate

| Original (Icebox) | Spike estimate | Delta |
|-------------------|----------------|------|
| **XL** | **M** (with inverse traversal) or **S** (without) | -50% to -75% |

The XL estimate was based on the assumption that this was a greenfield
design. With the existing pattern, it's mostly integration + tests.

## Files / artifacts

- `src/Engram.Verification/MemoryRelation.cs` (new, 22 lines)
- `src/Engram.Verification/MemoryRelationRepository.cs` (new, 121 lines)
- `src/Engram.Verification/MemoryLineageBuilder.cs` (new, 148 lines)
- `tests/Engram.Verification.Tests/MemoryRelationsSpikeTests.cs` (new, 183 lines)
- `.ai-work/eng-404-spike/spike.md` (this directory's hypothesis)
- `.ai-work/eng-404-spike/learnings.md` (this file)

## Verdict

**GO** — implement ENG-404 as a real feature.

The pattern is proven. The risk is low. The only open design question
is inverse traversal, which can be deferred to a follow-up ENG without
blocking v1.
