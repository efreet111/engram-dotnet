# Context Map — ENG-456: LlmVerifier Lazy Instantiation

> **Phase**: 0 (Discovery) | **Agent**: forge-discovery | **Date**: 2026-07-14

---

## 1. Code Map

### 1.1 The Bug — Constructor throws eagerly

| File | Lines | What |
|------|-------|------|
| `src/Engram.Cli/Program.cs` | 138–139 | `IVerifier` registered as singleton `_ => new LlmVerifier()` |
| `src/Engram.Verification/ArtifactVerifier.cs` | 44–51 | `LlmVerifier` constructor reads `ANTHROPIC_API_KEY` env var; throws `InvalidOperationException` if missing |
| `src/Engram.Mcp/EngramTools.cs` | 52 | Constructor takes `IVerifier verifier` as parameter (eager DI resolution) |
| `src/Engram.Mcp/EngramMcpServer.cs` | 30–32 | `.WithTools<EngramTools>()` — causes DI to resolve all EngramTools deps at server startup |

### 1.2 Dependency Chain

```
mcpBuilder.Build().RunAsync()
  └─ DI container resolves EngramTools
       └─ EngramTools(IStore, McpConfig, …, IVerifier verifier, …)
            └─ IVerifier is registered as _ => new LlmVerifier()
                 └─ LlmVerifier() constructor runs
                      └─ Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
                           └─ if null → throw InvalidOperationException("ANTHROPIC_API_KEY is not set")
                                └─ Propagates up → MCP server crashes → ALL tools broken
```

### 1.3 Key Files

| File | Purpose |
|------|---------|
| `src/Engram.Mcp/EngramMcpServer.cs` (36 lines) | MCP server builder — `.WithTools<EngramTools>()` triggers eager DI |
| `src/Engram.Mcp/EngramTools.cs` (1220 lines, line 52) | All 28 MCP tools; `IVerifier` only used in `MemVerifyArtifact` (line 727) |
| `src/Engram.Cli/Program.cs` (1440 lines, line 138–139) | DI registration of `IVerifier` → `LlmVerifier` |
| `src/Engram.Verification/ArtifactVerifier.cs` (170 lines) | Interface `IVerifier`, `LlmVerifier` impl, `FakeVerifier` for tests |
| `tests/Engram.Mcp.Tests/EngramToolsTests.cs` (863 lines, line 827–844) | `NoOpVerifier` test-only impl |

### 1.4 IVerifier Interface

Defined in `src/Engram.Verification/ArtifactVerifier.cs:7`:

```csharp
public interface IVerifier
{
    Task<VerificationReport> VerifyAsync(SpecParseResult spec, string codeDiff, int currentCycle);
}
```

### 1.5 IVerifier Implementations

| Implementation | Location | Purpose |
|---------------|----------|---------|
| `LlmVerifier` | `ArtifactVerifier.cs:33–149` | Production — calls Anthropic API |
| `FakeVerifier` | `ArtifactVerifier.cs:155–169` | Test — returns pre-configured result |
| `NoOpVerifier` | `tests/Engram.Mcp.Tests/EngramToolsTests.cs:827–844` | Test — returns empty passing reports |

### 1.6 All IVerifier Usages (production code)

| File | Line(s) | Usage |
|------|---------|-------|
| `src/Engram.Cli/Program.cs` | 138–139 | Registration: `AddSingleton<IVerifier>(_ => new LlmVerifier())` |
| `src/Engram.Mcp/EngramTools.cs` | 52 | Constructor param: `IVerifier verifier` |
| `src/Engram.Mcp/EngramTools.cs` | 727 | `MemVerifyArtifact` calls `verifier.VerifyAsync(spec, codeDiff, currentCycle)` |

**IVerifier is used in exactly ONE tool: `mem_verify_artifact`. All other 27 tools never touch it.**

### 1.7 NoOpVerifier (already exists in tests)

The `NoOpVerifier` class in `tests/Engram.Mcp.Tests/EngramToolsTests.cs:827-844` is production-ready:

