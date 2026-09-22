using System.CommandLine;
using System.CommandLine.Help;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Engram.Store;
using Xunit;

namespace Engram.Cli.Tests;

/// <summary>
/// HU-050 (ENG-480): Tests for the quick-capture CLI (`engram "&lt;texto&gt;"`).
/// Mirrors ProjectIdCliTests pattern — rebuilds the command tree in the test because
/// Program.cs uses top-level statements and is not referenceable.
/// </summary>
[Collection("CwdSensitive")]
public sealed class QuickCaptureTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dataDir;

    public QuickCaptureTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "engram-qc-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _dataDir = Path.Combine(_tempDir, "data");
        Directory.CreateDirectory(_dataDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    // ─── Command tree construction (mirrors Program.cs quick-capture block) ───

    private static Command BuildCommandTree(string dataDir)
    {
        var root = new RootCommand("Engram — persistent memory for AI coding agents");

        var contentArg = new Argument<string>("content")
        {
            Arity = ArgumentArity.ZeroOrOne,  // critical: allows subcommands to still route
        };
        var typeOpt = new Option<string>("--type", "-t") { DefaultValueFactory = _ => "note" };
        var projOpt = new Option<string?>("--project", "-p");
        root.Arguments.Add(contentArg);
        root.Options.Add(typeOpt);
        root.Options.Add(projOpt);

        // save subcommand (routing regression, NFR-002)
        var saveCmd = new Command("save", "Save a memory");
        var saveTitle = new Argument<string>("title");
        var saveContent = new Argument<string>("content");
        saveCmd.Arguments.Add(saveTitle);
        saveCmd.Arguments.Add(saveContent);
        saveCmd.SetAction((ParseResult pr) =>
        {
            Console.WriteLine($"SAVE-ROUTED title={pr.GetValue(saveTitle)} content={pr.GetValue(saveContent)}");
        });
        root.Subcommands.Add(saveCmd);

        // version subcommand (routing regression, NFR-002)
        var versionCmd = new Command("version", "Print version");
        versionCmd.SetAction(_ => Console.WriteLine("VERSION-ROUTED 1.3.0"));
        root.Subcommands.Add(versionCmd);

        // Quick-capture handler — mirrors Program.cs Change B exactly, but writes to
        // a real SqliteStore in the test data dir instead of OpenStore().
        root.SetAction(async (ParseResult parseResult) =>
        {
            var content = parseResult.GetValue(contentArg);
            if (string.IsNullOrWhiteSpace(content))
            {
                // Content explicitly provided but empty/whitespace → error (exit ≠ 0)
                if (parseResult.GetResult(contentArg) is not null)
                {
                    await Console.Error.WriteLineAsync("Memory content cannot be empty");
                    return 1;
                }
                // No args at all → show help
                new HelpAction().Invoke(parseResult);
                return 0;
            }

            // Project detection chain: -p → ENGRAM_PROJECT → DetectProject(cwd)
            var storeCfg = StoreConfig.FromEnvironment();
            var project = parseResult.GetValue(projOpt)
                ?? storeCfg.Project
                ?? ProjectDetector.DetectProject(Directory.GetCurrentDirectory());
            project = Normalizers.NormalizeProject(project);

            var title = GenerateQuickCaptureTitle(content);
            var sessionId = $"quick-capture-{DateTime.Now:yyyyMMddTHHmmss}";

            using var store = new SqliteStore(new StoreConfig { DataDir = dataDir });
            await store.CreateSessionAsync(sessionId, project, "");
            var id = await store.AddObservationAsync(new AddObservationParams
            {
                SessionId = sessionId,
                Type      = parseResult.GetValue(typeOpt)!,
                Title     = title,
                Content   = content,
                Project   = project,
            });

            Console.WriteLine($"✓ Memory saved: #{id} \"{title}\" ({parseResult.GetValue(typeOpt)}) [project: {project}]");
            return 0;
        });

        return root;
    }

    /// <summary>
    /// Mirrors Program.cs GenerateQuickCaptureTitle (FR-002, FR-006).
    /// </summary>
    private static string GenerateQuickCaptureTitle(string content)
    {
        // Split by whitespace (includes \n, \r, \t — FR-006)
        var words = content.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return content.Trim();

        // Content fits in 50 chars → title = content (trimmed)
        var trimmed = content.Trim();
        if (trimmed.Length <= 50) return trimmed;

        // Accumulate words: ≤7 words AND ≤50 chars
        var accumulated = new System.Text.StringBuilder();
        var count = 0;
        foreach (var word in words)
        {
            if (count >= 7) break;
            var addition = count == 0 ? word : " " + word;
            if (accumulated.Length + addition.Length > 50) break;
            accumulated.Append(addition);
            count++;
        }

        // Single word > 50 chars → truncate to 47 + "…"
        if (accumulated.Length == 0 && words.Length > 0)
        {
            var first = words[0];
            return first.Length > 50 ? first[..47] + "…" : first;
        }

        return accumulated.ToString();
    }

    // ─── Helpers ──────────────────────────────────────────────────────────

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

    private static long ParseId(string stdout)
    {
        var match = Regex.Match(stdout, @"#(\d+)");
        Assert.True(match.Success, $"No observation id found in output: {stdout}");
        return long.Parse(match.Groups[1].Value);
    }

    private SqliteStore OpenStore() => new(new StoreConfig { DataDir = _dataDir });

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
    public async Task QuickCapture_Basic()
    {
        var root = BuildCommandTree(_dataDir);
        using var _ = CwdScope.New(_tempDir);

        var (exitCode, stdout, _) = await InvokeAsync(root, new[] { "hello world" });

        Assert.Equal(0, exitCode);
        var id = ParseId(stdout);
        Assert.Contains($"Memory saved: #{id} \"", stdout);
        Assert.Contains("(note)", stdout);
        Assert.Contains("[project: ", stdout);

        using var store = OpenStore();
        var obs = await store.GetObservationAsync(id);
        Assert.NotNull(obs);
        Assert.Equal("hello world", obs!.Content);
        Assert.Equal("note", obs.Type);
        Assert.StartsWith("quick-capture-", obs.SessionId);
        Assert.False(string.IsNullOrWhiteSpace(obs.Project));
    }

    [Fact]
    public void QuickCapture_TitleGeneration()
    {
        // Long content → ≤7 words AND ≤50 chars
        var longTitle = GenerateQuickCaptureTitle("decisión: elegimos PostgreSQL sobre MongoDB por soporte JSONB de alto rendimiento");
        Assert.True(longTitle.Length <= 50, $"title too long: {longTitle}");
        Assert.True(longTitle.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length <= 7,
            $"title too many words: {longTitle}");

        // Short content → title = content (no ellipsis)
        var shortTitle = GenerateQuickCaptureTitle("usamos MediatR para CQRS");
        Assert.Equal("usamos MediatR para CQRS", shortTitle);

        // Single huge word → 47 chars + "…"
        var hugeTitle = GenerateQuickCaptureTitle(new string('a', 100));
        Assert.Equal(new string('a', 47) + "…", hugeTitle);
    }

    [Fact]
    public async Task QuickCapture_ProjectFromGitRoot()
    {
        var repo = InitGitRepo("My_QC_Repo");
        using var _ = CwdScope.New(repo);
        var root = BuildCommandTree(_dataDir);

        var prev = Environment.GetEnvironmentVariable("ENGRAM_PROJECT");
        Environment.SetEnvironmentVariable("ENGRAM_PROJECT", null);
        try
        {
            var (exitCode, stdout, _) = await InvokeAsync(root, new[] { "memo" });
            Assert.Equal(0, exitCode);
            var id = ParseId(stdout);
            Assert.Contains("[project: my-qc-repo]", stdout);

            using var store = OpenStore();
            var obs = await store.GetObservationAsync(id);
            Assert.Equal("my-qc-repo", obs!.Project);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ENGRAM_PROJECT", prev);
        }
    }

    [Fact]
    public async Task QuickCapture_ProjectEnvOverride()
    {
        var repo = InitGitRepo("some-repo");
        using var _ = CwdScope.New(repo);
        var root = BuildCommandTree(_dataDir);

        var prev = Environment.GetEnvironmentVariable("ENGRAM_PROJECT");
        try
        {
            // Sub-case 1: -p myproj wins over both ENGRAM_PROJECT and git detection
            Environment.SetEnvironmentVariable("ENGRAM_PROJECT", "envproj");
            var (exitCode1, stdout1, _) = await InvokeAsync(root, new[] { "-p", "myproj", "memo" });
            Assert.Equal(0, exitCode1);
            var id1 = ParseId(stdout1);
            Assert.Contains("[project: myproj]", stdout1);

            using (var store = OpenStore())
            {
                var obs = await store.GetObservationAsync(id1);
                Assert.Equal("myproj", obs!.Project);
            }

            // Sub-case 2: ENGRAM_PROJECT=envproj wins over git detection (no -p)
            var (exitCode2, stdout2, _) = await InvokeAsync(root, new[] { "memo2" });
            Assert.Equal(0, exitCode2);
            var id2 = ParseId(stdout2);
            Assert.Contains("[project: envproj]", stdout2);

            using (var store = OpenStore())
            {
                var obs = await store.GetObservationAsync(id2);
                Assert.Equal("envproj", obs!.Project);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("ENGRAM_PROJECT", prev);
        }
    }

    [Fact]
    public async Task QuickCapture_CustomType()
    {
        var root = BuildCommandTree(_dataDir);
        using var _ = CwdScope.New(_tempDir);

        var (exitCode, stdout, _) = await InvokeAsync(root, new[] { "-t", "insight", "pattern: usamos Result<T> para errores" });

        Assert.Equal(0, exitCode);
        var id = ParseId(stdout);
        Assert.Contains("(insight)", stdout);

        using var store = OpenStore();
        var obs = await store.GetObservationAsync(id);
        Assert.Equal("insight", obs!.Type);
    }

    [Fact]
    public async Task QuickCapture_MultiLine()
    {
        var root = BuildCommandTree(_dataDir);
        using var _ = CwdScope.New(_tempDir);

        var content = "primera línea de un contenido\nsegunda línea del mismo contenido\ntercera línea final";
        var (exitCode, stdout, _) = await InvokeAsync(root, new[] { content });

        Assert.Equal(0, exitCode);
        var id = ParseId(stdout);

        using var store = OpenStore();
        var obs = await store.GetObservationAsync(id);
        Assert.Equal(content, obs!.Content);          // newlines preserved verbatim
        Assert.DoesNotContain("\n", obs.Title);       // title is flat
        Assert.True(obs.Title.Length <= 50);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task QuickCapture_EmptyContent(string content)
    {
        var root = BuildCommandTree(_dataDir);
        using var _ = CwdScope.New(_tempDir);

        var (exitCode, _, stderr) = await InvokeAsync(root, new[] { content });

        Assert.NotEqual(0, exitCode);
        Assert.Contains("Memory content cannot be empty", stderr);

        using var store = OpenStore();
        var stats = await store.StatsAsync();
        Assert.Equal(0, stats.TotalObservations);
    }

    [Fact]
    public async Task QuickCapture_SubcommandsUnaffected()
    {
        var root = BuildCommandTree(_dataDir);
        using var _ = CwdScope.New(_tempDir);

        // save routes to the save subcommand (quick-capture does NOT fire)
        var (saveExit, saveStdout, saveStderr) = await InvokeAsync(root, new[] { "save", "t", "c" });
        Assert.Equal(0, saveExit);
        Assert.Contains("SAVE-ROUTED title=t content=c", saveStdout);
        Assert.DoesNotContain("Memory saved: #", saveStdout);
        Assert.DoesNotContain("Memory content cannot be empty", saveStderr);

        // version routes to the version subcommand
        var (verExit, verStdout, _) = await InvokeAsync(root, new[] { "version" });
        Assert.Equal(0, verExit);
        Assert.Contains("VERSION-ROUTED 1.3.0", verStdout);
    }
}
