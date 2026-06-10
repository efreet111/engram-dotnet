using Engram.Cli;
using Engram.Obsidian;
using Engram.Store;
using Xunit;

namespace Engram.Cli.Tests;

/// <summary>
/// Tests for watch mode in obsidian-export.
/// ENG-208 Phase 9.
/// </summary>
public class WatchModeTests
{
    private readonly string _tempDir;

    public WatchModeTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    // ─── ParseInterval ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("30s", 30)]
    [InlineData("5m", 300)]
    [InlineData("1h", 3600)]
    [InlineData("60s", 60)]
    public void ParseInterval_ValidFormats_Parses(string input, int expectedSeconds)
    {
        var result = WatchIntervalParser.Parse(input);

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), result);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("99x")]
    [InlineData("-1s")]
    public void ParseInterval_InvalidFormat_Throws(string input)
    {
        Assert.Throws<ArgumentException>(() => WatchIntervalParser.Parse(input));
    }

    [Fact]
    public void ParseInterval_Empty_ReturnsDefault()
    {
        // Empty returns default 60s
        var result = WatchIntervalParser.Parse("");
        Assert.Equal(TimeSpan.FromSeconds(60), result);
    }

    // ─── WatchLoop Tests ─────────────────────────────────────────────────

    [Fact]
    public async Task WatchLoop_RunsInitialExport()
    {
        var store = new MockStoreReaderForWatch
        {
            ExportData = new ExportData
            {
                Sessions = [new Session { Id = "sess-1", Project = "eng" }],
                Observations =
                [
                    new Observation { Id = 1, SessionId = "sess-1", Type = "bugfix", Title = "Fix", Content = "fix", Scope = "team", CreatedAt = "2026-01-01T10:00:00Z", UpdatedAt = "2026-01-01T10:00:00Z", Project = "eng" },
                ],
                Prompts = [],
            },
        };

        var config = new WatchConfig
        {
            VaultPath = _tempDir,
            Project = null,
            Interval = TimeSpan.FromMilliseconds(100), // Very short for test
            InitialSince = null,
            StoreReader = store,
        };

        var cts = new CancellationTokenSource();
        cts.CancelAfter(250);

        // Run watch loop for a short duration
        await WatchLoop.RunAsync(config, cts.Token);

        // Should have exported at least once
        Assert.True(store.ExportCallCount >= 1, $"Expected at least 1 export, got {store.ExportCallCount}");
    }

    [Fact]
    public async Task WatchLoop_TicksAfterInterval()
    {
        var store = new MockStoreReaderForWatch
        {
            ExportData = new ExportData
            {
                Sessions = [new Session { Id = "sess-1", Project = "eng" }],
                Observations =
                [
                    new Observation { Id = 1, SessionId = "sess-1", Type = "bugfix", Title = "Fix", Content = "fix", Scope = "team", CreatedAt = "2026-01-01T10:00:00Z", UpdatedAt = "2026-01-01T10:00:00Z", Project = "eng" },
                ],
                Prompts = [],
            },
        };

        var config = new WatchConfig
        {
            VaultPath = _tempDir,
            Project = null,
            Interval = TimeSpan.FromMilliseconds(200), // 200ms between ticks
            InitialSince = null,
            StoreReader = store,
        };

        var cts = new CancellationTokenSource();

        // Cancel after ~500ms (should trigger 2+ ticks)
        cts.CancelAfter(500);

        await WatchLoop.RunAsync(config, cts.Token);

        // Should have exported at least twice
        Assert.True(store.ExportCallCount >= 2, $"Expected at least 2 exports, got {store.ExportCallCount}");
    }

    [Fact]
    public async Task WatchLoop_ContinuesAfterCycleError()
    {
        var store = new MockStoreReaderForWatch
        {
            ExportData = new ExportData
            {
                Sessions = [],
                Observations = [],
                Prompts = [],
            },
            ThrowOnCall = 1, // Throw on first call
        };

        var config = new WatchConfig
        {
            VaultPath = _tempDir,
            Project = null,
            Interval = TimeSpan.FromMilliseconds(100),
            InitialSince = null,
            StoreReader = store,
        };

        var cts = new CancellationTokenSource();
        cts.CancelAfter(300);

        // Should not throw - should continue after error
        await WatchLoop.RunAsync(config, cts.Token);

        // Should have attempted at least 2 exports
        Assert.True(store.ExportCallCount >= 2);
    }

    [Fact]
    public async Task WatchLoop_GracefulShutdown_PersistsState()
    {
        var store = new MockStoreReaderForWatch
        {
            ExportData = new ExportData
            {
                Sessions = [new Session { Id = "sess-1", Project = "eng" }],
                Observations =
                [
                    new Observation { Id = 1, SessionId = "sess-1", Type = "bugfix", Title = "Fix", Content = "fix", Scope = "team", CreatedAt = "2026-01-01T10:00:00Z", UpdatedAt = "2026-01-01T10:00:00Z", Project = "eng" },
                ],
                Prompts = [],
            },
        };

        var config = new WatchConfig
        {
            VaultPath = _tempDir,
            Project = null,
            Interval = TimeSpan.FromSeconds(60),
            InitialSince = null,
            StoreReader = store,
        };

        var cts = new CancellationTokenSource();
        cts.CancelAfter(50);

        // Cancel after initial export completes
        await WatchLoop.RunAsync(config, cts.Token);

        // State file should exist after shutdown
        var stateFile = StateFile.ResolveStatePath(_tempDir, null);
        Assert.True(File.Exists(stateFile), "State file should exist after graceful shutdown");

        // Verify state has last_export_at
        var state = StateFile.ReadState(stateFile);
        Assert.NotEmpty(state.LastExportAt);
    }

    [Fact]
    public async Task WatchLoop_UsesLastSeq_WhenServerReachable()
    {
        var store = new MockStoreReaderForWatch
        {
            ExportData = new ExportData
            {
                Sessions = [new Session { Id = "sess-1", Project = "eng" }],
                Observations =
                [
                    new Observation { Id = 1, SessionId = "sess-1", Type = "bugfix", Title = "Fix", Content = "fix", Scope = "team", CreatedAt = "2026-01-01T10:00:00Z", UpdatedAt = "2026-01-01T10:00:00Z", Project = "eng" },
                ],
                Prompts = [],
            },
            // Return data with next_seq
            NextSeq = 100,
            HasMore = false,
        };

        // Pre-populate state with last_seq
        var stateFile = StateFile.ResolveStatePath(_tempDir, null);
        var initialState = new Engram.Obsidian.SyncState
        {
            LastSeq = 50,
            LastExportAt = "2026-01-01T00:00:00Z",
            Files = [],
            SessionHubs = [],
            TopicHubs = [],
        };
        StateFile.WriteState(stateFile, initialState);

        var config = new WatchConfig
        {
            VaultPath = _tempDir,
            Project = null,
            Interval = TimeSpan.FromMilliseconds(100),
            InitialSince = null,
            StoreReader = store,
        };

        var cts = new CancellationTokenSource();
        cts.CancelAfter(250);

        await WatchLoop.RunAsync(config, cts.Token);

        // Verify state was updated with new seq (strengthened assertion - verifies exact value)
        var state = StateFile.ReadState(stateFile);
        Assert.Equal(100, state.LastSeq);
    }

    [Fact]
    public async Task WatchLoop_PersistsLastSeqBetweenCycles()
    {
        var store = new MockStoreReaderForWatch
        {
            ExportData = new ExportData
            {
                Sessions = [new Session { Id = "sess-1", Project = "eng" }],
                Observations =
                [
                    new Observation { Id = 1, SessionId = "sess-1", Type = "bugfix", Title = "Fix", Content = "fix", Scope = "team", CreatedAt = "2026-01-01T10:00:00Z", UpdatedAt = "2026-01-01T10:00:00Z", Project = "eng" },
                ],
                Prompts = [],
            },
            NextSeq = 100,
            HasMore = false,
        };

        // Pre-populate state with last_seq
        var stateFile = StateFile.ResolveStatePath(_tempDir, null);
        var initialState = new Engram.Obsidian.SyncState
        {
            LastSeq = 50,
            LastExportAt = "2026-01-01T00:00:00Z",
            Files = [],
            SessionHubs = [],
            TopicHubs = [],
        };
        StateFile.WriteState(stateFile, initialState);

        var config = new WatchConfig
        {
            VaultPath = _tempDir,
            Project = null,
            Interval = TimeSpan.FromMilliseconds(50),
            InitialSince = null,
            StoreReader = store,
        };

        var cts = new CancellationTokenSource();
        cts.CancelAfter(400); // ~8 cycles at 50ms each

        await WatchLoop.RunAsync(config, cts.Token);

        // After multiple cycles, state.LastSeq should be 100 (from mock's NextSeq)
        // AND it should be persisted to disk (not just in memory)
        var state = StateFile.ReadState(stateFile);
        Assert.Equal(100, state.LastSeq);
        Assert.NotEmpty(state.LastExportAt); // also verify the timestamp was updated
    }

    [Fact]
    public async Task WatchLoop_FallsBackToLastExportAt_WhenOffline()
    {
        var store = new MockStoreReaderForWatch
        {
            ExportData = new ExportData
            {
                Sessions = [new Session { Id = "sess-1", Project = "eng" }],
                Observations =
                [
                    new Observation { Id = 1, SessionId = "sess-1", Type = "bugfix", Title = "Fix", Content = "fix", Scope = "team", CreatedAt = "2026-01-01T10:00:00Z", UpdatedAt = "2026-01-01T10:00:00Z", Project = "eng" },
                ],
                Prompts = [],
            },
            ThrowOnExportSince = true, // Simulate server unreachable
        };

        // Pre-populate state with last_export_at (but no last_seq)
        var stateFile = StateFile.ResolveStatePath(_tempDir, null);
        var initialState = new Engram.Obsidian.SyncState
        {
            LastExportAt = "2026-01-01T00:00:00Z",
            Files = [],
            SessionHubs = [],
            TopicHubs = [],
        };
        StateFile.WriteState(stateFile, initialState);

        var config = new WatchConfig
        {
            VaultPath = _tempDir,
            Project = null,
            Interval = TimeSpan.FromMilliseconds(100),
            InitialSince = null,
            StoreReader = store,
        };

        var cts = new CancellationTokenSource();
        cts.CancelAfter(250);

        // Should not throw - should fallback to timestamp
        await WatchLoop.RunAsync(config, cts.Token);

        // Should have tried to export
        Assert.True(store.ExportCallCount >= 1);
    }
}

