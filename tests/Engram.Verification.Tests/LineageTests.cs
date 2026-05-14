using Engram.Store;
using Engram.Verification;
using Xunit;

namespace Engram.Verification.Tests;

public class LineageTests
{
    [Fact]
    public void HasCycle_DirectCycle_Detected()
    {
        using var store = CreateStore();
        var repo = new TraceRepository(store);

        var relations = new List<TraceRelation>
        {
            new() { Type = "depends_on", Target = "RF-001" },
            new() { Type = "depends_on", Target = "RF-002" }
        };

        var result = repo.HasCycle("RF-001", relations);
        Assert.True(result);
    }

    [Fact]
    public async Task BuildLineage_Chain_NoCycle()
    {
        using var store = CreateStore();
        var repo = new TraceRepository(store);
        var builder = new LineageBuilder(repo);
        await store.CreateSessionAsync("lineage-test", "test", "/tmp");
        
        // RF-001 depends on RF-002 (no cycle)
        await repo.SaveTraceAsync("test", new TraceInfo
        {
            RequirementId = "RF-001",
            Relations = [new TraceRelation { Type = "depends_on", Target = "RF-002" }]
        }, "lineage-test");
        await repo.SaveTraceAsync("test", new TraceInfo
        {
            RequirementId = "RF-002",
            Relations = [new TraceRelation { Type = "related_to", Target = "RF-003" }]
        }, "lineage-test");
        
        var result = await builder.BuildLineageAsync("test", "RF-001");
        Assert.False(result.CycleDetected);
        Assert.NotEmpty(result.Ancestors);
    }

    [Fact]
    public async Task BuildLineage_MaxHops_Truncates()
    {
        using var store = CreateStore();
        var repo = new TraceRepository(store);
        await store.CreateSessionAsync("lineage-test", "test", "/tmp");

        // Create a chain of 15 -> should truncate at 10
        for (int i = 1; i <= 15; i++)
        {
            var trace = new TraceInfo
            {
                RequirementId = $"RF-{i:D3}",
                Relations = i < 15 ? [new TraceRelation { Type = "depends_on", Target = $"RF-{i + 1:D3}" }] : []
            };
            await repo.SaveTraceAsync("test", trace, "lineage-test");
        }

        var builder = new LineageBuilder(repo);
        var result = await builder.BuildLineageAsync("test", "RF-001");

        Assert.Equal(10, result.Hops);
    }

    private static IStore CreateStore()
    {
        var cfg = new StoreConfig
        {
            DataDir = Path.Combine(Path.GetTempPath(), $"engram-test-{Guid.NewGuid()}")
        };
        return new SqliteStore(cfg);
    }
}
