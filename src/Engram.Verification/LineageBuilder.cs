namespace Engram.Verification;

/// <summary>
/// BFS-based lineage tree builder for requirement traceability.
///
/// Traverses the graph of <see cref="TraceRelation"/> edges starting from a root requirement,
/// collecting ancestors (via <c>supersedes</c> and <c>depends_on</c> relations) and descendants
/// (via <c>related_to</c> relations). Includes cycle detection via a visited set and a
/// configurable hop limit (<see cref="MaxHops"/>) to prevent infinite traversal.
/// </summary>
public sealed class LineageBuilder
{
    private readonly TraceRepository _repo;

    /// <summary>
    /// Maximum traversal depth in hops. Hard ceiling to prevent unbounded BFS
    /// in the presence of deeply chained or cyclic graphs.
    /// </summary>
    public const int MaxHops = 10;

    public LineageBuilder(TraceRepository repo)
    {
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));
    }

    /// <summary>
    /// Build a full lineage tree for the given requirement, walking ancestors
    /// (supersedes/depends_on) and collecting related requirements.
    /// </summary>
    /// <param name="project">Project name for scoping trace lookups.</param>
    /// <param name="requirementId">The root requirement ID (e.g., "RF-001").</param>
    /// <returns>A <see cref="LineageResult"/> with root, ancestors, descendants, and cycle/truncation info.</returns>
    public async Task<LineageResult> BuildLineageAsync(string project, string requirementId)
    {
        var visited = new HashSet<string>();
        var ancestors = new List<TraceResult>();
        var descendants = new List<TraceResult>();
        bool cycleDetected = false;
        int hops = 0;

        // BFS ancestors (follows supersedes and depends_on upward)
        var queue = new Queue<(string id, int depth, bool isAncestor)>();
        queue.Enqueue((requirementId, 0, true));
        visited.Add(requirementId);

        while (queue.Count > 0 && hops < MaxHops)
        {
            var (current, depth, isAncestor) = queue.Dequeue();
            hops = Math.Max(hops, depth);

            var trace = await _repo.GetTraceAsync(project, current);
            if (trace is null) continue;

            foreach (var rel in trace.Relations)
            {
                if (rel.Type is "supersedes" or "depends_on")
                {
                    if (!visited.Add(rel.Target))
                    {
                        cycleDetected = true;
                        continue;
                    }

                    var targetResult = await ToTraceResult(project, rel.Target);

                    if (isAncestor)
                    {
                        ancestors.Add(targetResult);
                        queue.Enqueue((rel.Target, depth + 1, true));
                    }
                }

                if (rel.Type is "related_to")
                {
                    if (!visited.Add(rel.Target)) continue;
                    var targetResult = await ToTraceResult(project, rel.Target);
                    if (isAncestor)
                        descendants.Add(targetResult);
                    else
                        queue.Enqueue((rel.Target, depth + 1, false));
                }
            }
        }

        var root = await ToTraceResult(project, requirementId);

        return new LineageResult
        {
            Root = root,
            Ancestors = ancestors,
            Descendants = descendants,
            CycleDetected = cycleDetected,
            Hops = hops
        };
    }

    /// <summary>
    /// Convert a requirement ID to a <see cref="TraceResult"/> by loading its trace from the repository.
    /// Returns an "untraced" result if no trace exists.
    /// </summary>
    private async Task<TraceResult> ToTraceResult(string project, string reqId)
    {
        var trace = await _repo.GetTraceAsync(project, reqId);
        if (trace is null)
            return new TraceResult { RequirementId = reqId, Status = "untraced" };

        return new TraceResult
        {
            RequirementId = reqId,
            Status = "traced",
            Source = trace.Source,
            Lineage = trace.Relations.Select(r => $"{r.Type}:{r.Target}").ToList()
        };
    }
}
