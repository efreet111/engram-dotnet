using System.Text;
using Engram.Store;

namespace Engram.Cli;

/// <summary>
/// HU-023: Persistent deployment-profile configuration at <c>~/.engram/.env</c>.
///
/// Encapsulates the read/write logic for the declarative profile file so the CLI
/// handlers in <c>Program.cs</c> stay thin and the logic is unit-testable without
/// shelling out to the compiled binary.
///
/// The file format matches <c>docker/.env</c>: <c>KEY=value</c> lines with <c>#</c> comments.
/// <c>profile set</c> writes <c>ENGRAM_PROFILE=&lt;name&gt;</c> plus the profile's preset
/// defaults (<see cref="ProfileDefaults.For"/>) so the file is self-documenting.
/// </summary>
public static class ProfileConfig
{
    /// <summary>
    /// Environment variable that selects the deployment profile. Canonical name
    /// read by <see cref="DeployProfileExtensions.FromEnvironment"/>.
    /// </summary>
    public const string ProfileEnvVar = "ENGRAM_PROFILE";

    /// <summary>
    /// Resolves the path to the declarative profile file.
    /// Honors <c>ENGRAM_CONFIG_DIR</c> (same override as <see cref="SyncProjectsConfig"/>,
    /// used by tests); otherwise defaults to <c>~/.engram/.env</c>.
    /// </summary>
    public static string ResolveEnvPath()
    {
        var configDir = Environment.GetEnvironmentVariable("ENGRAM_CONFIG_DIR");
        if (!string.IsNullOrWhiteSpace(configDir))
            return Path.Combine(configDir, ".env");

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".engram", ".env");
    }

    /// <summary>
    /// Backup path for a given env file (<c>&lt;path&gt;.bak</c>).
    /// </summary>
    public static string BackupPath(string envPath) => envPath + ".bak";

    /// <summary>
    /// Reads <c>ENGRAM_PROFILE</c> from the given env file, or <c>null</c> when the file
    /// is missing or does not declare a profile. Case-insensitive on the key, tolerant of
    /// comments, blank lines, and inline whitespace.
    /// </summary>
    public static string? ReadProfileFromFile(string? envPath = null)
    {
        envPath ??= ResolveEnvPath();
        if (!File.Exists(envPath)) return null;

        foreach (var line in File.ReadLines(envPath))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;

            var eq = trimmed.IndexOf('=');
            if (eq <= 0) continue;

            var key = trimmed[..eq].Trim();
            var value = trimmed[(eq + 1)..].Trim();
            if (string.Equals(key, ProfileEnvVar, StringComparison.OrdinalIgnoreCase))
                return value;
        }

        return null;
    }

    /// <summary>
    /// Writes the declarative profile file for <paramref name="profile"/>.
    /// Backs up any existing file to <c>.env.bak</c> first, then writes
    /// <c>ENGRAM_PROFILE</c> plus the profile's preset defaults.
    /// </summary>
    public static void WriteProfile(DeployProfile profile, string? envPath = null)
    {
        envPath ??= ResolveEnvPath();

        var dir = Path.GetDirectoryName(envPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        if (File.Exists(envPath))
            File.Copy(envPath, BackupPath(envPath), overwrite: true);

        var sb = new StringBuilder();
        sb.AppendLine("# Engram deployment profile — managed by `engram profile set`");
        sb.AppendLine("# Change it with `engram profile set <profile>` instead of editing by hand.");
        sb.AppendLine($"{ProfileEnvVar}={profile.ToLabel()}");
        foreach (var (key, value) in ProfileDefaults.For(profile).OrderBy(kvp => kvp.Key, StringComparer.Ordinal))
            sb.AppendLine($"{key}={value}");

        File.WriteAllText(envPath, sb.ToString());
    }

    /// <summary>
    /// Parses a profile name (case-insensitive, trimmed) into a <see cref="DeployProfile"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">When the name is not a known profile.</exception>
    public static DeployProfile ParseProfileName(string name)
    {
        return name.Trim().ToLowerInvariant() switch
        {
            "local"         => DeployProfile.Local,
            "remote-server" => DeployProfile.RemoteServer,
            "offline-first" => DeployProfile.OfflineFirst,
            "desktop"       => DeployProfile.Desktop,
            _ => throw new InvalidOperationException(
                $"Unknown profile '{name}'. Use local, remote-server, offline-first, or desktop."),
        };
    }

    /// <summary>
    /// Builds a <see cref="StoreConfig"/> for the target profile so required-variable
    /// validation (<see cref="ProfileValidator.GetMissingVariables"/>) reflects what the
    /// profile will need — not the current environment's effective profile.
    /// </summary>
    public static StoreConfig StoreConfigForProfile(DeployProfile profile)
    {
        var defaults = ProfileDefaults.For(profile);
        var dbType = defaults.TryGetValue("ENGRAM_DB_TYPE", out var raw) && raw == "postgres"
            ? StoreDbType.Postgres
            : StoreDbType.Sqlite;

        return new StoreConfig
        {
            Profile = profile,
            DbType = dbType,
        };
    }

    /// <summary>
    /// Returns the profile's effective variables as <c>(key, value, source)</c> tuples,
    /// ordered by key. Source is <c>"profile-default"</c> when the variable comes from
    /// <see cref="ProfileDefaults.For"/>, or <c>"override"</c> when the user set it
    /// explicitly in the environment.
    /// </summary>
    public static IReadOnlyList<(string Key, string? Value, string Source)> GetEffectiveVariables(DeployProfile profile)
    {
        var result = new List<(string, string?, string)>();
        foreach (var (key, defaultValue) in ProfileDefaults.For(profile).OrderBy(kvp => kvp.Key, StringComparer.Ordinal))
        {
            var envValue = Environment.GetEnvironmentVariable(key);
            var source = envValue is null ? "profile-default" : "override";
            result.Add((key, envValue ?? defaultValue, source));
        }
        return result;
    }

    /// <summary>
    /// Idempotency check: whether the declarative file already declares the given profile.
    /// </summary>
    public static bool IsProfileAlreadySet(DeployProfile profile, string? envPath = null)
    {
        var current = ReadProfileFromFile(envPath);
        return string.Equals(current?.Trim(), profile.ToLabel(), StringComparison.OrdinalIgnoreCase);
    }
}
