# spec.md — ENG-456: LlmVerifier Lazy Instantiation

> **Phase**: 1 (Architecture) | **Agent**: forge-arch | **Date**: 2026-07-14

---

## 1. Overview

### 1.1 Problem Statement

`LlmVerifier` constructor (`ArtifactVerifier.cs:44-51`) throws `InvalidOperationException` when `ANTHROPIC_API_KEY` is missing. Because `IVerifier` is registered as an eager singleton in `Program.cs:138-139`, and `EngramTools` depends on it in its constructor (`EngramTools.cs:52`), the DI container resolves `IVerifier` at MCP server startup — before any tool is called.

**Result**: The entire MCP server crashes on startup. All 28 tools become unavailable.

### 1.2 Impact

| Dimension | Scope |
|-----------|-------|
| **Affected tools** | All 28 MCP tools (only `mem_verify_artifact` actually uses `IVerifier`) |
| **Affected IDEs** | OpenCode, Antigravity, Cursor, VS Code, Claude Desktop |
| **Affected users** | Every user who does not have `ANTHROPIC_API_KEY` set |
| **Current workaround** | Manually add `"ANTHROPIC_API_KEY": "sk-dummy-fallback"` to IDE config |
| **Severity** | **P0** — complete MCP toolset failure on fresh install |

### 1.3 Scope

Fix the startup crash. No new features. No changes to verification logic or MCP protocol.

---

## 2. Functional Requirements

| ID | Requirement | Acceptance Criteria |
|----|-------------|---------------------|
| **FR-001** | `engram mcp` starts successfully without `ANTHROPIC_API_KEY` | Server process remains running; all 28 tools are listed and callable |
| **FR-002** | `mem_save`, `mem_search`, `mem_get` (and all other non-verify tools) work without API key | Tools execute normally and return correct results |
| **FR-003** | `mem_verify_artifact` returns a friendly structured error when API key is missing | Response contains `api_key_missing` error code and a message explaining how to configure the key |
| **FR-004** | `LlmVerifier` is instantiated only when `ANTHROPIC_API_KEY` is present | Factory delegate checks env var before constructing `LlmVerifier` |
| **FR-005** | `NoOpVerifier` is available in production code (not just tests) | Class exists in `src/Engram.Verification/` and is referenced by the factory |
| **FR-006** | Backward compatible — users with `ANTHROPIC_API_KEY` see no behavior change | `LlmVerifier` is still constructed and used for `mem_verify_artifact` |

---

## 3. Non-Functional Requirements

| ID | Requirement | Verification |
|----|-------------|--------------|
| **NFR-001** | Zero runtime overhead for users without API key | `NoOpVerifier` is stateless; no allocations beyond the singleton instance |
| **NFR-002** | Clear error messages (not stack traces) | `mem_verify_artifact` returns JSON with `error_code`, `message`, `hint` — no `InvalidOperationException` propagation |
| **NFR-003** | No breaking changes to existing configs | No changes to `config/mcp/editors/` templates; no changes to `scripts/setup.sh` |
| **NFR-004** | Test coverage for all code paths | 6 tests (T1–T6) covering constructor behavior, NoOp behavior, DI startup, and integration |

---

## 4. Architecture Decision

### Decision: NoOpVerifier Factory Pattern

Replace the eager singleton registration with a factory delegate that checks `ANTHROPIC_API_KEY` at resolution time:

```csharp
// Program.cs:138-139 — replace
mcpBuilder.Services.AddSingleton<IVerifier>(sp =>
{
    var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
    if (string.IsNullOrEmpty(apiKey))
        return new NoOpVerifier();
    return new LlmVerifier();
});
```

### Rationale

| Criterion | NoOpVerifier Factory | Lazy\<IVerifier\> |
|-----------|---------------------|-------------------|
| Complexity | Low — conditional in factory delegate | Higher — `Lazy<IVerifier>` type changes in `EngramTools` constructor |
| User experience | Immediate friendly error on `mem_verify_artifact` | Still throws on first call (deferred crash) |
| Runtime overhead | Zero (stateless singleton) | `Lazy<T>` wrapper + lock on first access |
| Code changes | `Program.cs` only (+ `NoOpVerifier` move) | `EngramTools.cs` + `Program.cs` |
| Pattern precedent | Matches `IDiagnosticService` factory (Program.cs:155) | No precedent in codebase |

