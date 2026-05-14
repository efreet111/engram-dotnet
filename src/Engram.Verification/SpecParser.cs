using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Engram.Verification;

/// <summary>
/// Result of parsing a spec.md file.
/// </summary>
public sealed record SpecParseResult
{
    [JsonPropertyName("objective")] public string Objective { get; init; } = "";
    [JsonPropertyName("requirements")] public List<Requirement> Requirements { get; init; } = [];
    [JsonPropertyName("traceability")] public List<TraceInfo> Traceability { get; init; } = [];
    [JsonPropertyName("is_unparseable")] public bool IsUnparseable { get; init; }
    [JsonPropertyName("error")] public string? Error { get; init; }
}

/// <summary>
/// Result of parsing the ## Traceability section of a spec.md file.
/// </summary>
public sealed record TraceParseResult
{
    [JsonPropertyName("traces")] public List<TraceInfo> Traces { get; init; } = [];
    [JsonPropertyName("is_unparseable")] public bool IsUnparseable { get; init; }
    [JsonPropertyName("error")] public string? Error { get; init; }
}

/// <summary>
/// Regex-based parser for canonical EngramFlow spec.md format.
///
/// Recognizes sections:
///   - ## Objective / ## Objetivo
///   - ## Functional Requirements / ## Requisitos Funcionales
///   - ## Non-Functional Requirements / ## Requisitos No Funcionales
///   - ## Traceability
///
/// Extracts requirements matching: - RF-NNN: description or - RNF-NNN: description
/// </summary>
public sealed class SpecParser
{
    // Regex for requirement lines: "- RF-001: description" or "- RNF-001: description"
    private static readonly Regex RequirementRegex = new(
        @"-\s*(RF-\d+|RNF-\d+)\s*:\s*(.+)",
        RegexOptions.Compiled | RegexOptions.Multiline);

    // Regex for subsection headers within Traceability: "### RF-001: ..." or "### RNF-001: ..."
    private static readonly Regex TraceSubHeaderRegex = new(
        @"^###\s+(RF-\d+|RNF-\d+)\s*:\s*(.*)$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    // Regex for field lines: "- **Source**: value", "- **Author**: value", etc.
    private static readonly Regex FieldLineRegex = new(
        @"-\s*\*\*(\w+)\*\*:\s*(.*)",
        RegexOptions.Compiled);

    // Regex for individual relation entries: "Depends on RF-001", "Supersedes RF-003", etc.
    private static readonly Regex RelationEntryRegex = new(
        @"(Depends on|Supersedes|Conflicts with|Related to)\s+(RF-\d+|RNF-\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Parse a spec.md string into a <see cref="SpecParseResult"/>.
    /// </summary>
    /// <param name="markdown">Raw markdown content of spec.md.</param>
    /// <returns>Parsed result with objective, requirements, and parseability flag.</returns>
    public SpecParseResult Parse(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return new SpecParseResult
            {
                IsUnparseable = true,
                Error = "Empty markdown content"
            };
        }

        var lines = markdown.Split('\n');
        string? currentSection = null;
        var currentSectionLines = new List<string>();

        var objectiveParts = new List<string>();
        var requirements = new List<Requirement>();

        bool foundObjective = false;
        bool foundRf = false;
        bool foundRnf = false;

        void FlushSection()
        {
            if (currentSection is null) return;

            var header = currentSection.Trim();
            var headerLower = header.ToLowerInvariant();

            // Check for Objective section
            if (headerLower is "objective" or "objetivo")
            {
                foundObjective = true;
                var content = string.Join("\n", currentSectionLines).Trim();
                if (content.Length > 0)
                    objectiveParts.Add(content);
                return;
            }

            // Determine if this is a requirements section
            bool isRf = headerLower is "functional requirements" or "requisitos funcionales";
            bool isRnf = headerLower is "non-functional requirements" or "requisitos no funcionales";

            if (!isRf && !isRnf) return; // Unknown section, skip

            if (isRf) foundRf = true;
            if (isRnf) foundRnf = true;

            var sectionContent = string.Join("\n", currentSectionLines);

            foreach (Match match in RequirementRegex.Matches(sectionContent))
            {
                var id = match.Groups[1].Value;
                var description = match.Groups[2].Value.Trim();
                var type = id.StartsWith("RF") ? "RF" : "RNF";

                requirements.Add(new Requirement
                {
                    Id = id,
                    Type = type,
                    Description = description,
                    Section = header
                });
            }
        }

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');

            if (line.StartsWith("## "))
            {
                // Flush previous section
                FlushSection();

                // Start new section
                currentSection = line[3..]; // Everything after "## "
                currentSectionLines.Clear();
            }
            else
            {
                currentSectionLines.Add(line);
            }
        }

        // Flush last section
        FlushSection();

        // Determine if the document was parseable
        // Unparseable = no recognized sections at all
        bool isUnparseable = !foundObjective && !foundRf && !foundRnf;

        return new SpecParseResult
        {
            Objective = string.Join("\n", objectiveParts),
            Requirements = requirements,
            Traceability = TryParseTraceability(markdown),
            IsUnparseable = isUnparseable,
            Error = isUnparseable
                ? "No recognizable sections found. Expected one or more of: ## Objective, ## Objetivo, ## Functional Requirements, ## Requisitos Funcionales, ## Non-Functional Requirements, ## Requisitos No Funcionales"
                : null
        };
    }

