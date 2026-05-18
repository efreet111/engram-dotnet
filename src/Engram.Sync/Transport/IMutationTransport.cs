namespace Engram.Sync.Transport;

/// <summary>
/// Transport interface for mutation-based sync protocol.
/// Handles HTTP communication with cloud server for push/pull operations.
/// </summary>
public interface IMutationTransport
{
    /// <summary>
    /// Push mutations to cloud server.
    /// Returns accepted sequence numbers or pause error (409).
    /// </summary>
    /// <param name="entries">Mutations to push</param>
    /// <param name="createdBy">Optional contributor identity</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Push result with accepted seqs or pause error</returns>
    Task<PushResult> PushMutationsAsync(
        IReadOnlyList<MutationEntry> entries,
        string? createdBy = null,
        CancellationToken ct = default);

    /// <summary>
    /// Pull mutations from cloud server since a given sequence.
    /// Uses cursor-based pagination with has_more flag.
    /// </summary>
    /// <param name="sinceSeq">Sequence number to pull since (exclusive)</param>
    /// <param name="limit">Maximum mutations to return (default 100)</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Pull result with mutations, has_more flag, and latest seq</returns>
    Task<PullResult> PullMutationsAsync(
        long sinceSeq,
        int limit = 100,
        CancellationToken ct = default);
}

/// <summary>
/// Result of a push operation.
/// </summary>
/// <param name="AcceptedSeqs">Sequence numbers assigned by server (in input order)</param>
/// <param name="Project">Project name from response envelope</param>
/// <param name="PauseError">Non-null if 409 Conflict (sync paused)</param>
public sealed record PushResult(
    IReadOnlyList<long> AcceptedSeqs,
    string Project,
    string? PauseError = null);

/// <summary>
/// Result of a pull operation.
/// </summary>
public sealed record PullResult(
    IReadOnlyList<PulledMutation> Mutations,
    bool HasMore,
    long LatestSeq,
    string Project);
