using System.Text;
using Engram.Store;

namespace Engram.Obsidian;

/// <summary>
/// Converts observations into Obsidian-compatible markdown with YAML frontmatter,
/// H1 title, content body, and wikilink footer.
/// Port of Go's internal/obsidian/markdown.go.
/// </summary>
public static class MarkdownRenderer
{
    /// <summary>
    /// Converts an observation into a full Obsidian markdown document.
    /// Output structure:
    ///   --- YAML frontmatter ---
    ///   # Title
    ///   Content body
    ///   --- wikilinks footer ---
    /// </summary>
    public static string ObservationToMarkdown(Observation obs)
    {
        if (obs == null) throw new ArgumentNullException(nameof(obs));

        var sb = new StringBuilder();

        var topicKey = obs.TopicKey ?? "";
        var project = obs.Project ?? "";

        // ── YAML Frontmatter ──────────────────────────────────────────────────
        sb.AppendLine("---");
        sb.AppendLine($"id: {obs.Id}");
        sb.AppendLine($"type: {obs.Type}");
        sb.AppendLine(string.IsNullOrEmpty(project) ? "project: \"\"" : $"project: {project}");
        sb.AppendLine($"scope: {obs.Scope}");
        sb.AppendLine(string.IsNullOrEmpty(topicKey)
            ? "topic_key: \"\""
            : $"topic_key: {topicKey}");
        sb.AppendLine($"session_id: {obs.SessionId}");
        sb.AppendLine($"created_at: \"{obs.CreatedAt}\"");
        sb.AppendLine($"updated_at: \"{obs.UpdatedAt}\"");
        sb.AppendLine($"revision_count: {obs.RevisionCount}");
        sb.AppendLine("tags:");
        if (!string.IsNullOrEmpty(project))
            sb.AppendLine($"  - {project}");
        if (!string.IsNullOrEmpty(obs.Type))
            sb.AppendLine($"  - {obs.Type}");
        sb.AppendLine("aliases:");
        sb.AppendLine($"  - \"{obs.Title}\"");
        sb.AppendLine("---");

        // ── Title as H1 ───────────────────────────────────────────────────────
        sb.AppendLine();
        sb.AppendLine($"# {obs.Title}");
        sb.AppendLine();

        // ── Content Body ──────────────────────────────────────────────────────
        sb.AppendLine(obs.Content);

        // ── Wikilinks Footer ──────────────────────────────────────────────────
        var wikilinks = BuildWikilinks(obs.SessionId, topicKey);
        if (wikilinks.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("---");
            foreach (var link in wikilinks)
                sb.AppendLine(link);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Builds wikilink lines for a given session ID and topic key.
    /// Session wikilink: "*Session*: [[session-{sessionId}]]" (only when sessionId != "")
    /// Topic wikilink: "*Topic*: [[topic-{prefix}]]" (only when topicKey != "")
    /// where prefix = TopicPrefix(topicKey) with "/" replaced by "--".
    /// </summary>
    public static List<string> BuildWikilinks(string sessionId, string topicKey)
    {
        var links = new List<string>();

        if (!string.IsNullOrEmpty(sessionId))
            links.Add($"*Session*: [[session-{sessionId}]]");

        if (!string.IsNullOrEmpty(topicKey))
        {
            var prefix = TopicPrefix.Extract(topicKey);
            var prefixSafe = prefix.Replace("/", "--");
            links.Add($"*Topic*: [[topic-{prefixSafe}]]");
        }

        return links;
    }
}
