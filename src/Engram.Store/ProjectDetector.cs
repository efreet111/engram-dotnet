using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Engram.Store;

/// <summary>
/// Detects and normalizes project names from the working directory.
/// Priority: git remote origin → git root basename → directory basename.
/// Mirrors the Go original internal/project package.
/// </summary>
public static class ProjectDetector
{
    /// <summary>
    /// Detects the project name for a given directory.
    /// Priority: git remote origin repo name → git root basename → dir basename.
    /// The returned name is always non-empty and already normalized (lowercase, trimmed).
    /// </summary>
    public static string DetectProject(string dir)
    {
        if (string.IsNullOrEmpty(dir))
            return "unknown";

        // Guard against arg injection: a dir starting with "-" would be
        // interpreted as a git flag when passed to git -C <dir>.
        if (dir.StartsWith('-'))
            dir = "./" + dir;

        var fromRemote = DetectFromGitRemote(dir);
        if (!string.IsNullOrEmpty(fromRemote))
            return Normalizers.NormalizeProject(fromRemote);

        var fromRoot = DetectFromGitRoot(dir);
        if (!string.IsNullOrEmpty(fromRoot))
            return Normalizers.NormalizeProject(fromRoot);

        var baseName = Path.GetFileName(dir);
        if (string.IsNullOrEmpty(baseName) || baseName == ".")
            return "unknown";

        return Normalizers.NormalizeProject(baseName);
    }

    /// <summary>
    /// Attempts to determine the project name from the git remote "origin" URL.
    /// Returns empty string if git is unavailable, the directory is not a repo,
    /// or there is no origin remote.
    /// </summary>
    public static string DetectFromGitRemote(string dir)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = $"-C \"{dir}\" remote get-url origin",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }
            };
            process.Start();
            string output = process.StandardOutput.ReadToEnd().Trim();
            // 2-second timeout
            if (!process.WaitForExit(2000))
            {
                try { process.Kill(); } catch { /* best effort */ }
                return "";
            }
            if (process.ExitCode != 0 || string.IsNullOrEmpty(output))
                return "";

            return ExtractRepoName(output);
        }
        catch
        {
            return "";
        }
    }

    /// <summary>
    /// Returns the basename of the git repository root.
    /// Falls back to empty string when git is unavailable or the directory
    /// is not inside a git repository.
    /// </summary>
    public static string DetectFromGitRoot(string dir)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = $"-C \"{dir}\" rev-parse --show-toplevel",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }
            };
            process.Start();
            string output = process.StandardOutput.ReadToEnd().Trim();
            if (!process.WaitForExit(2000))
            {
                try { process.Kill(); } catch { /* best effort */ }
                return "";
            }
            if (process.ExitCode != 0 || string.IsNullOrEmpty(output))
                return "";

            return Path.GetFileName(output);
        }
        catch
        {
            return "";
        }
    }

    /// <summary>
    /// Parses a git remote URL and returns just the repository name.
    /// Supports SSH (git@github.com:user/repo.git), HTTPS (https://github.com/user/repo.git),
    /// and either with or without the trailing .git suffix.
    /// </summary>
    public static string ExtractRepoName(string url)
    {
        if (string.IsNullOrEmpty(url))
            return "";

        // Strip trailing .git suffix
        url = Regex.Replace(url, @"\.git$", "", RegexOptions.IgnoreCase);

        // Split on both "/" and ":" to handle SSH and HTTPS uniformly
        var parts = Regex.Split(url, @"[/\\:]+").Where(p => !string.IsNullOrEmpty(p)).ToArray();
        if (parts.Length == 0)
            return "";

        var name = parts[^1].Trim();
        return name;
    }

    // ─── Similar project detection ──────────────────────────────────────────

    /// <summary>
    /// Represents a project name that is similar to a query string.
    /// </summary>
    public record ProjectMatch(string Name, string MatchType, int Distance);

    /// <summary>
    /// Finds projects similar to the given name from a list of existing project names.
    /// Similarity is determined by three criteria:
    ///   1. Case-insensitive exact match (different case, same letters)
    ///   2. Substring containment (query is a substring of candidate or vice-versa)
    ///   3. Levenshtein distance ≤ maxDistance
    /// Exact matches (identical strings) are always excluded.
    /// Results are ordered: case-insensitive matches first, then substring matches,
    /// then levenshtein matches sorted by distance ascending.
    /// </summary>
    public static List<ProjectMatch> FindSimilar(string name, IList<string> existing, int maxDistance = 3)
    {
        if (maxDistance < 0)
            maxDistance = 0;

        var nameLower = (name ?? "").ToLowerInvariant().Trim();

        // Scale maxDistance for short names to avoid noisy matches
        // A 2-char name with maxDistance 3 would match almost everything
        var effectiveMax = maxDistance;
        if (nameLower.Length > 0)
        {
            var halfLen = Math.Max(1, nameLower.Length / 2);
            if (effectiveMax > halfLen)
                effectiveMax = halfLen;
        }

        var caseMatches = new List<ProjectMatch>();
        var subMatches = new List<ProjectMatch>();
        var levMatches = new List<ProjectMatch>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawCandidate in existing)
        {
            var candidate = rawCandidate ?? "";
            if (string.IsNullOrEmpty(candidate)) continue;
            // Skip exact match (same string, no drift)
            if (candidate == name)
                continue;

            var candidateLower = (candidate ?? "").ToLowerInvariant().Trim();

            // Case-insensitive match (different casing is still drift)
            if (candidateLower == nameLower)
            {
                if (candidate != name && seen.Add(candidate!))
                    caseMatches.Add(new ProjectMatch(candidate, "case-insensitive", 0));
                continue;
            }

            // Substring match — skip for very short names (< 3 chars)
            if (nameLower.Length >= 3)
            {
                if (candidateLower.Contains(nameLower) || nameLower.Contains(candidateLower))
                {
                    if (seen.Add(candidate!))
                    {
                        subMatches.Add(new ProjectMatch(candidate!, "substring", 0));
                        continue;
                    }
                }
            }

            // Levenshtein distance
            var dist = Levenshtein(nameLower, candidateLower);
            if (dist <= effectiveMax)
            {
                if (seen.Add(candidate!))
                    levMatches.Add(new ProjectMatch(candidate!, "levenshtein", dist));
            }
        }

        levMatches.Sort((a, b) => a.Distance.CompareTo(b.Distance));

        var result = new List<ProjectMatch>(caseMatches.Count + subMatches.Count + levMatches.Count);
        result.AddRange(caseMatches);
        result.AddRange(subMatches);
        result.AddRange(levMatches);
        return result;
    }

    /// <summary>
    /// Computes the Levenshtein (edit) distance between strings a and b.
    /// Uses O(min(|a|,|b|)) space by keeping only two rows of the DP table.
    /// </summary>
    public static int Levenshtein(string a, string b)
    {
        if (string.IsNullOrEmpty(a)) return b?.Length ?? 0;
        if (string.IsNullOrEmpty(b)) return a.Length;

        // Ensure a is the shorter string for space optimisation
        if (a.Length > b.Length)
            (a, b) = (b, a);

        var prev = new int[a.Length + 1];
        var curr = new int[a.Length + 1];

        for (var i = 0; i <= a.Length; i++)
            prev[i] = i;

        for (var j = 1; j <= b.Length; j++)
        {
            curr[0] = j;
            for (var i = 1; i <= a.Length; i++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                curr[i] = Math.Min(Math.Min(prev[i] + 1, curr[i - 1] + 1), prev[i - 1] + cost);
            }
            (prev, curr) = (curr, prev);
        }

        return prev[a.Length];
    }
}