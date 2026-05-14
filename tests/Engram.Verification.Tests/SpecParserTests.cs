using Engram.Verification;
using Xunit;

namespace Engram.Verification.Tests;

public class SpecParserTests
{
    private readonly SpecParser _parser = new();

    [Fact]
    public void Parse_CanonicalSpec_ExtractsAllRequirements()
    {
        var markdown = @"
## Objective
Implement user registration with email and password.

## Functional Requirements
- RF-001: POST /users accepts {email, password}
- RF-002: Validates email format
- RF-003: Password minimum 8 chars

## Non-Functional Requirements
- RNF-001: Response time < 200ms
- RNF-002: No password in logs
";
        var result = _parser.Parse(markdown);

        Assert.False(result.IsUnparseable);
        Assert.Equal("Implement user registration with email and password.", result.Objective.Trim());
        Assert.Equal(5, result.Requirements.Count);

        // Check RFs
        Assert.Contains(result.Requirements, r => r.Id == "RF-001" && r.Type == "RF");
        Assert.Contains(result.Requirements, r => r.Id == "RF-002" && r.Type == "RF");
        Assert.Contains(result.Requirements, r => r.Id == "RF-003" && r.Type == "RF");

        // Check RNFs
        Assert.Contains(result.Requirements, r => r.Id == "RNF-001" && r.Type == "RNF");
        Assert.Contains(result.Requirements, r => r.Id == "RNF-002" && r.Type == "RNF");
    }

    [Fact]
    public void Parse_SpanishHeaders_StillParses()
    {
        var markdown = @"
## Objetivo
Registro de usuarios.

## Requisitos Funcionales
- RF-001: Registro con email y password

## Requisitos No Funcionales
- RNF-001: Tiempo de respuesta < 200ms
";
        var result = _parser.Parse(markdown);

        Assert.False(result.IsUnparseable);
        Assert.Equal("Registro de usuarios.", result.Objective.Trim());
        Assert.Equal(2, result.Requirements.Count);
    }

    [Fact]
    public void Parse_MissingRnfSection_StillParsesRfs()
    {
        var markdown = @"
## Objective
Test

## Functional Requirements
- RF-001: Do something
";
        var result = _parser.Parse(markdown);

        Assert.False(result.IsUnparseable);
        Assert.Single(result.Requirements);
        Assert.Equal("RF-001", result.Requirements[0].Id);
    }

    [Fact]
    public void Parse_EmptyString_ReturnsUnparseable()
    {
        var result = _parser.Parse("");

        Assert.True(result.IsUnparseable);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void Parse_NoRecognizableSections_ReturnsUnparseable()
    {
        var markdown = @"
# Random Document
This is just text with no recognizable sections.
- Some bullet
- Another bullet
";
        var result = _parser.Parse(markdown);

        Assert.True(result.IsUnparseable);
        Assert.Empty(result.Requirements);
    }

    [Fact]
    public void Parse_ObjectiveOnly_ReturnsEmptyRequirements()
    {
        var markdown = @"
## Objective
Just an objective, no requirements sections.
";
        var result = _parser.Parse(markdown);

        Assert.False(result.IsUnparseable);
        Assert.Empty(result.Requirements);
    }

    [Fact]
    public void Parse_WhitespaceMarkdown_ReturnsUnparseable()
    {
        var result = _parser.Parse("   \n  \n  ");
        Assert.True(result.IsUnparseable);
    }

    // ─── Traceability parsing (4.1) ──────────────────────────────────────────

    [Fact]
    public void ParseTraceability_ValidEntry_ExtractsAllFields()
    {
        var markdown = @"
## Objective
Test

## Functional Requirements
- RF-001: Do something

## Traceability

### RF-001: Do something
- **Source**: GITHUB-ISSUE-42
- **Author**: Support Team
- **Date**: 2026-05-14
- **Rationale**: Users cannot register with Unicode emails
        - **Relations**: Depends on RF-002, Supersedes RF-003
";
        var parser = new SpecParser();
        var result = parser.Parse(markdown);

        Assert.NotEmpty(result.Traceability);
        var trace = result.Traceability[0];
        Assert.Equal("RF-001", trace.RequirementId);
        Assert.NotNull(trace.Source);
        Assert.Equal("GITHUB-ISSUE-42", trace.Source.Source);
        Assert.Equal("Support Team", trace.Source.Author);
        Assert.Equal("2026-05-14", trace.Source.Date);
        Assert.Contains("Unicode emails", trace.Source.Rationale);
        Assert.Contains(trace.Relations, r => r is { Type: "depends_on", Target: "RF-002" });
        Assert.Contains(trace.Relations, r => r is { Type: "supersedes", Target: "RF-003" });
    }

    [Fact]
    public void ParseTraceability_MissingAuthorDate_StillParses()
    {
        var markdown = @"
## Objective
Test

## Functional Requirements
- RF-001: Test

## Traceability

### RF-001: Test
- **Source**: BUG-123
- **Rationale**: Critical bug fix
";
        var parser = new SpecParser();
        var result = parser.Parse(markdown);

        Assert.NotEmpty(result.Traceability);
        var trace = result.Traceability[0];
        Assert.NotNull(trace.Source);
        Assert.Equal("BUG-123", trace.Source.Source);
        Assert.Null(trace.Source.Author);
        Assert.Null(trace.Source.Date);
    }

    [Fact]
    public void ParseTraceability_NoTraceabilitySection_ReturnsEmpty()
    {
        var markdown = @"
## Objective
Test

## Functional Requirements
- RF-001: Test
";
        var parser = new SpecParser();
        var result = parser.Parse(markdown);

        Assert.Empty(result.Traceability);
    }

    // ─── Relation parsing (4.2) ──────────────────────────────────────────────

    [Fact]
    public void ParseRelations_ValidTypes_ParsesCorrectly()
    {
        var relations = SpecParser.ParseRelations("Depends on RF-001, Supersedes RF-002, Conflicts with RF-003, Related to RF-004");

        Assert.Equal(4, relations.Count);
        Assert.Contains(relations, r => r is { Type: "depends_on", Target: "RF-001" });
        Assert.Contains(relations, r => r is { Type: "supersedes", Target: "RF-002" });
        Assert.Contains(relations, r => r is { Type: "conflicts_with", Target: "RF-003" });
        Assert.Contains(relations, r => r is { Type: "related_to", Target: "RF-004" });
    }

    [Fact]
    public void ParseRelations_InvalidType_SkipsEntry()
    {
        var relations = SpecParser.ParseRelations("invalid RF-001, Depends on RF-002");

        Assert.Single(relations);
        Assert.Equal("depends_on", relations[0].Type);
    }

    [Fact]
    public void ParseRelations_EmptyString_ReturnsEmpty()
    {
        var relations = SpecParser.ParseRelations("");
        Assert.Empty(relations);
    }
}
