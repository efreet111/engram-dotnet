# plan.md — ENG-456: LlmVerifier Lazy Instantiation

> **Phase**: 2 (Planning) | **Agent**: forge-plan | **Date**: 2026-07-14
> **Spec**: `.ai-work/eng-456-llm-verifier-lazy/spec.md` (approved CKP-1)

---

## Task Breakdown

### T-001 — Move `NoOpVerifier` to production code
- **Description**: Copy the `NoOpVerifier` class from `tests/Engram.Mcp.Tests/EngramToolsTests.cs:827-844` into `src/Engram.Verification/ArtifactVerifier.cs` (after `FakeVerifier`, line 170). Update the `Summary` string to indicate production use.
- **Files**:
  - `src/Engram.Verification/ArtifactVerifier.cs` — add `NoOpVerifier` class
  - `tests/Engram.Mcp.Tests/EngramToolsTests.cs` — remove local `NoOpVerifier` definition (lines 821-844); use production class via `using Engram.Verification;`
- **Effort**: XS (~5 min)
- **Dependencies**: None

### T-002 — Replace eager singleton with factory delegate in `Program.cs`
- **Description**: Change `Program.cs:138-139` from `_ => new LlmVerifier()` to a factory delegate that checks `ANTHROPIC_API_KEY` env var. If missing → `NoOpVerifier`; if present → `LlmVerifier`. Pattern matches existing `IDiagnosticService` factory at line 155.
- **Files**:
  - `src/Engram.Cli/Program.cs` — lines 138-139
- **Effort**: XS (~3 min)
- **Dependencies**: T-001 (needs `NoOpVerifier` in production assembly)

### T-003 — Add `api_key_missing` error code to `McpErrors.cs`
- **Description**: Add `"api_key_missing"` to the `ValidErrorCodes` HashSet so `mem_verify_artifact` can return a structured error with this code.
- **Files**:
  - `src/Engram.Mcp/McpErrors.cs` — line 22 (insert before `"internal_error"`)
- **Effort**: XS (~1 min)
- **Dependencies**: None

### T-004 — Add early return in `MemVerifyArtifact` for `NoOpVerifier`
- **Description**: Before calling `verifier.VerifyAsync()` at line 727, check `if (verifier is NoOpVerifier)` and return a structured `api_key_missing` error with a friendly message and hint.
- **Files**:
  - `src/Engram.Mcp/EngramTools.cs` — insert check before line 727
- **Effort**: XS (~3 min)
- **Dependencies**: T-001, T-003

### T-005 — Add unit tests T1, T2, T3
- **Description**: Add three tests to `VerifierTests.cs`:
  - **T1** `LlmVerifier_Constructor_MissingApiKey_Throws` — clear env var, assert `InvalidOperationException`
  - **T2** `LlmVerifier_Constructor_WithApiKey_Creates` — set env var, assert construction succeeds
  - **T3** `NoOpVerifier_VerifyAsync_ReturnsEmptyReport` — assert empty report with correct cycle
- **Files**:
  - `tests/Engram.Verification.Tests/VerifierTests.cs` — add 3 test methods
- **Effort**: S (~10 min)
- **Dependencies**: T-001

### T-006 — Add integration tests T4, T5, T6
- **Description**: Add three tests to MCP test project:
  - **T4** `McpServer_Starts_WithoutAnthropicKey` — build DI host without env var, assert `IVerifier` resolves to `NoOpVerifier`
  - **T5** `MemVerifyArtifact_WithoutApiKey_ReturnsError` — call `MemVerifyArtifact` with `NoOpVerifier`, assert `api_key_missing` in response
  - **T6** `MemSave_Works_WithoutAnthropicKey` — call `mem_save` with `NoOpVerifier`, assert normal execution
- **Files**:
  - `tests/Engram.Mcp.Tests/EngramToolsTests.cs` — add T5, T6 test methods
  - `tests/Engram.Mcp.Tests/` — new or existing file for T4
- **Effort**: S (~10 min)
- **Dependencies**: T-002, T-004

### T-007 — Update `docs/VERIFICATION.md`
- **Description**: Add a note in the configuration section clarifying that `ANTHROPIC_API_KEY` is only required for `mem_verify_artifact`. All other MCP tools work without it. Document the `api_key_missing` error response.
- **Files**:
  - `docs/VERIFICATION.md` — update config/prerequisites section
- **Effort**: XS (~3 min)
- **Dependencies**: T-003, T-004

### T-008 — Run full test suite (no regressions)
- **Description**: Execute the full test suite (SQLite, no Docker) and verify all existing + new tests pass.
  ```bash
  dotnet test -c Release --filter "FullyQualifiedName!~Engram.Postgres.Tests&Category!=RequiresDocker"
  ```
- **Files**: None (verification only)
- **Effort**: XS (~2 min)
- **Dependencies**: T-001 through T-007

---

## Contracts

### `IVerifier` interface (unchanged)
```csharp
// src/Engram.Verification/ArtifactVerifier.cs:7
public interface IVerifier
{
    Task<VerificationReport> VerifyAsync(
        SpecParseResult spec, string codeDiff, int currentCycle);
}
```

