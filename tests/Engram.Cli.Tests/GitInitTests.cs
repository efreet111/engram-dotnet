using System.CommandLine;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Engram.Cli;
using Xunit;

namespace Engram.Cli.Tests;

/// <summary>
/// HU-051 (ENG-481): Tests for `engram init` — git hooks auto-capture.
/// Mirrors QuickCaptureTests/ProjectIdCliTests patterns:
///   - <see cref="CwdScope"/> for serial CWD switching (collection "CwdSensitive")
///   - InitGitRepo/RunGit harness for real-git tests
///   - ENGRAM_BIN shim for hook-execution tests (G8/NFR-005)
/// </summary>
[Collection("CwdSensitive")]
public sealed class GitInitTests : IDisposable
{
    private readonly string _tempDir;

    public GitInitTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "engram-gitinit-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    // ─── Command tree (mirrors Program.cs registration) ───────────────────

    private static Command BuildCommandTree()
    {
        var root = new RootCommand("Engram test");
        root.Subcommands.Add(GitInitCommand.CreateCommand());
        return root;
    }

    // ─── Helpers ──────────────────────────────────────────────────────────

    private static async Task<(int ExitCode, string Stdout, string Stderr)> InvokeAsync(Command root, string[] args)
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

    /// <summary>Creates an executable shim that logs each invocation + argv to a file.</summary>
    private static (string ShimPath, string LogPath) CreateShim(string dir)
    {
        var logPath = Path.Combine(dir, "engram-shim.log");
        var shimPath = Path.Combine(dir, "engram-shim");
        var shim = "#!/bin/sh\n"
            + "printf 'ENG-INVOKE\\n' >> \"" + logPath + "\"\n"
            + "printf '%s\\n' \"$@\" >> \"" + logPath + "\"\n";
        File.WriteAllText(shimPath, shim, new UTF8Encoding(false));
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            File.SetUnixFileMode(shimPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        return (shimPath, logPath);
    }

    /// <summary>Stages a file and commits with the given message (via -F, shell-safe).</summary>
    private static void CommitFromMessage(string repo, string message)
    {
        var msgFile = Path.Combine(repo, ".engram-test-commit-msg");
        File.WriteAllText(msgFile, message);
        File.WriteAllText(Path.Combine(repo, "test.txt"), "change");
        RunGit(repo, "add test.txt");
        RunGit(repo, $"commit -F \"{msgFile}\"");
    }

    /// <summary>Runs `sh -n` (syntax check) on a hook to prove POSIX/dash compliance (NFR-003).</summary>
    private static int ShSyntaxCheck(string hookPath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "sh",
            Arguments = $"-n \"{hookPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi)!;
        p.WaitForExit();
        return p.ExitCode;
    }

    private static bool IsExecutable(string path)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            return true; // unix mode not available — skip check
        return (File.GetUnixFileMode(path) & UnixFileMode.UserExecute) != 0;
    }

    private static string PostCommitPath(string repo) => Path.Combine(repo, ".git", "hooks", "post-commit");
    private static string PrepareCommitMsgPath(string repo) => Path.Combine(repo, ".git", "hooks", "prepare-commit-msg");

    // ─── Tests ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GitInit_CreatesPostCommitHook()
    {
        var repo = InitGitRepo("my-init-repo");
        using var _ = CwdScope.New(repo);

        var prev = Environment.GetEnvironmentVariable("ENGRAM_PROJECT");
        Environment.SetEnvironmentVariable("ENGRAM_PROJECT", null);
        try
        {
            var (exitCode, stdout, stderr) = await InvokeAsync(BuildCommandTree(), new[] { "init" });

            Assert.Equal(0, exitCode);
            Assert.Empty(stderr);

            var hookPath = PostCommitPath(repo);
            Assert.True(File.Exists(hookPath), "post-commit hook was not created");
            Assert.True(IsExecutable(hookPath), "post-commit is not executable");

            var lines = File.ReadAllLines(hookPath);
            Assert.Equal("#!/bin/sh", lines[0]);
            Assert.Equal("# Generated by engram init", lines[1]);

            var content = File.ReadAllText(hookPath);
            Assert.Contains("PROJECT='my-init-repo'", content);
            Assert.Contains("ENGRAM_BIN=\"${ENGRAM_BIN:-'engram'}\"", content);

            Assert.False(File.Exists(PrepareCommitMsgPath(repo)), "prepare-commit-msg should not exist in default mode");
        }
        finally
        {
            Environment.SetEnvironmentVariable("ENGRAM_PROJECT", prev);
        }
    }

