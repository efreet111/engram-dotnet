using Engram.Store;
using Xunit;

namespace Engram.Cli.Tests;

/// <summary>
/// Tests for HU-013 YAML sync project configuration (SyncProjectsConfig).
/// Validates loading, parsing, saving/roundtrip, merge logic, and atomic writes.
/// </summary>
public sealed class SyncProjectsConfigTests : IDisposable
{
    private readonly string _tempDir;

    public SyncProjectsConfigTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "engram-config-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    private string TempFile(string name) => Path.Combine(_tempDir, name);

    // ─── Load tests ──────────────────────────────────────────────────────────

    /// <summary>
    /// Loading from a non-existent file path returns default config (fail-loud behavior).
    /// </summary>
    [Fact]
    public void Load_MissingFile_ReturnsDefaultConfig()
    {
        var missingPath = TempFile("nonexistent.yml");
        // Ensure file doesn't exist
        if (File.Exists(missingPath))
            File.Delete(missingPath);

        var config = SyncProjectsConfig.Load(missingPath);

        Assert.NotNull(config);
        Assert.Equal("fail-loud", config.DefaultBehavior);
        Assert.NotNull(config.Projects);
        Assert.Empty(config.Projects);
    }

    /// <summary>
    /// Loading from a valid YAML file parses all fields correctly.
    /// </summary>
    [Fact]
    public void Load_ValidFile_ParsesCorrectly()
    {
        var yaml = @"
default_behavior: silent-skip
projects:
  - name: project-alpha
    behavior: fail-loud
    excluded_servers:
      - server-a
      - server-b
  - name: project-beta
    behavior: silent-skip
    excluded_servers: []
";
        var path = TempFile("valid-config.yml");
        File.WriteAllText(path, yaml);

        var config = SyncProjectsConfig.Load(path);

        Assert.Equal("silent-skip", config.DefaultBehavior);
        Assert.Equal(2, config.Projects.Count);

        var alpha = config.Projects.Find(p => p.Name == "project-alpha");
        Assert.NotNull(alpha);
        Assert.Equal("fail-loud", alpha.Behavior);
        Assert.Equal(2, alpha.ExcludedServers.Count);
        Assert.Contains("server-a", alpha.ExcludedServers);
        Assert.Contains("server-b", alpha.ExcludedServers);

        var beta = config.Projects.Find(p => p.Name == "project-beta");
        Assert.NotNull(beta);
        Assert.Equal("silent-skip", beta.Behavior);
        Assert.Empty(beta.ExcludedServers);
    }

    /// <summary>
    /// Loading a YAML file with only default_behavior and no projects works.
    /// </summary>
    [Fact]
    public void Load_DefaultBehaviorOnly_ParsesCorrectly()
    {
        var yaml = "default_behavior: silent-skip\n";
        var path = TempFile("minimal-config.yml");
        File.WriteAllText(path, yaml);

        var config = SyncProjectsConfig.Load(path);

        Assert.Equal("silent-skip", config.DefaultBehavior);
        Assert.Empty(config.Projects);
    }

