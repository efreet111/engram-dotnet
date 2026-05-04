using Engram.Store;
using Xunit;

namespace Engram.Obsidian.Tests;

public class ExporterTests
{
    private readonly string _tempDir;

    public ExporterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    // ─── Config Validation ────────────────────────────────────────────────────

    [Fact]
    public void Export_MissingVaultPath_ThrowsArgumentException()
    {
        var store = new MockStoreReader();
        var exporter = new Exporter(store, new ExportConfig { VaultPath = "" });

        Assert.Throws<ArgumentException>(() => exporter.Export());
    }

    [Fact]
    public void Export_ValidVaultPath_ConstructsExporter()
    {
        var store = new MockStoreReader();
        var exporter = new Exporter(store, new ExportConfig { VaultPath = _tempDir });
        Assert.NotNull(exporter);
    }

    // ─── Full Export (Fresh Vault) ────────────────────────────────────────────

    [Fact]
    public void Export_FirstRun_ExportsAllObservations()
    {
        var store = new MockStoreReader
        {
            ExportData = new ExportData
            {
                Sessions = [new Session { Id = "sess-1", Project = "eng" }],
                Observations =
                [
                    new Observation { Id = 1, SessionId = "sess-1", Type = "bugfix", Title = "Fixed auth", Content = "Fixed auth bug", Scope = "team", CreatedAt = "2026-01-01T10:00:00Z", UpdatedAt = "2026-01-01T10:00:00Z", Project = "eng" },
                    new Observation { Id = 2, SessionId = "sess-1", Type = "decision", Title = "Use JWT", Content = "Decided to use JWT", Scope = "team", CreatedAt = "2026-01-02T10:00:00Z", UpdatedAt = "2026-01-02T10:00:00Z", Project = "eng" },
                ],
                Prompts = [],
            },
        };

        var exporter = new Exporter(store, new ExportConfig { VaultPath = _tempDir });
        var result = exporter.Export();

        Assert.Equal(2, result.Created);
        Assert.Equal(0, result.Updated);
        Assert.Equal(0, result.Deleted);

        // Files must exist
        Assert.True(File.Exists(Path.Combine(_tempDir, "engram", "eng", "bugfix", "fixed-auth-1.md")));
        Assert.True(File.Exists(Path.Combine(_tempDir, "engram", "eng", "decision", "use-jwt-2.md")));
    }

    // ─── Incremental Export ───────────────────────────────────────────────────

    [Fact]
    public void Export_Incremental_ExportsOnlyNewObservations()
    {
        // Seed state with obs ID=1 already exported
        var stateDir = Path.Combine(_tempDir, "engram");
        Directory.CreateDirectory(stateDir);
        var existingState = new SyncState
        {
            LastExportAt = "2026-01-01T12:00:00Z",
            Files = new Dictionary<long, string> { { 1, "eng/bugfix/fixed-auth-1.md" } },
            SessionHubs = [],
            TopicHubs = [],
            Version = 1,
        };
        StateFile.WriteState(Path.Combine(stateDir, ".engram-sync-state.json"), existingState);

        var store = new MockStoreReader
        {
            ExportData = new ExportData
            {
                Sessions = [new Session { Id = "sess-1", Project = "eng" }],
                Observations =
                [
                    // Old obs — updated_at before LastExportAt → should be skipped
                    new Observation { Id = 1, SessionId = "sess-1", Type = "bugfix", Title = "Fixed auth", Content = "Fixed auth bug", Scope = "team", CreatedAt = "2026-01-01T10:00:00Z", UpdatedAt = "2026-01-01T10:00:00Z", Project = "eng" },
                    // New obs — updated_at after LastExportAt → should be exported
                    new Observation { Id = 2, SessionId = "sess-1", Type = "decision", Title = "Use JWT", Content = "Decided to use JWT", Scope = "team", CreatedAt = "2026-02-01T10:00:00Z", UpdatedAt = "2026-02-01T10:00:00Z", Project = "eng" },
                ],
                Prompts = [],
            },
        };

        var exporter = new Exporter(store, new ExportConfig { VaultPath = _tempDir });
        var result = exporter.Export();

        Assert.Equal(1, result.Created);
        Assert.Equal(1, result.Skipped);
    }

    // ─── Deleted Observations ─────────────────────────────────────────────────

