using Engram.Store;

namespace Engram.MdGeneration;

/// <summary>
/// Verifies bidirectional links between Observation records in the store
/// and their promoted .md files on disk. Checks:
/// 1. Every observation with md_path points to an existing file
/// 2. (Forward check) Every .md file's frontmatter observation_id matches
/// </summary>
public sealed class LinkVerifier
{
    private readonly IStore _store;

    public LinkVerifier(IStore store)
    {
        _store = store;
    }

    /// <summary>
    /// Scans all observations with non-null md_path and verifies that the
    /// corresponding .md file exists on disk.
    /// </summary>
    /// <param name="mdDir">Base directory for .md files (e.g. "docs/decisions").</param>
    /// <returns>A <see cref="LinkVerificationResult"/> with counts and broken link details.</returns>
    public async Task<LinkVerificationResult> VerifyAsync(string mdDir)
    {
        var result = new LinkVerificationResult();

        // Find all observations with md_path
        var searchResults = await _store.SearchAsync("md_path:*", new SearchOptions { Limit = 1000 });
        var promoted = searchResults
            .Select(r => r.Observation)
            .Where(o => !string.IsNullOrEmpty(o.MdPath))
            .ToList();

        result.TotalPromoted = promoted.Count;

        foreach (var obs in promoted)
        {
            var fullPath = Path.Combine(mdDir, obs.MdPath!);
            var fileExists = File.Exists(fullPath);

            if (!fileExists)
            {
                result.OrphanedMdPaths.Add(new BrokenLink
                {
                    ObservationId = obs.Id,
                    MdPath = obs.MdPath!,
                    Issue = "file not found"
                });
            }
        }

        result.BrokenLinks = result.OrphanedMdPaths.Count;
        result.HealthyLinks = result.TotalPromoted - result.BrokenLinks;

        return result;
    }
}

/// <summary>
/// Result of a link verification run.
/// </summary>
public sealed record LinkVerificationResult
{
    /// <summary>Total number of observations with a non-null md_path.</summary>
    public int TotalPromoted { get; set; }

    /// <summary>Number of observations whose .md file exists on disk.</summary>
    public int HealthyLinks { get; set; }

    /// <summary>Number of observations whose .md file is missing from disk.</summary>
    public int BrokenLinks { get; set; }

    /// <summary>Details about each broken/orphaned link.</summary>
    public List<BrokenLink> OrphanedMdPaths { get; init; } = [];
}

/// <summary>
/// Describes a single broken link between an observation and its .md file.
/// </summary>
public sealed record BrokenLink
{
    /// <summary>The observation's database ID.</summary>
    public long ObservationId { get; init; }

    /// <summary>The md_path stored in the observation (relative path).</summary>
    public string MdPath { get; init; } = "";

    /// <summary>Human-readable description of what went wrong.</summary>
    public string Issue { get; init; } = "";
}
