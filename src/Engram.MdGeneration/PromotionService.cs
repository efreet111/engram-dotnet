using Engram.Store;

namespace Engram.MdGeneration;

/// <summary>
/// Orchestrates .md promotion: promote individual observations, batch sync, and index generation.
/// Delegates all storage and file I/O to the underlying IStore implementation.
/// </summary>
public sealed class PromotionService
{
    private readonly IStore _store;
    private readonly MdTemplateEngine _template;
    private readonly MdIndexGenerator _indexGen;

    public PromotionService(IStore store)
    {
        _store = store;
        _template = new MdTemplateEngine();
        _indexGen = new MdIndexGenerator();
    }

    public async Task<long> PromoteAsync(long observationId, string mdDir)
    {
        return await _store.PromoteToMdAsync(observationId, mdDir);
    }

    public async Task<SyncResult> SyncAsync(string mdDir, bool dryRun = false)
    {
        var count = await _store.SyncMdToRepoAsync(mdDir, dryRun);
        return new SyncResult { Promoted = count, DryRun = dryRun };
    }

    public async Task<string> GenerateIndexAsync(string mdDir)
    {
        return await _store.GenerateIndexAsync(mdDir);
    }
}

public sealed record SyncResult
{
    public int Promoted { get; init; }
    public bool DryRun { get; init; }
}
