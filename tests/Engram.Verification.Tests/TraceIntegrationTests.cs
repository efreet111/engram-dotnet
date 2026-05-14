using Engram.Store;
using Engram.Verification;
using Xunit;

namespace Engram.Verification.Tests;

public class TraceIntegrationTests : IDisposable
{
    private readonly string _testDir;
    private readonly IStore _store;
    private readonly TraceRepository _repo;
    private readonly LineageBuilder _builder;

    public TraceIntegrationTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"engram-trace-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDir);
        var cfg = new StoreConfig { DataDir = _testDir };
        _store = new SqliteStore(cfg);
        _repo = new TraceRepository(_store);
        _builder = new LineageBuilder(_repo);
    }

    [Fact]
    public async Task SaveAndGetTrace_Roundtrip_Works()
    {
        await _store.CreateSessionAsync("int-test", "test", "/tmp");

        var trace = new TraceInfo
        {
            RequirementId = "RF-001",
            Source = new TraceSource
            {
                Source = "ISSUE-42",
                Author = "Dev Team",
                Rationale = "Required for compliance"
            },
            Relations = [new TraceRelation { Type = "depends_on", Target = "RF-002" }]
        };

        await _repo.SaveTraceAsync("test", trace, "int-test");
        var loaded = await _repo.GetTraceAsync("test", "RF-001");

        Assert.NotNull(loaded);
        Assert.Equal("RF-001", loaded.RequirementId);
        Assert.NotNull(loaded.Source);
        Assert.Equal("ISSUE-42", loaded.Source.Source);
        Assert.Single(loaded.Relations);
    }

    [Fact]
    public async Task GetTrace_Untraced_ReturnsNull()
    {
        // No session needed for search-only operations
        var result = await _repo.GetTraceAsync("test", "RF-999");
        Assert.Null(result);
    }

    [Fact]
    public async Task Lineage_SingleNode_NoAncestors()
    {
        await _store.CreateSessionAsync("int-test", "test", "/tmp");
        await _repo.SaveTraceAsync("test", new TraceInfo { RequirementId = "RF-001" }, "int-test");

        var lineage = await _builder.BuildLineageAsync("test", "RF-001");

        Assert.Equal("traced", lineage.Root.Status);
        Assert.Empty(lineage.Ancestors);
        Assert.Empty(lineage.Descendants);
        Assert.False(lineage.CycleDetected);
    }

    public void Dispose()
    {
        _store.Dispose();
        try { Directory.Delete(_testDir, true); } catch { }
    }
}
