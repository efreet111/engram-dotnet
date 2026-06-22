using System.CommandLine;
using System.Diagnostics;
using System.Text.Json;
using Engram.Store;
using Xunit;

namespace Engram.Cli.Tests;

/// <summary>
/// ENG-432: Tests for `engram project id` CLI command.
/// Mirrors SyncStatusCliTests pattern (rebuilds the command tree in the test).
/// </summary>
[Collection("CwdSensitive")]
public sealed class ProjectIdCliTests : IDisposable
{
    private readonly string _tempDir;

    public ProjectIdCliTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "engram-project-id-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    // ─── Command tree construction (mirrors Program.cs project id block) ───

    private static (Command root, Option<bool> jsonOpt, Option<bool> regenOpt, Option<bool> yesOpt) BuildCommandTree()
    {
        var projectCmd   = new Command("project", "Project identity operations");
        var projectIdCmd = new Command("id", "Show or regenerate the project identity GUID (.engram-id)");
        var jsonOpt      = new Option<bool>("--json");
        var regenOpt     = new Option<bool>("--regenerate");
        var yesOpt       = new Option<bool>("-y", () => false);
        projectIdCmd.AddOption(jsonOpt);
        projectIdCmd.AddOption(regenOpt);
        projectIdCmd.AddOption(yesOpt);
        projectIdCmd.SetHandler(async (bool json, bool regen, bool assumeYes) =>
        {
            var cwd = Directory.GetCurrentDirectory();
            var fileGuid = ProjectIdentity.GetProjectId(cwd);
            var computedGuid = ProjectIdentity.TryComputeDeterministicGuid(cwd);
            var computed = computedGuid?.ToString("D");

            if (regen)
            {
                if (computedGuid is null)
                {
                    await Console.Error.WriteLineAsync("error: Cannot regenerate.");
                    return;
                }
                if (!assumeYes)
                {
                    Console.Write($"Regenerate? [y/N] ");
                    var answer = Console.ReadLine()?.Trim().ToLowerInvariant();
                    if (answer != "y" && answer != "yes") { Console.WriteLine("Cancelled."); return; }
                }
                ProjectIdentity.SaveProjectId(cwd, computedGuid.Value);
                Console.WriteLine($"Project identity regenerated: {computed}");
                return;
            }

            var source = fileGuid is not null ? "file" : (computed is not null ? "computed" : "none");
            if (json)
            {
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    project_id = fileGuid,
                    source,
                    computed,
                }));
                return;
            }
            if (fileGuid is not null) Console.WriteLine($"project_id: {fileGuid}");
            else if (computed is not null) Console.WriteLine($"project_id: {computed} (computed, not saved)");
            else Console.WriteLine("No project identity found.");
        }, jsonOpt, regenOpt, yesOpt);

        projectCmd.AddCommand(projectIdCmd);
        var root = new RootCommand();
        root.AddCommand(projectCmd);
        return (root, jsonOpt, regenOpt, yesOpt);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────

    private string InitGitRepo(string name)
    {
        var repo = Path.Combine(_tempDir, name);
        Directory.CreateDirectory(repo);
        RunGit(repo, "init");
        RunGit(repo, "config user.email t@t.local");
        RunGit(repo, "config user.name t");
        RunGit(repo, $"remote add origin git@github.com:test/{name}.git");
        File.WriteAllText(Path.Combine(repo, "README.md"), "# t");
        RunGit(repo, "add README.md");
        RunGit(repo, "commit -m init");
        return repo;
    }

    private static void RunGit(string cwd, string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = $"-C \"{cwd}\" {args}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi)!;
        p.WaitForExit();
    }

    // ─── Tests ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ProjectId_NoGit_PrintsNotFound()
    {
        var (root, _, _, _) = BuildCommandTree();
        using var _ = CwdScope.New(_tempDir);

        var stdout = new StringWriter();
        var originalOut = Console.Out;
        Console.SetOut(stdout);
        try
        {
            var result = await root.InvokeAsync("project id");
            Assert.Equal(0, result);
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        Assert.Contains("No project identity found", stdout.ToString());
    }

    [Fact]
    public async Task ProjectId_NoGit_WithJson_EmitsSourceNone()
    {
        var (root, _, _, _) = BuildCommandTree();
        using var _ = CwdScope.New(_tempDir);

        var stdout = new StringWriter();
        var originalOut = Console.Out;
        Console.SetOut(stdout);
        try
        {
            await root.InvokeAsync("project id --json");
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        var json = JsonSerializer.Deserialize<JsonElement>(stdout.ToString().Trim());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("project_id").ValueKind);
        Assert.Equal("none", json.GetProperty("source").GetString());
    }

    [Fact]
    public async Task ProjectId_WithGitRepo_ComputesGuid()
    {
        var repo = InitGitRepo("cli-compute");
        using var _ = CwdScope.New(repo);
        var (root, _, _, _) = BuildCommandTree();

        var stdout = new StringWriter();
        var originalOut = Console.Out;
        Console.SetOut(stdout);
        try
        {
            await root.InvokeAsync("project id");
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        // Should mention computed GUID and "not saved"
        Assert.Contains("project_id:", stdout.ToString());
        Assert.Contains("(computed, not saved)", stdout.ToString());
    }

    [Fact]
    public async Task ProjectId_WithGitRepo_WithJson_EmitsSourceComputed()
    {
        var repo = InitGitRepo("cli-json");
        using var _ = CwdScope.New(repo);
        var (root, _, _, _) = BuildCommandTree();

        var stdout = new StringWriter();
        var originalOut = Console.Out;
        Console.SetOut(stdout);
        try
        {
            await root.InvokeAsync("project id --json");
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        var json = JsonSerializer.Deserialize<JsonElement>(stdout.ToString().Trim());
        Assert.Equal("computed", json.GetProperty("source").GetString());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("project_id").ValueKind);
        // computed should be a non-null GUID
        Assert.NotEqual(JsonValueKind.Null, json.GetProperty("computed").ValueKind);
    }

    [Fact]
    public async Task ProjectId_RegenerateYesFlag_OverwritesEngramId()
    {
        var repo = InitGitRepo("cli-regen");
        using var _ = CwdScope.New(repo);
        var (root, _, _, _) = BuildCommandTree();

        // Pre-seed a fake GUID — should be overwritten
        File.WriteAllText(Path.Combine(repo, ".engram-id"), "00000000-0000-0000-0000-000000000000");

        var stdout = new StringWriter();
        var originalOut = Console.Out;
        Console.SetOut(stdout);
        try
        {
            await root.InvokeAsync("project id --regenerate -y");
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        var newGuid = File.ReadAllText(Path.Combine(repo, ".engram-id")).Trim();
        Assert.NotEqual("00000000-0000-0000-0000-000000000000", newGuid);
        Assert.True(Guid.TryParse(newGuid, out var parsed));
        Assert.NotEqual(Guid.Empty, parsed);
        Assert.Contains("regenerated", stdout.ToString());
    }

    [Fact]
    public async Task ProjectId_WithExistingEngramId_ShowsFromFile()
    {
        var repo = InitGitRepo("cli-file");
        using var scope = CwdScope.New(repo);
        var (root, _, _, _) = BuildCommandTree();

        // Pre-seed a known GUID (doesn't match deterministic)
        var seeded = "00112233-4455-6677-8899-aabbccddeeff";
        File.WriteAllText(Path.Combine(repo, ".engram-id"), seeded);

        var stdout = new StringWriter();
        var originalOut = Console.Out;
        Console.SetOut(stdout);
        try
        {
            await root.InvokeAsync("project id");
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        Assert.Contains(seeded, stdout.ToString());
        Assert.DoesNotContain("(computed, not saved)", stdout.ToString());
    }

    // ─── ENG-433: Auto-enroll tests (default ON, opt-out via --no-auto-enroll) ──

    [Fact]
    public async Task McpCommand_NoFlag_AutoEnrollsByDefault()
    {
        var repo = InitGitRepo("cli-auto-enroll-default");
        using var scope = CwdScope.New(repo);

        var stderr = new StringWriter();
        var originalErr = Console.Error;
        Console.SetError(stderr);

        try
        {
            var root = new RootCommand();
            var mcpCmd = new Command("mcp", "Start the MCP server");
            var noAutoEnrollOpt = new Option<bool>("--no-auto-enroll");
            mcpCmd.AddOption(noAutoEnrollOpt);
            mcpCmd.SetHandler(async (bool noAutoEnroll) =>
            {
                if (!noAutoEnroll)
                {
                    var cwd = Directory.GetCurrentDirectory();
                    if (ProjectIdentity.TryAutoEnroll(cwd, out var generatedId))
                    {
                        await Console.Error.WriteLineAsync($"[engram] Generated project identity: {generatedId}");
                    }
                }
                await Task.CompletedTask;
            }, noAutoEnrollOpt);
            root.AddCommand(mcpCmd);

            // No flag → auto-enroll ON by default
            await root.InvokeAsync("mcp");
        }
        finally
        {
            Console.SetError(originalErr);
        }

        var idFile = Path.Combine(repo, ".engram-id");
        Assert.True(File.Exists(idFile));
        var generatedId = File.ReadAllText(idFile).Trim();
        Assert.True(Guid.TryParse(generatedId, out _));
        Assert.Contains("Generated project identity", stderr.ToString());
    }

    [Fact]
    public async Task McpCommand_NoAutoEnroll_DoesNotGenerate()
    {
        var repo = InitGitRepo("cli-no-auto-enroll");
        using var scope = CwdScope.New(repo);

        var stderr = new StringWriter();
        var originalErr = Console.Error;
        Console.SetError(stderr);

        try
        {
            var root = new RootCommand();
            var mcpCmd = new Command("mcp", "Start the MCP server");
            var noAutoEnrollOpt = new Option<bool>("--no-auto-enroll");
            mcpCmd.AddOption(noAutoEnrollOpt);
            mcpCmd.SetHandler(async (bool noAutoEnroll) =>
            {
                if (!noAutoEnroll)
                {
                    var cwd = Directory.GetCurrentDirectory();
                    if (ProjectIdentity.TryAutoEnroll(cwd, out var generatedId))
                    {
                        await Console.Error.WriteLineAsync($"[engram] Generated project identity: {generatedId}");
                    }
                }
                await Task.CompletedTask;
            }, noAutoEnrollOpt);
            root.AddCommand(mcpCmd);

            await root.InvokeAsync("mcp --no-auto-enroll");
        }
        finally
        {
            Console.SetError(originalErr);
        }

        // .engram-id should NOT be created
        var idFile = Path.Combine(repo, ".engram-id");
        Assert.False(File.Exists(idFile));
    }
}

/// <summary>
/// Disposable that switches to a given CWD and restores the previous one on dispose.
/// Prevents test CWD changes from leaking across tests and from races with parallel
/// collection/parallel test execution in xUnit.
/// </summary>
internal readonly struct CwdScope : IDisposable
{
    private readonly string _previous;

    private CwdScope(string previous) => _previous = previous;

    public static CwdScope New(string newCwd)
    {
        var previous = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(newCwd);
        return new CwdScope(previous);
    }

    public void Dispose()
    {
        try { Directory.SetCurrentDirectory(_previous); } catch { /* best-effort */ }
    }
}
