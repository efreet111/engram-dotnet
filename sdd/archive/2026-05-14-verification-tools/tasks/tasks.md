# Tasks: Verification Tools — Artifact Compliance & Traceability

## Phase 1: Foundation

- [x] 1.1 Create `src/Engram.Verification/Engram.Verification.csproj` — .NET 10 classlib with `<ProjectReference>` to `Engram.Store`
- [x] 1.2 Create `src/Engram.Verification/Models.cs` — `Requirement`, `VerificationItem`, `Verdict` enum, `VerificationReport`, `TraceabilityEntry`, `TraceabilityMatrix`, `ReworkTicket` sealed records
- [x] 1.3 Create `src/Engram.Verification/SpecParser.cs` — regex parser extracting `## Objetivo`/`## Objective`, RF-NNN, RNF-NNNN sections; returns `unparseable` on no match

## Phase 2: Core Implementation

- [x] 2.1 Create `src/Engram.Verification/ArtifactVerifier.cs` — `IVerifier` interface + `LlmVerifier` calling Anthropic `/v1/messages` via `HttpClient`; model from `ENGRAM_VERIFICATION_MODEL` env
- [x] 2.2 Create `src/Engram.Verification/CycleTracker.cs` — `GetOrCreateCycle/IncrementCycle/ResetCycle` via `IStore` + `topic_key=$"cycle-count/{name}"`; `Escalate` at `ENGRAM_VERIFICATION_MAX_CYCLES` (default 3)
- [x] 2.3 Create `src/Engram.Verification/TraceabilityMatrix.cs` — `BuildMatrixAsync()` searches observations via `IStore.SearchAsync` per RF/RNF; returns `TraceabilityMatrix` with coverage flags

## Phase 3: Integration

- [x] 3.1 Add `mem_verify_artifact` `[McpServerTool]` to `src/Engram.Mcp/EngramTools.cs` — accepts specPath, planPath, diff; coordinates SpecParser + ArtifactVerifier + CycleTracker; returns formatted VerificationReport
- [x] 3.2 Add `mem_traceability` `[McpServerTool]` to `src/Engram.Mcp/EngramTools.cs` — accepts specPath, project; coordinates SpecParser + TraceabilityMatrix; returns structured matrix
- [x] 3.3 Register `IVerifier` (singleton `LlmVerifier`) and `CycleTracker` (singleton) in `src/Engram.Cli/Program.cs` DI for MCP server construction

## Phase 4: Testing

- [x] 4.1 Create `tests/Engram.Verification.Tests/Engram.Verification.Tests.csproj` — xUnit project referencing `Engram.Verification` + `Engram.Store`; follow existing `SqliteStore` test pattern
- [x] 4.2 Write `SpecParserTests.cs` — canonical spec (3 RF, 2 RNF), missing RNF, unparseable, empty, Objective-only
- [x] 4.3 Write `VerifierTests.cs` — `CycleTracker` create/increment/escalate/reset with fake `IStore`; `TraceabilityMatrix.BuildMatrix` with mocked search
- [x] 4.4 Write integration tests for `mem_verify_artifact` + `mem_traceability` with `SqliteStore` + `FakeVerifier`; cover pass, fail, empty diff, escalate

(End of file)
