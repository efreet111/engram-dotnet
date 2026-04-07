using System.Text.RegularExpressions;

namespace Engram.Store;

/// <summary>
/// Passive learning extraction — port of store.ExtractLearnings() from Go.
/// Parses structured learning items from agent output text.
/// </summary>
public static class PassiveCapture
{
    private const int MinLearningLength = 20;
    private const int MinLearningWords  = 4;

    // Matches "## Key Learnings:", "## Aprendizajes Clave:", etc.
    private static readonly Regex HeaderPattern = new(
        @"(?im)^#{2,3}\s+(?:Aprendizajes(?:\s+Clave)?|Key\s+Learnings?|Learnings?):?\s*$",
        RegexOptions.Compiled);

    private static readonly Regex NextHeaderPattern = new(
        @"\n#{1,3} ", RegexOptions.Compiled);

    private static readonly Regex NumberedPattern = new(
        @"(?m)^\s*\d+[.)]\s+(.+)", RegexOptions.Compiled);

    private static readonly Regex BulletPattern = new(
        @"(?m)^\s*[-*]\s+(.+)", RegexOptions.Compiled);

    private static readonly Regex BoldPattern    = new(@"\*\*([^*]+)\*\*", RegexOptions.Compiled);
    private static readonly Regex CodePattern    = new(@"`([^`]+)`",        RegexOptions.Compiled);
    private static readonly Regex ItalicPattern  = new(@"\*([^*]+)\*",      RegexOptions.Compiled);

    /// <summary>
    /// Extracts learnings from text, identical to Go ExtractLearnings().
    /// Returns learnings from the LAST matching section.
    /// </summary>
    public static List<string> ExtractLearnings(string text)
    {
        var matches = HeaderPattern.Matches(text);
        if (matches.Count == 0) return [];

        // Process in reverse — use first valid one (most recent)
        for (int i = matches.Count - 1; i >= 0; i--)
        {
            var sectionStart = matches[i].Index + matches[i].Length;
            var sectionText  = text[sectionStart..];

            // Cut off at next major section header
            var nextHeader = NextHeaderPattern.Match(sectionText);
            if (nextHeader.Success)
                sectionText = sectionText[..nextHeader.Index];

            var learnings = new List<string>();

            // Try numbered items first
            foreach (Match m in NumberedPattern.Matches(sectionText))
            {
                var cleaned = CleanMarkdown(m.Groups[1].Value);
                if (cleaned.Length >= MinLearningLength && cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= MinLearningWords)
                    learnings.Add(cleaned);
            }

            // Fall back to bullet items
            if (learnings.Count == 0)
            {
                foreach (Match m in BulletPattern.Matches(sectionText))
                {
                    var cleaned = CleanMarkdown(m.Groups[1].Value);
                    if (cleaned.Length >= MinLearningLength && cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= MinLearningWords)
                        learnings.Add(cleaned);
                }
            }

            if (learnings.Count > 0) return learnings;
        }

        return [];
    }

    private static string CleanMarkdown(string text)
    {
        text = BoldPattern.Replace(text, "$1");
        text = CodePattern.Replace(text, "$1");
        text = ItalicPattern.Replace(text, "$1");
        return string.Join(" ", text.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
