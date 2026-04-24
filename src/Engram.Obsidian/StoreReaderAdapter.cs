using Engram.Store;

namespace Engram.Obsidian;

/// <summary>
/// Adapter that wraps an IStore and exposes only the read operations
/// the Obsidian exporter needs.
/// </summary>
public class StoreReaderAdapter : IObsidianStoreReader
{
    private readonly IStore _store;

    public StoreReaderAdapter(IStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public Task<ExportData> ExportAsync() => _store.ExportAsync();

    public Task<Stats> StatsAsync() => _store.StatsAsync();
}
