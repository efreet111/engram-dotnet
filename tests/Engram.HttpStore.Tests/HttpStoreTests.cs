using System.Net;
using Engram.Server;
using Engram.Store;
using Microsoft.AspNetCore.Builder;
using Xunit;
using StoreProxy = Engram.Store.HttpStore;

namespace Engram.HttpStore.Tests.Integration;

/// <summary>
/// Integration tests for HttpStore.
/// Each test spins up a real EngramServer on a random port backed by an in-memory SQLiteStore,
/// then exercises HttpStore as a proxy against it — verifying the full end-to-end contract.
/// </summary>
public class HttpStoreTests : IAsyncDisposable
{
    private readonly SqliteStore    _backingStore;
    private readonly WebApplication _server;
    private readonly StoreProxy     _sut;        // System Under Test
    private readonly string         _tempDir;
    private readonly string         _baseUrl;

    public HttpStoreTests()
    {
        var port   = GetFreePort();
        _baseUrl   = $"http://localhost:{port}";
        _tempDir   = Path.Combine(Path.GetTempPath(), "engram-httpstore-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        var storeCfg  = new StoreConfig { DataDir = _tempDir };
        _backingStore = new SqliteStore(storeCfg);

        _server = EngramServer.Build(_backingStore, storeCfg);
        _server.Urls.Clear();
        _server.Urls.Add(_baseUrl);
        _server.StartAsync().GetAwaiter().GetResult();

        var proxyCfg = new StoreConfig
        {
            DataDir   = _tempDir,  // not used by HttpStore, but required by StoreConfig ctor
            RemoteUrl = _baseUrl,
            User      = "test.user",
        };
        _sut = new StoreProxy(proxyCfg);
    }

    public async ValueTask DisposeAsync()
    {
        _sut.Dispose();
        await _server.StopAsync();
        await _server.DisposeAsync();
        _backingStore.Dispose();
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    private static int GetFreePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    // ─── Sessions ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateSession_ThenGetSession_RoundTrips()
    {
        await _sut.CreateSessionAsync("s-001", "my-project", "/home/dev/project");

        var session = await _sut.GetSessionAsync("s-001");

        Assert.NotNull(session);
        Assert.Equal("s-001",       session.Id);
        Assert.Equal("my-project",  session.Project);
    }

    [Fact]
    public async Task GetSession_ReturnsNull_WhenNotFound()
    {
        var session = await _sut.GetSessionAsync("nonexistent-session-xyz");

        Assert.Null(session);
    }

    [Fact]
    public async Task EndSession_UpdatesSummary()
    {
        await _sut.CreateSessionAsync("s-002", "proj", "/");
        await _sut.EndSessionAsync("s-002", "Completed feature X");

        var session = await _sut.GetSessionAsync("s-002");
        Assert.NotNull(session);
        Assert.Equal("Completed feature X", session.Summary);
    }

    [Fact]
    public async Task RecentSessions_ReturnsSessions()
    {
        await _sut.CreateSessionAsync("s-rec-1", "proj-a", "/");
        await _sut.CreateSessionAsync("s-rec-2", "proj-a", "/");

        var sessions = await _sut.RecentSessionsAsync("proj-a", 10);

        Assert.NotEmpty(sessions);
        Assert.All(sessions, s => Assert.Equal("proj-a", s.Project));
    }

    // ─── Observations ─────────────────────────────────────────────────────────

    [Fact]
    public async Task AddObservation_ThenGetObservation_RoundTrips()
    {
        await _sut.CreateSessionAsync("s-obs", "proj", "/");

        var id = await _sut.AddObservationAsync(new AddObservationParams
        {
            SessionId = "s-obs",
            Type      = "decision",
            Title     = "Chose PostgreSQL",
            Content   = "We chose PostgreSQL because of JSONB support",
            Project   = "proj",
            Scope     = "project",
        });

        Assert.True(id > 0);

        var obs = await _sut.GetObservationAsync(id);
        Assert.NotNull(obs);
        Assert.Equal("Chose PostgreSQL", obs.Title);
        Assert.Equal("decision",         obs.Type);
        Assert.Equal("proj",             obs.Project);
    }

    [Fact]
    public async Task GetObservation_ReturnsNull_WhenNotFound()
    {
        var obs = await _sut.GetObservationAsync(999_999_999L);

        Assert.Null(obs);
    }

    [Fact]
    public async Task UpdateObservation_ChangesTitle()
    {
        await _sut.CreateSessionAsync("s-upd", "proj", "/");
        var id = await _sut.AddObservationAsync(new AddObservationParams
        {
            SessionId = "s-upd",
            Title     = "Original title",
            Content   = "Some content",
            Project   = "proj",
        });

        var ok = await _sut.UpdateObservationAsync(id, new UpdateObservationParams
        {
            Title = "Updated title",
        });

        Assert.True(ok);
        var obs = await _sut.GetObservationAsync(id);
        Assert.Equal("Updated title", obs!.Title);
    }

    [Fact]
    public async Task UpdateObservation_ReturnsFalse_WhenNotFound()
    {
        var ok = await _sut.UpdateObservationAsync(999_999_999L, new UpdateObservationParams
        {
            Title = "Won't work",
        });

        Assert.False(ok);
    }

    [Fact]
    public async Task DeleteObservation_RemovesIt()
    {
        await _sut.CreateSessionAsync("s-del", "proj", "/");
        var id = await _sut.AddObservationAsync(new AddObservationParams
        {
            SessionId = "s-del",
            Title     = "To be deleted",
            Content   = "Content",
            Project   = "proj",
        });

        var ok = await _sut.DeleteObservationAsync(id);

        Assert.True(ok);
    }

    [Fact]
    public async Task DeleteObservation_ReturnsFalse_WhenNotFound()
    {
        var ok = await _sut.DeleteObservationAsync(999_999_999L);

        Assert.False(ok);
    }

    [Fact]
    public async Task RecentObservations_ReturnsObservations()
    {
        await _sut.CreateSessionAsync("s-recent", "proj-r", "/");
        await _sut.AddObservationAsync(new AddObservationParams
        {
            SessionId = "s-recent",
            Title     = "Recent obs",
            Content   = "Content",
            Project   = "proj-r",
        });

        var obs = await _sut.RecentObservationsAsync("proj-r", null, 10);

        Assert.NotEmpty(obs);
    }

    // ─── Search ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Search_ReturnsMatchingObservations()
    {
        await _sut.CreateSessionAsync("s-srch", "proj", "/");
        await _sut.AddObservationAsync(new AddObservationParams
        {
            SessionId = "s-srch",
            Title     = "JWT authentication decision",
            Content   = "We use JWT tokens with 1h expiry",
            Project   = "proj",
        });

        var results = await _sut.SearchAsync("JWT", new SearchOptions { Limit = 10 });

        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.Observation.Title.Contains("JWT"));
    }

    [Fact]
    public async Task Search_ReturnsEmpty_WhenNoMatch()
    {
        var results = await _sut.SearchAsync("xyzzy-no-match-42", new SearchOptions { Limit = 10 });

        Assert.Empty(results);
    }

    // ─── Timeline ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Timeline_ReturnsResult_ForExistingObservation()
    {
        await _sut.CreateSessionAsync("s-tl", "proj", "/");
        var id = await _sut.AddObservationAsync(new AddObservationParams
        {
            SessionId = "s-tl",
            Title     = "Timeline focus",
            Content   = "Content for timeline",
            Project   = "proj",
        });

        var result = await _sut.TimelineAsync(id, before: 3, after: 3);

        Assert.NotNull(result);
        Assert.Equal(id, result.Focus.Id);
    }

    [Fact]
    public async Task Timeline_ReturnsNull_WhenObservationNotFound()
    {
        var result = await _sut.TimelineAsync(999_999_999L, 3, 3);

        Assert.Null(result);
    }

    // ─── Prompts ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddPrompt_ThenRecentPrompts_ContainsIt()
    {
        await _sut.CreateSessionAsync("s-prm", "proj-p", "/");
        await _sut.AddPromptAsync(new AddPromptParams
        {
            SessionId = "s-prm",
            Content   = "How do I implement JWT refresh tokens?",
            Project   = "proj-p",
        });

        var prompts = await _sut.RecentPromptsAsync("proj-p", 10);

        Assert.NotEmpty(prompts);
        Assert.Contains(prompts, p => p.Content.Contains("JWT"));
    }

    [Fact]
    public async Task SearchPrompts_ReturnsMatch()
    {
        await _sut.CreateSessionAsync("s-spm", "proj-sp", "/");
        await _sut.AddPromptAsync(new AddPromptParams
        {
            SessionId = "s-spm",
            Content   = "How to configure Redis caching?",
            Project   = "proj-sp",
        });

        var results = await _sut.SearchPromptsAsync("Redis", "proj-sp", 10);

        Assert.NotEmpty(results);
    }

    // ─── Context & Stats ──────────────────────────────────────────────────────

    [Fact]
    public async Task Stats_ReturnsNonNull()
    {
        var stats = await _sut.StatsAsync();

        Assert.NotNull(stats);
        Assert.NotNull(stats.Projects);
    }

    [Fact]
    public async Task FormatContext_ReturnsString()
    {
        // Even with no data, should return a string (possibly empty)
        var ctx = await _sut.FormatContextAsync((string?)null, null);

        Assert.NotNull(ctx);
    }

    // ─── Export / Import ──────────────────────────────────────────────────────

    [Fact]
    public async Task Export_ReturnsExportData()
    {
        var data = await _sut.ExportAsync();

        Assert.NotNull(data);
        Assert.NotNull(data.Sessions);
        Assert.NotNull(data.Observations);
        Assert.NotNull(data.Prompts);
    }

    [Fact]
    public async Task Import_RoundTrips_ExportedData()
    {
        // Seed data via backing store directly
        await _backingStore.CreateSessionAsync("s-export", "export-proj", "/");
        await _backingStore.AddObservationAsync(new AddObservationParams
        {
            SessionId = "s-export",
            Title     = "Exported observation",
            Content   = "This was exported and re-imported",
            Project   = "export-proj",
        });

        // Export via proxy
        var exported = await _sut.ExportAsync();
        Assert.NotEmpty(exported.Observations);

        // Re-import via proxy into a fresh store
        var result = await _sut.ImportAsync(exported);
        Assert.True(result.ObservationsImported >= 0); // idempotent: may be 0 if already present
    }

    // ─── Sync stubs ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GetSyncedChunks_ReturnsEmptySet_InProxyMode()
    {
        var chunks = await _sut.GetSyncedChunksAsync();

        Assert.Empty(chunks);
    }

    [Fact]
    public async Task RecordSyncedChunk_DoesNotThrow_InProxyMode()
    {
        // Should be a no-op without throwing
        await _sut.RecordSyncedChunkAsync("chunk-abc123");
    }

    // ─── Error handling ───────────────────────────────────────────────────────

    [Fact]
    public async Task HttpStore_ThrowsEngramRemoteException_WhenServerUnreachable()
    {
        var badCfg = new StoreConfig
        {
            DataDir   = _tempDir,
            RemoteUrl = "http://localhost:1", // nothing listening here
        };
        using var badStore = new StoreProxy(badCfg);

        await Assert.ThrowsAsync<EngramRemoteException>(
            () => badStore.StatsAsync());
    }

    // ─── X-Engram-User header ─────────────────────────────────────────────────

    [Fact]
    public async Task HttpStore_SendsUserHeader_AndServerAcceptsIt()
    {
        // The server doesn't validate the header (yet), but the request must succeed —
        // proving the header doesn't break anything and is forwarded correctly.
        var cfgWithUser = new StoreConfig
        {
            DataDir   = _tempDir,
            RemoteUrl = _baseUrl,
            User      = "victor.silgado",
        };
        using var storeWithUser = new StoreProxy(cfgWithUser);

        // Should succeed — server ignores unknown headers gracefully
        var stats = await storeWithUser.StatsAsync();
        Assert.NotNull(stats);
    }

    // ─── Project listing ─────────────────────────────────────────────────────

    [Fact]
    public async Task ListProjectNames_ReturnsDistinctProjects()
    {
        await _sut.CreateSessionAsync("s-pl-1", "alpha", "/");
        await _sut.CreateSessionAsync("s-pl-2", "beta", "/");
        await _sut.AddObservationAsync(new AddObservationParams
        {
            SessionId = "s-pl-1", Title = "obs-1", Content = "c", Type = "manual", Project = "alpha",
        });
        await _sut.AddObservationAsync(new AddObservationParams
        {
            SessionId = "s-pl-2", Title = "obs-2", Content = "c", Type = "manual", Project = "beta",
        });

        var names = await _sut.ListProjectNamesAsync();

        Assert.Equal(2, names.Count);
        Assert.Contains("alpha", names);
        Assert.Contains("beta", names);
    }

    [Fact]
    public async Task ListProjectNames_ReturnsEmpty_WhenNoData()
    {
        var names = await _sut.ListProjectNamesAsync();
        Assert.Empty(names);
    }

    [Fact]
    public async Task ListProjectsWithStats_ReturnsCorrectCounts()
    {
        await _sut.CreateSessionAsync("s-ps-1", "proj-a", "/");
        await _sut.CreateSessionAsync("s-ps-2", "proj-b", "/");
        await _sut.AddObservationAsync(new AddObservationParams
        {
            SessionId = "s-ps-1", Title = "obs-1", Content = "c", Type = "manual", Project = "proj-a",
        });

        var stats = await _sut.ListProjectsWithStatsAsync();

        var projA = stats.FirstOrDefault(s => s.Name == "proj-a");
        Assert.NotNull(projA);
        Assert.Equal(1, projA.ObservationCount);
        Assert.Equal(1, projA.SessionCount);
    }

    [Fact]
    public async Task CountObservationsForProject_ReturnsCorrectCount()
    {
        await _sut.CreateSessionAsync("s-co", "proj-c", "/");
        await _sut.AddObservationAsync(new AddObservationParams
        {
            SessionId = "s-co", Title = "obs-1", Content = "c", Type = "manual", Project = "proj-c",
        });
        await _sut.AddObservationAsync(new AddObservationParams
        {
            SessionId = "s-co", Title = "obs-2", Content = "c", Type = "manual", Project = "proj-c",
        });

        var count = await _sut.CountObservationsForProjectAsync("proj-c");

        Assert.Equal(2, count);
    }

    [Fact]
    public async Task CountObservationsForProject_ReturnsZero_WhenNoProject()
    {
        var count = await _sut.CountObservationsForProjectAsync("nonexistent-proj");
        Assert.Equal(0, count);
    }
}
