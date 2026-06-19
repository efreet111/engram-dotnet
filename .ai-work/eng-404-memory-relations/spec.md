# Spec: ENG-404 — Memory Relations

## 1. Objective and Scope

### Problem Statement

Memory observations in engram-dotnet are isolated nodes. There is no way to express that one observation depends on another, supersedes an older entry, conflicts with a parallel claim, or is thematically related. This limits the system's ability to answer questions like "what does this observation build on?" or "what contradicts it?" — queries that require graph traversal, not flat search.

### Objective

Add typed directed relations between memory observations, persisted as Engram observations themselves, with BFS-based lineage traversal and MCP tool access. The feature clones the proven `Engram.Verification` trace pattern (verified by spike commit 55bdbf8) at M effort.

### Scope

**In v1:**
- 4 canonical relation types: `depends_on`, `supersedes`, `conflicts_with`, `related_to`
- `mem_relations` MCP tool for add/get/delete operations
- `mem_lineage_obs` MCP tool for BFS lineage tree from an observation ID
- **CLI commands**: `engram relations` (add/get/delete) and `engram lineage` (traversal) — thin wrappers around MCP tools
- Relations stored as Engram observations under `memrel/{project}/{observationId}` (zero schema changes)
- BFS traversal: MaxHops=10 hard ceiling, **default 5**, cycle detection via visited set, follows outgoing edges only
- Idempotent saves (deduplicates by type+target)
- `supersedes` validation: source and target must share the same `topic_key` (enforced at MCP tool level)
- Retention: `memrel/*` observations count toward project budget (they are ordinary observations)
- Sync: relations sync naturally via SyncManager; `conflicts_with` does NOT trigger sync conflict flags in v1

**Out of v1:**
- Inverse traversal (finding descendants via inbound edges) — deferred to follow-up ENG, estimated M effort
- Semantic similarity validation for `conflicts_with` — agent's responsibility
- Moving spike code from `Engram.Verification` to `Engram.Store` — spike code is the implementation; refactor in place

---

## 2. Functional Requirements

### FR-001: Add Relation

Add a typed directed edge from one observation to another.

*Scenario A — Happy path (new relation):*
- GIVEN observation `A` (id=10) exists in project `myproj`
- WHEN `mem_relations` is called with `action="add"`, `observation_id=10`, `target_observation_id=20`, `type="depends_on"`
- THEN the relation is persisted as a `MemoryRelationSet` under `memrel/myproj/10`
- AND subsequent `mem_relations(action="get", observation_id=10)` returns the relation

*Scenario B — Duplicate is idempotent:*
- GIVEN observation `A` already has a `depends_on→20` relation
- WHEN `mem_relations` is called again with the same type and target
- THEN no duplicate is added
- AND the existing relation set is unchanged

*Scenario C — Invalid type is rejected:*
- GIVEN observation `A` (id=10) exists
- WHEN `mem_relations` is called with `type="invalid_type"`
- THEN an error is returned indicating the type is not valid
- AND no observation is created

*Scenario D — supersedes requires same topic_key:*
- GIVEN observation `A` has `topic_key="obs/myproj/10"`, observation `B` has `topic_key="different/42"`
- WHEN `mem_relations` is called with `type="supersedes"` and `target_observation_id=B.id`
- THEN an error is returned: supersedes requires same topic_key
- AND no relation is created

---

### FR-002: Query Lineage

Build a lineage tree from a root observation using BFS traversal.

*Scenario A — Ancestors found via depends_on and supersedes:*
- GIVEN a chain: `C` (id=3) supersedes `B` (id=2) which depends_on `A` (id=1)
- WHEN `mem_lineage_obs` is called with `observation_id=3`
- THEN ancestors list contains `B` then `A` (in traversal order)
- AND descendants list is empty
- AND `hops` equals 2
- AND `cycle_detected` is false

*Scenario B — Descendants found via outbound related_to:*
- GIVEN `A` (id=1) has an outbound `related_to→B` (id=2)
- WHEN `mem_lineage_obs` is called with `observation_id=1`
- THEN descendants list contains `B`
- AND ancestors list is empty

*Scenario C — Cycle is detected and flagged:*
- GIVEN `A` (id=1) depends_on `B` (id=2), and `B` supersedes `A` (cycle)
- WHEN `mem_lineage_obs` is called with `observation_id=1`
- THEN `cycle_detected` is true
- AND traversal stops at MaxHops=10 ceiling
- AND partial results are returned

---

### FR-003: Delete Relation

Remove a specific relation from an observation's outgoing edge list.

*Scenario A — Existing relation removed:*
- GIVEN observation `A` has two relations: `depends_on→B` and `related_to→C`
- WHEN `mem_relations` is called with `action="delete"`, `observation_id=A`, `target_observation_id=B`, `type="depends_on"`
- THEN only the `depends_on→B` relation is removed
- AND `related_to→C` remains
- AND `get` returns only the remaining relation

*Scenario B — Empty set triggers observation deletion:*
- GIVEN observation `A` has exactly one relation `depends_on→B`
- WHEN `mem_relations` is called to delete that relation
- THEN the underlying `memrel/{project}/A` observation is deleted entirely
- AND subsequent `get` returns an empty list

