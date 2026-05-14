using Engram.Store;

namespace Engram.Verification;

/// <summary>
/// Tracks verification cycles per change using the Engram observation store.
///
/// Each change has a cycle count stored as an observation with
/// <c>topic_key = "cycle-count/{changeName}"</c>. Every call to
/// <see cref="IncrementCycleAsync"/> upserts the observation and increments
/// <c>revision_count</c>, which serves as the current cycle number.
///
/// Configuration is read from environment variables:
/// <list type="bullet">
///   <item><c>ENGRAM_VERIFICATION_MAX_CYCLES</c> — max cycles before escalation (default: 3)</item>
/// </list>
/// </summary>
public sealed class CycleTracker
{
    private readonly IStore _store;
    private readonly int _maxCycles;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="store">The Engram observation store.</param>
    public CycleTracker(IStore store)
    {
        _store = store;
        _maxCycles = int.TryParse(
            Environment.GetEnvironmentVariable("ENGRAM_VERIFICATION_MAX_CYCLES"),
            out var max)
            ? max
            : 3;
    }

    /// <summary>
    /// Maximum number of verification cycles before escalation is required.
    /// </summary>
    public int MaxCycles => _maxCycles;

    /// <summary>
    /// Gets the current cycle count for a change, or 0 if not yet started.
    /// </summary>
    /// <param name="changeName">The change identifier (e.g. "verification-tools").</param>
    /// <returns>The current revision (cycle) count, or 0 if no cycles have been recorded.</returns>
    public async Task<int> GetCurrentCycleAsync(string changeName)
    {
        var topicKey = $"cycle-count/{changeName}";
        var results = await _store.SearchAsync(topicKey, new SearchOptions { Limit = 1 });

        if (results.Count > 0 && results[0].Observation.TopicKey == topicKey)
            return results[0].Observation.RevisionCount;

        return 0;
    }

    /// <summary>
    /// Increments the cycle count for a change and returns the new count.
    /// </summary>
    /// <param name="changeName">The change identifier.</param>
    /// <param name="sessionId">The current session identifier for provenance.</param>
    /// <returns>The new cycle count after incrementing.</returns>
    public async Task<int> IncrementCycleAsync(string changeName, string sessionId)
    {
        var topicKey = $"cycle-count/{changeName}";

        var id = await _store.AddObservationAsync(new AddObservationParams
        {
            SessionId = sessionId,
            Title = $"Cycle count: {changeName}",
            Content = $"Incremented cycle for {changeName}",
            Project = "engram",
            Type = "verification",
            TopicKey = topicKey,
        });

        var obs = await _store.GetObservationAsync(id);
        return obs?.RevisionCount ?? 1;
    }

    /// <summary>
    /// Returns <c>true</c> when the current cycle has reached or exceeded
    /// the maximum allowed cycles.
    /// </summary>
    public bool ShouldEscalate(int currentCycle) => currentCycle >= _maxCycles;

    /// <summary>
    /// Resets (deletes) the cycle-count observation for a change.
    /// </summary>
    /// <param name="changeName">The change identifier.</param>
    public async Task ResetCycleAsync(string changeName)
    {
        var topicKey = $"cycle-count/{changeName}";
        var results = await _store.SearchAsync(topicKey, new SearchOptions { Limit = 1 });

        if (results.Count > 0)
        {
            await _store.DeleteObservationAsync(results[0].Observation.Id);
        }
    }
}
