using Engram.Store;
using Xunit;

namespace Engram.MdGeneration.Tests;

public class StorePromotionTests : IDisposable
{
    private readonly string _testDir;
    private readonly SqliteStore _store;

    public StorePromotionTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"engram-promo-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDir);
        var cfg = new StoreConfig { DataDir = _testDir };
        _store = new SqliteStore(cfg);
    }

    [Fact]
    public async Task PromoteToMdAsync_Roundtrip_PersistsMdPath()
    {
        await _store.CreateSessionAsync("promo-test", "test", "/tmp");
        var obsId = await _store.AddObservationAsync(new AddObservationParams
        {
            SessionId = "promo-test",
            Type = "decision",
            Title = "Roundtrip Test",
            Content = "Testing roundtrip",
            Project = "test"
        });

        var mdDir = Path.Combine(_testDir, "docs/decisions");
        var result = await _store.PromoteToMdAsync(obsId, mdDir);

        Assert.Equal(obsId, result);

        // Verify md_path was persisted
        var obs = await _store.GetObservationAsync(obsId);
        Assert.NotNull(obs);
        Assert.NotNull(obs.MdPath);
        Assert.NotEmpty(obs.MdPath);

        // Verify file was created
        var fullPath = Path.Combine(mdDir, obs.MdPath);
        Assert.True(File.Exists(fullPath));

        // Verify file content has frontmatter
        var content = await File.ReadAllTextAsync(fullPath);
        Assert.Contains("observation_id: " + obsId, content);
        Assert.Contains("title: \"Roundtrip Test\"", content);
    }

    [Fact]
    public async Task PromoteToMdAsync_AlreadyPromoted_ReturnsZero()
    {
        await _store.CreateSessionAsync("promo-test", "test", "/tmp");
        var obsId = await _store.AddObservationAsync(new AddObservationParams
        {
            SessionId = "promo-test",
            Type = "decision",
            Title = "Already Promoted",
            Content = "Test",
            Project = "test"
        });

        var mdDir = Path.Combine(_testDir, "docs/decisions");
        await _store.PromoteToMdAsync(obsId, mdDir);

        // Second promote should return 0
        var result = await _store.PromoteToMdAsync(obsId, mdDir);
        Assert.Equal(0, result);
    }

    [Fact]
    public async Task PromoteToMdAsync_InvalidId_ReturnsZero()
    {
        var result = await _store.PromoteToMdAsync(99999, "/tmp/docs");
        Assert.Equal(0, result);
    }

    public void Dispose()
    {
        _store.Dispose();
        try { Directory.Delete(_testDir, true); } catch { }
    }
}
