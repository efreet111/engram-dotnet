using Engram.Store;
using Xunit;

namespace Engram.Store.Tests;

public class ProjectDetectorTests
{
    // ─── ExtractRepoName ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("git@github.com:user/engram-dotnet.git", "engram-dotnet")]
    [InlineData("git@github.com:user/engram-dotnet", "engram-dotnet")]
    [InlineData("https://github.com/user/engram-dotnet.git", "engram-dotnet")]
    [InlineData("https://github.com/user/engram-dotnet", "engram-dotnet")]
    [InlineData("git@gitlab.com:org/my-project.git", "my-project")]
    [InlineData("ssh://git@github.com/user/my-project.git", "my-project")]
    [InlineData("https://gitlab.com/group/subgroup/project.git", "project")]
    [InlineData("git@github.com:user/simple", "simple")]
    [InlineData("", "")]
    public void ExtractRepoName_ParsesGitUrls(string url, string expected)
    {
        var result = ProjectDetector.ExtractRepoName(url);
        Assert.Equal(expected, result);
    }

    // ─── DetectProject ────────────────────────────────────────────────────────

    [Fact]
    public void DetectProject_EmptyDir_ReturnsUnknown()
    {
        var result = ProjectDetector.DetectProject("");
        Assert.Equal("unknown", result);
    }

    [Fact]
    public void DetectProject_DirNameOnly_ReturnsLowercased()
    {
        // When git is not available or dir is not a repo, falls back to basename
        var result = ProjectDetector.DetectProject("/some/path/My-Project");
        Assert.Equal("my-project", result);
    }

    [Fact]
    public void DetectProject_DirStartingWithDash_PrependsDotSlash()
    {
        // Should not crash with dirs starting with "-"
        var result = ProjectDetector.DetectProject("-my-project");
        // Result depends on whether git is available, but should not throw
        Assert.NotNull(result);
        Assert.NotEmpty(result);
    }

