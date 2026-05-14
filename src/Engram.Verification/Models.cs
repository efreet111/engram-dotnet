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
    [JsonPropertyName("source_status")] public string? SourceStatus { get; init; }
    [JsonPropertyName("source_warning")] public string? SourceWarning { get; init; }
    [JsonPropertyName("superseded_by")] public string? SupersededBy { get; init; }
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

// ─── Traceability models ───────────────────────────────────────────────────────

/// <summary>
/// Source of a traceability link — e.g., a GitHub issue, JIRA ticket, or ADR.
/// </summary>
public sealed record TraceSource
{
    [JsonPropertyName("source")] public string Source { get; init; } = "";   // e.g., "GITHUB-ISSUE-42"
    [JsonPropertyName("author")] public string? Author { get; init; }
    [JsonPropertyName("date")] public string? Date { get; init; }
    [JsonPropertyName("rationale")] public string? Rationale { get; init; }
}

/// <summary>
/// A typed relation between two requirements.
/// </summary>
public sealed record TraceRelation
{
    [JsonPropertyName("type")] public string Type { get; init; } = "";       // depends_on, supersedes, conflicts_with, related_to
    [JsonPropertyName("target")] public string Target { get; init; } = "";   // e.g., "RF-003"
}

/// <summary>
/// Full trace information for a single requirement.
/// </summary>
public sealed record TraceInfo
{
    [JsonPropertyName("requirement_id")] public string RequirementId { get; init; } = "";
    [JsonPropertyName("source")] public TraceSource? Source { get; init; }
    [JsonPropertyName("relations")] public List<TraceRelation> Relations { get; init; } = [];
}

/// <summary>
/// Result of querying trace status for a requirement.
/// </summary>
public sealed record TraceResult
{
    [JsonPropertyName("requirement_id")] public string RequirementId { get; init; } = "";
    [JsonPropertyName("status")] public string Status { get; init; } = "";   // "traced", "untraced", "error"
    [JsonPropertyName("source")] public TraceSource? Source { get; init; }
    [JsonPropertyName("lineage")] public List<string>? Lineage { get; init; }
    [JsonPropertyName("error")] public string? Error { get; init; }
}

/// <summary>
/// Full lineage tree result including ancestry and descendants.
/// </summary>
public sealed record LineageResult
{
    [JsonPropertyName("root")] public TraceResult Root { get; init; } = new();
    [JsonPropertyName("ancestors")] public List<TraceResult> Ancestors { get; init; } = [];
    [JsonPropertyName("descendants")] public List<TraceResult> Descendants { get; init; } = [];
    [JsonPropertyName("cycle_detected")] public bool CycleDetected { get; init; }
    [JsonPropertyName("hops")] public int Hops { get; init; }
}