    /// <summary>
    /// Loading an invalid/corrupted YAML file falls back to default config.
    /// </summary>
    [Fact]
    public void Load_InvalidYaml_ReturnsDefaultConfig()
    {
        var yaml = "this is: not valid } yaml: [";
        var path = TempFile("invalid.yml");
        File.WriteAllText(path, yaml);

        // Redirect stderr to suppress warning output during test
        var originalError = Console.Error;
        var errorCapture = new StringWriter();
        Console.SetError(errorCapture);
        try
        {
            var config = SyncProjectsConfig.Load(path);

            Assert.NotNull(config);
            Assert.Equal("fail-loud", config.DefaultBehavior);
            Assert.Empty(config.Projects);

            var errorOutput = errorCapture.ToString();
            Assert.Contains("Warning: Failed to load sync config", errorOutput, StringComparison.Ordinal);
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    // ─── Save + Roundtrip tests ──────────────────────────────────────────────

    /// <summary>
    /// Saving a config and loading it back returns the same data (roundtrip).
    /// </summary>
    [Fact]
    public void Save_RoundTripsCorrectly()
    {
        var path = TempFile("roundtrip.yml");
        var original = new SyncProjectsConfig
        {
            DefaultBehavior = "silent-skip",
            Projects = new List<SyncProjectEntry>
            {
                new() { Name = "proj-1", Behavior = "fail-loud", ExcludedServers = new List<string> { "s1", "s2" } },
                new() { Name = "proj-2", Behavior = "silent-skip", ExcludedServers = new List<string>() },
            }
        };

        // Save
        original.Save(path);
        Assert.True(File.Exists(path));

        // Load back
        var loaded = SyncProjectsConfig.Load(path);

        Assert.Equal(original.DefaultBehavior, loaded.DefaultBehavior);
        Assert.Equal(original.Projects.Count, loaded.Projects.Count);

        foreach (var originalEntry in original.Projects)
        {
            var loadedEntry = loaded.Projects.Find(p => p.Name == originalEntry.Name);
            Assert.NotNull(loadedEntry);
            Assert.Equal(originalEntry.Behavior, loadedEntry.Behavior);
            Assert.Equal(originalEntry.ExcludedServers, loadedEntry.ExcludedServers);
        }
    }

    /// <summary>
    /// After saving, the file contains the expected YAML structure.
    /// </summary>
    [Fact]
    public void Save_ProducesValidYaml()
    {
        var path = TempFile("output.yml");
        var config = new SyncProjectsConfig
        {
            DefaultBehavior = "fail-loud",
            Projects = new List<SyncProjectEntry>
            {
                new() { Name = "engram-dotnet", Behavior = "silent-skip", ExcludedServers = new List<string> { "nas" } }
            }
        };

        config.Save(path);

        var yaml = File.ReadAllText(path);

        Assert.Contains("default_behavior: fail-loud", yaml, StringComparison.Ordinal);
        Assert.Contains("name: engram-dotnet", yaml, StringComparison.Ordinal);
        Assert.Contains("behavior: silent-skip", yaml, StringComparison.Ordinal);
        Assert.Contains("nas", yaml, StringComparison.Ordinal);
    }

    /// <summary>
    /// Saving an empty config (defaults) produces valid minimal YAML.
    /// </summary>
    [Fact]
    public void Save_EmptyConfig_ProducesMinimalYaml()
    {
        var path = TempFile("empty.yml");
        var config = new SyncProjectsConfig();

        config.Save(path);

        var yaml = File.ReadAllText(path);
        Assert.Contains("default_behavior: fail-loud", yaml, StringComparison.Ordinal);
        Assert.DoesNotContain("projects:", yaml, StringComparison.Ordinal);
    }

    // ─── MergeWithDb tests ──────────────────────────────────────────────────

    /// <summary>
    /// YAML excluded_servers takes precedence over DB values.
    /// YAML behavior also takes precedence if set.
    /// </summary>
    [Fact]
    public void MergeWithDb_YamlTakesPrecedence()
    {
        var yamlConfig = new SyncProjectsConfig
        {
            DefaultBehavior = "silent-skip",
            Projects = new List<SyncProjectEntry>
            {
                new() { Name = "proj-a", Behavior = "silent-skip", ExcludedServers = new List<string> { "yaml-server" } },
                new() { Name = "proj-b", Behavior = "silent-skip", ExcludedServers = new List<string>() },
            }
        };

        var dbEnrollments = new List<EnrolledProjectLocal>
        {
            new("proj-a", "fail-loud", DateTime.UtcNow.ToString("O")),
            new("proj-b", "fail-loud", DateTime.UtcNow.ToString("O")),
            new("proj-c", "fail-loud", DateTime.UtcNow.ToString("O")),
        };

        var merged = yamlConfig.MergeWithDb(dbEnrollments);

        // proj-a: YAML behavior takes precedence, YAML excluded_servers used
        var entryA = merged.FindEntry("proj-a");
        Assert.NotNull(entryA);
        Assert.Equal("silent-skip", entryA.Behavior);
        Assert.Single(entryA.ExcludedServers);
        Assert.Equal("yaml-server", entryA.ExcludedServers[0]);

        // proj-b: YAML behavior takes precedence
        var entryB = merged.FindEntry("proj-b");
        Assert.NotNull(entryB);
        Assert.Equal("silent-skip", entryB.Behavior);

        // proj-c: only in DB, uses DB behavior (fail-loud)
        var entryC = merged.FindEntry("proj-c");
        Assert.NotNull(entryC);
        Assert.Equal("fail-loud", entryC.Behavior);
        Assert.Empty(entryC.ExcludedServers);
    }

    /// <summary>
    /// When YAML entry has no explicit behavior, falls back to DB behavior.
    /// </summary>
    [Fact]
    public void MergeWithDb_YamlWithoutBehavior_UsesDbBehavior()
    {
        var yamlConfig = new SyncProjectsConfig
        {
            DefaultBehavior = "fail-loud",
            Projects = new List<SyncProjectEntry>
            {
                new() { Name = "proj-d", Behavior = "", ExcludedServers = new List<string> { "srv" } },
            }
        };

        var dbEnrollments = new List<EnrolledProjectLocal>
        {
            new("proj-d", "silent-skip", DateTime.UtcNow.ToString("O")),
        };

        var merged = yamlConfig.MergeWithDb(dbEnrollments);

        var entry = merged.FindEntry("proj-d");
        Assert.NotNull(entry);
        // Empty YAML behavior should fall back to DB behavior
        Assert.Equal("silent-skip", entry.Behavior);
        Assert.Equal("srv", entry.ExcludedServers[0]);
    }

    /// <summary>
    /// GetBehavior from YAML: entry-specific behavior wins over DefaultBehavior.
    /// </summary>
    [Fact]
    public void GetBehavior_EntryOverridesDefault()
    {
        var config = new SyncProjectsConfig
        {
            DefaultBehavior = "fail-loud",
            Projects = new List<SyncProjectEntry>
            {
                new() { Name = "override-proj", Behavior = "silent-skip" },
            }
        };

        Assert.Equal("silent-skip", config.GetBehavior("override-proj"));
        Assert.Equal("fail-loud", config.GetBehavior("unknown-proj"));
    }

    // ─── Atomic write tests ─────────────────────────────────────────────────

    /// <summary>
    /// Atomic write: data is written to .tmp first, then moved to final path.
    /// If the final file exists, it gets overwritten.
    /// </summary>
    [Fact]
    public void AtomicWrite_WritesSafely()
    {
        var path = TempFile("atomic.yml");
        var tmpPath = path + ".tmp";

        var config = new SyncProjectsConfig
        {
            DefaultBehavior = "fail-loud",
            Projects = new List<SyncProjectEntry>
            {
                new() { Name = "atomic-test", Behavior = "silent-skip" }
            }
        };

        // Ensure no temp file from previous runs
        if (File.Exists(tmpPath))
            File.Delete(tmpPath);

        config.Save(path);

        // Verify final file exists
        Assert.True(File.Exists(path));

        // Verify .tmp file was cleaned up (atomic write removes it after move)
        Assert.False(File.Exists(tmpPath));

        // Verify content is correct
        var loaded = SyncProjectsConfig.Load(path);
        Assert.Equal("fail-loud", loaded.DefaultBehavior);
        var entry = loaded.FindEntry("atomic-test");
        Assert.NotNull(entry);
        Assert.Equal("silent-skip", entry.Behavior);
    }

    /// <summary>
    /// Atomic write overwrites existing file without corruption.
    /// </summary>
    [Fact]
    public void AtomicWrite_OverwritesExistingFile()
    {
        var path = TempFile("overwrite.yml");

        // Write initial version
        var v1 = new SyncProjectsConfig
        {
            DefaultBehavior = "fail-loud",
            Projects = new List<SyncProjectEntry>
            {
                new() { Name = "v1", Behavior = "fail-loud" }
            }
        };
        v1.Save(path);

        // Overwrite with new version
        var v2 = new SyncProjectsConfig
        {
            DefaultBehavior = "silent-skip",
            Projects = new List<SyncProjectEntry>
            {
                new() { Name = "v2", Behavior = "silent-skip" }
            }
        };
        v2.Save(path);

        // Verify v1 is gone, v2 is current
        var loaded = SyncProjectsConfig.Load(path);
        Assert.Equal("silent-skip", loaded.DefaultBehavior);
        Assert.Null(loaded.FindEntry("v1"));
        var entry = loaded.FindEntry("v2");
        Assert.NotNull(entry);
        Assert.Equal("silent-skip", entry.Behavior);
    }

    /// <summary>
    /// If .tmp file somehow remains from a previous crash, the next Save cleans it up.
    /// </summary>
    [Fact]
    public void AtomicWrite_CleansUpStaleTmp()
    {
        var path = TempFile("stale-tmp.yml");
        var tmpPath = path + ".tmp";

        // Simulate a stale .tmp file
        File.WriteAllText(tmpPath, "stale content");

        var config = new SyncProjectsConfig { DefaultBehavior = "fail-loud" };
        config.Save(path);

        // After save, .tmp should be gone and final file correct
        Assert.False(File.Exists(tmpPath));
        Assert.True(File.Exists(path));

        var loaded = SyncProjectsConfig.Load(path);
        Assert.Equal("fail-loud", loaded.DefaultBehavior);
    }

    // ─── EnsureEntry / RemoveEntry tests ─────────────────────────────────────

    [Fact]
    public void EnsureEntry_CreatesNewEntryWithDefaultBehavior()
    {
        var config = new SyncProjectsConfig { DefaultBehavior = "silent-skip" };

        var entry = config.EnsureEntry("new-project");

        Assert.NotNull(entry);
        Assert.Equal("new-project", entry.Name);
        Assert.Equal("silent-skip", entry.Behavior);
        Assert.Empty(entry.ExcludedServers);
        Assert.Single(config.Projects);
    }

    [Fact]
    public void EnsureEntry_ReturnsExistingEntry()
    {
        var config = new SyncProjectsConfig
        {
            Projects = new List<SyncProjectEntry>
            {
                new() { Name = "existing", Behavior = "fail-loud", ExcludedServers = new List<string> { "srv" } }
            }
        };

        var entry = config.EnsureEntry("existing");

        Assert.Equal("fail-loud", entry.Behavior);
        Assert.Single(entry.ExcludedServers);
        Assert.Single(config.Projects); // did not duplicate
    }

    [Fact]
    public void RemoveEntry_RemovesAndReturnsTrue()
    {
        var config = new SyncProjectsConfig
        {
            Projects = new List<SyncProjectEntry>
            {
                new() { Name = "to-remove", Behavior = "fail-loud" }
            }
        };

        Assert.True(config.RemoveEntry("to-remove"));
        Assert.Empty(config.Projects);
        Assert.Null(config.FindEntry("to-remove"));
    }

    [Fact]
    public void RemoveEntry_ReturnsFalseForNonexistent()
    {
        var config = new SyncProjectsConfig();
        Assert.False(config.RemoveEntry("nonexistent"));
    }
}
