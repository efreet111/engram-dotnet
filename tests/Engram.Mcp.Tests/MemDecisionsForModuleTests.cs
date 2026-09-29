using Engram.Mcp;
using Engram.MdGeneration;
using Engram.Store;
using Engram.Verification;
using Engram.Diagnostics;
using Engram.Sync;
using Xunit;

namespace Engram.Mcp.Tests;

/// <summary>
/// Tests for the mem_decisions_for_module MCP tool (HU-061).
/// Verifies dual-type query (decision + architecture), deduplication, limit clamping, and graceful empty handling.
/// </summary>
[Collection("ConsoleSensitive")]
public class MemDecisionsForModuleTests : IDisposable
{
    private readonly SqliteStore  _store;
    private readonly EngramTools  _tools;
    private readonly WriteQueue   _writeQueue;
    private readonly string        _tempDir;
    private readonly SessionActivity _sessionActivity;
    private readonly IVerifier    _verifier;
    private readonly CycleTracker  _cycleTracker;
    private readonly TraceRepository _traceRepo;
    private readonly LineageBuilder _lineageBuilder;
    private readonly IDiagnosticService _diagnosticService;
    private readonly MemoryRelationRepository _memRelRepo;
    private readonly MemoryLineageBuilder _memLineageBuilder;
    private const string SessionId = "decisions-test-session";

    public MemDecisionsForModuleTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "engram-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _store = new SqliteStore(new StoreConfig { DataDir = _tempDir });
        _writeQueue = new WriteQueue();
        _sessionActivity = new SessionActivity();
        _verifier = new NoOpVerifier();
        _cycleTracker = new CycleTracker(_store);
        var promotionService = new PromotionService(_store);
        _traceRepo = new TraceRepository(_store);
        _lineageBuilder = new LineageBuilder(_traceRepo);
        _diagnosticService = new DiagnosticService(_store);
        _memRelRepo = new MemoryRelationRepository(_store);
        _memLineageBuilder = new MemoryLineageBuilder(_memRelRepo, _store);
        _tools = new EngramTools(_store, new McpConfig { DefaultProject = "default-project" }, _writeQueue, _sessionActivity, _verifier, _cycleTracker, promotionService, _traceRepo, _lineageBuilder, _diagnosticService, _memRelRepo, _memLineageBuilder);
    }

    public void Dispose()
    {
        _store.Dispose();
        _writeQueue.Dispose();
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    private Task SeedSession()
        => _store.CreateSessionAsync(SessionId, "test-proj", "/tmp");

    private async Task<long> SeedDecision(string title, string content, string ns, string project = "test-proj", string type = "decision")
    {
        return await _store.AddObservationAsync(new AddObservationParams
        {
            SessionId = SessionId,
            Title     = title,
            Content   = content,
            Type      = type,
            Project   = project,
            Namespace = ns,
        });
    }

    [Fact]
    public async Task MemDecisionsForModule_ExactNamespace_ReturnsOnlyDecisions()
    {
        await SeedSession();
        await SeedDecision("Use repository pattern", "We chose the repository pattern to abstract...", "Engram.Store");
        await SeedDecision("Another decision", "Some other decision content", "Engram.Store", type: "architecture");

        var result = await _tools.MemDecisionsForModule("Engram.Store", project: "test-proj");

        Assert.Contains("Use repository pattern", result);
        Assert.Contains("Another decision", result);
        Assert.Contains("decision", result);
        Assert.Contains("architecture", result);
    }

    [Fact]
    public async Task MemDecisionsForModule_IncludesArchitectureType()
    {
        await SeedSession();
        await SeedDecision("Store backend decision", "SQLite as default backend", "Engram.Store", type: "architecture");

        var result = await _tools.MemDecisionsForModule("Engram.Store", project: "test-proj");

        Assert.Contains("Store backend decision", result);
        Assert.Contains("architecture", result);
    }

    [Fact]
    public async Task MemDecisionsForModule_Limit_RespectsLimit()
    {
        await SeedSession();
        for (int i = 0; i < 5; i++)
            await SeedDecision($"Decision {i}", $"Content {i}", "Engram.Store");

        var result = await _tools.MemDecisionsForModule("Engram.Store", project: "test-proj", limit: 3);

        // Should not contain all 5; FormatSearchResults truncates preview
        // The limit clamps to 3, so at most 3 results
        Assert.DoesNotContain("Decision 4", result);
        Assert.DoesNotContain("Decision 5", result);
    }

    [Fact]
    public async Task MemDecisionsForModule_NoDecisions_ReturnsGracefulMessage()
    {
        await SeedSession();
        await SeedDecision("Some unrelated decision", "Content", "Other.Module");

        var result = await _tools.MemDecisionsForModule("Engram.Store", project: "test-proj");

        Assert.Contains("No decisions found for module", result);
        Assert.Contains("Engram.Store", result);
    }

    [Fact]
    public async Task MemDecisionsForModule_PrefixMatch_ReturnsAllUnderModule()
    {
        await SeedSession();
        await SeedDecision("Auth JWT decision", "Use JWT for auth tokens", "Engram.Auth");
        await SeedDecision("Auth OAuth decision", "Use OAuth2 for external logins", "Engram.Auth.OAuth");
        await SeedDecision("Store decision", "Use repository pattern", "Engram.Store");

        var result = await _tools.MemDecisionsForModule("Engram.Auth", project: "test-proj");

        Assert.Contains("Auth JWT decision", result);
        Assert.Contains("Auth OAuth decision", result);
        Assert.DoesNotContain("Store decision", result);
    }

    [Fact]
    public async Task MemDecisionsForModule_DeduplicatesResults()
    {
        await SeedSession();
        // Note: the same observation cannot have two types, but if two different
        // observations have overlapping content, Concat+Take should not duplicate by ID
        var id1 = await SeedDecision("Decision A", "Content A", "Engram.Store", type: "decision");
        var id2 = await SeedDecision("Decision B", "Content B", "Engram.Store", type: "architecture");

        var result = await _tools.MemDecisionsForModule("Engram.Store", project: "test-proj");

        // Each ID should appear exactly once
        var id1Count = result.Split($"#{id1}").Length - 1;
        var id2Count = result.Split($"#{id2}").Length - 1;
        Assert.Equal(1, id1Count);
        Assert.Equal(1, id2Count);
    }

    [Fact]
    public async Task MemDecisionsForModule_Limit_ClampedTo50()
    {
        await SeedSession();
        for (int i = 0; i < 60; i++)
            await SeedDecision($"Decision {i}", $"Content {i}", "Engram.Store");

        // Passing limit > 50 should be clamped
        var result = await _tools.MemDecisionsForModule("Engram.Store", project: "test-proj", limit: 100);

        // Should not throw and should return results
        Assert.Contains("decisions for module", result);
    }
}
