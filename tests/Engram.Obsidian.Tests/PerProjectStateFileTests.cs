using Xunit;

namespace Engram.Obsidian.Tests;

/// <summary>
/// Tests for per-project state file resolution (ENG-208 Phase 5).
/// </summary>
public class PerProjectStateFileTests
{
    private readonly string _tempDir;

    public PerProjectStateFileTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void ResolveStatePath_NoProject_ReturnsDefaultStateJson()
    {
        var vaultDir = "/some/vault";
        var path = StateFile.ResolveStatePath(vaultDir, project: null);
        Assert.Equal(Path.Combine(vaultDir, ".engram", "state.json"), path);
    }

    [Fact]
    public void ResolveStatePath_WithProject_ReturnsPerProjectStateJson()
    {
        var vaultDir = "/some/vault";
        var path = StateFile.ResolveStatePath(vaultDir, project: "my-project");
        Assert.Equal(Path.Combine(vaultDir, ".engram", "state-my-project.json"), path);
    }

    [Theory]
    [InlineData("simple", "simple")]
    [InlineData("with-dash", "with-dash")]
    [InlineData("with_underscore", "with_underscore")]
    [InlineData("auth/service", "auth_service")]
    [InlineData("path with spaces", "path_with_spaces")]
    [InlineData("inv.casamusica", "inv.casamusica")] // dots OK - user's real case
    [InlineData("user@host", "user_host")]
    public void SanitizeProjectName_HandlesSpecialChars(string input, string expected)
    {
        Assert.Equal(expected, StateFile.SanitizeProjectName(input));
    }

    [Fact]
    public void SyncState_WithLastSeq_Roundtrips()
    {
        var path = Path.Combine(_tempDir, "state.json");
        var original = new SyncState
        {
            LastExportAt = "2026-06-09T10:00:00Z",
            LastSeq = 12345,
            Files = [],
            SessionHubs = [],
            TopicHubs = [],
            Version = 2,
        };
        StateFile.WriteState(path, original);
        var loaded = StateFile.ReadState(path);
        Assert.Equal(12345, loaded.LastSeq);
        Assert.Equal("2026-06-09T10:00:00Z", loaded.LastExportAt);
    }

    [Fact]
    public void SyncState_OldVersion_NoLastSeq_LoadsAsNull()
    {
        // Simulate old v1 state file with no last_seq field
        var path = Path.Combine(_tempDir, "old-state.json");
        File.WriteAllText(path, """{"last_export_at":"2026-01-01T00:00:00Z","files":{},"session_hubs":{},"topic_hubs":{},"version":1}""");
        var loaded = StateFile.ReadState(path);
        Assert.Null(loaded.LastSeq);
        Assert.Equal("2026-01-01T00:00:00Z", loaded.LastExportAt);
    }
}