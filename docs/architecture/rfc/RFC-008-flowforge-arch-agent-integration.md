# RFC-008 — FlowForge Arch Agent Integration Protocol

**Status**: Active
**Author**: @kaito
**Created**: 2026-09-29
**Related HU**: HU-061 — code-context-arch-agent

---

## Summary

Protocol for the FlowForge Arch Agent to query past architectural decisions before new design work, enabling stateful context reuse instead of stateless re-derivation.

---

## Background

When the FlowForge Arch Agent starts design work for a new feature, it currently derives architectural decisions from scratch — even when prior decisions for the same module already exist in Engram memory. This leads to:

- **Duplicated reasoning**: The agent re-derives decisions already documented
- **Inconsistent design**: New decisions may contradict existing ones
- **Wasted context**: Historical decision rationale is ignored
- **Broken continuity**: Each session starts stateless

The Arch Agent needs a stateful context mechanism: query Engram for relevant decisions before designing, then proceed informed — or proceed stateless if no prior decisions exist.

---

## Call Sequence

```
┌─────────────────────────────────────────────────────────────────┐
│  FlowForge Orchestrator                                         │
│    └─> CKP-1 (Context): launches Arch Agent                    │
└─────────────────────────────────────────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────────────┐
│  Arch Agent startup                                             │
│    1. Detect current module/namespace from scope               │
│    2. Call mem_decisions_for_module(module)                     │
│    3. Receive formatted decision list (or graceful empty)       │
└─────────────────────────────────────────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────────────┐
│  Decision tree display (if results found)                       │
│    - Show decisions grouped by topic_key                        │
│    - Highlight active vs deprecated decisions                  │
│    - Present to human for review / confirmation                 │
└─────────────────────────────────────────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────────────┐
│  Human review checkpoint (CKP-2 or CKP-3)                       │
│    - Approve existing decisions as context                       │
│    - Flag contradictions / superseded decisions                │
│    - Proceed to design with informed context                    │
└─────────────────────────────────────────────────────────────────┘
```

---

## Tool Contract

### `mem_decisions_for_module`

```csharp
[Description("""
    Query architectural decisions (type=decision or type=architecture) for a specific module/namespace.

    Returns decisions where namespace matches the given module prefix.
    Use this before designing new features to avoid re-deriving existing decisions.

    EXAMPLES:
      mem_decisions_for_module("Engram.Store")
      mem_decisions_for_module("Engram.Auth", limit: 5)
    """)]
public async Task<string> MemDecisionsForModule(
    [Description("The namespace or module prefix to query (e.g. 'Engram.Auth' or 'Engram')")] string module,
    [Description("Filter by project name")] string? project = null,
    [Description("Max results (default: 10, max: 50)")] int limit = 10)
```

**Returns**: Formatted decision list via `FormatSearchResults` (same output format as other recall tools).

**Output example** (non-empty):

```
Found 2 decisions for module "Engram.Store":
[1] #42 (decision) — Use repository pattern
    We chose the repository pattern to abstract...
    2026-09-28T10:00:00Z | scope: personal | namespace: Engram.Store

[2] #38 (architecture) — SQLite backend design
    Decision to use SQLite as default backend...
    2026-09-20T14:30:00Z | scope: personal | namespace: Engram.Store
```

**Output example** (empty):

```
No decisions found for module: "Engram.Store"
```

---

## Graceful Handling

| Scenario | Behavior |
|----------|----------|
| No decisions for module | Return: `No decisions found for module: "X"` — proceed stateless |
| Engram unavailable | Return friendly message — proceed without memory context |
| Limit exceeded | Clamped to 50; results sorted by `CreatedAt` descending |

The Arch Agent is designed to operate with or without prior decisions. Empty results are not errors — they signal a greenfield design context.

---

## Example Call/Response

### Request

```
mem_decisions_for_module("Engram.Mcp")
```

### Response

```
Found 3 decisions for module "Engram.Mcp":
[1] #87 (architecture) — MCP tool registry design
    We adopted the ModelContextProtocol.Server attribute-based registration
    pattern for all 28 Engram tools. This provides type-safe tool discovery...
    2026-09-25T09:00:00Z | scope: personal | namespace: Engram.Mcp

[2] #76 (decision) — Dual-store sync strategy
    After evaluating write-through vs write-behind, we chose write-behind
    with WriteQueue buffering all observations...
    2026-09-18T14:30:00Z | scope: personal | namespace: Engram.Mcp

[3] #61 (architecture) — Observable diagnostics pipeline
    We needed a way to expose internal metrics (cache hit rate, query latency)
    without coupling the store layer to HTTP concerns...
    2026-09-10T11:15:00Z | scope: personal | namespace: Engram.Mcp
```

---

## Implementation Notes

- **Dual-type query**: The tool executes two store queries (type=`decision` AND type=`architecture`) and merges results. This reflects the semantic intent: "architectural decisions" include both types.
- **No store-layer changes**: The store's `GetMemoriesByModuleAsync` already supports type filtering. The tool layer handles the merge/dedupe.
- **Sorted by recency**: Results are ordered `OrderByDescending(CreatedAt)` to surface the most recent decisions first.
- **Deduplication**: `Concat(...).Take(clampedLimit)` naturally avoids duplicates from the same observation appearing in both queries (same ID, different type query).

---

## References

- HU-061: code-context-arch-agent
- `EngramTools.MemDecisionsForModule` (src/Engram.Mcp/EngramTools.cs)
- `GetMemoriesByModuleAsync` (src/Engram.Store/SqliteStore.cs:1047)
- FlowForge Orchestrator: `forge-orchestrator` skill
