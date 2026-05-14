using System.Text.Json;
using Engram.Store;

namespace Engram.Verification;

/// <summary>
/// Persistence layer for requirement traceability information.
/// Saves and loads <see cref="TraceInfo"/> as Engram observations using IStore.
/// </summary>
public sealed class TraceRepository
{
    private readonly IStore _store;

    public TraceRepository(IStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <summary>
    /// Save trace information for a requirement as an Engram observation.
    /// Uses <c>topic_key: trace/{project}/{requirementId}</c> for consistent lookup.
    /// </summary>
    /// <param name="project">Project name for scoping the observation.</param>
    /// <param name="trace">The trace information to persist.</param>
    /// <param name="sessionId">Session ID for provenance.</param>
    public async Task SaveTraceAsync(string project, TraceInfo trace, string sessionId)
    {
        var topicKey = $"trace/{project}/{trace.RequirementId}";
        var content = JsonSerializer.Serialize(trace);

        await _store.AddObservationAsync(new AddObservationParams
        {
            SessionId = sessionId,
            Type = "trace",
            Title = $"Trace: {trace.RequirementId}",
            Content = content,
            Project = project,
            TopicKey = topicKey,
            Scope = "team"
        });
    }

    /// <summary>
    /// Load trace information for a requirement from Engram observations.
    /// </summary>
    /// <param name="project">Project name used when saving the trace.</param>
    /// <param name="requirementId">The requirement identifier (e.g., "RF-001").</param>
    /// <returns>The trace info if found, or null if not traced.</returns>
    public async Task<TraceInfo?> GetTraceAsync(string project, string requirementId)
    {
        var topicKey = $"trace/{project}/{requirementId}";
        var results = await _store.SearchAsync(topicKey, new SearchOptions { Limit = 1 });

        if (results.Count == 0)
            return null;

        var obs = results[0].Observation;
        return JsonSerializer.Deserialize<TraceInfo>(obs.Content);
    }

    /// <summary>
    /// Persist a trace that was parsed from a spec.md ## Traceability section.
    /// Uses the explicitly provided <paramref name="requirementId"/> for the topic key,
    /// enabling the spec parser to control the storage key independently of the trace payload.
    /// </summary>
    /// <param name="project">Project name for scoping the observation.</param>
    /// <param name="requirementId">The requirement identifier (e.g., "RF-001") used as topic key.</param>
    /// <param name="trace">The full trace info to persist (RequirementId inside trace may differ).</param>
    /// <param name="sessionId">Session ID for provenance.</param>
    public async Task SaveTraceFromSpecAsync(string project, string requirementId, TraceInfo trace, string sessionId)
    {
        var topicKey = $"trace/{project}/{requirementId}";
        var content = JsonSerializer.Serialize(trace);

        await _store.AddObservationAsync(new AddObservationParams
        {
            SessionId = sessionId,
            Type = "trace",
            Title = $"Trace: {requirementId}",
            Content = content,
            Project = project,
            TopicKey = topicKey,
            Scope = "team"
        });
    }

    /// <summary>
    /// List all requirement IDs that have been traced in a given project.
    /// </summary>
    /// <param name="project">Project name to search within.</param>
    /// <returns>List of requirement IDs (e.g., "RF-001", "RNF-003").</returns>
    public async Task<List<string>> ListTracedRequirementsAsync(string project)
    {
        var results = await _store.SearchAsync($"trace/{project}/", new SearchOptions { Limit = 100 });

        return results
            .Select(r => r.Observation.TopicKey?.Split('/').LastOrDefault() ?? "")
            .Where(id => !string.IsNullOrEmpty(id))
            .ToList();
    }

    // ─── Cycle detection ──────────────────────────────────────────────────────

    /// <summary>
    /// Detect whether the given set of relations contains a cycle starting from <paramref name="startId"/>.
    /// Uses DFS with a recursion stack. Only considers <c>depends_on</c> and <c>supersedes</c> as
    /// directional edges; <c>related_to</c> and <c>conflicts_with</c> are treated as undirected/bidirectional
    /// and do not participate in cycle formation.
    /// </summary>
    /// <param name="startId">The requirement ID to start traversal from.</param>
    /// <param name="relations">The list of relations belonging to that requirement.</param>
    /// <returns><c>true</c> if a cycle is detected; <c>false</c> otherwise.</returns>
    public bool HasCycle(string startId, List<TraceRelation> relations)
    {
        var visited = new HashSet<string>();
        var recursionStack = new HashSet<string>();
        return DfsHasCycle(startId, relations, visited, recursionStack);
    }

    private static bool DfsHasCycle(string current, List<TraceRelation> relations, HashSet<string> visited, HashSet<string> stack)
    {
        if (stack.Contains(current)) return true;
        if (visited.Contains(current)) return false;

        visited.Add(current);
        stack.Add(current);

        foreach (var rel in relations)
        {
            if (rel.Type is "depends_on" or "supersedes")
            {
                if (DfsHasCycle(rel.Target, relations, visited, stack))
                    return true;
            }
        }

        stack.Remove(current);
        return false;
    }
}