*Scenario C — Delete non-existent relation returns false:*
- GIVEN observation `A` has no relations
- WHEN `mem_relations` is called with `action="delete"` and any target
- THEN the operation returns indicating no relation was found
- AND no error is raised

---

### FR-004: Get Relations

Retrieve all outgoing relations for an observation.

*Scenario A — Returns all outgoing edges:*
- GIVEN observation `A` has 3 relations: `depends_on→B`, `supersedes→C`, `related_to→D`
- WHEN `mem_relations` is called with `action="get"` and `observation_id=A`
- THEN all 3 relations are returned with correct `type` and `target_observation_id`

*Scenario B — Empty for unconnected observation:*
- GIVEN observation `X` has no relations stored
- WHEN `mem_relations` is called with `observation_id=X`
- THEN an empty list is returned
- AND no error is raised

---

### FR-005: Inverse Traversal

*Scenario A — Documented as v1 limitation:*
- GIVEN observation `B` has an inbound `related_to` from observation `A`
- WHEN `mem_lineage_obs` is called from `B`'s perspective
- THEN `A` is NOT found in the descendants list (BFS only follows outbound edges from B)
- AND the documentation states this is a known limitation, addressable in a follow-up ENG

---

## 3. MCP Tool API Surface

### `mem_relations`

Add, retrieve, or delete typed relations between observations.

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `observation_id` | `long` | Yes | Source observation ID |
| `action` | `string` | Yes | One of: `"add"`, `"get"`, `"delete"` |
| `target_observation_id` | `long` | Yes (add/delete) | Target observation ID |
| `type` | `string` | Yes (add/delete) | Relation type: `depends_on`, `supersedes`, `conflicts_with`, `related_to` |
| `project` | `string` | No | Project name (defaults to configured default project) |

**Return type:** `string` (human-readable status text)

**add result:** `"Relation depends_on:42 added to observation 10."`
**get result:** `"Relations for obs#10:\n- depends_on: 42\n- related_to: 55"`
**delete result:** `"Relation depends_on:42 removed from observation 10."` or `"No matching relation found."`
**Error:** Error message string starting with `"Error:"`

**Idempotency:** `add` is idempotent (duplicate detection by type+target).

---

### `mem_lineage_obs`

Build a lineage tree for a memory observation.

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `observation_id` | `long` | Yes | Root observation ID |
| `max_hops` | `int` | No | Max traversal depth (default **5**, max 10) |
| `project` | `string` | No | Project name |

**Return type:** `string` (human-readable lineage report)

**Output format:**
```
## Lineage: obs#10

### Ancestors (↑)
- obs#5: "Title of obs#5" (traced)
  - supersedes: 3
- obs#3: "Title of obs#3" (traced)
  - depends_on: 1

### Descendants (↓)
- obs#20: "Related note" (traced)
  - related_to: 55

Hops: 3
⚠️ Cycle detected!
```

**Notes:**
- Ancestors climb via `supersedes` and `depends_on` edges
- Descendants follow outbound `related_to` edges only
- Inverse traversal (inbound edges) is out of scope for v1
- `cycle_detected: true` is indicated by `⚠️ Cycle detected!`

---

## 4. Design Decisions

### DD-1: Inverse Traversal — Follow-up M, Not v1 Blocker

**Decision:** `mem_lineage_obs` follows outgoing edges only. A follow-up ENG will add inverse traversal (scanning all project observations for inbound edges to a given node).

**Rationale:** The spike confirmed BFS correctly follows only outbound edges. Adding inverse traversal requires a full-project scan of `memrel/{project}/*` to find inbound edges — estimated M effort and not required for the core use cases (answering "what does this observation depend on?" and "what is related?").

**Spec reference:** Documented in FR-005 and in `mem_lineage_obs` output notes.

---

### DD-2: MCP Tool API Shape + CLI Exposure

**Decision:** Two dedicated tools: `mem_relations` (crud) and `mem_lineage_obs` (traversal). Action-based (`action="add"|"get"|"delete"`) rather than separate tool names.

**CLI commands:** `engram relations` (add/get/delete) and `engram lineage` (traversal) — thin wrappers around the MCP tools. Estimated ~50-80 lines each (consistent with existing CLI patterns in `Engram.Cli/Program.cs`).

**Rationale:** Mirrors the pattern used by `mem_lineage` (requirement traceability) and keeps related operations grouped. Tool-per-action would fragment the API. String return type matches existing MCP tool conventions in the codebase (`EngramTools.cs`). CLI commands provide terminal access for debugging and manual testing without requiring an MCP client.

**Parameter conventions:**
- `observation_id` (long, required) — observation ID
- `target_observation_id` (long, conditional) — required for add/delete
- `type` (string, conditional) — required for add/delete, validated against `RelationValidator`
- `project` (string, optional) — defaults to configured project
- `max_hops` (int, optional, default **5**, max 10) — traversal depth for `mem_lineage_obs`

---

### DD-3: Sync Semantics

**Decision:** Relations are ordinary Engram observations with `topic_key=memrel/{project}/{observationId}` and `type=memory_relation`. They sync naturally via SyncManager with no special handling.

