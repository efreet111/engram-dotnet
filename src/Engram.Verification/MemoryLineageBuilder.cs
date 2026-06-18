using System.Text.Json.Serialization;
using Engram.Store;

namespace Engram.Verification;

/// <summary>
/// A node in the memory observation lineage graph, projected for BFS results.
/// </summary>
public sealed record MemoryTraceNode
{
    [JsonPropertyName("observation_id")] public long   ObservationId { get; init; }
    [JsonPropertyName("title")]          public string Title         { get; init; } = "";
    [JsonPropertyName("type")]           public string Type          { get; init; } = ""; // "traced" | "untraced"
    [JsonPropertyName("lineage")]        public List<string> Lineage { get; init; } = []; // ["depends_on:42", "supersedes:50"]
}

/// <summary>
/// Result of a lineage traversal: ancestors (via <c>supersedes</c>/<c>depends_on</c>),
/// descendants (via <c>related_to</c>), and traversal metadata.
/// </summary>
public sealed record MemoryLineageResult
{
    [JsonPropertyName("root_observation_id")] public long   RootObservationId { get; init; }
    [JsonPropertyName("ancestors")]            public List<MemoryTraceNode> Ancestors    { get; init; } = [];
    [JsonPropertyName("descendants")]          public List<MemoryTraceNode> Descendants  { get; init; } = [];
    [JsonPropertyName("cycle_detected")]       public bool   CycleDetected    { get; init; }
    [JsonPropertyName("hops")]                 public int    Hops             { get; init; }
}

/// <summary>
/// BFS-based lineage tree builder for memory observations.
///
/// Mirrors <see cref="LineageBuilder"/> but works on <see cref="long"/> observation IDs
/// via <see cref="MemoryRelationRepository"/> and fetches titles from <see cref="IStore"/>.
/// Ancestors follow <c>supersedes</c> and <c>depends_on</c> edges upward; descendants
/// follow <c>related_to</c> edges. Cycle detection via a visited set; <see cref="MaxHops"/>
/// bounds traversal depth.
/// </summary>
public sealed class MemoryLineageBuilder
{
    private readonly MemoryRelationRepository _repo;
    private readonly IStore _store;

    /// <summary>
    /// Maximum traversal depth in hops. Hard ceiling to prevent unbounded BFS
    /// in the presence of deeply chained or cyclic graphs.
    /// </summary>
    public const int MaxHops = 10;

    public MemoryLineageBuilder(MemoryRelationRepository repo, IStore store)
    {
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <summary>
    /// Build a lineage graph rooted at <paramref name="rootObservationId"/>.
    /// </summary>
    /// <param name="project">Project name for scoping relation lookups.</param>
    /// <param name="rootObservationId">The root observation ID.</param>
    public async Task<MemoryLineageResult> BuildLineageAsync(string project, long rootObservationId)
    {
        var visited = new HashSet<long> { rootObservationId };
        var ancestors = new List<MemoryTraceNode>();
        var descendants = new List<MemoryTraceNode>();
        bool cycleDetected = false;
        int hops = 0;

        // BFS: isAncestor=true while climbing via supersedes/depends_on, then flips
        // to false if we ever descend via related_to and keep walking.
        var queue = new Queue<(long id, int depth, bool isAncestor)>();
        queue.Enqueue((rootObservationId, 0, true));

        while (queue.Count > 0 && hops < MaxHops)
        {
            var (current, depth, isAncestor) = queue.Dequeue();
            hops = Math.Max(hops, depth);

            var relations = await _repo.GetRelationsAsync(project, current);

            foreach (var rel in relations)
            {
                if (rel.Type is "supersedes" or "depends_on")
                {
                    if (!visited.Add(rel.TargetObservationId))
                    {
                        cycleDetected = true;
                        continue;
                    }

                    var node = await ToMemoryTraceNode(project, rel.TargetObservationId);
                    if (isAncestor)
                    {
                        ancestors.Add(node);
                        queue.Enqueue((rel.TargetObservationId, depth + 1, true));
                    }
                }

                if (rel.Type is "related_to")
                {
                    if (!visited.Add(rel.TargetObservationId))
                        continue;

                    var node = await ToMemoryTraceNode(project, rel.TargetObservationId);
                    if (isAncestor)
                        descendants.Add(node);
                    else
                        queue.Enqueue((rel.TargetObservationId, depth + 1, false));
                }
            }
        }

        return new MemoryLineageResult
        {
            RootObservationId = rootObservationId,
            Ancestors = ancestors,
            Descendants = descendants,
            CycleDetected = cycleDetected,
            Hops = hops
        };
    }

    /// <summary>
    /// Resolve an observation ID to a <see cref="MemoryTraceNode"/> by fetching its
    /// observation row and its outgoing relation set. Returns "untraced" if the
    /// observation row is missing.
    /// </summary>
    private async Task<MemoryTraceNode> ToMemoryTraceNode(string project, long observationId)
    {
        var obs = await _store.GetObservationAsync(observationId);
        var relations = await _repo.GetRelationsAsync(project, observationId);

        if (obs is null)
            return new MemoryTraceNode
            {
                ObservationId = observationId,
                Type = "untraced",
                Lineage = relations.Select(r => $"{r.Type}:{r.TargetObservationId}").ToList()
            };

        return new MemoryTraceNode
        {
            ObservationId = observationId,
            Title = obs.Title,
            Type = "traced",
            Lineage = relations.Select(r => $"{r.Type}:{r.TargetObservationId}").ToList()
        };
    }
}