```csharp
public sealed class NoOpVerifier : IVerifier
{
    public Task<VerificationReport> VerifyAsync(SpecParseResult spec, string codeDiff, int currentCycle)
    {
        return Task.FromResult(new VerificationReport
        {
            Items = [], CoveragePct = 100.0, PassPct = 100.0,
            Total = 0, Passed = 0, Failed = 0,
            Cycle = currentCycle, Escalate = false,
            Summary = "No-op verifier for tests"
        });
    }
}
```

This is defined in the **test project**, so it cannot be referenced from production code. A production-ready copy (or factory) would be needed.

---

## 2. IDE Map

### 2.1 MCP Launch Chain

All IDEs launch engram as a stdio subprocess:
```
{editor} → spawns `engram mcp` (stdio transport) → engram-dotnet MCP server listens on stdin/stdout
```

### 2.2 Config File Locations

| IDE | Config File | Format | ANTHROPIC_API_KEY? |
|-----|------------|--------|-------------------|
| **OpenCode** | `~/.config/opencode/opencode.json` | `{ mcp: { engram: { command: ["<path>", "mcp"], environment: {...} } } }` | ❌ Not in template. **Workaround**: users add `ANTHROPIC_API_KEY: "dummy"` manually |
| **Cursor** | `~/.cursor/mcp.json` | `{ mcpServers: { engram: { command: "<path>", args: ["mcp"], env: {...} } } }` | ❌ Not in template |
| **VS Code** | VS Code MCP extension config | `{ servers: { engram: { command: "<path>", args: ["mcp"], env: {...} } } }` | ❌ Not in template |
| **Claude Desktop** | `~/.config/Claude/claude_desktop_config.json` | `{ mcpServers: { engram: { command: "<path>", args: ["mcp"], env: {...} } } }` | ❌ Not in template |
| **Antigravity** | Product-specific MCP settings | `mcpServers` format (same as Cursor) | ❌ Not in template |

### 2.3 Template Variables (generated by setup.sh)

All template configs in `config/mcp/editors/*.mcp.json` use `{{ENGRAM_COMMAND}}`, `{{ENGRAM_DATA_DIR}}`, `{{ENGRAM_USER}}`, `{{ENGRAM_SYNC_ENABLED}}`, `{{ENGRAM_SERVER_URL}}`.

No template includes `ANTHROPIC_API_KEY` — it must be set at the OS environment level or injected per editor.

### 2.4 Current Workaround

**The bug is currently worked around** by users manually adding to their IDE config:

```json
"env": {
  "ANTHROPIC_API_KEY": "sk-dummy-fallback",
  ...
}
```

This is documented in `docs/BACKLOG.md` line 1007. The workaround is fragile and undocumented for new users — a classic P0.

### 2.5 Setup Script Flow (`scripts/setup.sh`)

1. Generates MCP configs into `config/mcp/generated/`
2. These configs contain only: `ENGRAM_DATA_DIR`, `ENGRAM_USER`, `ENGRAM_SYNC_ENABLED`, `ENGRAM_SERVER_URL`
3. Copies to `~/.cursor/mcp.json`, `~/.config/opencode/opencode.json`, etc.
4. `ANTHROPIC_API_KEY` is NEVER included — the bug surfaces on first startup

### 2.6 Impact Per IDE

| IDE | Impact | Notes |
|-----|--------|-------|
| **OpenCode** | **P0** — Primary development environment. Full MCP toolset broken. | Workaround in `~/.config/opencode/opencode.json` |
| **Antigravity** | **P0** — Also broken. No built-in workaround detected. | Manual config only |
| **Cursor** | **P0** — Broken if used. | Manual config only |
| **VS Code** | P1 — Lower usage but same issue. | Manual config only |

---

## 3. Test Map

### 3.1 Existing Test Coverage

