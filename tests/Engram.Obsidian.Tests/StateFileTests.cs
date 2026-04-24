using Xunit;

namespace Engram.Obsidian.Tests;

public class StateFileTests
{
    private readonly string _tempDir;

    public StateFileTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void ReadState_MissingFile_ReturnsEmptyState_NoError()
    {
        var path = Path.Combine(_tempDir, "nonexistent", ".engram-sync-state.json");
        var state = StateFile.ReadState(path);

        Assert.Equal("", state.LastExportAt);
        Assert.Equal(0, state.Version);
        Assert.Empty(state.Files);
        Assert.Empty(state.SessionHubs);
        Assert.Empty(state.TopicHubs);
    }

    [Fact]
    public void WriteState_CreatesFileWithValidJson()
    {
        var path = Path.Combine(_tempDir, ".engram-sync-state.json");
        var state = new SyncState
        {
            LastExportAt = "2026-01-01T00:00:00Z",
            Files = [],
            SessionHubs = [],
            TopicHubs = [],
            Version = 1,
        };

        StateFile.WriteState(path, state);

        Assert.True(File.Exists(path));
        var json = File.ReadAllText(path);
        Assert.Contains("last_export_at", json);
        Assert.Contains("2026-01-01T00:00:00Z", json);
    }

    [Fact]
    public void ReadWriteState_RoundTrip_ReturnsIdenticalState()
    {
        var path = Path.Combine(_tempDir, ".engram-sync-state.json");
        var original = new SyncState
        {
            LastExportAt = "2026-04-06T14:00:00Z",
            Files = new Dictionary<long, string>
            {
                { 1, "eng/bugfix/fixed-fts5-1.md" },
                { 42, "eng/decision/chose-sqlite-42.md" },
                { 100, "core/architecture/db-schema-100.md" },
            },
            SessionHubs = new Dictionary<string, string>
            {
                { "sess-001", "_sessions/sess-001.md" },
            },
            TopicHubs = new Dictionary<string, string>
            {
                { "sdd", "_topics/sdd.md" },
            },
            Version = 1,
        };

        StateFile.WriteState(path, original);
        var got = StateFile.ReadState(path);

        Assert.Equal(original.LastExportAt, got.LastExportAt);
        Assert.Equal(original.Version, got.Version);
        Assert.Equal(original.Files.Count, got.Files.Count);
        Assert.Equal("eng/decision/chose-sqlite-42.md", got.Files[42]);
        Assert.Equal("_sessions/sess-001.md", got.SessionHubs["sess-001"]);
        Assert.Equal("_topics/sdd.md", got.TopicHubs["sdd"]);
    }

    [Fact]
    public void WriteState_CreatesParentDirectory()
    {
        var path = Path.Combine(_tempDir, "nested", "dir", ".engram-sync-state.json");
        var state = new SyncState { Files = [], SessionHubs = [], TopicHubs = [] };

        StateFile.WriteState(path, state);

        Assert.True(File.Exists(path));
    }
}
