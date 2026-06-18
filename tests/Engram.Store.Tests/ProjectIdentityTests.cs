using System.Diagnostics;
using Engram.Store;
using Xunit;

namespace Engram.Store.Tests;

/// <summary>
/// ENG-431: Validación de consistencia del GUID en .engram-id.
/// </summary>
public class ProjectIdentityTests : IDisposable
{
    private readonly string _tempDir;

    public ProjectIdentityTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "engram-identity-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    // ─── Scenario 1: GUID coincide (normal) ───────────────────────────────

    [Fact]
    public void Validate_MatchingGuid_ReturnsMatch()
    {
        var repo = InitGitRepo(_tempDir, "match-repo", commitMessage: "init");

        // Compute the expected GUID from the same remote
        var remoteUrl = GetRemoteUrl(repo);
        var firstSha = GetFirstCommitSha(repo);
        var expected = ProjectIdentity.ComputeProjectId(remoteUrl, firstSha).ToString("D");

        File.WriteAllText(Path.Combine(repo, ".engram-id"), expected);

        var result = ProjectIdentity.Validate(repo, expected);

        Assert.NotNull(result);
        Assert.True(result.Match);
        Assert.Equal(expected, result.FileGuid);
        Assert.Equal(expected, result.ComputedGuid);
    }

    // ─── Scenario 2: GUID editado manualmente ─────────────────────────────

    [Fact]
    public void Validate_MismatchedGuid_ReturnsMismatchAndDoesNotThrow()
    {
        var repo = InitGitRepo(_tempDir, "mismatch-repo", commitMessage: "init");

        // Pre-seed an arbitrary GUID — won't match the deterministic one
        var fakeId = "11111111-1111-5111-8111-111111111111";

        // Capture stderr to verify the warning is logged
        var originalErr = Console.Error;
        var stringWriter = new StringWriter();
        Console.SetError(stringWriter);
        try
        {
            var result = ProjectIdentity.Validate(repo, fakeId);

            // Non-strict mode: result is returned, not thrown
            Assert.NotNull(result);
            Assert.False(result.Match);
            Assert.Equal(fakeId, result.FileGuid);
            Assert.NotEqual(fakeId, result.ComputedGuid);
        }
        finally
        {
            Console.SetError(originalErr);
        }

        // Warning should be logged to stderr
        var stderr = stringWriter.ToString();
        Assert.Contains("project_id mismatch", stderr);
        Assert.Contains(fakeId, stderr);
    }

    // ─── Scenario 3: Modo estricto (CI) ────────────────────────────────────

