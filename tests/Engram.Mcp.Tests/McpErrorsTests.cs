using System.Text.Json;
using Engram.Mcp;
using Xunit;

namespace Engram.Mcp.Tests;

/// <summary>
/// Tests for McpErrors.Structured() helper (REQ-ERR-003).
/// </summary>
public class McpErrorsTests
{
    [Fact]
    public void Structured_WithAllFields_ReturnsValidJson()
    {
        // Given: error with available_projects and hint
        // When: Structured is called
        var result = McpErrors.Structured(
            "ambiguous_project",
            "Multiple repos",
            new List<string> { "a", "b" },
            "navigate to one");

        // Then: JSON contains all 4 fields
        var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;

        Assert.True(root.GetProperty("error").GetBoolean());
        Assert.Equal("ambiguous_project", root.GetProperty("error_code").GetString());
        Assert.Equal("Multiple repos", root.GetProperty("message").GetString());
        Assert.Equal("navigate to one", root.GetProperty("hint").GetString());

        var projects = root.GetProperty("available_projects");
        Assert.Equal(2, projects.GetArrayLength());
        Assert.Equal("a", projects[0].GetString());
        Assert.Equal("b", projects[1].GetString());
    }

    [Fact]
    public void Structured_MinimalFields_ReturnsValidJson()
    {
        // Given: error with only required fields
        // When: Structured is called with just errorCode and message
        var result = McpErrors.Structured("session_not_found", "x");

        // Then: JSON has error and error_code and message
        var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;

        Assert.True(root.GetProperty("error").GetBoolean());
        Assert.Equal("session_not_found", root.GetProperty("error_code").GetString());
        Assert.Equal("x", root.GetProperty("message").GetString());

        // hint and available_projects are NOT present
        Assert.False(root.TryGetProperty("hint", out _));
        Assert.False(root.TryGetProperty("available_projects", out _));
    }

    [Fact]
    public void Structured_InvalidSnakeCase_Throws()
    {
        // Given: error code is NOT snake_case
        // When: Structured is called
        // Then: throws ArgumentException
        Assert.Throws<ArgumentException>(() =>
            McpErrors.Structured("INVALID-CODE", "x"));
    }

    [Fact]
    public void Structured_ResultIsNotIsError()
    {
        // Given: structured error
        // When: Structured is called
        var result = McpErrors.Structured("observation_not_found", "observation 123 not found");

        // Then: result is valid JSON (not a protocol error)
        // The structured JSON itself carries the error info
        var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;

        Assert.True(root.GetProperty("error").GetBoolean());
        Assert.Equal("observation_not_found", root.GetProperty("error_code").GetString());
    }

    [Theory]
    [InlineData("ambiguous_project")]
    [InlineData("unknown_project")]
    [InlineData("project_not_found")]
    [InlineData("session_not_found")]
    [InlineData("prompt_not_found")]
    [InlineData("observation_not_found")]
    [InlineData("validation_error")]
    [InlineData("blocked_by_observations")]
    [InlineData("internal_error")]
    public void Structured_ValidErrorCodes_AllWork(string errorCode)
    {
        // Given: valid error code from the spec
        // When: Structured is called
        var result = McpErrors.Structured(errorCode, "test message");

        // Then: no exception
        var doc = JsonDocument.Parse(result);
        Assert.Equal(errorCode, doc.RootElement.GetProperty("error_code").GetString());
    }
}