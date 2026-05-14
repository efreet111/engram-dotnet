using System.Text.Json.Serialization;

namespace Engram.Verification;

/// <summary>
/// A requirement extracted from a spec.md file.
/// </summary>
public sealed record Requirement
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";                       // "RF-001" or "RNF-001"
    [JsonPropertyName("type")] public string Type { get; init; } = "";                     // "RF" or "RNF"
    [JsonPropertyName("description")] public string Description { get; init; } = "";
    [JsonPropertyName("section")] public string Section { get; init; } = "";              // e.g. "Functional Requirements"
}

/// <summary>
/// Individual verification result for a single requirement.
/// </summary>
public sealed record VerificationItem
{
    [JsonPropertyName("requirement")] public Requirement Requirement { get; init; } = new();
    [JsonPropertyName("verdict")] public Verdict Verdict { get; init; } = Verdict.Untested;
    [JsonPropertyName("reasoning")] public string? Reasoning { get; init; }
    [JsonPropertyName("confidence")] public double Confidence { get; init; }               // 0.0 to 1.0
    [JsonPropertyName("evidence")] public string? Evidence { get; init; }                  // e.g., file path or line reference
}

/// <summary>
/// Verdict for a verification item.
/// </summary>
public enum Verdict
{
    Pass,
    Fail,
    Untested,
    Error,
    Escalate
}

/// <summary>
/// Full verification report covering all requirements.
/// </summary>
public sealed record VerificationReport
{
    [JsonPropertyName("items")] public List<VerificationItem> Items { get; init; } = [];
    [JsonPropertyName("coverage_pct")] public double CoveragePct { get; init; }
    [JsonPropertyName("pass_pct")] public double PassPct { get; init; }
    [JsonPropertyName("total")] public int Total { get; init; }
    [JsonPropertyName("passed")] public int Passed { get; init; }
    [JsonPropertyName("failed")] public int Failed { get; init; }
    [JsonPropertyName("cycle")] public int Cycle { get; init; }
    [JsonPropertyName("escalate")] public bool Escalate { get; init; }
    [JsonPropertyName("summary")] public string Summary { get; init; } = "";
}

/// <summary>
/// Traceability entry linking a requirement to code locations.
/// </summary>
public sealed record TraceabilityEntry
{
    [JsonPropertyName("requirement")] public Requirement Requirement { get; init; } = new();
    [JsonPropertyName("status")] public string Status { get; init; } = "";                  // "covered", "partial", "missing", "untraced"
    [JsonPropertyName("evidence")] public List<string> Evidence { get; init; } = [];
}

/// <summary>
/// Traceability matrix mapping all requirements to code coverage.
/// </summary>
public sealed record TraceabilityMatrix
{
    [JsonPropertyName("entries")] public List<TraceabilityEntry> Entries { get; init; } = [];
    [JsonPropertyName("total")] public int Total { get; init; }
    [JsonPropertyName("covered")] public int Covered { get; init; }
    [JsonPropertyName("missing")] public int Missing { get; init; }
    [JsonPropertyName("coverage_pct")] public double CoveragePct { get; init; }
}

/// <summary>
/// Rework ticket generated when verification detects failures.
/// </summary>
public sealed record ReworkTicket
{
    [JsonPropertyName("cycle")] public int Cycle { get; init; }
    [JsonPropertyName("max_cycles")] public int MaxCycles { get; init; } = 3;
    [JsonPropertyName("failed_items")] public List<VerificationItem> FailedItems { get; init; } = [];
    [JsonPropertyName("instructions")] public string Instructions { get; init; } = "";
    [JsonPropertyName("escalate")] public bool Escalate { get; init; }
}
