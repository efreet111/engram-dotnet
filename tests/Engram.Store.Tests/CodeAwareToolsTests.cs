using Engram.Store;
using Xunit;

namespace Engram.Store.Tests;

/// <summary>
/// HU-064: Code-Context Query Tools tests.
/// Tests GetMemoriesByFilePathAsync, GetMemoriesByModuleAsync, GetMemoriesBySymbolAsync
/// </summary>
public class CodeAwareToolsTests : IDisposable
{
    private readonly SqliteStore _store;
    private readonly string      _tempDir;
    private const string SessionId = "test-session-codeaware";

    public CodeAwareToolsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "engram-tests-codeaware", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        var cfg = new StoreConfig { DataDir = _tempDir };
        _store = new SqliteStore(cfg);
    }

    public void Dispose()
    {
        _store.Dispose();
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    private async Task SeedSession()
        => await _store.CreateSessionAsync(SessionId, "test-project", "/tmp");

    private async Task<long> SeedObservation(
        string title    = "Test observation",
        string content  = "This is some content",
        string type     = "manual",
        string? project = "test-project",
        string? filePath = null,
        string? symbol   = null,
        string? ns      = null)
    {
        return await _store.AddObservationAsync(new AddObservationParams
        {
            SessionId = SessionId,
            Title     = title,
            Content   = content,
            Type      = type,
            Project   = project,
            FilePath  = filePath,
            Symbol    = symbol,
            Namespace = ns,
        });
    }

    // ─── GetMemoriesByFilePathAsync ─────────────────────────────────────────

    [Fact]
    public async Task GetMemoriesByFilePathAsync_ExactMatch_ReturnsMatching()
    {
        await SeedSession();
        await SeedObservation("Decision about JWT", "We use RS256", "decision", filePath: "src/Auth/JwtBearer.cs");
        await SeedObservation("Bug in auth", "JWT validation fails", "bugfix", filePath: "src/Auth/AuthHandler.cs");
        await SeedObservation("Unrelated", "Some other file", "manual", filePath: "src/Other/File.cs");

        var results = await _store.GetMemoriesByFilePathAsync("src/Auth/JwtBearer.cs", null, null, 10);

        Assert.Single(results);
        Assert.Equal("Decision about JWT", results[0].Observation.Title);
        Assert.Equal("src/Auth/JwtBearer.cs", results[0].Observation.FilePath);
    }

    [Fact]
    public async Task GetMemoriesByFilePathAsync_PrefixMatch_ReturnsAllUnderPath()
    {
        await SeedSession();
        await SeedObservation("Auth decision 1", "Content 1", "decision", filePath: "src/Auth/JwtBearer.cs");
        await SeedObservation("Auth decision 2", "Content 2", "decision", filePath: "src/Auth/Claims/ClaimsProcessor.cs");
        await SeedObservation("Other file", "Content 3", "manual", filePath: "src/Other/File.cs");

        var results = await _store.GetMemoriesByFilePathAsync("src/Auth/", null, null, 10);

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task GetMemoriesByFilePathAsync_NoMatch_ReturnsEmpty()
    {
        await SeedSession();
        await SeedObservation("Some decision", "Content", "decision", filePath: "src/Unrelated/File.cs");

        var results = await _store.GetMemoriesByFilePathAsync("src/Auth/JwtBearer.cs", null, null, 10);

        Assert.Empty(results);
    }

    [Fact]
    public async Task GetMemoriesByFilePathAsync_WithTypeFilter_ReturnsFiltered()
    {
        await SeedSession();
        await SeedObservation("JWT decision", "We use RS256", "decision", filePath: "src/Auth/JwtBearer.cs");
        await SeedObservation("JWT bug", "Fix validation", "bugfix", filePath: "src/Auth/JwtBearer.cs");

        var results = await _store.GetMemoriesByFilePathAsync("src/Auth/JwtBearer.cs", null, "decision", 10);

        Assert.Single(results);
        Assert.Equal("decision", results[0].Observation.Type);
    }

    [Fact]
    public async Task GetMemoriesByFilePathAsync_WithProjectFilter_ReturnsFiltered()
    {
        await SeedSession();
        await SeedObservation("JWT decision proj-a", "Content", "decision", project: "proj-a", filePath: "src/Auth/JwtBearer.cs");
        await SeedObservation("JWT decision proj-b", "Content", "decision", project: "proj-b", filePath: "src/Auth/JwtBearer.cs");

        var results = await _store.GetMemoriesByFilePathAsync("src/Auth/JwtBearer.cs", "proj-a", null, 10);

        Assert.Single(results);
        Assert.Equal("proj-a", results[0].Observation.Project);
    }

    [Fact]
    public async Task GetMemoriesByFilePathAsync_GracefulDegradation_EmptyProject()
    {
        await SeedSession();
        await SeedObservation("Some decision", "Content", "decision", filePath: "src/Auth/JwtBearer.cs");

        var results = await _store.GetMemoriesByFilePathAsync("src/Auth/JwtBearer.cs", null, null, 10);

        Assert.Single(results);
    }

    // ─── GetMemoriesByModuleAsync ────────────────────────────────────────────

    [Fact]
    public async Task GetMemoriesByModuleAsync_ExactNamespace_ReturnsMatching()
    {
        await SeedSession();
        await SeedObservation("Store decision", "Use repository pattern", "architecture", ns: "Engram.Store");
        await SeedObservation("Auth decision", "Use JWT", "decision", ns: "Engram.Auth");
        await SeedObservation("Unrelated", "Other", "manual", ns: "Other.Module");

        var results = await _store.GetMemoriesByModuleAsync("Engram.Store", null, null, 10);

        Assert.Single(results);
        Assert.Equal("Store decision", results[0].Observation.Title);
        Assert.Equal("Engram.Store", results[0].Observation.Namespace);
    }

    [Fact]
    public async Task GetMemoriesByModuleAsync_PrefixMatch_ReturnsAllUnderModule()
    {
        await SeedSession();
        await SeedObservation("Store decision", "Content", "architecture", ns: "Engram.Store");
        await SeedObservation("Store sub decision", "Content", "decision", ns: "Engram.Store.Repositories");
        await SeedObservation("Auth decision", "Content", "decision", ns: "Engram.Auth");

        var results = await _store.GetMemoriesByModuleAsync("Engram.Store", null, null, 10);

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task GetMemoriesByModuleAsync_NoMatch_ReturnsEmpty()
    {
        await SeedSession();
        await SeedObservation("Some decision", "Content", "decision", ns: "Other.Module");

        var results = await _store.GetMemoriesByModuleAsync("Engram.Store", null, null, 10);

        Assert.Empty(results);
    }

    [Fact]
    public async Task GetMemoriesByModuleAsync_WithTypeFilter_ReturnsFiltered()
    {
        await SeedSession();
        await SeedObservation("Store architecture", "Repository pattern", "architecture", ns: "Engram.Store");
        await SeedObservation("Store bug", "Fix null ref", "bugfix", ns: "Engram.Store");

        var results = await _store.GetMemoriesByModuleAsync("Engram.Store", null, "architecture", 10);

        Assert.Single(results);
        Assert.Equal("architecture", results[0].Observation.Type);
    }

    // ─── GetMemoriesBySymbolAsync ────────────────────────────────────────────

    [Fact]
    public async Task GetMemoriesBySymbolAsync_ExactMatch_ReturnsMatching()
    {
        await SeedSession();
        await SeedObservation("IStore interface decision", "Use IStore abstraction", "architecture", symbol: "IStore");
        await SeedObservation("Store implementation", "SqliteStore", "manual", symbol: "SqliteStore");
        await SeedObservation("Unrelated", "Other", "manual", symbol: "OtherClass");

        var results = await _store.GetMemoriesBySymbolAsync("IStore", null, 10);

        Assert.Single(results);
        Assert.Equal("IStore interface decision", results[0].Observation.Title);
        Assert.Equal("IStore", results[0].Observation.Symbol);
    }

    [Fact]
    public async Task GetMemoriesBySymbolAsync_NoMatch_ReturnsEmpty()
    {
        await SeedSession();
        await SeedObservation("Some decision", "Content", "decision", symbol: "SomeSymbol");

        var results = await _store.GetMemoriesBySymbolAsync("NonExistentSymbol", null, 10);

        Assert.Empty(results);
    }

    [Fact]
    public async Task GetMemoriesBySymbolAsync_WithProjectFilter_ReturnsFiltered()
    {
        await SeedSession();
        await SeedObservation("IStore decision proj-a", "Content", "architecture", project: "proj-a", symbol: "IStore");
        await SeedObservation("IStore decision proj-b", "Content", "architecture", project: "proj-b", symbol: "IStore");

        var results = await _store.GetMemoriesBySymbolAsync("IStore", "proj-a", 10);

        Assert.Single(results);
        Assert.Equal("proj-a", results[0].Observation.Project);
    }

    [Fact]
    public async Task GetMemoriesBySymbolAsync_MultipleObservationsForSymbol_ReturnsAll()
    {
        await SeedSession();
        await SeedObservation("IStore decision v1", "Original decision", "architecture", symbol: "IStore");
        await SeedObservation("IStore decision v2", "Updated decision", "decision", symbol: "IStore");

        var results = await _store.GetMemoriesBySymbolAsync("IStore", null, 10);

        Assert.Equal(2, results.Count);
    }

    // ─── Edge cases ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetMemoriesByFilePathAsync_Limit_RespectsLimit()
    {
        await SeedSession();
        for (int i = 0; i < 20; i++)
            await SeedObservation($"Decision {i}", $"Content {i}", "decision", filePath: "src/Auth/File.cs");

        var results = await _store.GetMemoriesByFilePathAsync("src/Auth/File.cs", null, null, 5);

        Assert.Equal(5, results.Count);
    }

    [Fact]
    public async Task GetMemoriesBySymbolAsync_Limit_RespectsLimit()
    {
        await SeedSession();
        for (int i = 0; i < 15; i++)
            await SeedObservation($"Decision {i}", $"Content {i}", "decision", symbol: "TestSymbol");

        var results = await _store.GetMemoriesBySymbolAsync("TestSymbol", null, 3);

        Assert.Equal(3, results.Count);
    }

    [Fact]
    public async Task GetMemoriesByFilePathAsync_DeletedObservation_Excluded()
    {
        await SeedSession();
        var id = await SeedObservation("Active decision", "Content", "decision", filePath: "src/Auth/JwtBearer.cs");

        await _store.DeleteObservationAsync(id);

        var results = await _store.GetMemoriesByFilePathAsync("src/Auth/JwtBearer.cs", null, null, 10);

        Assert.Empty(results);
    }
}
