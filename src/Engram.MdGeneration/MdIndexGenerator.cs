using System.Text;
using Engram.Store;

namespace Engram.MdGeneration;

public sealed class MdIndexGenerator
{
    /// <summary>
    /// Generate an index.md file listing all promoted observations.
    /// </summary>
    public string GenerateIndex(IReadOnlyList<Observation> promoted)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Decision Records");
        sb.AppendLine();
        sb.AppendLine($"Total: {promoted.Count} records");
        sb.AppendLine();

        // Group by type
        var grouped = promoted.GroupBy(o => o.Type)
                              .OrderBy(g => g.Key);

        foreach (var group in grouped)
        {
            sb.AppendLine($"## {group.Key}");
            sb.AppendLine();
            foreach (var obs in group.OrderByDescending(o => o.CreatedAt))
            {
                var mdLink = !string.IsNullOrEmpty(obs.MdPath)
                    ? $"[{obs.Title}]({obs.MdPath})"
                    : obs.Title;
                var date = obs.CreatedAt.Length >= 10 ? obs.CreatedAt[..10] : obs.CreatedAt;
                sb.AppendLine($"- {date} — {mdLink}");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }
}
