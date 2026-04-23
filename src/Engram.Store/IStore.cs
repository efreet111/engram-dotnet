namespace Engram.Store;

public interface IStore : IDisposable
{
    // Sessions
    Task CreateSessionAsync(string id, string project, string directory);
    Task EndSessionAsync(string id, string summary);
    Task<Session?> GetSessionAsync(string id);
    Task<IList<SessionSummary>> RecentSessionsAsync(string? project, int limit);

    // Observations
    Task<long> AddObservationAsync(AddObservationParams p);
    Task<Observation?> GetObservationAsync(long id);
    Task<IList<Observation>> RecentObservationsAsync(string? project, string? scope, int limit);
    Task<bool> UpdateObservationAsync(long id, UpdateObservationParams p);
    Task<bool> DeleteObservationAsync(long id);

    // Search
    Task<IList<SearchResult>> SearchAsync(string query, SearchOptions opts);
    Task<IList<SearchResult>> SearchAsync(string query, IList<string> projects, SearchOptions opts);
    Task<TimelineResult?> TimelineAsync(long observationId, int before, int after);

    // Prompts
    Task<long> AddPromptAsync(AddPromptParams p);
    Task<IList<Prompt>> RecentPromptsAsync(string? project, int limit);
    Task<IList<Prompt>> SearchPromptsAsync(string query, string? project, int limit);

    // Context & stats
    Task<string> FormatContextAsync(string? project, string? scope);
    Task<string> FormatContextAsync(IList<string> projects, string? scope);
    Task<Stats> StatsAsync();

    // Export / Import
    Task<ExportData> ExportAsync();
    Task<ImportResult> ImportAsync(ExportData data);

    // Projects
    Task<MergeResult> MergeProjectsAsync(IList<string> sources, string canonical);
    Task<IList<string>> ListProjectNamesAsync();
    Task<IList<ProjectStats>> ListProjectsWithStatsAsync();
    Task<int> CountObservationsForProjectAsync(string project);
    Task<PruneResult> PruneProjectAsync(string project);

    // Sync chunks
    Task<ISet<string>> GetSyncedChunksAsync();
    Task RecordSyncedChunkAsync(string chunkId);

    int MaxObservationLength { get; }
}
