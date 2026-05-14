using System.Text;
using Engram.Store;

namespace Engram.MdGeneration;

public sealed class MdTemplateEngine
{
    /// <summary>
    /// Render an Observation as a .md file with YAML frontmatter and body.
    /// </summary>
    public string Render(Observation obs)
    {
        var sb = new StringBuilder();

        // Frontmatter
        sb.AppendLine("---");
        sb.AppendLine($"observation_id: {obs.Id}");
        sb.AppendLine($"type: \"{obs.Type}\"");
        sb.AppendLine($"title: \"{EscapeYaml(obs.Title)}\"");
        sb.AppendLine($"created_at: \"{obs.CreatedAt}\"");
        if (!string.IsNullOrEmpty(obs.TopicKey))
            sb.AppendLine($"topic_key: \"{obs.TopicKey}\"");
        sb.AppendLine($"generated_at: \"{DateTime.UtcNow:O}\"");
        sb.AppendLine("---");
        sb.AppendLine();

        // Body
        sb.AppendLine($"# {obs.Title}");
        sb.AppendLine();
        sb.AppendLine(obs.Content);

        return sb.ToString();
    }

    private static string EscapeYaml(string value)
    {
        return value.Replace("\\", "\\\\")
                    .Replace("\"", "\\\"")
                    .Replace("\n", "\\n");
    }
}
