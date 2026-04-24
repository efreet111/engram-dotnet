using System.Text.RegularExpressions;

namespace Engram.Obsidian;

/// <summary>
/// Converts an observation title and ID into a filesystem-safe slug.
/// Port of Go's internal/obsidian/slug.go.
/// </summary>
public static partial class Slug
{
    private const int MaxSlugLen = 60;

    [GeneratedRegex(@"[^a-z0-9]+", RegexOptions.Compiled)]
    private static partial Regex NonAlphanumeric();

    /// <summary>
    /// Converts a title and ID into a filesystem-safe slug.
    /// Algorithm:
    /// 1. Lowercase the title
    /// 2. Replace non-alphanumeric characters with hyphens
    /// 3. Trim leading/trailing hyphens
    /// 4. Truncate to 60 chars (trimming trailing hyphens after truncation)
    /// 5. Append the ID for collision safety
    /// If the title is empty, returns "observation-{id}".
    /// </summary>
    public static string Slugify(string title, long id)
    {
        if (string.IsNullOrEmpty(title))
            return $"observation-{id}";

        var s = title.ToLowerInvariant();
        s = NonAlphanumeric().Replace(s, "-");
        s = s.Trim('-');

        if (s.Length > MaxSlugLen)
        {
            s = s[..MaxSlugLen];
            s = s.TrimEnd('-');
        }

        return $"{s}-{id}";
    }
}
