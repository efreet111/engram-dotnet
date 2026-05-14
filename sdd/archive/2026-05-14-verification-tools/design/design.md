# Design: Verification Tools — Artifact Compliance & Traceability

## Technical Approach

Nuevo proyecto `Engram.Verification` con tres componentes: **SpecParser** (parsing de spec.md canónico), **ArtifactVerifier** (LLM-as-Judge contra RF/RNF), y **TraceabilityMatrix** (cobertura observacional). Dos tools MCP nuevas (`mem_verify_artifact`, `mem_traceability`) se registran en `EngramTools` siguiendo el patrón `[McpServerTool]` existente. El ciclo de rework se trackea con observaciones en la store vía `topic_key`.

## Architecture Decisions

### Decision: LLM Client — `IVerifier` interface con `LlmVerifier`

| Opción | Tradeoff | Decisión |
|--------|----------|----------|
| `Microsoft.Extensions.AI` abstracciones | Moderno pero agrega dependencia externa para algo simple | ❌ |
| Raw `HttpClient` directo | Simple, ya usado en `HttpStore` | ✅ |
| `IVerifier` interface | Permite testear sin LLM real (mock) y swap de backend | ✅ |

**Rationale**: `IVerifier` permite inyectar un `FakeVerifier` en tests (reporta resultados determinísticos). `LlmVerifier` implementa el contrato llamando a Anthropic/OpenAI compatible via `HttpClient`. `ENGRAM_VERIFICATION_MODEL` (default: `claude-sonnet-4-20250514`) configura el modelo.

### Decision: Cycle count — Observation con `topic_key`

| Opción | Tradeoff | Decisión |
|--------|----------|----------|
| Archivo JSON local | No persiste en multi-máquina, fuera del ecosistema engram | ❌ |
| Observation con `topic_key` | Usa la store existente, visible via `mem_search`, upsertable | ✅ |

**Rationale**: `topic_key = $"cycle-count/{changeName}"` permite upsert vía `mem_save` + `topic_key`. Se lee con `mem_search` por topic_key o `GetObservationAsync`. Scope `team` para visibilidad compartida.

### Decision: SpecParser — Regex section-based sin parser externo

| Opción | Tradeoff | Decisión |
|--------|----------|----------|
| Markdown AST parser (Markdig) | Overkill para 3 secciones fijas | ❌ |
| Regex section extraction | Simple, ~50 líneas, sin dependencias | ✅ |

**Rationale**: El formato canónico es fijo: `## Objective`, `## Functional Requirements`, `## Non-Functional Requirements`. Regex multilínea basta. Si no encuentra secciones, reporta "unparseable" sin crash.

### Decision: Two tools vs one combined

| Opción | Tradeoff | Decisión |
|--------|----------|----------|
| Tool única `mem_verify` | Menos tools, pero mezcla concerns | ❌ |
| `mem_verify_artifact` + `mem_traceability` | Separación clara de concerns | ✅ |

**Rationale**: `mem_verify_artifact` es pesada (LLM call). `mem_traceability` es ligera (solo search + parser). Son operaciones conceptualmente distintas.

### Decision: `IVerifier` injected via DI en `EngramTools`

| Opción | Tradeoff | Decisión |
|--------|----------|----------|
| Verifier como static util | No testeable, no intercambiable | ❌ |
| Verifier via DI en constructor | Sigue el patrón `IStore`, `WriteQueue`, `McpConfig` existente | ✅ |

**Rationale**: El patrón de DI constructor injection ya está establecido en `EngramTools`. `IVerifier` se registra en `Program.cs` al construir el MCP server.

## Data Flow

```
    LLM Request (claude-sonnet-4)
         ▲
         │ HTTP POST
         │
┌─────────────────────────────────────────────────────────────┐
│  mem_verify_artifact(spec, plan, diff, project)             │
│                                                             │
│  ┌──────────┐   ┌──────────────┐   ┌──────────────────┐    │
│  │ SpecParser│──→│ArtifactVerifier│──→│ CycleTracker     │    │
│  │ .ParseSpec│   │ .Verify()    │   │ .GetOrCreateCycle│    │
│  └──────────┘   └──────┬───────┘   └────────┬─────────┘    │
│                        │                     │              │
│                        │          ┌──────────▼─────────┐   │
│                        │          │ Store (topic_key:   │   │
│                        │          │  cycle-count/{name})│   │
│                        │          └────────────────────┘   │
│                        ▼                                   │
│              ┌──────────────────┐                          │
│              │ VerificationReport│ ← JSON + Markdown       │
│              │ (passed, failed,  │                          │
│              │  untested,        │                          │
│              │  coverage_pct,    │                          │
│              │  escalate)        │                          │
│              └──────────────────┘                          │
└─────────────────────────────────────────────────────────────┘

┌──────────────────────────────────────────────────────────────┐
│  mem_traceability(spec, project)                             │
│                                                              │
│  ┌──────────┐   ┌──────────────────┐                        │
│  │ SpecParser│──→│ TraceabilityMatrix│                       │
│  │ .ParseSpec│   │ .BuildMatrix()   │                        │
│  └──────────┘   │ → busca RF/RNF en │                       │
│                 │   store via search │                       │
│                 │ → genera matrix    │                       │
│                 └──────────────────┘                        │
└──────────────────────────────────────────────────────────────┘
```

## File Changes

