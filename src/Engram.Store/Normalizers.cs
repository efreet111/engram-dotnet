using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Linq;

namespace Engram.Store;

public static class Normalizers
{
    public static string HashNormalized(string content)
    {
        var normalized = string.Join(" ",
            content.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .ToLowerInvariant();
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string NormalizeTopicKey(string? topic)
    {
        if (string.IsNullOrWhiteSpace(topic)) return "";
        var v = string.Join("-",
            topic.Trim().ToLowerInvariant()
                 .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return v.Length > 120 ? v[..120] : v;
    }

    public static string NormalizeProject(string? project)
    {
        var (normalized, _) = NormalizeProjectWithWarning(project);
        return normalized;
    }

    /// <summary>
    /// Normalizes a project name and returns a warning message if the name was changed.
    /// This mirrors the Go original NormalizeProject which returns (normalized, warning).
    /// The warning is empty when no normalization was needed.
    /// </summary>
    public static (string normalized, string warning) NormalizeProjectWithWarning(string? project)
    {
        if (string.IsNullOrWhiteSpace(project)) return ("", "");
        var originalNormalized = project.Trim().ToLowerInvariant();
        // replace underscores and whitespace with hyphens
        var n = Regex.Replace(originalNormalized, @"[_\s]+", "-");
        // collapse consecutive hyphens
        while (n.Contains("--")) n = n.Replace("--", "-");
        if (n == originalNormalized) return (n, "");
        return (n, $"⚠️ Project name normalized: \"{project}\" → \"{n}\"");
    }

    public static string DedupeWindowExpression(TimeSpan window)
    {
        var minutes = Math.Max(1, (int)window.TotalMinutes);
        return $"-{minutes} minutes";
    }

    public static string SanitizeFts5Query(string query)
    {
        var tokens = query.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", tokens.Select(t => $"\"{t.Replace("\"", "\"\"")}\""));
    }

    // ─── Topic key suggestion (port of store.SuggestTopicKey from Go) ───────

    /// <summary>
    /// Generates a stable topic_key suggestion from type/title/content.
    /// Infers a family prefix (e.g. "architecture", "bug") and appends a normalized slug.
    /// </summary>
    public static string SuggestTopicKey(string? type, string? title, string? content)
    {
        var family  = InferTopicFamily(type, title, content);
        var segment = NormalizeTopicSegment(title ?? "");

        if (string.IsNullOrEmpty(segment))
        {
            var words = (content ?? "").ToLowerInvariant()
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Take(8);
            segment = NormalizeTopicSegment(string.Join(" ", words));
        }

        if (string.IsNullOrEmpty(segment)) segment = "general";

        var familyPrefix = family + "-";
        if (segment.StartsWith(familyPrefix, StringComparison.Ordinal))
            segment = segment[familyPrefix.Length..];

        if (string.IsNullOrEmpty(segment) || segment == family)
            segment = "general";

        return family + "/" + segment;
    }

    private static string InferTopicFamily(string? type, string? title, string? content)
    {
        var t = (type ?? "").Trim().ToLowerInvariant();
        return t switch
        {
            "architecture" or "design" or "adr" or "refactor" => "architecture",
            "bug" or "bugfix" or "fix" or "incident" or "hotfix" => "bug",
            "decision" => "decision",
            "pattern" or "convention" or "guideline" => "pattern",
            "config" or "setup" or "infra" or "infrastructure" or "ci" => "config",
            "discovery" or "investigation" or "root_cause" or "root-cause" => "discovery",
            "learning" or "learn" => "learning",
            "session_summary" => "session",
            _ => InferFamilyFromText(t, title, content),
        };
    }

    private static string InferFamilyFromText(string t, string? title, string? content)
    {
        var text = ((title ?? "") + " " + (content ?? "")).ToLowerInvariant();
        if (HasAny(text, "bug", "fix", "panic", "error", "crash", "regression", "incident", "hotfix")) return "bug";
        if (HasAny(text, "architecture", "design", "adr", "boundary", "hexagonal", "refactor"))       return "architecture";
        if (HasAny(text, "decision", "tradeoff", "chose", "choose", "decide"))                        return "decision";
        if (HasAny(text, "pattern", "convention", "naming", "guideline"))                              return "pattern";
        if (HasAny(text, "config", "setup", "environment", "env", "docker", "pipeline"))              return "config";
        if (HasAny(text, "discovery", "investigate", "investigation", "found", "root cause"))          return "discovery";
        if (HasAny(text, "learned", "learning"))                                                       return "learning";
        if (!string.IsNullOrEmpty(t) && t != "manual") return NormalizeTopicSegment(t);
        return "topic";
    }

    private static bool HasAny(string text, params string[] words)
        => words.Any(text.Contains);

    private static string NormalizeTopicSegment(string s)
    {
        var v = s.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(v)) return "";
        v = Regex.Replace(v, @"[^a-z0-9]+", " ");
        v = string.Join("-", v.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return v.Length > 100 ? v[..100] : v;
    }
}