    [Fact]
    public async Task GitInit_NotARepo_FailsLoud()
    {
        var dir = Path.Combine(_tempDir, "not-a-repo");
        Directory.CreateDirectory(dir);
        using var _ = CwdScope.New(dir);

        var (exitCode, _, stderr) = await InvokeAsync(BuildCommandTree(), new[] { "init" });

        Assert.NotEqual(0, exitCode);
        Assert.Contains("Not a git repository", stderr);
        Assert.Empty(Directory.GetFileSystemEntries(dir));
    }

    [Fact]
    public async Task GitInit_BackupsForeignHook()
    {
        var repo = InitGitRepo("backup-repo");
        using var _ = CwdScope.New(repo);

        var hookPath = PostCommitPath(repo);
        var original = "#!/bin/sh\necho 'custom user hook'\n";
        File.WriteAllText(hookPath, original, new UTF8Encoding(false));

        var (exitCode, stdout, _) = await InvokeAsync(BuildCommandTree(), new[] { "init" });

        Assert.Equal(0, exitCode);

        var backupPath = hookPath + ".engram-backup";
        Assert.True(File.Exists(backupPath), "foreign hook was not backed up");
        Assert.Equal(original, File.ReadAllText(backupPath));

        Assert.True(File.Exists(hookPath));
        Assert.Contains("# Generated by engram init", File.ReadAllText(hookPath));
        Assert.Contains("*.engram-backup", stdout);
    }

    [Fact]
    public async Task GitInit_BackupPreexisting_FailsLoud()
    {
        var repo = InitGitRepo("backup-exists-repo");
        using var _ = CwdScope.New(repo);

        var hookPath = PostCommitPath(repo);
        var foreignContent = "#!/bin/sh\necho 'custom'\n";
        File.WriteAllText(hookPath, foreignContent, new UTF8Encoding(false));
        var backupPath = hookPath + ".engram-backup";
        File.WriteAllText(backupPath, "stale backup", new UTF8Encoding(false));

        var (exitCode, _, stderr) = await InvokeAsync(BuildCommandTree(), new[] { "init" });

        Assert.NotEqual(0, exitCode);
        Assert.Contains("already exists", stderr);
        Assert.Equal(foreignContent, File.ReadAllText(hookPath));
        Assert.Equal("stale backup", File.ReadAllText(backupPath));
    }

    [Fact]
    public async Task GitInit_ReinitOwnHook_Idempotent()
    {
        var repo = InitGitRepo("reinit-repo");
        using var _ = CwdScope.New(repo);

        var (exit1, _, _) = await InvokeAsync(BuildCommandTree(), new[] { "init" });
        Assert.Equal(0, exit1);

        var (exit2, _, _) = await InvokeAsync(BuildCommandTree(), new[] { "init" });
        Assert.Equal(0, exit2);

        var hookPath = PostCommitPath(repo);
        Assert.False(File.Exists(hookPath + ".engram-backup"), "re-init must not create a backup");
        Assert.True(File.Exists(hookPath));
        Assert.Contains("# Generated by engram init", File.ReadAllText(hookPath));
    }