### Rejected Alternatives

| Alternative | Why Rejected |
|-------------|--------------|
| **Lazy\<IVerifier\>** | More complex; still throws on first `mem_verify_artifact` call; no UX improvement over NoOp |
| **Catch at call site** | DI resolution happens at startup, not at call site — too late to catch |
| **Remove IVerifier registration** | Breaks `mem_verify_artifact` for users who DO have API key |
| **Set dummy env var in setup.sh** | Perpetuates the hack; doesn't help users who skip setup |

---

## 5. Implementation Plan

### 5.1 Changes

| # | File | Change | Lines Affected |
|---|------|--------|----------------|
| 1 | `src/Engram.Verification/ArtifactVerifier.cs` | Add `NoOpVerifier` class (move from tests) | New class after line 170 |
| 2 | `src/Engram.Cli/Program.cs` | Replace eager singleton with factory delegate | Lines 138–139 |
| 3 | `src/Engram.Mcp/McpErrors.cs` | Add `api_key_missing` to `ValidErrorCodes` | Line 13–24 |
| 4 | `src/Engram.Mcp/EngramTools.cs` | Add early return in `MemVerifyArtifact` if verifier is `NoOpVerifier` | Around line 726 |
| 5 | `tests/Engram.Verification.Tests/VerifierTests.cs` | Add T1, T2, T3 tests | New test methods |
| 6 | `tests/Engram.Mcp.Tests/EngramToolsTests.cs` | Add T5, T6 tests; update NoOpVerifier to use production impl | Existing + new |
| 7 | `docs/VERIFICATION.md` | Document that `ANTHROPIC_API_KEY` is only needed for verify | Update config section |

### 5.2 NoOpVerifier (production implementation)

```csharp
/// <summary>
/// No-op verifier that returns empty passing reports.
/// Used when ANTHROPIC_API_KEY is not configured — allows MCP server to start
/// and all non-verify tools to function normally.
/// </summary>
public sealed class NoOpVerifier : IVerifier
{
    /// <summary>
    /// Returns an empty passing report. Callers should check for NoOpVerifier
    /// before invoking VerifyAsync to return a user-friendly error.
    /// </summary>
    public Task<VerificationReport> VerifyAsync(
        SpecParseResult spec, string codeDiff, int currentCycle)
    {
        return Task.FromResult(new VerificationReport
        {
            Items = [],
            CoveragePct = 100.0,
            PassPct = 100.0,
            Total = 0,
            Passed = 0,
            Failed = 0,
            Cycle = currentCycle,
            Escalate = false,
            Summary = "Verification not available — ANTHROPIC_API_KEY not configured"
        });
    }
}
```

### 5.3 McpErrors.cs Update

Add `"api_key_missing"` to the `ValidErrorCodes` set:

```csharp
private static readonly HashSet<string> ValidErrorCodes =
[
    "ambiguous_project",
    "unknown_project",
    "project_not_found",
    "session_not_found",
    "prompt_not_found",
    "observation_not_found",
    "validation_error",
    "blocked_by_observations",
    "api_key_missing",        // ← NEW
    "internal_error"
];
```

### 5.4 MemVerifyArtifact Enhancement

Before calling `verifier.VerifyAsync()`, check if the verifier is `NoOpVerifier`:

```csharp
// Before line 727 (current code: var report = await verifier.VerifyAsync(...))
if (verifier is NoOpVerifier)
    return McpErrors.Structured(
        "api_key_missing",
        "ANTHROPIC_API_KEY is not configured — verification is unavailable.",
        hint: "Set the ANTHROPIC_API_KEY environment variable to enable LLM-based verification");
```

---

## 6. STRIDE Threat Analysis

