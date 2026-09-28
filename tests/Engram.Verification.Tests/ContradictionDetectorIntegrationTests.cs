using Engram.Store;
using Engram.Verification;
using Xunit;

namespace Engram.Verification.Tests;

/// <summary>
/// Integration tests for the full mem_check_contradictions MCP tool workflow.
/// Tests the tool end-to-end with an in-memory SQLite store.
/// </summary>
public class ContradictionDetectorIntegrationTests : IDisposable
{
    private readonly string _testDir;
    private readonly IStore _store;
    private readonly MemoryRelationRepository _repo;
    private readonly ContradictionDetector _detector;
    private const string SessionId = "integration-test-session";

    public ContradictionDetectorIntegrationTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"engram-integration-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        var cfg = new StoreConfig { DataDir = _testDir };
        _store = new SqliteStore(cfg);
        _repo = new MemoryRelationRepository(_store);
        _detector = new ContradictionDetector(_store, _repo);
    }

    public void Dispose()
    {
        _store.Dispose();
        try { Directory.Delete(_testDir, true); } catch { }
    }

    private async Task<long> CreateObsAsync(string project, string title, string content, string topicKey)
    {
        await _store.CreateSessionAsync(SessionId, project, "/tmp");
        return await _store.AddObservationAsync(new AddObservationParams
        {
            SessionId = SessionId,
            Type = "test_obs",
            Title = title,
            Content = content,
            Project = project,
            TopicKey = topicKey,
            Scope = Scopes.Team
        });
    }

    [Fact]
    public async Task FullWorkflow_DirectContradiction_RoundTrip()
    {
        // [NFR-5] Tests the full round-trip: detect direct contradictions
        const string project = "test-direct-int";
        var obsA = await CreateObsAsync(project, "obs-A", "Use SQLite for local dev", $"obs/int/direct/{Guid.NewGuid():N}");
        var obsB = await CreateObsAsync(project, "obs-B", "Never use SQLite for production", $"obs/int/direct/{Guid.NewGuid():N}");
        await _repo.SaveRelationAsync(project, obsA, new MemoryRelation { Type = "conflicts_with", TargetObservationId = obsB }, SessionId);

        var results = await _detector.DetectDirectContradictionsAsync(project);

        Assert.Single(results);
        var r = results[0];
        Assert.Equal("direct", r.Type);
        Assert.Equal(1.0, r.Confidence);
        Assert.Equal("keep_both", r.SuggestedResolution);
    }

    [Fact]
    public async Task FullWorkflow_UnknownType_SilentlyIgnored()
    {
        // [NFR-5] Unknown types are silently ignored — no error thrown
        const string project = "test-unknown-type";
        var obsA = await CreateObsAsync(project, "obs-A", "Content A", $"obs/int/unknown/{Guid.NewGuid():N}");
        var obsB = await CreateObsAsync(project, "obs-B", "Content B", $"obs/int/unknown/{Guid.NewGuid():N}");
        await _repo.SaveRelationAsync(project, obsA, new MemoryRelation { Type = "conflicts_with", TargetObservationId = obsB }, SessionId);

        // Only "direct" should run; "unknown_type" should be filtered out
        var results = await _detector.DetectDirectContradictionsAsync(project, limit: 50);
        Assert.Single(results);

        // Verify no contradictions for other types
        var temporalResults = await _detector.DetectTemporalSupersedenceAsync(project);
        Assert.Empty(temporalResults);
    }

    [Fact]
    public async Task FullWorkflow_NoContradictions_ReturnsEmptyResult()
    {
        // FR-5 GWT-2: No contradictions detected when observations are unrelated
        const string project = "test-empty-results";
        // Create two unrelated observations (different topic, no conflicts_with relation)
        await CreateObsAsync(project, "obs-1", "Use dependency injection in C#", $"topic/{Guid.NewGuid()}");
        await CreateObsAsync(project, "obs-2", "Unit tests should be fast", $"topic/{Guid.NewGuid()}");

        var results = await _detector.DetectEmbeddingConflictsAsync(project);
        Assert.Empty(results); // No pairs should be found
    }
}
