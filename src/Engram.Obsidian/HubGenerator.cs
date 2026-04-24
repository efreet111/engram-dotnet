using System.Text;

namespace Engram.Obsidian;

/// <summary>
/// Lightweight reference to an observation for use in hub notes.
/// Carries only the fields needed to build wikilinks and type annotations.
/// Port of Go's ObsRef from internal/obsidian/hub.go.
/// </summary>
public record ObsRef
{
    /// <summary>Filename slug without .md extension, e.g. "fixed-auth-bug-1".</summary>
    public string Slug { get; init; } = "";

    /// <summary>Human-readable title.</summary>
    public string Title { get; init; } = "";

    /// <summary>Observation's topic_key (may be empty).</summary>
    public string TopicKey { get; init; } = "";

    /// <summary>Observation type (e.g. "bugfix", "architecture").</summary>
    public string Type { get; init; } = "";
}

/// <summary>
/// Generates session and topic hub markdown notes for Obsidian.
/// Port of Go's internal/obsidian/hub.go.
/// </summary>
public static class HubGenerator
{
    /// <summary>
    /// Reports whether a topic prefix has enough observations to warrant
    /// creating a hub note. Threshold is ≥2 (REQ-EXPORT-05).
    /// </summary>
    public static bool ShouldCreateTopicHub(int count) => count >= 2;

    /// <summary>
    /// Generates markdown for a session hub note.
    /// Lists all observations in the session as wikilinks.
    /// Output path: {vault}/engram/_sessions/{sessionId}.md
    /// </summary>
    public static string SessionHubMarkdown(string sessionId, IList<ObsRef> observations)
    {
        if (string.IsNullOrEmpty(sessionId))
            throw new ArgumentException("sessionId cannot be empty", nameof(sessionId));

        var sb = new StringBuilder();

        // ── YAML Frontmatter ──────────────────────────────────────────────────
        sb.AppendLine("---");
        sb.AppendLine("type: session-hub");
        sb.AppendLine($"session_id: {sessionId}");
        sb.AppendLine("tags:");
        sb.AppendLine("  - session");
        sb.AppendLine("---");

        // ── Title ─────────────────────────────────────────────────────────────
        sb.AppendLine();
        sb.AppendLine($"# Session: {sessionId}");

        // ── Observations list ─────────────────────────────────────────────────
        sb.AppendLine();
        sb.AppendLine("## Observations");
        foreach (var ref_ in observations)
            sb.AppendLine($"- [[{ref_.Slug}]]");

        return sb.ToString();
    }

    /// <summary>
    /// Generates markdown for a topic cluster hub note.
    /// Lists all observations sharing the same topic prefix as wikilinks
    /// with type annotations.
    /// Output path: {vault}/engram/_topics/{prefix}.md
    /// where prefix uses "--" instead of "/" for filesystem safety.
    /// </summary>
    public static string TopicHubMarkdown(string prefix, IList<ObsRef> observations)
    {
        if (string.IsNullOrEmpty(prefix))
            throw new ArgumentException("prefix cannot be empty", nameof(prefix));

        var sb = new StringBuilder();

        // ── YAML Frontmatter ──────────────────────────────────────────────────
        sb.AppendLine("---");
        sb.AppendLine("type: topic-hub");
        sb.AppendLine($"topic_prefix: {prefix}");
        sb.AppendLine("tags:");
        sb.AppendLine("  - topic");
        sb.AppendLine("---");

        // ── Title ─────────────────────────────────────────────────────────────
        sb.AppendLine();
        sb.AppendLine($"# Topic: {prefix}");

        // ── Related Observations list ─────────────────────────────────────────
        sb.AppendLine();
        sb.AppendLine("## Related Observations");
        foreach (var ref_ in observations)
            sb.AppendLine($"- [[{ref_.Slug}]] ({ref_.Type})");

        return sb.ToString();
    }
}
