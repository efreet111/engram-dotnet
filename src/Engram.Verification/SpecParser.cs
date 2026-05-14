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
///
/// Extracts requirements matching: - RF-NNN: description or - RNF-NNN: description
/// </summary>
public sealed class SpecParser
{
    // Regex for requirement lines: "- RF-001: description" or "- RNF-001: description"
    private static readonly Regex RequirementRegex = new(
        @"-\s*(RF-\d+|RNF-\d+)\s*:\s*(.+)",
        RegexOptions.Compiled | RegexOptions.Multiline);

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
            IsUnparseable = isUnparseable,
            Error = isUnparseable
                ? "No recognizable sections found. Expected one or more of: ## Objective, ## Objetivo, ## Functional Requirements, ## Requisitos Funcionales, ## Non-Functional Requirements, ## Requisitos No Funcionales"
                : null
        };
    }
}