| Test File | Tests | Covers IVerifier? |
|-----------|-------|--------------------|
| `tests/Engram.Mcp.Tests/EngramToolsTests.cs` | Full tool-level tests (28 tools) | Yes — uses `NoOpVerifier` |
| `tests/Engram.Mcp.Tests/StructuredErrorMigrationTests.cs` | Error path tests for `mem_verify_artifact` | Yes — uses `NoOpVerifier` |
| `tests/Engram.Verification.Tests/VerifierTests.cs` | `CycleTrackerTests` (6 tests), `TraceabilityMatrixTests` (2 tests) | No — tests `CycleTracker` only |
| `tests/Engram.Verification.Tests/IntegrationTests.cs` | Integration tests | Unknown — check file |

### 3.2 Gaps

1. **No LlmVerifier-specific test** — No test validates that `LlmVerifier` constructor throws when `ANTHROPIC_API_KEY` is missing.
2. **No DI startup test** — No test validates that the MCP server starts gracefully without `ANTHROPIC_API_KEY`.
3. **No integration test for fix** — No test validates the lazy/NoOp behavior end-to-end.
4. **No production NoOpVerifier** — The `NoOpVerifier` exists only in test project.
5. **No MemVerifyArtifact error test** — No test confirms `mem_verify_artifact` returns friendly error when API key is missing.

### 3.3 Tests That Need To Be Added/Updated

| # | Test | Type | Location | What It Validates |
|---|------|------|----------|-------------------|
| T1 | `LlmVerifier_Constructor_MissingApiKey_Throws` | Unit | `tests/Engram.Verification.Tests/VerifierTests.cs` | Validates current throwing behavior (regression guard) |
| T2 | `LlmVerifier_Constructor_WithApiKey_Creates` | Unit | `tests/Engram.Verification.Tests/VerifierTests.cs` | Validates successful construction |
| T3 | `NoOpVerifier_VerifyAsync_ReturnsEmptyReport` | Unit | `tests/Engram.Verification.Tests/VerifierTests.cs` | Validates NoOp behavior (after moving to production) |
| T4 | `McpServer_Starts_WithoutAnthropicKey` | Integration | `tests/Engram.Mcp.Tests/` (new) | E2E: DI host builds without ANTHROPIC_API_KEY |
| T5 | `MemVerifyArtifact_WithoutApiKey_ReturnsError` | Integration | `tests/Engram.Mcp.Tests/EngramToolsTests.cs` | mem_verify_artifact returns friendly error when NoOp/skipped |
| T6 | `MemSave_Works_WithoutAnthropicKey` | Integration | `tests/Engram.Mcp.Tests/EngramToolsTests.cs` | Regression: mem_save works without ANTHROPIC_API_KEY |

---

## 4. Risk Assessment

### 4.1 Backward Compatibility

| Scenario | Risk Level | Impact |
|----------|-----------|--------|
| Users with `ANTHROPIC_API_KEY` set | ✅ None | LlmVerifier still works as before |
| Users with dummy workaround (`sk-dummy-fallback`) | ✅ None | Fix removes need for workaround; config still works (env var is present but unused) |
| Users without `ANTHROPIC_API_KEY` | ✅ Positive | Fix unblocks MCP server entirely |
| `mem_verify_artifact` called without key | ⚠️ Behavior change | Before: server crash. After: friendly error message. Acceptable per acceptance criteria. |

### 4.2 Risks

| Risk | Severity | Mitigation |
|------|----------|------------|
| NoOpVerifier silently skips verification without informing user | Low | MemVerifyArtifact must check if verifier is NoOp and return clear message |
| Lazy<T> threading issues in DI singleton | Low | .NET DI handles singleton Lazy<T> correctly |
| Other eager registrations with same pattern | Medium | Audit required (see §4.3) |
| Bug appears in both `engram mcp` AND `engram serve` if serve also registers verifier | Medium | Check if server path (`engram serve`) has same registration — see §4.4 |

### 4.3 Similar Eager Registrations Audit

Checked `src/Engram.Cli/Program.cs` for other eager singletons that could fail at startup:

