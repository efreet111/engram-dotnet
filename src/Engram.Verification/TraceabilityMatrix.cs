using Engram.Store;

namespace Engram.Verification;

/// <summary>
/// Builds a traceability matrix by searching the observation store for evidence
/// related to each requirement in a specification.
///
/// For every requirement (RF and RNF), the builder searches the store and classifies
/// the traceability status based on FTS5 rank:
/// <list type="bullet">
///   <item><c>covered</c> — strong FTS5 match (rank < -2)</item>
///   <item><c>partial</c> — weak FTS5 match (rank < 0)</item>
///   <item><c>untraced</c> — evidence found but poor relevance (rank >= 0)</item>
///   <item><c>missing</c> — no evidence found</item>
/// </list>
/// </summary>
public sealed class TraceabilityMatrixBuilder
{
    private readonly IStore _store;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="store">The Engram observation store.</param>
    public TraceabilityMatrixBuilder(IStore store)
    {
        _store = store;
    }

    /// <summary>
    /// Builds the traceability matrix for the given specification.
    /// </summary>
    /// <param name="spec">The parsed specification.</param>
    /// <param name="project">The project to scope the search to.</param>
    /// <returns>A <see cref="TraceabilityMatrix"/> with per-requirement entries.</returns>
    public async Task<TraceabilityMatrix> BuildMatrixAsync(
        SpecParseResult spec,
        string project)
    {
        var entries = new List<TraceabilityEntry>();
        int covered = 0;
        int missing = 0;

        foreach (var req in spec.Requirements)
        {
            // Search for observations related to this requirement
            var searchResults = await _store.SearchAsync(
                $"{req.Id} {req.Description}",
                new SearchOptions { Project = project, Limit = 5 });

            var evidence = searchResults
                .Select(r => r.Observation.Title)
                .ToList();

            string status;
            if (evidence.Count > 0)
            {
                // Classify based on best (most negative) FTS5 rank
                var bestRank = searchResults.Min(r => r.Rank);
                status = bestRank < -2 ? "covered"
                       : bestRank < 0  ? "partial"
                       :                  "untraced";
                covered++;
            }
            else
            {
                status = "missing";
                missing++;
            }

            entries.Add(new TraceabilityEntry
            {
                Requirement = req,
                Status = status,
                Evidence = evidence,
            });
        }

        var total = entries.Count;
        return new TraceabilityMatrix
        {
            Entries = entries,
            Total = total,
            Covered = covered,
            Missing = missing,
            CoveragePct = total > 0
                ? Math.Round((double)covered / total * 100, 1)
                : 0,
        };
    }
}