/// <summary>
/// Mock IObsidianStoreReader for watch mode tests.
/// </summary>
public class MockStoreReaderForWatch : IObsidianStoreReader
{
    public ExportData ExportData { get; set; } = new();
    public Stats Stats { get; set; } = new();
    public int ExportCallCount { get; private set; }
    public long? NextSeq { get; set; }
    public bool HasMore { get; set; }
    public int ThrowOnCall { get; set; }
    public bool ThrowOnExportSince { get; set; }

    public Task<ExportData> ExportAsync()
    {
        ExportCallCount++;
        if (ThrowOnCall > 0 && ExportCallCount >= ThrowOnCall)
            throw new InvalidOperationException("Simulated error");
        // Copy NextSeq to ExportData before returning
        if (NextSeq.HasValue)
            ExportData.NextSeq = NextSeq.Value;
        ExportData.HasMore = HasMore;
        return Task.FromResult(ExportData);
    }

    public Task<Stats> StatsAsync() => Task.FromResult(Stats);

    public Task<ExportData> ExportProjectAsync(string project) => ExportAsync();

    public Task<ExportData> ExportSinceAsync(string? project, long afterSeq, int limit)
    {
        if (ThrowOnExportSince)
            throw new InvalidOperationException("Server unreachable");
        // Copy NextSeq to ExportData before returning
        if (NextSeq.HasValue)
            ExportData.NextSeq = NextSeq.Value;
        ExportData.HasMore = HasMore;
        return Task.FromResult(ExportData);
    }
}