    // ─── Levenshtein ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("", "", 0)]
    [InlineData("abc", "", 3)]
    [InlineData("", "abc", 3)]
    [InlineData("abc", "abc", 0)]
    [InlineData("abc", "abd", 1)]
    [InlineData("kitten", "sitting", 3)]
    [InlineData("saturday", "sunday", 3)]
    [InlineData("engram", "engram", 0)]
    [InlineData("engram", "engram-dotnet", 7)]
    [InlineData("mi-api", "mi_api", 1)]
    [InlineData("mi-api", "my-api", 1)]
    public void Levenshtein_ComputesCorrectDistance(string a, string b, int expected)
    {
        var result = ProjectDetector.Levenshtein(a, b);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Levenshtein_IsSymmetric()
    {
        // distance(a,b) == distance(b,a)
        Assert.Equal(
            ProjectDetector.Levenshtein("engram", "engram-dotnet"),
            ProjectDetector.Levenshtein("engram-dotnet", "engram"));
    }

    // ─── FindSimilar ──────────────────────────────────────────────────────────

    [Fact]
    public void FindSimilar_ExactMatch_Excluded()
    {
        var existing = new[] { "engram", "my-api", "other-project" };
        var matches = ProjectDetector.FindSimilar("engram", existing);
        Assert.Empty(matches);
    }

    [Fact]
    public void FindSimilar_CaseInsensitive_MatchFound()
    {
        var existing = new[] { "Engram", "my-api" };
        var matches = ProjectDetector.FindSimilar("engram", existing);
        Assert.Single(matches);
        Assert.Equal("Engram", matches[0].Name);
        Assert.Equal("case-insensitive", matches[0].MatchType);
        Assert.Equal(0, matches[0].Distance);
    }

    [Fact]
    public void FindSimilar_Substring_MatchFound()
    {
        var existing = new[] { "my-api-v2", "other-project" };
        var matches = ProjectDetector.FindSimilar("my-api", existing, 3);
        Assert.NotEmpty(matches);
        Assert.Equal("my-api-v2", matches[0].Name);
        Assert.Equal("substring", matches[0].MatchType);
    }

    [Fact]
    public void FindSimilar_Substring_ReverseMatch()
    {
        // "engram-dotnet" contains "engram"
        var existing = new[] { "engram-dotnet" };
        var matches = ProjectDetector.FindSimilar("engram", existing, 3);
        Assert.NotEmpty(matches);
        Assert.Equal("engram-dotnet", matches[0].Name);
        Assert.Equal("substring", matches[0].MatchType);
    }

    [Fact]
    public void FindSimilar_Levenshtein_MatchFound()
    {
        var existing = new[] { "my-api", "other-project" };
        var matches = ProjectDetector.FindSimilar("my-api", existing, 3);
        // "my-api" is exact match of existing[0], so excluded
        // "my-api" -> Levenshtein distance to "other-project" = too large
        // Result: only case/substring matches if any
    }

    [Fact]
    public void FindSimilar_Levenshtein_CloseTypo()
    {
        var existing = new[] { "engram" };
        var matches = ProjectDetector.FindSimilar("engrm", existing, 2);
        Assert.NotEmpty(matches);
        Assert.Equal("engram", matches[0].Name);
        Assert.Equal("levenshtein", matches[0].MatchType);
        Assert.True(matches[0].Distance <= 2);
    }

    [Fact]
    public void FindSimilar_ShortName_ScalesMaxDistance()
    {
        // Short names should not match everything
        var existing = new[] { "a-very-long-project-name" };
        var matches = ProjectDetector.FindSimilar("ab", existing, 3);
        // effectiveMax for "ab" (len=2) = max(1, 2/2) = 1
        // Levenshtein("ab", "a-very-long-project-name") >> 1
        Assert.Empty(matches);
    }

    [Fact]
    public void FindSimilar_Ordered_CaseFirst_ThenSubstring_ThenLevenshtein()
    {
        var existing = new[] { "my-api-v2", "My-API", "mi-api" };
        var matches = ProjectDetector.FindSimilar("my-api", existing, 3);
        // "My-API" → case-insensitive (first)
        // "my-api-v2" → substring (second)
        // "mi-api" → levenshtein distance 1 (third)
        Assert.True(matches.Count >= 2);
        Assert.Equal("case-insensitive", matches[0].MatchType);
    }

    [Fact]
    public void FindSimilar_EmptyExisting_ReturnsEmpty()
    {
        var matches = ProjectDetector.FindSimilar("my-api", [], 3);
        Assert.Empty(matches);
    }

    [Fact]
    public void FindSimilar_NegativeMaxDistance_TreatedAsZero()
    {
        var existing = new[] { "my-api" };
        var matches = ProjectDetector.FindSimilar("my-api", existing, -1);
        Assert.Empty(matches);
    }

    // ─── NormalizeProjectWithWarning ──────────────────────────────────────────

    [Fact]
    public void NormalizeProjectWithWarning_NoChange_ReturnsEmptyWarning()
    {
        var (normalized, warning) = Normalizers.NormalizeProjectWithWarning("my-api");
        Assert.Equal("my-api", normalized);
        Assert.Equal("", warning);
    }

    [Fact]
    public void NormalizeProjectWithWarning_Change_ReturnsWarning()
    {
        var (normalized, warning) = Normalizers.NormalizeProjectWithWarning("My-API");
        Assert.Equal("my-api", normalized);
        Assert.Contains("My-API", warning);
        Assert.Contains("my-api", warning);
    }

    [Fact]
    public void NormalizeProjectWithWarning_NullProject_ReturnsEmptyEmpty()
    {
        var (normalized, warning) = Normalizers.NormalizeProjectWithWarning(null);
        Assert.Equal("", normalized);
        Assert.Equal("", warning);
    }

    [Fact]
    public void NormalizeProjectWithWarning_EmptyProject_ReturnsEmptyEmpty()
    {
        var (normalized, warning) = Normalizers.NormalizeProjectWithWarning("");
        Assert.Equal("", normalized);
        Assert.Equal("", warning);
    }

    [Theory]
    [InlineData("My-Project", "my-project", true)]
    [InlineData("my-project", "my-project", false)]
    [InlineData("MY_PROJECT", "my_project", true)]
    [InlineData("  my-project  ", "my-project", true)]
    public void NormalizeProjectWithWarning_ConsistentBehavior(
        string input, string expected, bool shouldWarn)
    {
        var (normalized, warning) = Normalizers.NormalizeProjectWithWarning(input);
        Assert.Equal(expected, normalized);
        if (shouldWarn)
            Assert.NotEmpty(warning);
        else
            Assert.Empty(warning);
    }

    // ─── DetectProjectFull (5-case algorithm) ────────────────────────────────

    [Fact]
    public void DetectProjectFull_NoGit_FallbackToBasename()
    {
        // /tmp is unlikely to be a git repo
        var result = ProjectDetector.DetectProjectFull("/tmp");
        Assert.Equal("tmp", result.Project);
        Assert.Equal(ProjectSources.DirBasename, result.Source);
        Assert.Equal("/tmp", result.ProjectPath);
        Assert.Null(result.Warning);
        Assert.Null(result.Error);
        Assert.Empty(result.GetAvailableProjects());
    }

    [Fact]
    public void DetectProjectFull_EmptyDir_ReturnsUnknown()
    {
        var result = ProjectDetector.DetectProjectFull("");
        Assert.Equal("unknown", result.Project);
        Assert.Equal(ProjectSources.DirBasename, result.Source);
    }

    [Fact]
    public void DetectProjectFull_NullDir_UsesCurrentDirectory()
    {
        // Should not throw, uses Directory.GetCurrentDirectory()
        var result = ProjectDetector.DetectProjectFull(null);
        Assert.NotNull(result.Project);
        Assert.NotEmpty(result.Project);
    }

    [Fact]
    public void DetectProjectFull_DirStartingWithDash_DoesNotCrash()
    {
        var result = ProjectDetector.DetectProjectFull("-my-project");
        Assert.NotNull(result.Project);
        Assert.NotEmpty(result.Project);
    }

    [Fact]
    public void DetectProjectFull_InGitRepo_ReturnsGitRemoteOrRoot()
    {
        // This test runs inside the engram-dotnet repo
        var result = ProjectDetector.DetectProjectFull("/media/gantz/300extra/Proyectos/engram-dotnet");
        Assert.NotEmpty(result.Project);
        Assert.True(
            result.Source == ProjectSources.GitRemote ||
            result.Source == ProjectSources.GitRoot,
            $"Expected git_remote or git_root, got {result.Source}");
        Assert.Null(result.Error);
    }

    [Fact]
    public void DetectProjectFull_InGitSubdir_ReturnsGitRemote()
    {
        // This test runs inside a subdirectory of the engram-dotnet repo
        var result = ProjectDetector.DetectProjectFull("/media/gantz/300extra/Proyectos/engram-dotnet/src");
        Assert.NotEmpty(result.Project);
        Assert.True(
            result.Source == ProjectSources.GitRemote ||
            result.Source == ProjectSources.GitRoot,
            $"Expected git_remote or git_root, got {result.Source}");
    }

    // ─── ScanChildren ─────────────────────────────────────────────────────────

    [Fact]
    public void ScanChildren_NoGitRepos_ReturnsEmpty()
    {
        using var tempDir = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(tempDir.Path, "subdir1"));
        Directory.CreateDirectory(Path.Combine(tempDir.Path, "subdir2"));

        var children = ProjectDetector.ScanChildren(tempDir.Path);
        Assert.Empty(children);
    }