| Service | Line | Constructor Risk | Analysis |
|---------|------|-----------------|----------|
| `IVerifier` (LlmVerifier) | 138–139 | 🔴 **BUG** | Reads env var, throws if missing |
| `CycleTracker` | 140–141 | ✅ Safe | Takes `IStore` via DI |
| `PromotionService` | 144 | ✅ Safe | No env reading in constructor |
| `TraceRepository` | 147 | ✅ Safe | No env reading |
| `LineageBuilder` | 148 | ✅ Safe | No env reading |
| `MemoryRelationRepository` | 151 | ✅ Safe | No env reading |
| `MemoryLineageBuilder` | 152 | ✅ Safe | No env reading |
| `IDiagnosticService` | 155–160 | ✅ Safe | Reads `ENGRAM_SERVER_URL` but uses null-coalescing |
| `SyncManager` | 180 | ✅ Safe | Lazy via factory |

**Only `LlmVerifier` has this problem.** The `IDiagnosticService` and `SyncManager` patterns are potential reference for the fix — they use factory delegates that handle missing env gracefully.

### 4.4 `engram serve` Impact

The `serve` command handler (line 48) does **NOT** register `IVerifier` — the verification services are only registered in the `mcp` command handler (lines 137–152). The server path is unaffected.

---

## 5. Recommendations

### 5.1 Recommended Fix: `NoOpVerifier` Factory Pattern

**Winner** over `Lazy<IVerifier>` for these reasons:

| Criterion | Lazy<IVerifier> | NoOpVerifier Factory |
|-----------|----------------|---------------------|
| Complexity | Higher — requires `Lazy<IVerifier>` type changes in EngramTools constructor | Lower — simple conditional in factory delegate |
| Testability | Good | Excellent — NoOpVerifier is easily testable |
| User experience | Still throws on mem_verify_artifact call (deferred) | Returns friendly error message immediately |
| Backward compat | ✅ | ✅ |
| Code changes | EngramTools.cs + Program.cs | Program.cs only (factory change) |
| Runtime overhead | Lazy<T> wrapper + lock on first access | Zero (NoOpVerifier is stateless) |
| Pattern precedent in codebase | None | Matches `IDiagnosticService` factory pattern (Program.cs:155) |

### 5.2 Implementation Sketch

**File**: `src/Engram.Cli/Program.cs` lines 137–139

Replace:
```csharp
mcpBuilder.Services.AddSingleton<IVerifier>(
    _ => new LlmVerifier());
```

With:
```csharp
mcpBuilder.Services.AddSingleton<IVerifier>(sp =>
{
    var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
    if (string.IsNullOrEmpty(apiKey))
        return new NoOpVerifier();
    return new LlmVerifier();
});
```

**Where to put NoOpVerifier**: Move from `tests/Engram.Mcp.Tests/EngramToolsTests.cs` into production code under `src/Engram.Verification/ArtifactVerifier.cs` (or own file `NoOpVerifier.cs` in same project).

**mem_verify_artifact enhancement** (EngramTools.cs:727): After fix, `MemVerifyArtifact` could detect NoOp via a marker (e.g., check if summary contains "No-op" or add an `IsNoOp` property to `IVerifier`). Alternatively, the factory approach gives a clean `if (noApiKey) return "ANTHROPIC_API_KEY not configured"` before even touching the verifier.

### 5.3 Recommended Additional Changes

| Change | Scope | Why |
|--------|-------|-----|
| Add `api_key_missing` error code to `McpErrors.cs` | Small | So `mem_verify_artifact` can return structured error |
| Move `NoOpVerifier` to `src/Engram.Verification/` | Small | Production code needs it |
| Add tests T1–T6 from §3.3 | Medium | Prevent regression |
| Update `docs/VERIFICATION.md` | Small | Document that ANTHROPIC_API_KEY is only needed for verify |
| Update `config/mcp/editors/` templates | Small | No change needed (never included ANTHROPIC_API_KEY) |

### 5.4 Rejected Alternatives

| Alternative | Why Rejected |
|-------------|-------------|
| `Lazy<IVerifier>` in DI | Works but more complex; still throws on first `mem_verify_artifact` call; NoOp approach is cleaner |
| Always register LlmVerifier and catch in MemVerifyArtifact | Catches at call site but doesn't prevent DI from throwing on construction (already too late) |
| Remove LlmVerifier registration entirely | Breaks `mem_verify_artifact` for users who DO have API key |
| Set dummy env var in setup.sh | Perpetuates the hack; doesn't solve for users who skip setup |