    // ─── Traceability parsing ────────────────────────────────────────────────

    /// <summary>
    /// Parse the ## Traceability section from a spec.md string.
    /// Returns a <see cref="TraceParseResult"/> with trace entries or error info.
    /// </summary>
    /// <param name="markdown">Raw markdown content of spec.md.</param>
    /// <returns>Parsed result with trace entries, parseability flag, and optional error.</returns>
    public TraceParseResult ParseTraceability(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return new TraceParseResult
            {
                IsUnparseable = true,
                Error = "Empty markdown content"
            };
        }

        var traces = TryParseTraceability(markdown);
        return new TraceParseResult
        {
            Traces = traces,
            IsUnparseable = traces.Count == 0,
            Error = traces.Count == 0
                ? "No ## Traceability section found"
                : null
        };
    }

    /// <summary>
    /// Attempt to extract traceability entries from the ## Traceability section.
    /// </summary>
    private static List<TraceInfo> TryParseTraceability(string markdown)
    {
        var lines = markdown.Split('\n');
        string? currentSection = null;
        var sectionLines = new List<string>();
        var traces = new List<TraceInfo>();

        bool inTraceability = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');

            if (line.StartsWith("## "))
            {
                // Flush previous subsection before switching sections
                if (inTraceability && currentSection is not null)
                {
                    var trace = ParseTraceSubsection(currentSection, sectionLines);
                    if (trace is not null)
                        traces.Add(trace);
                }

                currentSection = null;
                sectionLines.Clear();

                var header = line[3..].Trim().ToLowerInvariant();
                inTraceability = header == "traceability";
                continue;
            }

            if (!inTraceability)
                continue;

            if (line.StartsWith("### ") && TraceSubHeaderRegex.IsMatch(line))
            {
                // Flush previous subsection
                if (currentSection is not null)
                {
                    var trace = ParseTraceSubsection(currentSection, sectionLines);
                    if (trace is not null)
                        traces.Add(trace);
                }

                currentSection = line;
                sectionLines.Clear();
            }
            else
            {
                sectionLines.Add(line);
            }
        }

        // Flush last subsection
        if (inTraceability && currentSection is not null)
        {
            var trace = ParseTraceSubsection(currentSection, sectionLines);
            if (trace is not null)
                traces.Add(trace);
        }

        return traces;
    }

    /// <summary>
    /// Parse a single ### RF-NNN/RNF-NNN subsection within Traceability.
    /// </summary>
    private static TraceInfo? ParseTraceSubsection(string headerLine, List<string> contentLines)
    {
        var match = TraceSubHeaderRegex.Match(headerLine);
        if (!match.Success)
            return null;

        var requirementId = match.Groups[1].Value;

        string? sourceValue = null;
        string? author = null;
        string? date = null;
        string? rationale = null;
        string? relationsLine = null;

        foreach (var line in contentLines)
        {
            var fieldMatch = FieldLineRegex.Match(line);
            if (!fieldMatch.Success)
                continue;

            var fieldName = fieldMatch.Groups[1].Value.ToLowerInvariant();
            var fieldValue = fieldMatch.Groups[2].Value.Trim();

            switch (fieldName)
            {
                case "source":
                    sourceValue = fieldValue;
                    break;
                case "author":
                    author = fieldValue;
                    break;
                case "date":
                    date = fieldValue;
                    break;
                case "rationale":
                    rationale = fieldValue;
                    break;
                case "relations":
                    relationsLine = fieldValue;
                    break;
            }
        }

        var traceSource = sourceValue is not null
            ? new TraceSource
            {
                Source = sourceValue,
                Author = author,
                Date = date,
                Rationale = rationale
            }
            : null;

        var relations = ParseRelations(relationsLine);

        return new TraceInfo
        {
            RequirementId = requirementId,
            Source = traceSource,
            Relations = relations
        };
    }

    /// <summary>
    /// Parse a relations string like "Depends on RF-001, Supersedes RF-003" into a list of <see cref="TraceRelation"/>.
    /// </summary>
    public static List<TraceRelation> ParseRelations(string? relationsLine)
    {
        if (string.IsNullOrWhiteSpace(relationsLine))
            return [];

        var relations = new List<TraceRelation>();

        // Split by comma — each segment is one relation
        var segments = relationsLine.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        foreach (var segment in segments)
        {
            var relMatch = RelationEntryRegex.Match(segment);
            if (!relMatch.Success)
                continue;

            var verb = relMatch.Groups[1].Value.ToLowerInvariant();
            var target = relMatch.Groups[2].Value;

            var type = verb switch
            {
                "depends on" => "depends_on",
                "supersedes" => "supersedes",
                "conflicts with" => "conflicts_with",
                "related to" => "related_to",
                _ => verb.Replace(' ', '_')
            };

            relations.Add(new TraceRelation
            {
                Type = type,
                Target = target
            });
        }

        return relations;
    }
}