| Threat | Category | Analysis | Risk |
|--------|----------|----------|------|
| **Spoofing** | Can an attacker spoof the API key? | No — `ANTHROPIC_API_KEY` is an OS environment variable, not user-controlled input. The factory reads it once at DI resolution time. | **None** |
| **Tampering** | Can an attacker tamper with verifier selection? | No — the factory delegate is internal to `Program.cs`. An attacker cannot inject a different `IVerifier` implementation without modifying source code. | **None** |
| **Repudiation** | Can a user deny calling verify? | N/A — MCP tool calls are logged by the host IDE. No change to logging behavior. | **None** |
| **Information Disclosure** | Does `NoOpVerifier` leak sensitive data? | No — returns an empty `VerificationReport` with no references to keys, tokens, or internal state. | **None** |
| **Denial of Service** | Can an attacker cause DoS via this fix? | No — `NoOpVerifier` is stateless and returns immediately. No network calls, no allocations beyond the singleton. | **None** |
| **Elevation of Privilege** | Can an attacker gain privileges? | No — no authentication or authorization changes. The fix only affects which `IVerifier` implementation is registered. | **None** |

**Summary**: No new attack surface introduced. The fix reduces attack surface by preventing unhandled exceptions from propagating to MCP clients.

---

## 7. Testing Strategy

### 7.1 Unit Tests

| Test | Location | What It Validates |
|------|----------|-------------------|
| **T1** `LlmVerifier_Constructor_MissingApiKey_Throws` | `VerifierTests.cs` | Constructor throws `InvalidOperationException` when `ANTHROPIC_API_KEY` is missing (regression guard) |
| **T2** `LlmVerifier_Constructor_WithApiKey_Creates` | `VerifierTests.cs` | Constructor succeeds when `ANTHROPIC_API_KEY` is set |
| **T3** `NoOpVerifier_VerifyAsync_ReturnsEmptyReport` | `VerifierTests.cs` | `NoOpVerifier` returns a valid empty report with correct cycle number |

### 7.2 Integration Tests

| Test | Location | What It Validates |
|------|----------|-------------------|
| **T4** `McpServer_Starts_WithoutAnthropicKey` | `Engram.Mcp.Tests/` (new) | DI host builds successfully without `ANTHROPIC_API_KEY`; `IVerifier` resolves to `NoOpVerifier` |
| **T5** `MemVerifyArtifact_WithoutApiKey_ReturnsError` | `EngramToolsTests.cs` | `mem_verify_artifact` returns structured `api_key_missing` error when `NoOpVerifier` is active |
| **T6** `MemSave_Works_WithoutAnthropicKey` | `EngramToolsTests.cs` | `mem_save` executes normally without `ANTHROPIC_API_KEY` (regression guard) |

### 7.3 Test Execution

```bash
# All tests (SQLite, no Docker)
dotnet test -c Release --filter "FullyQualifiedName!~Engram.Postgres.Tests&Category!=RequiresDocker"

# Verification-specific tests only
dotnet test tests/Engram.Verification.Tests/ -c Release

# MCP tool tests only
dotnet test tests/Engram.Mcp.Tests/ -c Release
```

---

## 8. Rollout Plan

| Aspect | Action |
|--------|--------|
| **Migration** | None required — fix is backward compatible |
| **Config changes** | None required — no template or setup script changes |
| **Workaround removal** | Users with `"ANTHROPIC_API_KEY": "sk-dummy-fallback"` in their IDE config can optionally remove it after updating |
| **Rollback** | Revert the single `Program.cs` change — no data migration to undo |

---

## 9. BLOCKERs

**None.** The fix is straightforward:

- Well-understood root cause (eager singleton with env-var dependency)
- Proven pattern in codebase (`IDiagnosticService` factory at `Program.cs:155`)
- `NoOpVerifier` already exists in tests — just needs to move to production code
- No architectural decisions pending
- No external dependencies or coordination required

---

## 10. Memory Signal

```yaml
decision: NoOpVerifier factory pattern for IVerifier DI registration
pattern: factory-delegate-singleton-with-env-var-fallback
file: src/Engram.Cli/Program.cs:138-139
prevents: MCP server crash when ANTHROPIC_API_KEY is missing
reference: IDiagnosticService factory pattern (Program.cs:155)
eng: ENG-456
```