    [Fact]
    public void Validate_MismatchedGuid_StrictMode_Throws()
    {
        var repo = InitGitRepo(_tempDir, "strict-repo", commitMessage: "init");

        var fakeId = "22222222-2222-5222-8222-222222222222";
        File.WriteAllText(Path.Combine(repo, ".engram-id"), fakeId);

        var previousStrict = Environment.GetEnvironmentVariable("ENGRAM_STRICT_PROJECT_ID");
        Environment.SetEnvironmentVariable("ENGRAM_STRICT_PROJECT_ID", "true");
        try
        {
            var ex = Assert.Throws<ProjectIdMismatchException>(() =>
                ProjectIdentity.Validate(repo, fakeId));

            Assert.Equal(fakeId, ex.FileGuid);
            Assert.NotEqual(fakeId, ex.ComputedGuid);
            Assert.Contains("mismatch", ex.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ENGRAM_STRICT_PROJECT_ID", previousStrict);
        }
    }

    // ─── Edge: sin git remote → no se puede validar → null ───────────────

    [Fact]
    public void Validate_NoGitRemote_ReturnsNull()
    {
        // Plain directory, no git init
        var plainDir = Path.Combine(_tempDir, "no-git");
        Directory.CreateDirectory(plainDir);

        var result = ProjectIdentity.Validate(plainDir, "33333333-3333-5333-8333-333333333333");

        Assert.Null(result);
    }

    // ─── Edge: GUID en archivo inválido → Guid.Parse throws ───────────────

    [Fact]
    public void Validate_InvalidGuidFromFile_ThrowsFormatException()
    {
        var repo = InitGitRepo(_tempDir, "invalid-guid-repo", commitMessage: "init");

        // Sanity: with a real remote and valid GUID, returns a result
        var remoteUrl = GetRemoteUrl(repo);
        var firstSha = GetFirstCommitSha(repo);
        var validGuid = ProjectIdentity.ComputeProjectId(remoteUrl, firstSha).ToString("D");
        Assert.NotNull(ProjectIdentity.Validate(repo, validGuid));

        // An invalid GUID string should throw FormatException on Guid.Parse
        Assert.Throws<FormatException>(() =>
            ProjectIdentity.Validate(repo, "not-a-valid-guid"));
    }

    // ─── ENG-433: TryAutoEnroll ─────────────────────────────────────────

    [Fact]
    public void TryAutoEnroll_NoExistingFile_GeneratesId()
    {
        var repo = InitGitRepo(_tempDir, "auto-enroll-1", commitMessage: "init");

        var result = ProjectIdentity.TryAutoEnroll(repo, out var generatedId);

        Assert.True(result);
        Assert.NotNull(generatedId);
        Assert.True(Guid.TryParse(generatedId, out var parsed));
        Assert.NotEqual(Guid.Empty, parsed);

        // .engram-id should exist now
        var idFile = Path.Combine(repo, ".engram-id");
        Assert.True(File.Exists(idFile));
        Assert.Equal(generatedId, File.ReadAllText(idFile).Trim());
    }

    [Fact]
    public void TryAutoEnroll_ExistingFile_DoesNotOverwrite()
    {
        var repo = InitGitRepo(_tempDir, "auto-enroll-2", commitMessage: "init");

        // Pre-seed a fake GUID
        var fakeId = "11111111-1111-5111-8111-111111111111";
        File.WriteAllText(Path.Combine(repo, ".engram-id"), fakeId);

        var result = ProjectIdentity.TryAutoEnroll(repo, out var generatedId);

        Assert.False(result);
        Assert.Null(generatedId);

        // .engram-id should still have the fake ID
        Assert.Equal(fakeId, File.ReadAllText(Path.Combine(repo, ".engram-id")).Trim());
    }

    [Fact]
    public void TryAutoEnroll_NoGitRepo_ReturnsFalse()
    {
        var plainDir = Path.Combine(_tempDir, "no-git-auto-enroll");
        Directory.CreateDirectory(plainDir);

        var result = ProjectIdentity.TryAutoEnroll(plainDir, out var generatedId);

        Assert.False(result);
        Assert.Null(generatedId);

        // .engram-id should NOT be created
        Assert.False(File.Exists(Path.Combine(plainDir, ".engram-id")));
    }

    // ─── Helpers ──────────────────────────────────────────────────────────

    /// <summary>Initializes a git repo with origin remote and a single commit, returns its path.</summary>
    private static string InitGitRepo(string parent, string name, string commitMessage)
    {
        var repo = Path.Combine(parent, name);
        Directory.CreateDirectory(repo);
        RunGit(repo, "init");
        RunGit(repo, "config user.email test@test.local");
        RunGit(repo, "config user.name \"Test User\"");
        // Add a remote — required for Validate to compute the deterministic GUID
        RunGit(repo, $"remote add origin git@github.com:test/{name}.git");
        File.WriteAllText(Path.Combine(repo, "README.md"), "# test");
        RunGit(repo, "add README.md");
        RunGit(repo, $"commit -m \"{commitMessage}\"");
        return repo;
    }

    private static void RunGit(string workingDir, string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = $"-C \"{workingDir}\" {arguments}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var process = Process.Start(psi)!;
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            var err = process.StandardError.ReadToEnd();
            throw new InvalidOperationException($"git {arguments} failed: {err}");
        }
    }

    private static string GetRemoteUrl(string repo)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = $"-C \"{repo}\" config --get remote.origin.url",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var process = Process.Start(psi)!;
        process.WaitForExit();
        return process.StandardOutput.ReadToEnd().Trim();
    }

    private static string GetFirstCommitSha(string repo)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = $"-C \"{repo}\" rev-list --max-parents=0 --reverse HEAD",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var process = Process.Start(psi)!;
        process.WaitForExit();
        return process.StandardOutput.ReadToEnd().Trim().Split('\n')[0].Trim();
    }
}