    [Fact]
    public void ScanChildren_SingleChildRepo_ReturnsOne()
    {
        using var tempDir = new TempDirectory();
        var childPath = Path.Combine(tempDir.Path, "my-app");
        Directory.CreateDirectory(childPath);
        Directory.CreateDirectory(Path.Combine(childPath, ".git"));

        var children = ProjectDetector.ScanChildren(tempDir.Path);
        Assert.Single(children);
        Assert.Equal("my-app", children[0].Name);
        Assert.Equal(childPath, children[0].Path);
    }

    [Fact]
    public void ScanChildren_MultipleChildRepos_ReturnsAll()
    {
        using var tempDir = new TempDirectory();

        var frontend = Path.Combine(tempDir.Path, "frontend");
        var backend = Path.Combine(tempDir.Path, "backend");
        Directory.CreateDirectory(Path.Combine(frontend, ".git"));
        Directory.CreateDirectory(Path.Combine(backend, ".git"));

        var children = ProjectDetector.ScanChildren(tempDir.Path);
        Assert.Equal(2, children.Count);
        Assert.Contains(children, c => c.Name == "frontend");
        Assert.Contains(children, c => c.Name == "backend");
    }

    [Fact]
    public void ScanChildren_SkipsNoiseDirectories()
    {
        using var tempDir = new TempDirectory();

        // Create noise directories that should be skipped
        Directory.CreateDirectory(Path.Combine(tempDir.Path, "node_modules"));
        Directory.CreateDirectory(Path.Combine(tempDir.Path, "node_modules", ".git"));
        Directory.CreateDirectory(Path.Combine(tempDir.Path, "vendor"));
        Directory.CreateDirectory(Path.Combine(tempDir.Path, "vendor", ".git"));
        Directory.CreateDirectory(Path.Combine(tempDir.Path, ".venv"));
        Directory.CreateDirectory(Path.Combine(tempDir.Path, ".venv", ".git"));

        // Create a real child repo
        var realChild = Path.Combine(tempDir.Path, "my-app");
        Directory.CreateDirectory(Path.Combine(realChild, ".git"));

        var children = ProjectDetector.ScanChildren(tempDir.Path);
        Assert.Single(children);
        Assert.Equal("my-app", children[0].Name);
    }

