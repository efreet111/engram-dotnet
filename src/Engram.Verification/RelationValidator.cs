namespace Engram.Verification;

/// <summary>
/// Validates trace relation types against the known set of allowed values.
/// </summary>
public static class RelationValidator
{
    private static readonly HashSet<string> ValidTypes =
        ["depends_on", "supersedes", "conflicts_with", "related_to"];

    /// <summary>
    /// Determines whether the specified relation type is one of the known valid types.
    /// </summary>
    /// <param name="type">The relation type to validate (e.g., "depends_on").</param>
    /// <returns><c>true</c> if the type is valid; otherwise <c>false</c>.</returns>
    public static bool IsValidType(string type) => ValidTypes.Contains(type);

    /// <summary>
    /// Returns a copy of the valid relation types array.
    /// </summary>
    public static string[] ValidRelationTypes => [.. ValidTypes];
}
