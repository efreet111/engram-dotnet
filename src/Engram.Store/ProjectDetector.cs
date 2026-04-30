using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Engram.Store;

/// <summary>
/// Constants for project detection sources.
/// Mirrors the Go original internal/project package source constants.
/// </summary>
public static class ProjectSources
{
    public const string GitRemote = "git_remote";
    public const string GitRoot = "git_root";
    public const string GitChild = "git_child";
    public const string Ambiguous = "ambiguous";
    public const string DirBasename = "dir_basename";
    public const string ExplicitOverride = "explicit_override";
    public const string RequestBody = "request_body";
}

/// <summary>
/// Result of full project detection with all metadata.
/// </summary>
public record DetectionResult(
    string Project,
    string Source,
    string ProjectPath,
    string? Warning = null,
    string? Error = null,
    List<string>? AvailableProjects = null
)
{
    /// <summary>
    /// Returns AvailableProjects if set, otherwise an empty list.
    /// </summary>
    public IReadOnlyList<string> GetAvailableProjects() => AvailableProjects ?? [];
}

/// <summary>
/// Directories to skip when scanning for child git repositories.
/// </summary>
internal static class NoiseDirectories
{
    public static readonly HashSet<string> Set = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", "node_modules", "vendor", ".venv", "__pycache__",
        ".tox", ".mypy_cache", ".pytest_cache", "venv", "env",
        ".terraform", ".idea", ".vscode", "target", "build", "dist"
    };
}

/// <summary>
/// Detects and normalizes project names from the working directory.
/// Priority: git remote origin → git root basename → directory basename.
/// Mirrors the Go original internal/project package.
/// </summary>
public static class ProjectDetector
{
    /// <summary>
    /// Full project detection returning all metadata (5-case algorithm).
    /// Cases evaluated in order:
    /// 1. git_remote — cwd is inside a git repo with origin remote
    /// 2. git_root — cwd IS a git repo root
    /// 3. git_child — cwd contains exactly ONE child git repo
    /// 4. ambiguous — cwd contains TWO OR MORE child git repos
    /// 5. dir_basename — fallback when no git repo exists
    /// </summary>
    public static DetectionResult DetectProjectFull(string? workingDir = null)
    {
        // Empty or whitespace-only → unknown
        if (string.IsNullOrWhiteSpace(workingDir))
        {
            return new DetectionResult(
                Project: "unknown",
                Source: ProjectSources.DirBasename,
                ProjectPath: string.IsNullOrEmpty(workingDir) ? "" : workingDir!
            );
        }

        var dir = workingDir;

        // Guard against arg injection
        if (dir.StartsWith('-'))
            dir = "./" + dir;

        // Case 1: git remote origin
        var remoteUrl = DetectFromGitRemote(dir);
        if (!string.IsNullOrEmpty(remoteUrl))
        {
            var rootPath = DetectGitRootPath(dir);
            return new DetectionResult(
                Project: Normalizers.NormalizeProject(remoteUrl),
                Source: ProjectSources.GitRemote,
                ProjectPath: string.IsNullOrEmpty(rootPath) ? dir : rootPath
            );
        }

        // Case 2: git root
        var rootPath2 = DetectGitRootPath(dir);
        if (!string.IsNullOrEmpty(rootPath2))
        {
            var isRoot = Path.GetFullPath(dir) == Path.GetFullPath(rootPath2);
            if (isRoot)
            {
                var name = Path.GetFileName(rootPath2);
                if (!string.IsNullOrEmpty(name) && name != ".")
                {
                    return new DetectionResult(
                        Project: Normalizers.NormalizeProject(name),
                        Source: ProjectSources.GitRoot,
                        ProjectPath: rootPath2
                    );
                }
            }
        }

        // Case 3 & 4: scan for child git repos
        var children = ScanChildren(dir);
        if (children.Count == 1)
        {
            var child = children[0];
            return new DetectionResult(
                Project: Normalizers.NormalizeProject(child.Name),
                Source: ProjectSources.GitChild,
                ProjectPath: child.Path,
                Warning: $"Auto-promoted single child repo '{child.Name}'"
            );
        }
        if (children.Count >= 2)
        {
            var names = children.Select(c => Normalizers.NormalizeProject(c.Name)).ToList();
            return new DetectionResult(
                Project: "",
                Source: ProjectSources.Ambiguous,
                ProjectPath: dir,
                Error: "Ambiguous project: multiple git repositories found",
                AvailableProjects: names
            );
        }

        // Case 5: dir basename fallback
        var baseName = Path.GetFileName(dir);
        if (string.IsNullOrEmpty(baseName) || baseName == ".")
            baseName = "unknown";

        return new DetectionResult(
            Project: Normalizers.NormalizeProject(baseName),
            Source: ProjectSources.DirBasename,
            ProjectPath: dir
        );
    }

    /// <summary>
    /// Detects the project name for a given directory.
    /// Priority: git remote origin repo name → git root basename → dir basename.
    /// The returned name is always non-empty and already normalized (lowercase, trimmed).
    /// </summary>
    public static string DetectProject(string? workingDir = null)
    {
        var result = DetectProjectFull(workingDir);

        // If ambiguous, return the first available project (backward compat)
        if (result.Source == ProjectSources.Ambiguous && result.AvailableProjects?.Count > 0)
            return result.AvailableProjects[0];

        // If empty project (shouldn't happen with fallback), return unknown
        return string.IsNullOrEmpty(result.Project) ? "unknown" : result.Project;
    }

    /// <summary>
    /// Scans immediate subdirectories for git repositories.
    /// Timeout: 200ms per scan. Cap: 20 entries.
    /// Skips noise directories (.git, node_modules, vendor, etc).
    /// </summary>
    internal static List<(string Name, string Path)> ScanChildren(string dir)
    {
        var results = new List<(string Name, string Path)>();
        if (!Directory.Exists(dir)) return results;

        string[] subdirs;
        try
        {
            subdirs = Directory.GetDirectories(dir);
        }
        catch
        {
            return results;
        }

        using var cts = new CancellationTokenSource(200);

        foreach (var subdir in subdirs)
        {
            if (cts.IsCancellationRequested) break;
            if (results.Count >= 20) break;

            var name = Path.GetFileName(subdir);
            if (NoiseDirectories.Set.Contains(name)) continue;

            try
            {
                var gitDir = Path.Combine(subdir, ".git");
                if (Directory.Exists(gitDir) || File.Exists(gitDir))
                {
                    results.Add((name, subdir));
                }
            }
            catch
            {
                // Skip inaccessible directories
            }
        }

        return results;
    }

    /// <summary>
    /// Returns the absolute path of the git repository root.
    /// Returns empty string when git is unavailable or the directory
    /// is not inside a git repository.
    /// </summary>
    private static string DetectGitRootPath(string dir)
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

            return output;
        }
        catch
        {
            return "";
        }
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
        var rootPath = DetectGitRootPath(dir);
        return string.IsNullOrEmpty(rootPath) ? "" : Path.GetFileName(rootPath);
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