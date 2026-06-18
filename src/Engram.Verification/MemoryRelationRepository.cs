using System.Text.Json;
using Engram.Store;

namespace Engram.Verification;

/// <summary>
/// Persistence layer for memory observation relations.
/// Saves and loads <see cref="MemoryRelationSet"/> as Engram observations using IStore,
/// keyed by <c>memrel/{project}/{observationId}</c> so they upsert cleanly.
/// </summary>
public sealed class MemoryRelationRepository
{
    private readonly IStore _store;

    public MemoryRelationRepository(IStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    private static string TopicKey(string project, long observationId)
        => $"memrel/{project}/{observationId}";

    /// <summary>
    /// Append a relation to the outgoing edge list of <paramref name="fromObsId"/>.
    /// Loads any existing <see cref="MemoryRelationSet"/> for the observation, appends,
    /// and re-saves — the IStore topic_key upsert replaces the content in place.
    /// </summary>
    /// <param name="project">Project name for scoping the observation.</param>
    /// <param name="fromObsId">Source observation ID (the "from" side of the edge).</param>
    /// <param name="rel">The relation to append.</param>
    /// <param name="sessionId">Session ID for provenance.</param>
    public async Task SaveRelationAsync(string project, long fromObsId, MemoryRelation rel, string sessionId)
    {
        var existing = await GetRelationsAsync(project, fromObsId);
        if (existing.Any(r => r.Type == rel.Type && r.TargetObservationId == rel.TargetObservationId))
            return;

        var set = new MemoryRelationSet
        {
            ObservationId = fromObsId,
            Relations = [.. existing, rel]
        };

        await _store.AddObservationAsync(new AddObservationParams
        {
            SessionId = sessionId,
            Type = "memory_relation",
            Title = $"Relations for obs#{fromObsId}",
            Content = JsonSerializer.Serialize(set),
            Project = project,
            TopicKey = TopicKey(project, fromObsId),
            Scope = Scopes.Team
        });
    }

    /// <summary>
    /// Load the outgoing relations for an observation. Returns an empty list if none exist.
    /// </summary>
    /// <param name="project">Project name used when saving the relations.</param>
    /// <param name="observationId">The source observation ID.</param>
    public async Task<List<MemoryRelation>> GetRelationsAsync(string project, long observationId)
    {
        var results = await _store.SearchAsync(TopicKey(project, observationId), new SearchOptions { Limit = 1 });
        if (results.Count == 0)
            return [];

        var content = results[0].Observation.Content;
        if (string.IsNullOrWhiteSpace(content))
            return [];

        var set = JsonSerializer.Deserialize<MemoryRelationSet>(content);
        return set?.Relations ?? [];
    }

    /// <summary>
    /// Remove a specific relation from an observation's outgoing edge list.
    /// If the resulting list is empty, the underlying observation is deleted.
    /// </summary>
    /// <param name="project">Project name used when saving the relations.</param>
    /// <param name="fromObsId">Source observation ID.</param>
    /// <param name="toObsId">Target observation ID.</param>
    /// <param name="type">Relation type (e.g., "depends_on").</param>
    /// <param name="sessionId">Session ID for provenance.</param>
    /// <returns><c>true</c> if a relation was removed; <c>false</c> if none matched.</returns>
    public async Task<bool> DeleteRelationAsync(string project, long fromObsId, long toObsId, string type, string sessionId)
    {
        var existing = await GetRelationsAsync(project, fromObsId);
        var filtered = existing
            .Where(r => !(r.Type == type && r.TargetObservationId == toObsId))
            .ToList();

        if (filtered.Count == existing.Count)
            return false;

        if (filtered.Count == 0)
        {
            var results = await _store.SearchAsync(TopicKey(project, fromObsId), new SearchOptions { Limit = 1 });
            if (results.Count > 0)
                await _store.DeleteObservationAsync(results[0].Observation.Id);
            return true;
        }

        var set = new MemoryRelationSet
        {
            ObservationId = fromObsId,
            Relations = filtered
        };

        await _store.AddObservationAsync(new AddObservationParams
        {
            SessionId = sessionId,
            Type = "memory_relation",
            Title = $"Relations for obs#{fromObsId}",
            Content = JsonSerializer.Serialize(set),
            Project = project,
            TopicKey = TopicKey(project, fromObsId),
            Scope = Scopes.Team
        });

        return true;
    }
}