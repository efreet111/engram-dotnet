namespace Engram.Store;

/// <summary>
/// Server-side cloud chunk storage interface.
/// Phase 1: Schema only (table creation, existence check).
/// Phase 2+: Full chunk protocol implementation (write, read, backfill).
/// </summary>
public interface ICloudChunkStore
{
    /// <summary>
    /// Write a chunk payload to the cloud store.
    /// </summary>
    Task WriteChunkAsync(
        string project,
        string chunkId,
        string createdBy,
        DateTime clientCreatedAt,
        byte[] payload,
        CancellationToken ct = default);

    /// <summary>
    /// Read a chunk payload from the cloud store.
    /// Returns null if not found.
    /// </summary>
    Task<byte[]?> ReadChunkAsync(string project, string chunkId, CancellationToken ct = default);

    /// <summary>
    /// Check if a chunk exists in the cloud store.
    /// </summary>
    Task<bool> ChunkExistsAsync(string project, string chunkId, CancellationToken ct = default);
}

/// <summary>
/// Chunk metadata for cloud storage.
/// </summary>
public sealed record ChunkMetadata(
    string ChunkId,
    string Project,
    string CreatedBy,
    DateTime ClientCreatedAt,
    int SessionsCount,
    int ObservationsCount,
    int PromptsCount);