    [Fact]
    public async Task GitInit_InteractiveInstallsBothHooks()
    {
        var repo = InitGitRepo("interactive-repo");
        using var _ = CwdScope.New(repo);

        var (exitCode, stdout, _) = await InvokeAsync(BuildCommandTree(), new[] { "init", "--interactive" });

        Assert.Equal(0, exitCode);

        var postCommit = PostCommitPath(repo);
        var prepareMsg = PrepareCommitMsgPath(repo);
        Assert.True(File.Exists(postCommit));
        Assert.True(File.Exists(prepareMsg));
        Assert.True(IsExecutable(postCommit));
        Assert.True(IsExecutable(prepareMsg));

        Assert.Contains("if [ ! -f \"$MARKER\" ]", File.ReadAllText(postCommit));
        Assert.Contains("¿Guardar este commit", File.ReadAllText(prepareMsg));

        Assert.Contains("post-commit (auto-capture with interactive prompt)", stdout);
        Assert.Contains("prepare-commit-msg (interactive prompt)", stdout);
    }

    [Fact]
    public async Task GitInit_UnsafeProjectName_FailsLoud()
    {
        var repo = InitGitRepo("unsafe-repo");
        using var _ = CwdScope.New(repo);

        var (exitCode, _, stderr) = await InvokeAsync(BuildCommandTree(), new[] { "init", "--project", "my'project" });

        Assert.NotEqual(0, exitCode);
        Assert.Contains("--project <safe-name>", stderr);
        Assert.False(File.Exists(PostCommitPath(repo)), "no hook should be written on unsafe project name");
    }

    [Fact]
    public async Task HookExecution_CapturesCommit()
    {
        var repo = InitGitRepo("capture-repo");
        using var _ = CwdScope.New(repo);

        var (initExit, _, _) = await InvokeAsync(BuildCommandTree(), new[] { "init", "--project", "test-proj" });
        Assert.Equal(0, initExit);

        var (shimPath, logPath) = CreateShim(_tempDir);
        var prev = Environment.GetEnvironmentVariable("ENGRAM_BIN");
        Environment.SetEnvironmentVariable("ENGRAM_BIN", shimPath);
        try
        {
            CommitFromMessage(repo, "test: add feature X");
        }
        finally
        {
            Environment.SetEnvironmentVariable("ENGRAM_BIN", prev);
        }

        Assert.True(File.Exists(logPath), "shim was never invoked");
        var log = File.ReadAllText(logPath);

        Assert.Single(Regex.Matches(log, "ENG-INVOKE"));
        Assert.Contains("ENG-INVOKE\nsave\n", log);
        Assert.Contains("commit: test: add feature X", log);
        Assert.Contains("Commit: ", log);
        Assert.Contains("Changed files: ", log);
        Assert.Contains("Full message: test: add feature X", log);
        Assert.Contains("--type\ncommit", log);
        Assert.Contains("--project\ntest-proj", log);
    }

    [Fact]
    public async Task HookExecution_InjectionRegression()
    {
        // Clean up any residue from a prior run
        if (File.Exists("/tmp/engram-pwned")) File.Delete("/tmp/engram-pwned");

        var repo = InitGitRepo("inject-repo");
        using var _ = CwdScope.New(repo);

        var (initExit, _, _) = await InvokeAsync(BuildCommandTree(), new[] { "init", "--project", "test-proj" });
        Assert.Equal(0, initExit);

        var (shimPath, logPath) = CreateShim(_tempDir);
        var prev = Environment.GetEnvironmentVariable("ENGRAM_BIN");
        Environment.SetEnvironmentVariable("ENGRAM_BIN", shimPath);
        try
        {
            var msg = "fix: \"$(touch /tmp/engram-pwned)\" `cmd`";
            CommitFromMessage(repo, msg);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ENGRAM_BIN", prev);
        }

        Assert.False(File.Exists("/tmp/engram-pwned"), "command substitution in commit message was executed");

        Assert.True(File.Exists(logPath));
        var log = File.ReadAllText(logPath);
        Assert.Contains("$(touch /tmp/engram-pwned)", log);
        Assert.Contains("`cmd`", log);

        // NFR-003: hooks are POSIX-clean (dash-safe) — `sh -n` passes with no bashisms
        Assert.Equal(0, ShSyntaxCheck(PostCommitPath(repo)));
    }
}
