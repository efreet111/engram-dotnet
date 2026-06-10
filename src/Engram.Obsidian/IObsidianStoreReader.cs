using Engram.Store;

namespace Engram.Obsidian;

/// <summary>
/// Narrow read-only interface the Obsidian exporter needs.
/// Keeps the dependency minimal — easy to mock in tests.
/// </summary>
public interface IObsidianStoreReader
{
    /// <summary>
    /// Export all sessions, observations, and prompts from the store.
    /// </summary>
    Task<ExportData> ExportAsync();

    /// <summary>
    /// Get system statistics (total sessions, observations, prompts, projects).
    /// </summary>
    Task<Stats> StatsAsync();

    /// <summary>
    /// Export filtered by project (ENG-208).
    /// </summary>
    Task<ExportData> ExportProjectAsync(string project);

    /// <summary>
    /// Incremental export via mutation_seq cursor (ENG-208).
    /// </summary>
    Task<ExportData> ExportSinceAsync(string? project, long afterSeq, int limit);
}