| File | Action | Description |
|------|--------|-------------|
| `src/Engram.Verification/Engram.Verification.csproj` | Create | .NET 10 classlib; ref `Engram.Store` |
| `src/Engram.Verification/Models.cs` | Create | `VerificationReport`, `VerificationItem`, `TraceabilityMatrix`, `TraceabilityEntry`, `ReworkTicket` |
| `src/Engram.Verification/SpecParser.cs` | Create | Parse spec.md canónico → `(Objective, List<RfItem>, List<RnfItem>)` |
| `src/Engram.Verification/ArtifactVerifier.cs` | Create | `IVerifier` interface + `LlmVerifier` impl |
| `src/Engram.Verification/CycleTracker.cs` | Create | Read/increment cycle count via IStore + topic_key |
| `src/Engram.Verification/TraceabilityMatrix.cs` | Create | Build RF/RNF → code matrix via store search |
| `src/Engram.Mcp/EngramTools.cs` | Modify | Add `mem_verify_artifact`, `mem_traceability` methods |
| `src/Engram.Cli/Program.cs` | Modify | Register `IVerifier` + `CycleTracker` in DI for MCP |
| `tests/Engram.Verification.Tests/Engram.Verification.Tests.csproj` | Create | xUnit test project |
| `tests/Engram.Verification.Tests/SpecParserTests.cs` | Create | Unit: canonical, malformed, missing sections |
| `tests/Engram.Verification.Tests/VerifierTests.cs` | Create | Unit: fake verifier, edge cases |

## Interfaces / Contracts

```csharp
// ─── Engram.Verification/IVerifier.cs ───
namespace Engram.Verification;

public interface IVerifier
{
    /// <summary>
    /// Judge whether the given code diff/file-list satisfies an RF or RNF.
    /// Returns structured verdict per requirement.
    /// </summary>
    Task<VerificationReport> VerifyAsync(
        IReadOnlyList<Requirement> functional,
        IReadOnlyList<Requirement> nonFunctional,
        string codeDiff,
        CancellationToken ct = default);
}

// ─── Engram.Verification/Models.cs ───
public sealed record Requirement(string Id, string Description);

public sealed record VerificationItem(
    string RequirementId,
    string Description,
    Verdict Status,        // Pass | Fail | Untested
    double Confidence,     // 0.0–1.0
    string? Reasoning);    // LLM reasoning

public enum Verdict { Pass, Fail, Untested }

public sealed record VerificationReport(
    IReadOnlyList<VerificationItem> Passed,
    IReadOnlyList<VerificationItem> Failed,
    IReadOnlyList<VerificationItem> Untested,
    double CoveragePct,           // passed / total
    int CycleCount,
    int MaxCycles = 3,
    bool Escalate => CycleCount >= MaxCycles);

public sealed record TraceabilityEntry(
    string RequirementId,
    string Description,
    IReadOnlyList<string> RelatedObservations, // matched obs titles/IDs
    bool HasTestCoverage);

public sealed record TraceabilityMatrix(
    IReadOnlyList<TraceabilityEntry> Functional,
    IReadOnlyList<TraceabilityEntry> NonFunctional);

public sealed record ReworkTicket(
    string CycleLabel,         // "Cycle {N}/{MAX}"
    IReadOnlyList<VerificationItem> FailedItems,
    string Instructions,       // Generated instructions for Dev Agent
    bool Escalate);
```

## Testing Strategy

| Layer | What to Test | Approach |
|-------|-------------|----------|
| Unit | `SpecParser.ParseSpec()` | Canonical spec → 3+ RF, 2+ RNF. Missing sections → "unparseable". Empty → empty lists. |
| Unit | `CycleTracker` | Create → returns 1. Increment → returns 2. At 3 → `Escalate=true`. Persisted via fake `IStore`. |
| Unit | `TraceabilityMatrix.BuildMatrix()` | Mock `IStore.SearchAsync` returns known results. Matrix shows correct coverage. |
| Integration | `mem_verify_artifact` via `FakeVerifier` | Tool returns formatted report with pass/fail. Checks escalate logic. |
| Integration | `mem_traceability` via real store | Seed observations, call tool, verify matrix content. |

Test pattern: seguir el existente en `tests/Engram.Mcp.Tests/` — xUnit con `SqliteStore` en temp dir, tests llaman a tools directamente (sin JSON-RPC).

## Rework Ticket Format (Canonic)

```markdown
# Rework Ticket — Cycle {N}/{MAX}

## Failed Items
- [ ] {RF-NNN}: {razón del fallo}
- [ ] {RNF-NNNN}: {razón del fallo}

## Instructions
{Verify Agent → Dev Agent: qué cambiar y por qué}

## Verdict
{escalate: true} si cycle >= max, sino omitido
```

## Configuration

- `ENGRAM_VERIFICATION_MODEL` — modelo LLM (default: `claude-sonnet-4-20250514`)
- `ENGRAM_VERIFICATION_MAX_CYCLES` — ciclo máximo antes de escalate (default: `3`)

## Migration / Rollout

No migration required. Las tools son read-only y opcionales. Rollback: remover tools de `EngramTools.cs` + revertir registro DI.

## Open Questions

- [ ] Endpoint URL del LLM: Anthropic directo vs proxy propio vs OpenAI compatible? Asumo `ANTHROPIC_API_KEY` + `https://api.anthropic.com/v1/messages`. Se resuelve en implementación.
- [ ] `codeDiff` en `mem_verify_artifact`: ¿string plano de diff o lista de paths para leer del FS? La propuesta dice "diff de cambios (o permite pasar file list)". El diseño expone `string codeDiff` en `IVerifier.VerifyAsync`; el MCP tool puede formatear antes de llamar.