### `NoOpVerifier` class (new in production)
```csharp
// src/Engram.Verification/ArtifactVerifier.cs (after line 170)
public sealed class NoOpVerifier : IVerifier
{
    public Task<VerificationReport> VerifyAsync(
        SpecParseResult spec, string codeDiff, int currentCycle)
    {
        return Task.FromResult(new VerificationReport
        {
            Items = [], CoveragePct = 100.0, PassPct = 100.0,
            Total = 0, Passed = 0, Failed = 0,
            Cycle = currentCycle, Escalate = false,
            Summary = "Verification not available — ANTHROPIC_API_KEY not configured"
        });
    }
}
```

### Factory delegate (Program.cs replacement)
```csharp
// src/Engram.Cli/Program.cs:138-139
mcpBuilder.Services.AddSingleton<Engram.Verification.IVerifier>(sp =>
{
    var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
    if (string.IsNullOrEmpty(apiKey))
        return new Engram.Verification.NoOpVerifier();
    return new Engram.Verification.LlmVerifier();
});
```

### `mem_verify_artifact` early return
```csharp
// src/Engram.Mcp/EngramTools.cs (before line 727)
if (verifier is Verification.NoOpVerifier)
    return McpErrors.Structured(
        "api_key_missing",
        "ANTHROPIC_API_KEY is not configured — verification is unavailable.",
        hint: "Set the ANTHROPIC_API_KEY environment variable to enable LLM-based verification");
```

---

## Effort Estimates

| Task | Effort | Time |
|------|--------|------|
| T-001 Move NoOpVerifier | XS | ~5 min |
| T-002 Factory delegate | XS | ~3 min |
| T-003 Error code | XS | ~1 min |
| T-004 Early return | XS | ~3 min |
| T-005 Unit tests (T1-T3) | S | ~10 min |
| T-006 Integration tests (T4-T6) | S | ~10 min |
| T-007 Docs update | XS | ~3 min |
| T-008 Test suite run | XS | ~2 min |
| **Total** | | **~37 min** |

**BACKLOG estimate**: XS (15-30 min). Plan is slightly above due to test writing; forge-dev may be faster.

---

## Test Checklist

### New tests (must pass)
- [x] **T1** `LlmVerifier_Constructor_MissingApiKey_Throws`
- [x] **T2** `LlmVerifier_Constructor_WithApiKey_Creates`
- [x] **T3** `NoOpVerifier_VerifyAsync_ReturnsEmptyReport`
- [x] **T4** `McpServer_Starts_WithoutAnthropicKey`
- [x] **T5** `MemVerifyArtifact_WithoutApiKey_ReturnsError`
- [x] **T6** `MemSave_Works_WithoutAnthropicKey`

### Existing tests (no regressions)
- [x] `tests/Engram.Verification.Tests/` — all existing CycleTracker + TraceabilityMatrix tests
- [x] `tests/Engram.Mcp.Tests/EngramToolsTests.cs` — all existing tool tests
- [x] `tests/Engram.Mcp.Tests/StructuredErrorMigrationTests.cs` — all error path tests
- [x] Full suite: `dotnet test -c Release --filter "FullyQualifiedName!~Engram.Postgres.Tests&Category!=RequiresDocker"`

---

## Verification Steps

### Automated
1. `dotnet build -c Release` — must succeed with zero warnings
2. `dotnet test -c Release --filter "FullyQualifiedName!~Engram.Postgres.Tests&Category!=RequiresDocker"` — all tests pass

### Manual (MCP server without API key)
3. Unset `ANTHROPIC_API_KEY`: `unset ANTHROPIC_API_KEY`
4. Start MCP server: `dotnet run --project src/Engram.Cli -c Release -- mcp`
5. Verify server starts and stays running (no crash)
6. Call `mem_save` → should work normally
7. Call `mem_verify_artifact` → should return JSON with `error_code: "api_key_missing"` and a hint

### Manual (MCP server with API key — regression)
8. Set `ANTHROPIC_API_KEY=sk-...` (real or valid-format key)
9. Start MCP server: `dotnet run --project src/Engram.Cli -c Release -- mcp`
10. Call `mem_verify_artifact` → should proceed to LLM verification (or fail at API call, not at startup)

---

## Rollout Checklist

- [ ] Code changes merged to `main`
- [x] All tests passing (SQLite + new tests T1-T6)
- [x] `docs/VERIFICATION.md` updated
- [ ] `docs/BACKLOG.md` — ENG-456 status → Done
- [ ] Commit message: `fix: lazy IVerifier instantiation prevents MCP crash without ANTHROPIC_API_KEY (ENG-456)`
- [ ] Users with `sk-dummy-fallback` workaround can optionally remove it

---

## Risks & Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Test NoOpVerifier references break after move | Low | Update `using` in `EngramToolsTests.cs`; delete local class |
| `is NoOpVerifier` check is fragile (type check) | Low | Only 2 implementations exist (`LlmVerifier`, `NoOpVerifier`); `FakeVerifier` is test-only |
| Env var read at DI resolution is once-per-process | None | Singleton — correct behavior; env var doesn't change at runtime |

---

## Implementation Order

```
T-001 ──→ T-002 ──→ T-004 ──→ T-006 ──→ T-008
  │                    ↑                  ↑
  └──→ T-003 ─────────┘                  │
  │                                       │
  └──→ T-005 ─────────────────────────────┘
                                          │
  T-007 ──────────────────────────────────┘
```

T-001 and T-003 can be done in parallel. T-005 depends only on T-001. T-007 can be done any time after T-004.