    [Fact]
    public void Export_DeletedObs_RemovesFileFromVault()
    {
        var engDir = Path.Combine(_tempDir, "engram");
        var obsDir = Path.Combine(engDir, "eng", "bugfix");
        Directory.CreateDirectory(obsDir);
        var obsFile = Path.Combine(obsDir, "some-fix-3.md");
        File.WriteAllText(obsFile, "old content");

        var existingState = new SyncState
        {
            LastExportAt = "2026-01-01T12:00:00Z",
            Files = new Dictionary<long, string> { { 3, "eng/bugfix/some-fix-3.md" } },
            SessionHubs = [],
            TopicHubs = [],
            Version = 1,
        };
        StateFile.WriteState(Path.Combine(engDir, ".engram-sync-state.json"), existingState);

        var deletedAt = "2026-02-01T00:00:00Z";
        var store = new MockStoreReader
        {
            ExportData = new ExportData
            {
                Sessions = [],
                Observations =
                [
                    new Observation
                    {
                        Id = 3,
                        SessionId = "sess-1",
                        Type = "bugfix",
                        Title = "Some fix",
                        Content = "some fix",
                        Scope = "team",
                        CreatedAt = "2026-01-01T10:00:00Z",
                        UpdatedAt = deletedAt,
                        DeletedAt = deletedAt,
                        Project = "eng",
                    },
                ],
                Prompts = [],
            },
        };

        var exporter = new Exporter(store, new ExportConfig { VaultPath = _tempDir });
        var result = exporter.Export();

        Assert.Equal(1, result.Deleted);
        Assert.False(File.Exists(obsFile));
    }

    // ─── Project Filter ───────────────────────────────────────────────────────

    [Fact]
    public void Export_ProjectFilter_ExportsOnlyMatchingProject()
    {
        var store = new MockStoreReader
        {
            ExportData = new ExportData
            {
                Sessions =
                [
                    new Session { Id = "sess-1", Project = "eng" },
                    new Session { Id = "sess-2", Project = "gentle-ai" },
                ],
                Observations =
                [
                    new Observation { Id = 1, SessionId = "sess-1", Type = "bugfix", Title = "Eng fix", Content = "eng fix content", Scope = "team", CreatedAt = "2026-01-01T10:00:00Z", UpdatedAt = "2026-01-01T10:00:00Z", Project = "eng" },
                    new Observation { Id = 2, SessionId = "sess-2", Type = "decision", Title = "AI decision", Content = "gentle-ai content", Scope = "team", CreatedAt = "2026-01-02T10:00:00Z", UpdatedAt = "2026-01-02T10:00:00Z", Project = "gentle-ai" },
                ],
                Prompts = [],
            },
        };

        var exporter = new Exporter(store, new ExportConfig { VaultPath = _tempDir, Project = "eng" });
        var result = exporter.Export();

        Assert.Equal(1, result.Created);
        // The gentle-ai obs must NOT have a file
        var aiFile = Path.Combine(_tempDir, "engram", "gentle-ai", "decision", "ai-decision-2.md");
        Assert.False(File.Exists(aiFile));
    }

    [Fact]
    public void Export_NoProjectFilter_ExportsAllProjects()
    {
        var store = new MockStoreReader
        {
            ExportData = new ExportData
            {
                Sessions =
                [
                    new Session { Id = "sess-1", Project = "eng" },
                    new Session { Id = "sess-2", Project = "gentle-ai" },
                ],
                Observations =
                [
                    new Observation { Id = 1, SessionId = "sess-1", Type = "bugfix", Title = "Eng fix", Content = "eng fix", Scope = "team", CreatedAt = "2026-01-01T10:00:00Z", UpdatedAt = "2026-01-01T10:00:00Z", Project = "eng" },
                    new Observation { Id = 2, SessionId = "sess-2", Type = "decision", Title = "AI decision", Content = "ai decision", Scope = "team", CreatedAt = "2026-01-02T10:00:00Z", UpdatedAt = "2026-01-02T10:00:00Z", Project = "gentle-ai" },
                ],
                Prompts = [],
            },
        };

        var exporter = new Exporter(store, new ExportConfig { VaultPath = _tempDir });
        var result = exporter.Export();

        Assert.Equal(2, result.Created);
    }

    // ─── Full Pipeline ────────────────────────────────────────────────────────

