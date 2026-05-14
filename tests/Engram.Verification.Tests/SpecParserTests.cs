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
}