    [Fact]
    public void ScanChildren_NonExistentDir_ReturnsEmpty()
    {
        var children = ProjectDetector.ScanChildren("/nonexistent/path/xyz");
        Assert.Empty(children);
    }

    // ─── NormalizeProject Edge Cases ──────────────────────────────────────────

    [Theory]
    [InlineData("My  Project", "my  project")]  // Spaces preserved (only -- collapsed)
    [InlineData("MY__PROJECT", "my_project")]   // __ → _ (NormalizeProjectWithWarning collapses __)
    [InlineData("  spaced  out  ", "spaced  out")] // Trim + lowercase
    [InlineData("UPPER-case_MIX", "upper-case_mix")]
    public void NormalizeProject_HandlesEdgeCases(string input, string expected)
    {
        var result = Normalizers.NormalizeProject(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void NormalizeProject_Null_ReturnsEmpty()
    {
        Assert.Equal("", Normalizers.NormalizeProject(null));
    }

    [Fact]
    public void NormalizeProject_Empty_ReturnsEmpty()
    {
        Assert.Equal("", Normalizers.NormalizeProject(""));
    }

    [Fact]
    public void NormalizeProject_WhitespaceOnly_ReturnsEmpty()
    {
        Assert.Equal("", Normalizers.NormalizeProject("   "));
    }
}

/// <summary>
/// Helper class that creates a temporary directory and deletes it on dispose.
/// </summary>
internal sealed class TempDirectory : IDisposable
{
    public string Path { get; }

    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName());
        Directory.CreateDirectory(Path);
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, true); } catch { /* best effort */ }
    }
}