using Engram.Store;
using Xunit;

namespace Engram.MdGeneration.Tests;

public class StoreSyncTests : IDisposable
{
    private readonly string _testDir;
    private readonly SqliteStore _store;

    public StoreSyncTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"engram-sync-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDir);
        var cfg = new StoreConfig { DataDir = _testDir };
        _store = new SqliteStore(cfg);
    }

    [Fact]
    public async Task SyncMdToRepoAsync_Batch_PromotesAll()
    {
        await _store.CreateSessionAsync("sync-test", "test", "/tmp");

        // Create 3 unpromoted observations
        var ids = new List<long>();
        for (int i = 0; i < 3; i++)
        {
            var id = await _store.AddObservationAsync(new AddObservationParams
            {
                SessionId = "sync-test",
                Type = "decision",
                Title = $"Sync Test {i}",
                Content = $"Content {i}",
                Project = "test"
            });
            ids.Add(id);
        }

        var mdDir = Path.Combine(_testDir, "docs/decisions");
        var count = await _store.SyncMdToRepoAsync(mdDir, dryRun: false);

        Assert.Equal(3, count);

        // Verify each observation has md_path
        foreach (var id in ids)
        {
            var obs = await _store.GetObservationAsync(id);
            Assert.NotNull(obs);
            Assert.NotNull(obs.MdPath);
            Assert.True(File.Exists(Path.Combine(mdDir, obs.MdPath)));
        }
    }

    [Fact]
    public async Task SyncMdToRepoAsync_DryRun_ReturnsCountWithoutFiles()
    {
        await _store.CreateSessionAsync("sync-test", "test", "/tmp");
        await _store.AddObservationAsync(new AddObservationParams
        {
            SessionId = "sync-test",
            Type = "decision",
            Title = "Dry Run Test",
            Content = "Should not create file",
            Project = "test"
        });

        var mdDir = Path.Combine(_testDir, "docs/decisions");
        var count = await _store.SyncMdToRepoAsync(mdDir, dryRun: true);

        Assert.Equal(1, count);
        Assert.False(Directory.Exists(mdDir));
    }

    [Fact]
    public async Task SyncMdToRepoAsync_EmptySync_ReturnsZero()
    {
        var mdDir = Path.Combine(_testDir, "docs/decisions");
        var count = await _store.SyncMdToRepoAsync(mdDir, dryRun: false);
        Assert.Equal(0, count);
    }

    public void Dispose()
    {
        _store.Dispose();
        try { Directory.Delete(_testDir, true); } catch { }
    }
}
