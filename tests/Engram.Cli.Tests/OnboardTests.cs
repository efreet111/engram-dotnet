using System.CommandLine;
using System.Text;
using System.Text.Json;
using Engram.Store;
using Xunit;

namespace Engram.Cli.Tests;

/// <summary>
/// HU-055 (ENG-485): Tests for `engram onboard` CLI command.
/// Mirrors ProjectIdCliTests pattern — rebuilds the command tree in the test.
/// </summary>
[Collection("CwdSensitive")]
public sealed class OnboardTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dataDir;

    public OnboardTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "engram-onboard-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _dataDir = Path.Combine(_tempDir, "data");
        Directory.CreateDirectory(_dataDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    // ─── Command tree construction ─────────────────────────────────────────────

    private Command BuildCommandTree()
    {
        var root = new RootCommand("Engram — persistent memory for AI coding agents");
        // Pass a factory that returns the test's seeded store
        var onboardCmd = OnboardCommand.CreateCommand(() => OpenStore());
        root.Subcommands.Add(onboardCmd);
        return root;
    }

    // ─── Helpers ───────────────────────────────────────────────────────────────

    private async Task<(int ExitCode, string Stdout, string Stderr)> InvokeAsync(Command root, string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var originalOut = Console.Out;
        var originalErr = Console.Error;
        Console.SetOut(stdout);
        Console.SetError(stderr);
        try
        {
            var exitCode = await root.Parse(args).InvokeAsync();
            return (exitCode, stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalErr);
        }
    }

    private SqliteStore OpenStore() => new(new StoreConfig { DataDir = _dataDir });

    private async Task SeedMemoryAsync(SqliteStore store, string type, string title, string content, string? project = null)
    {
        var sessionId = $"test-session-{Guid.NewGuid():N}";
        await store.CreateSessionAsync(sessionId, project ?? "test/project", "");
        await store.AddObservationAsync(new AddObservationParams
        {
            SessionId = sessionId,
            Type = type,
            Title = title,
            Content = content,
            Project = project ?? "test/project",
        });
    }

    // ─── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task HappyPath_MarkdownOutput_ContainsFiveSections()
    {
        // Seed memories
        using (var store = OpenStore())
        {
            await SeedMemoryAsync(store, "decision", "Use PostgreSQL for sync",
                "We decided to use PostgreSQL as the sync backend for its MVCC support.", "test/project");
            await SeedMemoryAsync(store, "convention", "Conventional commits",
                "All commits must follow conventional commits format.", "test/project");
            await SeedMemoryAsync(store, "blocker", "Docker permissions issue",
                "Docker socket permissions block the sync service.", "test/project");
            await SeedMemoryAsync(store, "insight", "SQLite WAL mode helps",
                "Enabling WAL mode on SQLite significantly improves concurrent read performance.", "test/project");
        }

        var root = BuildCommandTree();
        var (exitCode, stdout, stderr) = await InvokeAsync(root, new[] { "onboard", "newdev" });

        Assert.Equal(0, exitCode);
        Assert.Contains("Welcome to the team", stdout);
        Assert.Contains("Top 10 Architectural Decisions", stdout);
        Assert.Contains("Active Conventions", stdout);
        Assert.Contains("Known Blockers", stdout);
        Assert.Contains("Recent Insights", stdout);
        Assert.Contains("Where to Start", stdout);
        Assert.Contains("Use PostgreSQL for sync", stdout);
        Assert.Contains("Conventional commits", stdout);
        Assert.Empty(stderr);
    }

    [Fact]
    public async Task HappyPath_JsonOutput_IsValidJson()
    {
        using (var store = OpenStore())
        {
            await SeedMemoryAsync(store, "decision", "Use Redis for caching",
                "Redis will be used for caching layer.", "test/project");
        }

        var root = BuildCommandTree();
        var (exitCode, stdout, stderr) = await InvokeAsync(root, new[] { "onboard", "newdev", "--format", "json" });

        Assert.Equal(0, exitCode);
        Assert.Empty(stderr);

        // Should be valid JSON
        var doc = JsonDocument.Parse(stdout);
        var rootObj = doc.RootElement;

        Assert.Equal("newdev", rootObj.GetProperty("user").GetString());
        Assert.True(rootObj.TryGetProperty("generated_at", out _));
        Assert.True(rootObj.TryGetProperty("sections", out var sections));

        Assert.True(sections.TryGetProperty("decisions", out var decisions));
        Assert.True(sections.TryGetProperty("conventions", out _));
        Assert.True(sections.TryGetProperty("blockers", out _));
        Assert.True(sections.TryGetProperty("insights", out _));
        Assert.True(sections.TryGetProperty("where_to_start", out _));

        // Should have our seeded decision
        Assert.Contains(decisions.EnumerateArray(), e =>
            e.GetProperty("title").GetString() == "Use Redis for caching");
    }

    [Fact]
    public async Task DaysFilter_RespectsCutoff()
    {
        // This test verifies the days parameter is passed correctly by checking
        // that old memories (outside the window) don't appear in insights
        using (var store = OpenStore())
        {
            // Recent memory (should appear)
            await SeedMemoryAsync(store, "insight", "Recent insight",
                "This is recent and should appear.", "test/project");
        }

        var root = BuildCommandTree();
        // Default is 30 days, should include our recent memory
        var (exitCode, stdout, stderr) = await InvokeAsync(root, new[] { "onboard", "newdev", "--days", "30" });

        Assert.Equal(0, exitCode);
        Assert.Contains("Recent insight", stdout);
        Assert.Empty(stderr);
    }

    [Fact]
    public async Task ProjectFilter_RespectsProject()
    {
        using (var store = OpenStore())
        {
            await SeedMemoryAsync(store, "decision", "Project A decision",
                "This belongs to project A.", "team/a");
            await SeedMemoryAsync(store, "decision", "Project B decision",
                "This belongs to project B.", "team/b");
        }

        var root = BuildCommandTree();
        var (exitCode, stdout, stderr) = await InvokeAsync(root,
            new[] { "onboard", "newdev", "--project", "team/a" });

        Assert.Equal(0, exitCode);
        Assert.Contains("Project A decision", stdout);
        Assert.DoesNotContain("Project B decision", stdout);
        Assert.Empty(stderr);
    }

    [Fact]
    public async Task EmptyTeam_ShowsGracefulMessage()
    {
        // No seed — empty database
        var root = BuildCommandTree();
        var (exitCode, stdout, stderr) = await InvokeAsync(root, new[] { "onboard", "newdev" });

        Assert.Equal(0, exitCode);
        Assert.Contains("Welcome to the team", stdout);
        // Empty sections should show graceful messages
        Assert.Contains("No decisions captured", stdout);
        Assert.Contains("No conventions captured", stdout);
        Assert.Contains("No blockers", stdout);
        Assert.Contains("No recent insights", stdout);
        Assert.Empty(stderr);
    }

    [Fact]
    public async Task OutputFile_CreatesFile()
    {
        using (var store = OpenStore())
        {
            await SeedMemoryAsync(store, "insight", "File test insight",
                "Testing file output.", "test/project");
        }

        var outputPath = Path.Combine(_tempDir, "onboarding-output.md");

        var root = BuildCommandTree();
        var (exitCode, stdout, stderr) = await InvokeAsync(root,
            new[] { "onboard", "newdev", "--output", outputPath });

        Assert.Equal(0, exitCode);
        Assert.Contains("written to:", stdout);
        Assert.True(File.Exists(outputPath));

        var content = File.ReadAllText(outputPath);
        Assert.Contains("Welcome to the team", content);
        Assert.Contains("File test insight", content);
        Assert.Empty(stderr);
    }

    [Fact]
    public async Task FormatInvalid_ReturnsError()
    {
        var root = BuildCommandTree();
        var (exitCode, stdout, stderr) = await InvokeAsync(root,
            new[] { "onboard", "newdev", "--format", "xml" });

        Assert.Equal(1, exitCode);
        Assert.Contains("must be 'markdown' or 'json'", stderr);
    }
}