    [Fact]
    public void Export_FullPipeline_CorrectCountsAndState()
    {
        var deletedAt = "2026-03-15T00:00:00Z";
        var store = new MockStoreReader
        {
            ExportData = TestFixtures.BuildPipelineFixtures(deletedAt),
        };

        var exporter = new Exporter(store, new ExportConfig { VaultPath = _tempDir });
        var result = exporter.Export();

        // 19 live obs created (1 deleted — not in state on first run, so not deleted from vault)
        Assert.Equal(19, result.Created);

        // State file must exist
        var stateFile = Path.Combine(_tempDir, "engram", ".engram-sync-state.json");
        Assert.True(File.Exists(stateFile));

        // Read back state and verify 19 entries
        var state = StateFile.ReadState(stateFile);
        Assert.Equal(19, state.Files.Count);
        Assert.NotEmpty(state.LastExportAt);

        // Session hub notes: 5 sessions with obs → 5 hub files
        var sessionHubsDir = Path.Combine(_tempDir, "engram", "_sessions");
        Assert.True(Directory.Exists(sessionHubsDir));
        var hubCount = Directory.GetFiles(sessionHubsDir, "*.md").Length;
        Assert.Equal(5, hubCount);

        // Topic hubs: only prefixes with ≥2 obs
        var topicHubsDir = Path.Combine(_tempDir, "engram", "_topics");
        Assert.True(Directory.Exists(topicHubsDir));
    }

    // ─── Incremental Second Run ───────────────────────────────────────────────

    [Fact]
    public void Export_SecondRun_IsIncremental_NothingNew()
    {
        var deletedAt = "2026-03-15T00:00:00Z";
        var store = new MockStoreReader
        {
            ExportData = TestFixtures.BuildPipelineFixtures(deletedAt),
        };

        // First export
        var exporter1 = new Exporter(store, new ExportConfig { VaultPath = _tempDir });
        var first = exporter1.Export();
        Assert.Equal(19, first.Created);

        // Second export with same data → nothing new
        var exporter2 = new Exporter(store, new ExportConfig { VaultPath = _tempDir });
        var second = exporter2.Export();

        Assert.Equal(0, second.Created);
        Assert.Equal(19, second.Skipped);
    }

    // ─── Force Re-export ──────────────────────────────────────────────────────

    [Fact]
    public void Export_Force_ReExportsEverything()
    {
        var store = new MockStoreReader
        {
            ExportData = new ExportData
            {
                Sessions = [new Session { Id = "sess-1", Project = "eng" }],
                Observations =
                [
                    new Observation { Id = 1, SessionId = "sess-1", Type = "bugfix", Title = "Fixed auth", Content = "Fixed auth bug", Scope = "team", CreatedAt = "2026-01-01T10:00:00Z", UpdatedAt = "2026-01-01T10:00:00Z", Project = "eng" },
                ],
                Prompts = [],
            },
        };

        // First export
        var exporter1 = new Exporter(store, new ExportConfig { VaultPath = _tempDir });
        var first = exporter1.Export();
        Assert.Equal(1, first.Created);

        // Force re-export
        var exporter2 = new Exporter(store, new ExportConfig { VaultPath = _tempDir, Force = true });
        var second = exporter2.Export();

        // Force should re-create the file (content matches → skipped on idempotency check)
        // But since --force resets state, the file exists but state doesn't track it
        // So it will be checked for content idempotency → skipped
        Assert.Equal(0, second.Created);
        Assert.Equal(1, second.Skipped);
    }

    // ─── Graph Config ─────────────────────────────────────────────────────────

    [Fact]
    public void Export_GraphConfigForce_CreatesGraphJson()
    {
        var store = new MockStoreReader();
        var exporter = new Exporter(store, new ExportConfig { VaultPath = _tempDir, GraphConfig = GraphConfigMode.Force });
        exporter.Export();

        var graphPath = Path.Combine(_tempDir, ".obsidian", "graph.json");
        Assert.True(File.Exists(graphPath));
    }

    [Fact]
    public void Export_GraphConfigSkip_DoesNotCreateGraphJson()
    {
        var store = new MockStoreReader();
        var exporter = new Exporter(store, new ExportConfig { VaultPath = _tempDir, GraphConfig = GraphConfigMode.Skip });
        exporter.Export();

        var graphPath = Path.Combine(_tempDir, ".obsidian", "graph.json");
        Assert.False(File.Exists(graphPath));
    }

