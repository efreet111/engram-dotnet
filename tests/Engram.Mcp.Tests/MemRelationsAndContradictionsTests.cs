using Engram.Mcp;
using Engram.Store;
using Engram.Verification;
using Engram.Diagnostics;
using Engram.MdGeneration;
using Xunit;

namespace Engram.Mcp.Tests;

/// <summary>
/// Tests for mem_relations and mem_check_contradictions MCP tools.
/// </summary>
[Collection("ConsoleSensitive")]
public class MemRelationsAndContradictionsTests : IDisposable
{
    private readonly SqliteStore _store;
    private readonly EngramTools _tools;
    private readonly WriteQueue _writeQueue;
    private readonly string _tempDir;
    private readonly SessionActivity _sessionActivity;
    private readonly IVerifier _verifier;
    private readonly CycleTracker _cycleTracker;
    private readonly TraceRepository _traceRepo;
    private readonly LineageBuilder _lineageBuilder;
    private readonly IDiagnosticService _diagnosticService;
    private readonly MemoryRelationRepository _memRelRepo;
    private readonly MemoryLineageBuilder _memLineageBuilder;
    private const string SessionId = "relations-test-session";
    private const string TestProject = "test-proj";

    public MemRelationsAndContradictionsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "engram-relations-tests", Guid.NewGuid().ToString("N"));
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
        _tools = new EngramTools(
            _store,
            new McpConfig { DefaultProject = TestProject },
            _writeQueue,
            _sessionActivity,
            _verifier,
            _cycleTracker,
            promotionService,
            _traceRepo,
            _lineageBuilder,
            _diagnosticService,
            _memRelRepo,
            _memLineageBuilder);
    }

    public void Dispose()
    {
        _store.Dispose();
        _writeQueue.Dispose();
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    private async Task SeedSession()
        => await _store.CreateSessionAsync(SessionId, TestProject, "/tmp");

    private async Task SeedDefaultSession()
        => await _store.CreateSessionAsync($"manual-save-{TestProject}", TestProject, "/tmp");

    private async Task<long> SeedObservation(string title, string content, string topicKey = null, string type = "manual")
        => await _store.AddObservationAsync(new AddObservationParams
        {
            SessionId = SessionId,
            Title = title,
            Content = content,
            Type = type,
            Project = TestProject,
            TopicKey = topicKey,
        });

    private async Task<long> SeedObservationWithScope(string title, string content, string topicKey, string scope)
        => await _store.AddObservationAsync(new AddObservationParams
        {
            SessionId = SessionId,
            Title = title,
            Content = content,
            Type = "manual",
            Project = TestProject,
            TopicKey = topicKey,
            Scope = scope,
        });

    // ═══════════════════════════════════════════════════════════════════════
    // mem_relations TESTS
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task MemRelations_Get_ReturnsNoRelations_WhenNoneExist()
    {
        await SeedSession();
        var obsId = await SeedObservation("Test obs", "Test content");

        var result = await _tools.MemRelations(obsId, "get");

        Assert.Contains($"No relations found", result);
        Assert.Contains($"{obsId}", result);
    }

    [Fact]
    public async Task MemRelations_Get_ReturnsError_WhenObsIdIsZero()
    {
        var result = await _tools.MemRelations(0, "get");

        Assert.Contains("Error", result);
        Assert.Contains("observation_id", result);
    }

    [Fact]
    public async Task MemRelations_Add_Success_ForRelatedTo()
    {
        await SeedSession();
        await SeedDefaultSession();
        var obsA = await SeedObservation("Obs A", "Content A");
        var obsB = await SeedObservation("Obs B", "Content B");

        var result = await _tools.MemRelations(obsA, "add", obsB, "related_to", TestProject);

        Assert.Contains("Relation related_to", result);
        Assert.Contains($"{obsB}", result);
        Assert.Contains("added", result);
    }

    [Fact]
    public async Task MemRelations_Add_Success_ForConflictsWith()
    {
        await SeedSession();
        await SeedDefaultSession();
        var obsA = await SeedObservation("Decision A", "Use SQLite");
        var obsB = await SeedObservation("Decision B", "Use Postgres");

        var result = await _tools.MemRelations(obsA, "add", obsB, "conflicts_with", TestProject);

        Assert.Contains("Relation conflicts_with", result);
        Assert.Contains($"{obsB}", result);
    }

    [Fact]
    public async Task MemRelations_Add_Success_ForDependsOn()
    {
        await SeedSession();
        await SeedDefaultSession();
        var obsA = await SeedObservation("Feature B", "Dependent on B");
        var obsB = await SeedObservation("Feature B dependency", "Base feature");

        var result = await _tools.MemRelations(obsA, "add", obsB, "depends_on", TestProject);

        Assert.Contains("Relation depends_on", result);
    }

    [Fact]
    public async Task MemRelations_Add_Success_ForSupersedes_SameTopicKey()
    {
        await SeedSession();
        await SeedDefaultSession();
        var obsA = await SeedObservation("Old decision", "Use v1", "auth/jwt");
        var obsB = await SeedObservation("New decision", "Use v2", "auth/jwt");

        var result = await _tools.MemRelations(obsA, "add", obsB, "supersedes", TestProject);

        Assert.Contains("Relation supersedes", result);
    }

    [Fact]
    public async Task MemRelations_Add_ReturnsError_WhenSupersedesWithDifferentTopicKey()
    {
        await SeedSession();
        await SeedDefaultSession();
        var obsA = await SeedObservation("Old auth", "Use JWT", "auth/jwt");
        var obsB = await SeedObservation("New db", "Use Postgres", "db/postgres");

        var result = await _tools.MemRelations(obsA, "add", obsB, "supersedes", TestProject);

        Assert.Contains("Error", result);
        Assert.Contains("same topic_key", result);
    }

    [Fact]
    public async Task MemRelations_Add_ReturnsError_WhenTargetIdMissing()
    {
        await SeedSession();
        var obsA = await SeedObservation("Obs A", "Content A");

        var result = await _tools.MemRelations(obsA, "add", null, "related_to", TestProject);

        Assert.Contains("Error", result);
        Assert.Contains("target_observation_id", result);
    }

    [Fact]
    public async Task MemRelations_Add_ReturnsError_WhenTypeMissing()
    {
        await SeedSession();
        await SeedDefaultSession();
        var obsA = await SeedObservation("Obs A", "Content A");
        var obsB = await SeedObservation("Obs B", "Content B");

        var result = await _tools.MemRelations(obsA, "add", obsB, null, TestProject);

        Assert.Contains("Error", result);
        Assert.Contains("type is required", result);
    }

    [Fact]
    public async Task MemRelations_Add_ReturnsError_WhenInvalidType()
    {
        await SeedSession();
        await SeedDefaultSession();
        var obsA = await SeedObservation("Obs A", "Content A");
        var obsB = await SeedObservation("Obs B", "Content B");

        var result = await _tools.MemRelations(obsA, "add", obsB, "invalid_type", TestProject);

        Assert.Contains("Error", result);
        Assert.Contains("Invalid relation type", result);
    }

    [Fact]
    public async Task MemRelations_Get_ReturnsAddedRelations()
    {
        await SeedSession();
        await SeedDefaultSession();
        var obsA = await SeedObservation("Obs A", "Content A");
        var obsB = await SeedObservation("Obs B", "Content B");
        var obsC = await SeedObservation("Obs C", "Content C");

        // Add two relations
        await _tools.MemRelations(obsA, "add", obsB, "related_to", TestProject);
        await _tools.MemRelations(obsA, "add", obsC, "depends_on", TestProject);

        var result = await _tools.MemRelations(obsA, "get", project: TestProject);

        Assert.Contains($"Relations for obs#{obsA}", result);
        Assert.Contains("related_to", result);
        Assert.Contains("depends_on", result);
        Assert.Contains($"{obsB}", result);
        Assert.Contains($"{obsC}", result);
    }

    [Fact]
    public async Task MemRelations_Delete_Success()
    {
        await SeedSession();
        await SeedDefaultSession();
        var obsA = await SeedObservation("Obs A", "Content A");
        var obsB = await SeedObservation("Obs B", "Content B");

        // Add relation
        await _tools.MemRelations(obsA, "add", obsB, "related_to", TestProject);

        // Verify it exists
        var getResult = await _tools.MemRelations(obsA, "get", project: TestProject);
        Assert.Contains("related_to", getResult);

        // Delete it
        var deleteResult = await _tools.MemRelations(obsA, "delete", obsB, "related_to", TestProject);
        Assert.Contains("removed", deleteResult);

        // Verify it's gone
        var afterDelete = await _tools.MemRelations(obsA, "get", project: TestProject);
        Assert.Contains("No relations found", afterDelete);
    }

    [Fact]
    public async Task MemRelations_Delete_ReturnsNoMatch_WhenRelationDoesNotExist()
    {
        await SeedSession();
        await SeedDefaultSession();
        var obsA = await SeedObservation("Obs A", "Content A");
        var obsB = await SeedObservation("Obs B", "Content B");

        var result = await _tools.MemRelations(obsA, "delete", obsB, "related_to", TestProject);

        Assert.Contains("No matching relation found", result);
    }

    [Fact]
    public async Task MemRelations_ReturnsError_ForInvalidAction()
    {
        await SeedSession();
        var obsA = await SeedObservation("Obs A", "Content A");

        var result = await _tools.MemRelations(obsA, "invalid_action", project: TestProject);

        Assert.Contains("Error", result);
        Assert.Contains("Invalid action", result);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // mem_check_contradictions TESTS
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task MemCheckContradictions_ReturnsNoContradictions_WhenNoneExist()
    {
        await SeedSession();
        // Create unrelated observations - no conflicts_with relations, different topic_keys
        await SeedObservation("Meeting notes", "Discussed project timeline", "meeting/notes");
        await SeedObservation("Code review", "Reviewed PR #123", "code/review");

        var result = await _tools.MemCheckContradictions(TestProject);

        Assert.Contains("No contradictions detected", result);
    }

    // NOTE: MemCheckContradictions tests require specific project resolution setup
    // that differs from how EngramTools resolves projects in tests.
    // The contradiction detection is already covered by ContradictionDetectorIntegrationTests
    // in the Engram.Verification.Tests project.
}
