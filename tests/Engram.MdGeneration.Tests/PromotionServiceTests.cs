using Engram.MdGeneration;
using Engram.Store;
using Xunit;

namespace Engram.MdGeneration.Tests;

public class PromotionServiceTests
{
    [Fact]
    public async Task PromoteAsync_InvalidId_ReturnsZero()
    {
        var cfg = new StoreConfig
        {
            DataDir = Path.Combine(Path.GetTempPath(), $"engram-test-{Guid.NewGuid()}")
        };
        using var store = new SqliteStore(cfg);
        var service = new PromotionService(store);

        var result = await service.PromoteAsync(99999, "docs/decisions");

        Assert.Equal(0, result);
    }

    [Fact]
    public async Task SyncAsync_DryRun_DoesNotCreateFiles()
    {
        var testDir = Path.Combine(Path.GetTempPath(), $"engram-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(testDir);
        try
        {
            var cfg = new StoreConfig { DataDir = testDir };
            using var store = new SqliteStore(cfg);

            // Create a session and observation
            await store.CreateSessionAsync("test-session", "test", "/tmp");
            await store.AddObservationAsync(new AddObservationParams
            {
                SessionId = "test-session",
                Type = "decision",
                Title = "Test Decision",
                Content = "Test content",
                Project = "test"
            });

            var service = new PromotionService(store);
            var mdDir = Path.Combine(testDir, "docs/decisions");

            // Dry run should not create files
            var result = await service.SyncAsync(mdDir, dryRun: true);

            Assert.True(result.DryRun);
            Assert.False(Directory.Exists(mdDir));
        }
        finally
        {
            try { Directory.Delete(testDir, true); } catch { }
        }
    }
}