    [Fact]
    public void Export_GraphConfigZeroValue_DefaultsToPreserve()
    {
        var store = new MockStoreReader();
        var exporter = new Exporter(store, new ExportConfig { VaultPath = _tempDir });
        exporter.Export();

        // Default is Preserve, not Skip — Preserve creates graph.json if absent
        var graphPath = Path.Combine(_tempDir, ".obsidian", "graph.json");
        Assert.True(File.Exists(graphPath));
    }

    // ─── Content Idempotency ──────────────────────────────────────────────────

    [Fact]
    public void Export_ContentUnchanged_SkipsFile()
    {
        var store = new MockStoreReader
        {
            ExportData = new ExportData
            {
                Sessions = [new Session { Id = "sess-1", Project = "eng" }],
                Observations =
                [
                    new Observation { Id = 1, SessionId = "sess-1", Type = "bugfix", Title = "Fixed auth", Content = "Fixed auth bug", Scope = "team", CreatedAt = "2026-01-01T10:00:00Z", UpdatedAt = "2026-01-01T10:00:00Z", Project = "eng" },
                ],
                Prompts = [],
            },
        };

        // First export
        var exporter1 = new Exporter(store, new ExportConfig { VaultPath = _tempDir });
        var first = exporter1.Export();
        Assert.Equal(1, first.Created);

        // Second export — content unchanged → skipped
        var exporter2 = new Exporter(store, new ExportConfig { VaultPath = _tempDir });
        var second = exporter2.Export();

        Assert.Equal(0, second.Created);
        Assert.Equal(1, second.Skipped);
    }

    // ─── Hub Generation During Export ─────────────────────────────────────────

    [Fact]
    public void Export_CreatesSessionAndTopicHubs()
    {
        var store = new MockStoreReader
        {
            ExportData = new ExportData
            {
                Sessions =
                [
                    new Session { Id = "sess-1", Project = "eng" },
                    new Session { Id = "sess-2", Project = "eng" },
                ],
                Observations =
                [
                    // Two obs with same topic prefix "auth" → topic hub
                    new Observation { Id = 1, SessionId = "sess-1", Type = "architecture", Title = "Auth Arch", Content = "auth arch", Scope = "team", CreatedAt = "2026-01-01T10:00:00Z", UpdatedAt = "2026-01-01T10:00:00Z", Project = "eng", TopicKey = "auth/jwt" },
                    new Observation { Id = 2, SessionId = "sess-2", Type = "decision", Title = "Auth Decision", Content = "auth decision", Scope = "team", CreatedAt = "2026-01-02T10:00:00Z", UpdatedAt = "2026-01-02T10:00:00Z", Project = "eng", TopicKey = "auth/sessions" },
                ],
                Prompts = [],
            },
        };

        var exporter = new Exporter(store, new ExportConfig { VaultPath = _tempDir });
        var result = exporter.Export();

        // 2 session hubs (sess-1, sess-2) + 1 topic hub (auth)
        Assert.Equal(3, result.HubsCreated);

        var sessionHubsDir = Path.Combine(_tempDir, "engram", "_sessions");
        Assert.True(File.Exists(Path.Combine(sessionHubsDir, "sess-1.md")));
        Assert.True(File.Exists(Path.Combine(sessionHubsDir, "sess-2.md")));

        var topicHubsDir = Path.Combine(_tempDir, "engram", "_topics");
        Assert.True(File.Exists(Path.Combine(topicHubsDir, "auth.md")));
    }

    // ─── Scope Security ───────────────────────────────────────────────────────

    [Fact]
    public void Export_Default_ExcludesPersonalScope()
    {
        var store = new MockStoreReader
        {
            ExportData = new ExportData
            {
                Sessions =
                [
                    new Session { Id = "sess-1", Project = "eng" },
                ],
                Observations =
                [
                    new Observation { Id = 1, SessionId = "sess-1", Type = "bugfix", Title = "Team fix", Content = "team fix", Scope = "team", CreatedAt = "2026-01-01T10:00:00Z", UpdatedAt = "2026-01-01T10:00:00Z", Project = "eng" },
                    new Observation { Id = 2, SessionId = "sess-1", Type = "decision", Title = "Personal decision", Content = "personal decision", Scope = "personal", CreatedAt = "2026-01-02T10:00:00Z", UpdatedAt = "2026-01-02T10:00:00Z", Project = "eng" },
                ],
                Prompts = [],
            },
        };

        var exporter = new Exporter(store, new ExportConfig { VaultPath = _tempDir });
        var result = exporter.Export();

        Assert.Equal(1, result.Created); // Only team scope
    }

