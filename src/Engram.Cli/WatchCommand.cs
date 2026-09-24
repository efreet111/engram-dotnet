using System.CommandLine;
using System.Text;
using Engram.Store;

namespace Engram.Cli;

/// <summary>
/// HU-053 (ENG-483): `engram watch` — code-aware automatic memory capture.
/// Watches files (single | directory | simple glob) and captures a memory
/// (<c>type=code_change</c>) when a file changes by at least <c>--threshold</c>
/// lines (positional diff, no git, no LCS).
///
/// Encapsulates all command logic (target resolution, binary blocklist, positional
/// diff, ignore-pattern matching, content building, project detection) so
/// <c>Program.cs</c> top-level statements stay lean and the pure functions are
/// testable without shelling out to the compiled binary — the same pattern used by
/// <see cref="GitInitCommand"/>.
/// </summary>
public static class WatchCommand
{
    /// <summary>Maximum number of change snippets included in the captured content.</summary>
    private const int MaxSnippets = 20;

    /// <summary>Maximum length of a single snippet line before truncation.</summary>
    private const int MaxSnippetChars = 120;

    /// <summary>
    /// Extension blocklist for binary detection (case-insensitive). Phase 1 detects
    /// binaries by extension only — deterministic, no content sniffing (FR-007/FR-009).
    /// </summary>
    private static readonly HashSet<string> BinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        // native / compiled
        ".dll", ".exe", ".so", ".dylib", ".bin", ".o", ".a", ".lib", ".pdb", ".obj",
        ".wasm", ".jar", ".class",
        // images
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".webp", ".pdf",
        // archives
        ".zip", ".tar", ".gz", ".7z", ".rar",
        // fonts
        ".woff", ".woff2", ".ttf", ".otf", ".eot",
        // media
        ".mp3", ".mp4", ".avi", ".mov", ".wav", ".flac",
    };

    /// <summary>
    /// Builds the <c>watch</c> command with positional paths and the
    /// <c>--threshold</c>, <c>--ignore-pattern</c>, and <c>--project</c> options.
    /// </summary>
    public static Command CreateCommand()
    {
        var watchCmd = new Command("watch", "Watch files and auto-capture code changes as memories");

        var pathsArg = new Argument<string[]>("path")
        {
            Description = "Files, directories (non-recursive), or simple globs to watch",
            Arity = ArgumentArity.OneOrMore,
        };
        var thresholdOpt = new Option<int>("--threshold")
        {
            Description = "Minimum changed lines to capture (default: 10)",
            DefaultValueFactory = _ => 10,
        };
        var ignoreOpt = new Option<string[]>("--ignore-pattern")
        {
            Description = "Glob pattern to exclude (can be repeated)",
        };
        var projectOpt = new Option<string?>("--project")
        {
            Description = "Project name to use (default: auto-detected)",
        };

        watchCmd.Arguments.Add(pathsArg);
        watchCmd.Options.Add(thresholdOpt);
        watchCmd.Options.Add(ignoreOpt);
        watchCmd.Options.Add(projectOpt);

        watchCmd.SetAction(async (ParseResult parseResult) =>
        {
            var paths = parseResult.GetValue(pathsArg) ?? [];
            var threshold = parseResult.GetValue(thresholdOpt);
            var ignorePatterns = parseResult.GetValue(ignoreOpt) ?? [];
            var cliProject = parseResult.GetValue(projectOpt);
            var cwd = Directory.GetCurrentDirectory();

            // FR-006: validate threshold (fail-loud before store or watchers)
            if (threshold <= 0)
            {
                await Console.Error.WriteLineAsync("error: --threshold must be a positive integer");
                return 1;
            }

            // FR-001/FR-009: resolve targets (fail-loud on not-found)
            IReadOnlyList<string> targets;
            try
            {
                targets = ResolveTargets(paths, ignorePatterns, cwd);
            }
            catch (FileNotFoundException ex)
            {
                await Console.Error.WriteLineAsync($"error: {ex.Message}");
                return 1;
            }

            if (targets.Count == 0)
            {
                await Console.Error.WriteLineAsync("error: No files to watch");
                return 1;
            }

            // FR-004: project resolution (canonical chain)
            var project = ResolveProjectName(cliProject, cwd);

            // FR-003: one session per invocation
            var sessionId = $"watch-{DateTime.Now:yyyyMMddTHHmmss}";

            var config = new FileWatchConfig
            {
                Targets = targets,
                Paths = paths,
                Threshold = threshold,
                IgnorePatterns = ignorePatterns,
                Project = project,
                SessionId = sessionId,
                Cwd = cwd,
            };

            // FR-008: graceful shutdown (clone of Program.cs:1688-1703)
            var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            int captured;
            try
            {
                captured = await FileWatchLoop.RunAsync(config, cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Graceful shutdown
                captured = 0;
            }
            catch (Exception ex)
            {
                // Store inaccessible or other startup failure (fail-loud, FR-009)
                await Console.Error.WriteLineAsync($"error: {ex.Message}");
                return 1;
            }

            Console.WriteLine($"✓ Watch stopped. {captured} memories captured.");
            return 0;
        });

        return watchCmd;
    }

    // ─── Pure functions (internal static — testable without instantiation) ───

    /// <summary>
    /// Resolves positional targets to a concrete, deduplicated set of absolute file paths.
    /// Expands files (existing → target), directories (non-recursive enumeration), and
    /// simple globs (<c>*</c>/<c>?</c>, via <c>Directory.GetFiles</c>). Filters binaries
    /// (blocklist) and ignore patterns. Explicit binary targets emit a <c>⚠</c> warning;
    /// binaries discovered by enumeration/glob are skipped silently (FR-009).
    /// </summary>
    /// <exception cref="FileNotFoundException">An explicit path does not exist.</exception>
    internal static IReadOnlyList<string> ResolveTargets(string[] paths, string[] ignorePatterns, string cwd)
    {
        var results = new HashSet<string>(StringComparer.Ordinal);

        foreach (var rawPath in paths)
        {
            if (HasGlobChars(rawPath))
            {
                foreach (var expanded in ExpandGlob(rawPath))
                {
                    if (IsBinaryFile(expanded)) continue; // silent (discovered)
                    if (MatchesIgnorePattern(expanded, cwd, ignorePatterns)) continue;
                    results.Add(Path.GetFullPath(expanded));
                }
            }
            else if (File.Exists(rawPath))
            {
                if (IsBinaryFile(rawPath))
                {
                    // explicit binary → visible warning (FR-009)
                    Console.WriteLine($"⚠ Ignoring binary file: {NormalizeRelPath(rawPath, cwd)}");
                    continue;
                }
                if (MatchesIgnorePattern(rawPath, cwd, ignorePatterns)) continue;
                results.Add(Path.GetFullPath(rawPath));
            }
            else if (Directory.Exists(rawPath))
            {
                foreach (var file in Directory.GetFiles(rawPath))
                {
                    if (IsBinaryFile(file)) continue; // silent (discovered)
                    if (MatchesIgnorePattern(file, cwd, ignorePatterns)) continue;
                    results.Add(Path.GetFullPath(file));
                }
            }
            else
            {
                throw new FileNotFoundException($"File not found: {rawPath}", rawPath);
            }
        }

        return results.ToArray();
    }

    /// <summary>Returns true when the path has a <c>*</c> or <c>?</c> glob metacharacter.</summary>
    internal static bool HasGlobChars(string path) => path.Contains('*') || path.Contains('?');

    /// <summary>
    /// Expands a simple glob using <c>Directory.GetFiles</c> semantics (no <c>**</c>).
    /// Returns an empty sequence when the directory does not exist or matches nothing.
    /// </summary>
    private static IEnumerable<string> ExpandGlob(string pattern)
    {
        var dir = Path.GetDirectoryName(pattern);
        var filePattern = Path.GetFileName(pattern);
        if (string.IsNullOrEmpty(dir)) dir = ".";
        if (string.IsNullOrEmpty(filePattern)) return [];
        if (!Directory.Exists(dir)) return [];
        return Directory.GetFiles(dir, filePattern);
    }

    /// <summary>Extension-based binary detection (case-insensitive blocklist).</summary>
    internal static bool IsBinaryFile(string path)
        => BinaryExtensions.Contains(Path.GetExtension(path));

    /// <summary>
    /// Positional line diff: <c>modified</c> = lines differing at the same index (ordinal),
    /// <c>added</c>/<c>removed</c> = length excess of the new/old array. O(min(old,new)).
    /// </summary>
    internal static (int Modified, int Added, int Removed) CountChangedLines(string[] oldLines, string[] newLines)
    {
        var common = Math.Min(oldLines.Length, newLines.Length);
        var modified = 0;
        for (var i = 0; i < common; i++)
        {
            if (!string.Equals(oldLines[i], newLines[i], StringComparison.Ordinal))
                modified++;
        }
        var added = newLines.Length > common ? newLines.Length - common : 0;
        var removed = oldLines.Length > common ? oldLines.Length - common : 0;
        return (modified, added, removed);
    }

    /// <summary>
    /// True when any ignore pattern matches the file's name OR its CWD-relative path
    /// (separators <c>/</c>). Simple glob semantics: <c>*</c> matches any chars, <c>?</c> one char.
    /// </summary>
    internal static bool MatchesIgnorePattern(string filePath, string cwd, string[] patterns)
    {
        if (patterns.Length == 0) return false;
        var relPath = NormalizeRelPath(filePath, cwd);
        var fileName = Path.GetFileName(filePath);
        foreach (var pattern in patterns)
        {
            if (GlobMatch(pattern, fileName)) return true;
            if (GlobMatch(pattern, relPath)) return true;
        }
        return false;
    }

    /// <summary>Iterative glob match (<c>*</c> = any chars, <c>?</c> = single char; no <c>**</c>).</summary>
    private static bool GlobMatch(string pattern, string input)
    {
        var p = 0;
        var i = 0;
        var starIdx = -1;
        var matchIdx = 0;

        while (i < input.Length)
        {
            if (p < pattern.Length && (pattern[p] == '?' || pattern[p] == input[i]))
            {
                p++;
                i++;
            }
            else if (p < pattern.Length && pattern[p] == '*')
            {
                starIdx = p;
                matchIdx = i;
                p++;
            }
            else if (starIdx != -1)
            {
                p = starIdx + 1;
                matchIdx++;
                i = matchIdx;
            }
            else
            {
                return false;
            }
        }

        while (p < pattern.Length && pattern[p] == '*') p++;
        return p == pattern.Length;
    }

    /// <summary>
    /// Builds the summary content block: file header, change counts, threshold, timestamp,
    /// then up to 20 change snippets (each truncated to 120 chars).
    /// </summary>
    internal static string BuildChangeContent(
        string relPath,
        int modified,
        int added,
        int removed,
        int threshold,
        string[] changedLines)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"File: {relPath}");
        sb.AppendLine($"~{modified} modified / +{added} added / -{removed} removed");
        sb.AppendLine($"Threshold: {threshold}");
        sb.AppendLine($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        foreach (var line in changedLines.Take(MaxSnippets))
        {
            sb.AppendLine(Truncate(line, MaxSnippetChars));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Generates up to 20 prefixed change snippets from a positional diff:
    /// <c>~</c> modified (same index differs), <c>+</c> added (excess in new), <c>-</c> removed (excess in old).
    /// </summary>
    internal static string[] BuildSnippets(string[] oldLines, string[] newLines)
    {
        var snippets = new List<string>(MaxSnippets);
        var common = Math.Min(oldLines.Length, newLines.Length);

        for (var i = 0; i < common && snippets.Count < MaxSnippets; i++)
        {
            if (!string.Equals(oldLines[i], newLines[i], StringComparison.Ordinal))
                snippets.Add("~ " + newLines[i]);
        }
        for (var i = common; i < newLines.Length && snippets.Count < MaxSnippets; i++)
            snippets.Add("+ " + newLines[i]);
        for (var i = common; i < oldLines.Length && snippets.Count < MaxSnippets; i++)
            snippets.Add("- " + oldLines[i]);

        return snippets.ToArray();
    }

    /// <summary>
    /// Returns the CWD-relative path (separators <c>/</c>) when the file is under CWD,
    /// otherwise the absolute path. Case-sensitive boundary check (no case-folding).
    /// </summary>
    internal static string NormalizeRelPath(string fullPath, string cwd)
    {
        var abs = Path.GetFullPath(fullPath);
        var absCwd = Path.GetFullPath(cwd);
        string result;
        if (abs.Equals(absCwd, StringComparison.Ordinal))
        {
            result = ".";
        }
        else if (abs.StartsWith(absCwd + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            result = abs.Substring(absCwd.Length + 1);
        }
        else
        {
            result = abs;
        }
        return result.Replace('\\', '/');
    }

    /// <summary>
    /// Project resolution chain: cliProject → ENGRAM_PROJECT → DetectProject → NormalizeProject.
    /// Mirrors the canonical chain in Program.cs (quick-capture) and <see cref="GitInitCommand"/>.
    /// </summary>
    internal static string ResolveProjectName(string? cliProject, string cwd)
    {
        var storeCfg = StoreConfig.FromEnvironment();
        var project = cliProject
            ?? storeCfg.Project
            ?? ProjectDetector.DetectProject(cwd);
        return Normalizers.NormalizeProject(project);
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max];
}
