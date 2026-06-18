using System.Text.Json.Serialization;

namespace Engram.Verification;

/// <summary>
/// A typed relation between two memory observations.
/// Mirrors <see cref="TraceRelation"/> for the general observation graph.
/// </summary>
public sealed record MemoryRelation
{
    [JsonPropertyName("type")] public string Type { get; init; } = "";
    [JsonPropertyName("target_observation_id")] public long TargetObservationId { get; init; }
}

/// <summary>
/// Container for all relations emanating from a single observation.
/// Persisted as an Engram observation with topic_key <c>memrel/{project}/{observationId}</c>.
/// </summary>
public sealed record MemoryRelationSet
{
    [JsonPropertyName("observation_id")] public long ObservationId { get; init; }
    [JsonPropertyName("relations")] public List<MemoryRelation> Relations { get; init; } = [];
}