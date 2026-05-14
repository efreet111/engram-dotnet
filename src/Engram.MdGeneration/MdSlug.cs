namespace Engram.MdGeneration;

public static class MdSlug
{
    /// <summary>
    /// Generate a URL-safe slug from a title.
    /// Lowercase, hyphens, 60-char max, appends short hash on collision.
    /// </summary>
    public static string Slugify(string title, long? observationId = null)
    {
        if (string.IsNullOrWhiteSpace(title))
            return $"untitled-{observationId ?? Random.Shared.Next():x8}";

        // Lowercase, replace non-alphanumeric with hyphens
        var slug = System.Text.RegularExpressions.Regex.Replace(
            title.ToLowerInvariant(),
            @"[^a-z0-9]+", "-")
            .Trim('-');

        if (slug.Length > 60)
            slug = slug[..60].Trim('-');

        if (string.IsNullOrEmpty(slug))
            slug = $"untitled-{observationId ?? Random.Shared.Next():x8}";

        return slug;
    }

    /// <summary>
    /// Generate a filename: YYYY-MM-DD-slug.md
    /// </summary>
    public static string ToFilename(string title, DateTime? date = null, long? observationId = null)
    {
        var d = date ?? DateTime.UtcNow;
        var slug = Slugify(title, observationId);
        return $"{d:yyyy-MM-dd}-{slug}.md";
    }
}