### 5.5 Key Learning

The bug was introduced because:
1. `IVerifier` is injected into EngramTools constructor (needed for DI auto-resolution by `WithTools<T>`)
2. `LlmVerifier` constructor eagerly reads env vars
3. No lazy/factory pattern was used

The `IDiagnosticService` registration (Program.cs:155) demonstrates the correct pattern — use a factory delegate that handles missing configuration gracefully.

---

## Appendix A: File Statistics

| File | Lines | Role |
|------|-------|------|
| `src/Engram.Cli/Program.cs` | 1440 | CLI entry point, DI registration |
| `src/Engram.Mcp/EngramMcpServer.cs` | 36 | MCP server builder |
| `src/Engram.Mcp/EngramTools.cs` | 1220 | MCP tools (28 tools) |
| `src/Engram.Mcp/McpErrors.cs` | 81 | Structured error helpers |
| `src/Engram.Verification/ArtifactVerifier.cs` | 170 | IVerifier + LlmVerifier + FakeVerifier |
| `src/Engram.Verification/Models.cs` | — | VerificationReport record |
| `tests/Engram.Mcp.Tests/EngramToolsTests.cs` | 863 | MCP tool tests (includes NoOpVerifier) |
| `tests/Engram.Mcp.Tests/StructuredErrorMigrationTests.cs` | 383 | Error path tests |
| `tests/Engram.Verification.Tests/VerifierTests.cs` | 124 | CycleTracker + TraceabilityMatrix tests |
| `config/mcp/editors/opencode.mcp.json` | 15 | OpenCode MCP template |
| `config/mcp/editors/cursor.mcp.json` | 15 | Cursor MCP template |
| `config/mcp/editors/vscode.mcp.json` | 15 | VS Code MCP template |
| `config/mcp/editors/claude-desktop.mcp.json` | 14 | Claude Desktop MCP template |
| `config/mcp/editors/antigravity.notes.md` | 20 | Antigravity setup notes |
| `scripts/setup.sh` | 172 | MCP setup wizard |
| `docs/VERIFICATION.md` | 204 | Verification tool docs |
| `docs/ARCHITECTURE.md` | 271 | Architecture docs |
| `docs/BACKLOG.md` | ~1100 | Backlog (ENG-456 at line 999) |

## Appendix B: Quick Reference — Key Line Numbers

```
Program.cs:126        mcpBuilder = EngramMcpServer.CreateBuilder(args)
Program.cs:138-139    IVerifier registration (THE BUG)
Program.cs:194        mcpBuilder.Build().RunAsync()

EngramMcpServer.cs:18 CreateBuilder() method
EngramMcpServer.cs:30 .AddMcpServer()
EngramMcpServer.cs:32 .WithTools<EngramTools>()  ← triggers DI resolution

EngramTools.cs:52     EngramTools constructor (takes IVerifier)
EngramTools.cs:698    [McpServerTool(Name = "mem_verify_artifact")]
EngramTools.cs:727    verifier.VerifyAsync(spec, codeDiff, currentCycle) ← only usage

ArtifactVerifier.cs:7  IVerifier interface
ArtifactVerifier.cs:33 LlmVerifier class
ArtifactVerifier.cs:44 LlmVerifier() constructor (throws)
ArtifactVerifier.cs:49-50 Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
ArtifactVerifier.cs:155 FakeVerifier class

EngramToolsTests.cs:37  _verifier = new NoOpVerifier();
EngramToolsTests.cs:45  _tools = new EngramTools(..., _verifier, ...);
EngramToolsTests.cs:827 NoOpVerifier class (test-only)

BACKLOG.md:999       ENG-456 bug description
BACKLOG.md:1003      Root cause analysis
BACKLOG.md:1007      Current workaround
BACKLOG.md:1010-1014 Acceptance criteria
BACKLOG.md:1015      Proposed fix
```