`conflicts_with` does NOT cause sync conflict flags in v1 — it is a relation type only, not a semantic conflict signal at the sync layer.

**Rationale:** Zero schema changes (spike confirmed). Relations follow the same sync semantics as any other observation. Adding conflict semantics to `conflicts_with` would require defining what "conflict" means in sync terms — deferred.

---

### DD-4: Validation Rules on Insert

**Decision:**
- `supersedes`: source and target must share the same `topic_key` prefix (validation at MCP tool layer, not store/repository layer)
- `conflicts_with`: no semantic similarity check in v1 — agent's responsibility to establish relevance before creating the relation

**Rationale:** `supersedes` without shared topic_key is semantically meaningless (you're superseding something in a different context). Enforcing at tool layer keeps the repository simple and testable. `conflicts_with` semantic validation (do these actually contradict?) is an AI judgment call — out of scope for the storage layer.

---

### DD-5: Retention

**Decision:** `memrel/{project}/*` observations count toward the project's retention budget. They are ordinary observations; there is no separate retention policy for relation metadata.

**Rationale:** Relations are first-class observations. A project's retention budget should reflect all of its stored data. Special-casing relation retention would add complexity without clear benefit. If a project is near its retention limit, it should prune relations along with other observations.

---

## 5. Non-Functional Requirements

### NFR-PERF-001
`mem_lineage_obs` must complete lineage traversal in < 500ms for graphs with up to 100 nodes (10 hops, avg degree 3) on a warm SQLite store.

### NFR-PERF-002
`mem_relations` get/delete operations must complete in < 50ms on warm store.

### NFR-TEST-001
Unit test coverage target: >85% line coverage on `MemoryRelationRepository`, `MemoryLineageBuilder`, and related models.

### NFR-TEST-002
Integration tests must cover: SQLite in-memory (T1/T2) and PostgreSQL (T3). PostgreSQL tests run via Testcontainers in CI.

### NFR-API-001
All MCP tool parameters must be validated server-side. Invalid `type` values return an error string. Missing required parameters return an error string.

### NFR-SEC-001
No new authentication or authorization requirements. Relations inherit the `Scopes.Team` scope of the underlying observations.

### NFR-DATA-001
No schema changes. Relations persist as observations via topic_key upsert at `SqliteStore.cs:583-621`.

---

## 6. Capability Matrix

```yaml
capability_matrix:
  ai_reasoning:
    - BFS traversal order for same-hop nodes (deterministic but not guaranteed stable across implementations)
    - UX text formatting of lineage output (presentation concern only)
    - Whether to surface untraced nodes (observations without a row in the store) in lineage results
  deterministic:
    - Relation types are exactly: depends_on, supersedes, conflicts_with, related_to (RelationValidator)
    - BFS MaxHops = 10 hard ceiling
    - Cycle detection via visited set (HashSet<long>)
    - Duplicate relation (same type + target) is deduplicated on save
    - supersedes requires same topic_key (enforced at MCP tool level)
    - memrel/{project}/{observationId} topic_key pattern
    - Relations persist with type="memory_relation" and Scope=Team
    - memrel/* observations count toward project retention budget
```

---

## 7. Developer Manual Tests (Required — Mark [x] Before /flow-close)

| ID | Case / Flow | Steps (Summary) | Expected Result | [x] |
|----|-------------|-----------------|-----------------|-----|
| PM-1 | Add relation via MCP tool | 1. Create two observations via `mem_save`<br>2. Call `mem_relations(action="add", observation_id=<1>, target_observation_id=<2>, type="depends_on")`<br>3. Call `mem_relations(action="get", observation_id=<1>)` | Relation appears in get output | [ ] |
| PM-2 | Lineage traversal | 1. Create chain: obs3→depends_on→obs2→supersedes→obs1<br>2. Call `mem_lineage_obs(observation_id=<obs3>)` | Ancestors show obs2 then obs1; hops=2 | [ ] |
| PM-3 | Cycle detection | 1. Create cycle: obs1→depends_on→obs2, obs2→supersedes→obs1<br>2. Call `mem_lineage_obs(observation_id=<obs1>)` | `cycle_detected` flag is true in output | [ ] |
| PM-4 | Delete relation | 1. Add two relations to obs1<br>2. Delete one via `mem_relations(action="delete", ...)`<br>3. Call `mem_relations(action="get", observation_id=<obs1>)` | Only the non-deleted relation remains | [ ] |
| PM-5 | Duplicate idempotency | 1. Add same `depends_on` relation twice<br>2. Call `mem_relations(action="get", ...)` | Only one relation stored | [ ] |

---

## 8. Open Questions for Human (Resolved)

All questions resolved on 2026-06-18:

1. ~~CLI exposure~~ → **RESOLVED**: CLI + MCP in v1. `engram relations` and `engram lineage` as thin wrappers (~50-80 lines each).
2. ~~Default max_hops~~ → **RESOLVED**: Default **5**, max 10.
3. ~~Follow-up ENG owner~~ → **RESOLVED**: Same team. No specialized roles currently.
