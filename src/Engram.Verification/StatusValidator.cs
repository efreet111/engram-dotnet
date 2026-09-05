namespace Engram.Verification;

/// <summary>
/// Validates observation status against the closed lifecycle enum.
/// Mirrors the RelationValidator pattern.
/// ENG-412: status is one of: active | deprecated | deleted.
/// </summary>
public static class StatusValidator
{
    /// <summary>
    /// The closed set of valid observation lifecycle statuses.
    /// </summary>
    public static readonly string[] ValidStatuses = ["active", "deprecated", "deleted"];

    private static readonly HashSet<string> ValidSet = new(ValidStatuses);

    /// <summary>
    /// Returns true if the status is one of: active, deprecated, deleted.
    /// </summary>
    public static bool IsValid(string? status)
        => status is not null && ValidSet.Contains(status);

    /// <summary>
    /// Comma-separated list of valid values for error messages.
    /// </summary>
    public static string ValidValuesDisplay => "active, deprecated, deleted";
}
