using System;
using System.Collections.Generic;
using System.IO;
using Engram.Store;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

/// <summary>
/// YAML-based sync project configuration (Phase 4, HU-013).
/// Persists per-project sync behavior and excluded servers to
/// ~/.engram/sync-projects.dotnet.yml (overridable via ENGRAM_CONFIG_DIR).
/// 
/// Supplements DB enrollment — behavior lives in DB, excluded_servers
/// and YAML overrides live here. MergeWithDb resolves conflicts with
/// YAML taking precedence for excluded_servers.
/// </summary>
namespace Engram.Cli;

/// <summary>
/// Root configuration for multi-project sync behavior.
/// Serialized to/from ~/.engram/sync-projects.dotnet.yml.
/// </summary>
public sealed record SyncProjectsConfig
{
    /// <summary>
    /// Default sync behavior for projects not explicitly configured.
    /// Valid: "fail-loud" (default), "silent-skip".
    /// </summary>
    public string DefaultBehavior { get; init; } = "fail-loud";

    /// <summary>
    /// Per-project overrides.
    /// </summary>
    public List<SyncProjectEntry> Projects { get; init; } = [];

    // ─── Static helpers ────────────────────────────────────────────────────

    /// <summary>
    /// Resolve the config file path, honoring ENGRAM_CONFIG_DIR override.
    /// Priority: ENGRAM_CONFIG_DIR > ~/.engram
    /// </summary>
    public static string ResolveConfigPath()
    {
        var configDir = Environment.GetEnvironmentVariable("ENGRAM_CONFIG_DIR");
        if (!string.IsNullOrWhiteSpace(configDir))
        {
            Directory.CreateDirectory(configDir);
            return Path.Combine(configDir, "sync-projects.dotnet.yml");
        }

        var baseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".engram");
        Directory.CreateDirectory(baseDir);
        return Path.Combine(baseDir, "sync-projects.dotnet.yml");
    }

    /// <summary>
    /// Load config from the given path. Returns default config if file is missing.
    /// </summary>
    public static SyncProjectsConfig Load(string? path = null)
    {
        path ??= ResolveConfigPath();

        if (!File.Exists(path))
            return new SyncProjectsConfig();

        try
        {
            var yaml = File.ReadAllText(path);
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();

            var config = deserializer.Deserialize<SyncProjectsConfig>(yaml);
            return config ?? new SyncProjectsConfig();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[engram] Warning: Failed to load sync config from {path}: {ex.Message}");
            Console.Error.WriteLine($"[engram] Using default config (fail-loud for all projects).");
            return new SyncProjectsConfig();
        }
    }

    // ─── Instance methods ──────────────────────────────────────────────────

    /// <summary>
    /// Atomic write: serialize to {path}.tmp, then File.Move (overwrite),
    /// then cleanup .tmp on error.
    /// </summary>
    public void Save(string? path = null)
    {
        path ??= ResolveConfigPath();

        var serializer = new SerializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitEmptyCollections)
            .Build();
        var yaml = serializer.Serialize(this);

        var tmpPath = path + ".tmp";
        try
        {
            File.WriteAllText(tmpPath, yaml);
            File.Move(tmpPath, path, overwrite: true);
        }
        catch
        {
            try { File.Delete(tmpPath); } catch { /* best effort */ }
            throw;
        }
    }

    /// <summary>
    /// Merge YAML config with DB enrollments. YAML takes precedence for
    /// excluded_servers; behavior defaults to DB value unless YAML overrides.
    /// </summary>
    public SyncProjectsConfig MergeWithDb(List<EnrolledProjectLocal> enrolledProjects)
    {
        var mergedList = new List<SyncProjectEntry>();
        var processedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var ep in enrolledProjects)
        {
            var yamlEntry = Projects.Find(
                p => string.Equals(p.Name, ep.Project, StringComparison.OrdinalIgnoreCase));

            mergedList.Add(new SyncProjectEntry
            {
                Name = ep.Project,
                Behavior = string.IsNullOrWhiteSpace(yamlEntry?.Behavior) ? ep.Behavior : yamlEntry.Behavior,
                ExcludedServers = yamlEntry?.ExcludedServers ?? [],
            });

            processedNames.Add(ep.Project);
        }

        foreach (var ye in Projects)
        {
            if (!processedNames.Contains(ye.Name))
                mergedList.Add(ye);
        }

        return new SyncProjectsConfig
        {
            DefaultBehavior = DefaultBehavior,
            Projects = mergedList,
        };
    }

    /// <summary>
    /// Get the YAML entry for a project, or null if not configured.
    /// </summary>
    public SyncProjectEntry? FindEntry(string project)
    {
        return Projects.Find(
            p => string.Equals(p.Name, project, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Get the effective behavior for a project from YAML only.
    /// Falls back to DefaultBehavior if not explicitly configured.
    /// </summary>
    public string GetBehavior(string project)
    {
        var entry = FindEntry(project);
        return string.IsNullOrWhiteSpace(entry?.Behavior) ? DefaultBehavior : entry!.Behavior;
    }

    /// <summary>
    /// Ensure a project entry exists in the config and return it.
    /// </summary>
    public SyncProjectEntry EnsureEntry(string project)
    {
        var entry = FindEntry(project);
        if (entry is not null)
            return entry;

        entry = new SyncProjectEntry
        {
            Name = project,
            Behavior = DefaultBehavior,
        };
        Projects.Add(entry);
        return entry;
    }

    /// <summary>
    /// Remove a project entry from the config entirely.
    /// Returns true if it was found and removed.
    /// </summary>
    public bool RemoveEntry(string project)
    {
        var entry = FindEntry(project);
        if (entry is null)
            return false;

        Projects.Remove(entry);
        return true;
    }
}

/// <summary>
/// Per-project sync configuration entry in YAML.
/// </summary>
public sealed record SyncProjectEntry
{
    public string Name { get; init; } = "";
    public string Behavior { get; init; } = "fail-loud";
    public List<string> ExcludedServers { get; init; } = [];
}
