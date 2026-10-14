using System.CommandLine;
using Engram.Cli;
using Xunit;

namespace Engram.Cli.Tests;

/// <summary>
/// HU-053 (ENG-483): Tests for `engram watch` — code-aware memory capture.
/// Mirrors GitInitTests/ProjectIdCliTests patterns:
///   - Pure functions tested directly (internal static, exposed via InternalsVisibleTo)
///   - Command-tree rebuild with redirected stdout/stderr for exit-code assertions
///   - No real <see cref="System.IO.FileSystemWatcher"/> tests (flaky — PM-* covers the real behavior)
/// </summary>
[Collection("CwdSensitive")]
public sealed class FileWatchTests : IDisposable
{
    private readonly string _tempDir;

    public FileWatchTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "engram-filewatch-tests", Guid.NewGuid().ToString("N"));
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
        root.Subcommands.Add(WatchCommand.CreateCommand());
        return root;
    }

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

    // ─── CountChangedLines ────────────────────────────────────────────────

    [Fact]
    public void CountChangedLines_IdenticalFiles_ReturnsZero()
    {
        var lines = new[] { "a", "b", "c" };

        var (modified, added, removed) = WatchCommand.CountChangedLines(lines, lines);

        Assert.Equal(0, modified);
        Assert.Equal(0, added);
        Assert.Equal(0, removed);
    }

    [Fact]
    public void CountChangedLines_PositionalDiff_CorrectCounts()
    {
        var oldLines = new[] { "a", "b", "c" };
        var newLines = new[] { "a", "x", "c", "d" };

        var (modified, added, removed) = WatchCommand.CountChangedLines(oldLines, newLines);

        Assert.Equal(1, modified); // "b" → "x" at index 1
        Assert.Equal(1, added);    // "d" at index 3
        Assert.Equal(0, removed);
    }

    [Fact]
    public void CountChangedLines_AddedAndRemoved_CorrectCounts()
    {
        var oldLines = new[] { "1", "2", "3", "4" };
        var newLines = new[] { "1", "2" };

        var (modified, added, removed) = WatchCommand.CountChangedLines(oldLines, newLines);

        Assert.Equal(0, modified);
        Assert.Equal(0, added);
        Assert.Equal(2, removed); // "3", "4" removed
    }

    // ─── IsBinaryFile ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("lib.dll")]
    [InlineData("image.png")]
    [InlineData("archive.zip")]
    [InlineData("app.exe")]
    public void IsBinaryFile_KnownExtensions_ReturnsTrue(string fileName)
    {
        Assert.True(WatchCommand.IsBinaryFile(fileName));
    }

    [Theory]
    [InlineData("Program.cs")]
    [InlineData("README.md")]
    [InlineData("notes.txt")]
    [InlineData("config.json")]
    public void IsBinaryFile_TextExtensions_ReturnsFalse(string fileName)
    {
        Assert.False(WatchCommand.IsBinaryFile(fileName));
    }

    // ─── ResolveTargets ───────────────────────────────────────────────────

    [Fact]
    public void ResolveTargets_FileDirGlob_ExpandsCorrectly()
    {
        var aCs = Path.Combine(_tempDir, "a.cs");
        var bLog = Path.Combine(_tempDir, "b.log");
        var cPng = Path.Combine(_tempDir, "c.png");
        File.WriteAllText(aCs, "class A {}");
        File.WriteAllText(bLog, "log line");
        File.WriteAllText(cPng, "fake-png-bytes");

        // Directory target with ignore pattern: only a.cs remains (b.log ignored, c.png binary-skipped)
        var dirResult = WatchCommand.ResolveTargets(new[] { _tempDir }, new[] { "*.log" }, _tempDir);
        Assert.Single(dirResult);
        Assert.Equal(Path.GetFullPath(aCs), dirResult[0]);

        // Glob target: only *.cs matches
        var globResult = WatchCommand.ResolveTargets(new[] { Path.Combine(_tempDir, "*.cs") }, [], _tempDir);
        Assert.Single(globResult);
        Assert.Equal(Path.GetFullPath(aCs), globResult[0]);

        // Explicit single-file target
        var fileResult = WatchCommand.ResolveTargets(new[] { aCs }, [], _tempDir);
        Assert.Single(fileResult);
        Assert.Equal(Path.GetFullPath(aCs), fileResult[0]);
    }

    [Fact]
    public void ResolveTargets_NotExists_ThrowsFileNotFound()
    {
        var missing = Path.Combine(_tempDir, "does-not-exist.cs");

        var ex = Assert.Throws<FileNotFoundException>(
            () => WatchCommand.ResolveTargets(new[] { missing }, [], _tempDir));

        Assert.Contains("File not found", ex.Message);
    }

    // ─── MatchesIgnorePattern ─────────────────────────────────────────────

    [Fact]
    public void MatchesIgnorePattern_NameAndRelPath_MatchesBoth()
    {
        var patterns = new[] { "*.log" };

        // filename match
        Assert.True(WatchCommand.MatchesIgnorePattern(
            Path.Combine(_tempDir, "foo.log"), _tempDir, patterns));

        // nested file — still matches by filename
        var nested = Path.Combine(_tempDir, "sub", "dir");
        Directory.CreateDirectory(nested);
        Assert.True(WatchCommand.MatchesIgnorePattern(
            Path.Combine(nested, "foo.log"), _tempDir, patterns));

        // non-matching name
        Assert.False(WatchCommand.MatchesIgnorePattern(
            Path.Combine(_tempDir, "foo.cs"), _tempDir, patterns));
    }

    [Fact]
    public void MatchesIgnorePattern_RelativePathPattern_Matches()
    {
        var nested = Path.Combine(_tempDir, "sub", "dir");
        Directory.CreateDirectory(nested);
        var file = Path.Combine(nested, "notes.txt");
        File.WriteAllText(file, "x");

        // relative-path glob: "sub/*.log" matches "sub/notes.log" (relpath), not by filename
        Assert.True(WatchCommand.MatchesIgnorePattern(file, _tempDir, new[] { "sub/*.txt" }));
        Assert.False(WatchCommand.MatchesIgnorePattern(file, _tempDir, new[] { "*.log" }));
    }

    // ─── NormalizeRelPath ─────────────────────────────────────────────────

    [Fact]
    public void NormalizeRelPath_UnderCwd_ReturnsRelativeWithForwardSlash()
    {
        var cwd = Path.Combine(_tempDir, "proj");
        var file = Path.Combine(cwd, "src", "Auth", "JwtBearer.cs");

        var rel = WatchCommand.NormalizeRelPath(file, cwd);

        Assert.Equal("src/Auth/JwtBearer.cs", rel);
    }

    [Fact]
    public void NormalizeRelPath_OutsideCwd_ReturnsAbsolute()
    {
        var cwd = Path.Combine(_tempDir, "proj");
        var file = Path.Combine(_tempDir, "other", "file.cs");

        var rel = WatchCommand.NormalizeRelPath(file, cwd);

        Assert.Equal(Path.GetFullPath(file).Replace('\\', '/'), rel);
    }

    // ─── Command-tree validation ──────────────────────────────────────────

    [Fact]
    public async Task WatchCommand_ThresholdZero_ExitCode1()
    {
        var (exitCode, _, stderr) = await InvokeAsync(
            BuildCommandTree(),
            new[] { "watch", "file.cs", "--threshold", "0" });

        Assert.NotEqual(0, exitCode);
        Assert.Contains("--threshold must be a positive integer", stderr);
    }
}
