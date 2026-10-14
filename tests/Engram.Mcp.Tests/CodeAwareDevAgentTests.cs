using Engram.Mcp;
using Engram.MdGeneration;
using Engram.Store;
using Engram.Verification;
using Engram.Diagnostics;
using Engram.Sync;
using Xunit;

namespace Engram.Mcp.Tests;

/// <summary>
/// Integration tests for code-aware memory recall (HU-059).
/// Verifies that memories saved with code metadata (file_path, symbol, namespace)
/// can be recalled by file, module, or symbol context.
/// </summary>
[CollectionDefinition("ConsoleSensitive", DisableParallelization = true)]
public sealed class CodeAwareDevAgentTests : IDisposable
{
    private readonly SqliteStore            _store;
    private readonly EngramTools             _tools;
    private readonly WriteQueue              _writeQueue;
    private readonly string                  _tempDir;
    private readonly SessionActivity         _sessionActivity;
    private readonly IVerifier               _verifier;
    private readonly CycleTracker            _cycleTracker;
    private readonly TraceRepository         _traceRepo;
    private readonly LineageBuilder          _lineageBuilder;
    private readonly IDiagnosticService      _diagnosticService;
    private readonly MemoryRelationRepository _memRelRepo;
    private readonly MemoryLineageBuilder    _memLineageBuilder;
    private const string SessionId = "code-aware-test-session";

    public CodeAwareDevAgentTests()
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
        _tools = new EngramTools(_store, new McpConfig { DefaultProject = "test-proj" },
            _writeQueue, _sessionActivity, _verifier, _cycleTracker, promotionService,
            _traceRepo, _lineageBuilder, _diagnosticService, _memRelRepo, _memLineageBuilder);
    }

    public void Dispose()
    {
        _store.Dispose();
        _writeQueue.Dispose();
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    private Task SeedSession()
        => _store.CreateSessionAsync(SessionId, "test-proj", "/tmp");

    // ─── FR-001: Save with code metadata → Recall by file ─────────────────────

    [Fact]
    public async Task SaveWithCodeMetadata_RecallByFile_ReturnsMatch()
    {
        await SeedSession();
        await _store.AddObservationAsync(new AddObservationParams
        {
            SessionId = SessionId,
            Title     = "JWT decision",
            Content   = "We use RS256 for JWT tokens",
            Type      = "decision",
            Project   = "test-proj",
            FilePath  = "src/Auth/JwtBearer.cs",
            Symbol    = "JwtBearerHandler",
            Namespace = "Engram.Auth",
        });

        var result = await _tools.MemRecallForFile("src/Auth/JwtBearer.cs", project: "test-proj");

        Assert.Contains("JWT decision", result);
        Assert.Contains("JwtBearerHandler", result);
        Assert.Contains("Engram.Auth", result);
    }

    // ─── FR-001: Save with code metadata → Recall by module ──────────────────

    [Fact]
    public async Task SaveWithCodeMetadata_RecallByModule_ReturnsMatch()
    {
        await SeedSession();
        await _store.AddObservationAsync(new AddObservationParams
        {
            SessionId = SessionId,
            Title     = "Store interface decision",
            Content   = "IStore uses a generic ReadAsync method",
            Type      = "decision",
            Project   = "test-proj",
            Namespace = "Engram.Store",
        });

        var result = await _tools.MemRecallForModule("Engram.Store", project: "test-proj");

        Assert.Contains("Store interface decision", result);
        Assert.Contains("Engram.Store", result);
    }

    // ─── FR-001: Save with code metadata → Recall by symbol ───────────────────

    [Fact]
    public async Task SaveWithCodeMetadata_RecallBySymbol_ReturnsMatch()
    {
        await SeedSession();
        await _store.AddObservationAsync(new AddObservationParams
        {
            SessionId = SessionId,
            Title     = "IStore interface decision",
            Content   = "IStore is the primary abstraction for Engram stores",
            Type      = "architecture",
            Project   = "test-proj",
            Symbol    = "IStore",
        });

        var result = await _tools.MemRecallForSymbol("IStore", project: "test-proj");

        Assert.Contains("IStore interface decision", result);
        Assert.Contains("IStore", result);
    }

    // ─── Graceful Degradation: no memories for file ───────────────────────────

    [Fact]
    public async Task GracefulDegradation_NoMemoriesForFile_ReturnsDescriptiveMessage()
    {
        await SeedSession();
        var result = await _tools.MemRecallForFile("src/NonExistent/File.cs", project: "test-proj");
        Assert.Contains("No memories found for file path", result);
    }

    // ─── Graceful Degradation: no memories for module ─────────────────────────

    [Fact]
    public async Task GracefulDegradation_NoMemoriesForModule_ReturnsDescriptiveMessage()
    {
        await SeedSession();
        var result = await _tools.MemRecallForModule("NonExistent.Module", project: "test-proj");
        Assert.Contains("No memories found for module", result);
    }

    // ─── Graceful Degradation: no memories for symbol ─────────────────────────

    [Fact]
    public async Task GracefulDegradation_NoMemoriesForSymbol_ReturnsDescriptiveMessage()
    {
        await SeedSession();
        var result = await _tools.MemRecallForSymbol("NonExistentSymbol", project: "test-proj");
        Assert.Contains("No memories found for symbol", result);
    }
}