    [Fact]
    public void Export_IncludePersonal_IncludesPersonalScope()
    {
        var store = new MockStoreReader
        {
            ExportData = new ExportData
            {
                Sessions =
                [
                    new Session { Id = "sess-1", Project = "eng" },
                ],
                Observations =
                [
                    new Observation { Id = 1, SessionId = "sess-1", Type = "bugfix", Title = "Team fix", Content = "team fix", Scope = "team", CreatedAt = "2026-01-01T10:00:00Z", UpdatedAt = "2026-01-01T10:00:00Z", Project = "eng" },
                    new Observation { Id = 2, SessionId = "sess-1", Type = "decision", Title = "Personal decision", Content = "personal decision", Scope = "personal", CreatedAt = "2026-01-02T10:00:00Z", UpdatedAt = "2026-01-02T10:00:00Z", Project = "eng" },
                ],
                Prompts = [],
            },
        };

        var exporter = new Exporter(store, new ExportConfig { VaultPath = _tempDir, IncludePersonal = true });
        var result = exporter.Export();

        Assert.Equal(2, result.Created); // Both team and personal
    }

    // ─── Limit ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Export_Limit_StopsAtMaxObservations()
    {
        var store = new MockStoreReader
        {
            ExportData = new ExportData
            {
                Sessions = [new Session { Id = "sess-1", Project = "eng" }],
                Observations =
                [
                    new Observation { Id = 1, SessionId = "sess-1", Type = "bugfix", Title = "Fix one", Content = "fix one", Scope = "team", CreatedAt = "2026-01-01T10:00:00Z", UpdatedAt = "2026-01-01T10:00:00Z", Project = "eng" },
                    new Observation { Id = 2, SessionId = "sess-1", Type = "decision", Title = "Decision two", Content = "decision two", Scope = "team", CreatedAt = "2026-01-02T10:00:00Z", UpdatedAt = "2026-01-02T10:00:00Z", Project = "eng" },
                    new Observation { Id = 3, SessionId = "sess-1", Type = "architecture", Title = "Arch three", Content = "arch three", Scope = "team", CreatedAt = "2026-01-03T10:00:00Z", UpdatedAt = "2026-01-03T10:00:00Z", Project = "eng" },
                    new Observation { Id = 4, SessionId = "sess-1", Type = "pattern", Title = "Pattern four", Content = "pattern four", Scope = "team", CreatedAt = "2026-01-04T10:00:00Z", UpdatedAt = "2026-01-04T10:00:00Z", Project = "eng" },
                    new Observation { Id = 5, SessionId = "sess-1", Type = "bugfix", Title = "Fix five", Content = "fix five", Scope = "team", CreatedAt = "2026-01-05T10:00:00Z", UpdatedAt = "2026-01-05T10:00:00Z", Project = "eng" },
                ],
                Prompts = [],
            },
        };

        var exporter = new Exporter(store, new ExportConfig { VaultPath = _tempDir, Limit = 3 });
        var result = exporter.Export();

        Assert.Equal(3, result.Created);
        Assert.Equal(0, result.Skipped); // Remaining 2 were not processed (loop broke)

        // Only 3 files should exist
        var engDir = Path.Combine(_tempDir, "engram", "eng");
        var fileCount = Directory.GetFiles(engDir, "*.md", SearchOption.AllDirectories).Length;
        Assert.Equal(3, fileCount);
    }

    [Fact]
    public void Export_LimitZero_NoLimit()
    {
        var store = new MockStoreReader
        {
            ExportData = new ExportData
            {
                Sessions = [new Session { Id = "sess-1", Project = "eng" }],
                Observations =
                [
                    new Observation { Id = 1, SessionId = "sess-1", Type = "bugfix", Title = "Fix one", Content = "fix one", Scope = "team", CreatedAt = "2026-01-01T10:00:00Z", UpdatedAt = "2026-01-01T10:00:00Z", Project = "eng" },
                    new Observation { Id = 2, SessionId = "sess-1", Type = "decision", Title = "Decision two", Content = "decision two", Scope = "team", CreatedAt = "2026-01-02T10:00:00Z", UpdatedAt = "2026-01-02T10:00:00Z", Project = "eng" },
                ],
                Prompts = [],
            },
        };

        var exporter = new Exporter(store, new ExportConfig { VaultPath = _tempDir, Limit = 0 });
        var result = exporter.Export();

        Assert.Equal(2, result.Created); // All observations exported
    }
}
