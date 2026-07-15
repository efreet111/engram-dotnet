using System.Text.Json;
using System.Text.RegularExpressions;

namespace Engram.Mcp;

/// <summary>
/// Helper for creating structured error responses in MCP tools.
/// All error codes must be snake_case.
/// </summary>
public static partial class McpErrors
{
    // Valid error codes (snake_case)
    private static readonly HashSet<string> ValidErrorCodes =
    [
        "ambiguous_project",
        "unknown_project",
        "project_not_found",
        "session_not_found",
        "prompt_not_found",
        "observation_not_found",
        "validation_error",
        "blocked_by_observations",
        "api_key_missing",
        "internal_error"
    ];

    /// <summary>
    /// Creates a structured error response as a JSON string.
    /// The JSON contains error information but IsError is NOT set to true
    /// (the structured JSON itself carries the error info).
    /// </summary>
    /// <param name="errorCode">snake_case error code</param>
    /// <param name="message">Human readable description</param>
    /// <param name="availableProjects">Optional list of available projects</param>
    /// <param name="hint">Optional suggestion for the user</param>
    public static string Structured(
        string errorCode,
        string message,
        IList<string>? availableProjects = null,
        string? hint = null)
    {
        // Validate snake_case format
        if (!SnakeCaseRegex().IsMatch(errorCode))
        {
            throw new ArgumentException(
                $"Error code must be snake_case, got: {errorCode}",
                nameof(errorCode));
        }

        if (!ValidErrorCodes.Contains(errorCode))
        {
            throw new ArgumentException(
                $"Unknown error code: {errorCode}. Must be one of: {string.Join(", ", ValidErrorCodes)}",
                nameof(errorCode));
        }

        var errorObj = new Dictionary<string, object?>
        {
            ["error"] = true,
            ["error_code"] = errorCode,
            ["message"] = message
        };

        if (availableProjects is { Count: > 0 })
        {
            errorObj["available_projects"] = availableProjects;
        }

        if (!string.IsNullOrEmpty(hint))
        {
            errorObj["hint"] = hint;
        }

        return JsonSerializer.Serialize(errorObj, new JsonSerializerOptions
        {
            WriteIndented = false
        });
    }

    [GeneratedRegex("^[a-z][a-z0-9_]*$")]
    private static partial Regex SnakeCaseRegex();
}