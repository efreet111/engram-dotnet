namespace Engram.Store;

/// <summary>
/// Deployment profile that selects preset configurations for database backend, sync behavior,
/// and required environment variables. Controlled by the <c>ENGRAM_PROFILE</c> environment variable.
/// </summary>
/// <remarks>
/// The profile is the first composition layer in config resolution:
/// <c>explicit env var > profile default > hardcoded default</c>.
/// Each member maps to preset defaults via <see cref="ProfileDefaults"/> and validated
/// requirements via <see cref="ProfileValidator"/>.
/// </remarks>
public enum DeployProfile
{
    /// <summary>
    /// Solo developer — SQLite backend, no sync. No required env vars.
    /// </summary>
    Local,

    /// <summary>
    /// Team shared DB on a remote server — PostgreSQL backend, no sync, multi-user isolation via X-Engram-User header.
    /// Requires <c>ENGRAM_PG_CONNECTION</c> (non-localhost).
    /// Does <b>not</b> require <c>ENGRAM_USER</c> because the server does not save memories with its own identity;
    /// clients identify themselves via the X-Engram-User header on each request.
    /// </summary>
    RemoteServer,

    /// <summary>
    /// Large team offline-first — SQLite backend + SyncManager enabled.
    /// Requires <c>ENGRAM_SERVER_URL</c> and <c>ENGRAM_USER</c>.
    /// </summary>
    OfflineFirst,

    /// <summary>
    /// Desktop hybrid — SQLite local backend (source of truth) + SyncManager enabled,
    /// with PostgreSQL Docker demoted to a sync server (not the primary backend).
    /// Requires <c>ENGRAM_SERVER_URL</c> and <c>ENGRAM_USER</c>.
    /// </summary>
    [Obsolete("Desktop profile is temporarily suspended. See HU-058 and ADR-014.", DiagnosticId = "ENGRAM_DEPRECATED")]
    Desktop,
}

/// <summary>
/// Extension methods for parsing the <see cref="DeployProfile"/> from environment.
/// </summary>
public static class DeployProfileExtensions
{
    /// <summary>
    /// Reads <c>ENGRAM_PROFILE</c> from the environment and returns the corresponding <see cref="DeployProfile"/>.
    /// Parsing is case-insensitive and trims whitespace.
    /// </summary>
    /// <returns>The parsed profile, or <see cref="DeployProfile.Local"/> when the variable is unset or empty.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <c>ENGRAM_PROFILE</c> contains an unrecognized value. Lists valid options in the message.
    /// </exception>
    public static DeployProfile FromEnvironment()
    {
        var raw = Environment.GetEnvironmentVariable("ENGRAM_PROFILE");
        if (string.IsNullOrWhiteSpace(raw)) return DeployProfile.Local;
        return raw.Trim().ToLowerInvariant() switch
        {
            "local"          => DeployProfile.Local,
            "remote-server"  => DeployProfile.RemoteServer,
            "offline-first"  => DeployProfile.OfflineFirst,
            "desktop"        => throw new NotSupportedException(
                "The 'desktop' profile is temporarily suspended. See HU-058 and ADR-014."),
            _ => throw new InvalidOperationException(
                $"Unknown profile '{raw}'. Use local, remote-server, offline-first, or desktop."),
        };
    }

    /// <summary>
    /// Returns the canonical lower-case label for the profile, used in user-facing
    /// output and diagnostic messages (e.g. <c>"local"</c>, <c>"remote-server"</c>).
    /// </summary>
    /// <param name="profile">The deployment profile to label.</param>
    /// <returns>The canonical label (matches the <c>ENGRAM_PROFILE</c> value).</returns>
    public static string ToLabel(this DeployProfile profile) => profile switch
    {
        DeployProfile.Local        => "local",
        DeployProfile.RemoteServer => "remote-server",
        DeployProfile.OfflineFirst => "offline-first",
        DeployProfile.Desktop      => "desktop",
        _ => profile.ToString().ToLowerInvariant(),
    };
}

/// <summary>
/// Provides preset default configuration values for each <see cref="DeployProfile"/>.
/// </summary>
/// <remarks>
/// Defaults returned by <see cref="For"/> are the middle layer in the config merge:
/// explicit env var overrides profile default, profile default overrides hardcoded fallback.
/// Keys use the same name as their corresponding environment variables for uniform lookup.
/// </remarks>
public static class ProfileDefaults
{
    /// <summary>
    /// Returns a dictionary of preset defaults for the given profile.
    /// Keys match environment variable names so callers can merge via uniform lookup.
    /// </summary>
    /// <param name="p">The deployment profile to get defaults for.</param>
    /// <returns>A dictionary mapping env var names to their profile-default values.</returns>
    public static Dictionary<string, string?> For(DeployProfile p) => p switch
    {
        DeployProfile.Local       => new() { ["ENGRAM_DB_TYPE"] = "sqlite",   ["ENGRAM_SYNC_ENABLED"] = "false" },
        DeployProfile.RemoteServer => new() { ["ENGRAM_DB_TYPE"] = "postgres", ["ENGRAM_SYNC_ENABLED"] = "false" },
        DeployProfile.OfflineFirst => new() { ["ENGRAM_DB_TYPE"] = "sqlite",   ["ENGRAM_SYNC_ENABLED"] = "true",
                                              ["ENGRAM_SYNC_POLL_SECONDS"] = "30", ["ENGRAM_SYNC_TARGET"] = "cloud" },
        DeployProfile.Desktop     => new() { ["ENGRAM_DB_TYPE"] = "sqlite", ["ENGRAM_SYNC_ENABLED"] = "true",
                                              ["ENGRAM_SYNC_POLL_SECONDS"] = "30", ["ENGRAM_SYNC_TARGET"] = "desktop",
                                              ["ENGRAM_SERVER_URL"] = "http://localhost:7437" },
    };
}

/// <summary>
/// Validates that all required environment variables for the effective <see cref="StoreConfig"/>
/// are set and non-empty. Designed to be called before store initialization for fail-fast behavior.
/// </summary>
/// <remarks>
/// Validation is based on the effective configuration (after profile defaults are merged):
/// <list type="bullet">
///   <item><b>Local</b>: none required</item>
///   <item><b>OfflineFirst</b>: <c>ENGRAM_SERVER_URL</c> required (sync enabled)</item>
///   <item><b>PostgreSQL backend</b>: <c>ENGRAM_PG_CONNECTION</c> required</item>
///   <item><b>RemoteServer profile</b>: <c>ENGRAM_PG_CONNECTION</c> must NOT point to localhost (security gate)</item>
///   <item><b>Sync enabled</b>: <c>ENGRAM_SERVER_URL</c> required</item>
///   <item><b>Desktop/OfflineFirst profiles (sync profiles)</b>: <c>ENGRAM_USER</c> required for sync identity</item>
///   <item><b>RemoteServer profile</b>: <c>ENGRAM_USER</c> NOT required — the server uses X-Engram-User header from clients</item>
/// </list>
/// </remarks>
public static class ProfileValidator
{
    /// <summary>
    /// Checks that all required environment variables for the effective config are set
    /// and are non-empty. Throws immediately, naming every missing or invalid variable.
    /// </summary>
    /// <param name="cfg">The store configuration to validate (uses effective DbType, sync settings, and profile).</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when one or more required variables are missing or empty, or when
    /// the RemoteServer profile uses a localhost connection string.
    /// Message includes all missing variable names.
    /// </exception>
    /// <summary>
    /// Returns the names of required environment variables that are missing or invalid
    /// for the effective configuration. Does not throw; returns an empty list when valid.
    /// </summary>
    /// <param name="cfg">The store configuration to validate (uses effective DbType, sync settings, and profile).</param>
    /// <returns>Human-readable names of missing or invalid variables.</returns>
    public static IReadOnlyList<string> GetMissingVariables(StoreConfig cfg)
    {
        var missing = new List<string>();

        // PostgreSQL backend requires connection string
        if (cfg.IsPostgres && string.IsNullOrWhiteSpace(cfg.PgConnectionString))
            missing.Add("ENGRAM_PG_CONNECTION");

        // Sync requires server URL
        if (cfg.IsSyncEnabled && string.IsNullOrWhiteSpace(cfg.RemoteUrl))
            missing.Add("ENGRAM_SERVER_URL");

        // RemoteServer profile: reject localhost connection strings (security gate)
        // Exception: all-in-one containers set ENGRAM_ALLINONE=1 to explicitly allow
        // localhost/loopback PostgreSQL when the DB runs on the same host.
        var allowLocalhostPg = Environment.GetEnvironmentVariable("ENGRAM_ALLINONE") == "1";
        if (cfg.Profile is DeployProfile.RemoteServer && !string.IsNullOrWhiteSpace(cfg.PgConnectionString) && !allowLocalhostPg)
        {
            if (IsLocalhostConnection(cfg.PgConnectionString))
                missing.Add("ENGRAM_PG_CONNECTION (localhost not allowed for remote-server profile)");
        }

        return missing;
    }

    /// <summary>
    /// Checks that all required environment variables for the effective config are set
    /// and are non-empty. Throws immediately, naming every missing or invalid variable.
    /// </summary>
    /// <param name="cfg">The store configuration to validate (uses effective DbType, sync settings, and profile).</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when one or more required variables are missing or empty, or when
    /// the RemoteServer profile uses a localhost connection string.
    /// Message includes all missing variable names.
    /// </exception>
    public static void Validate(StoreConfig cfg)
    {
        var missing = GetMissingVariables(cfg);

        if (missing.Count > 0)
            throw new InvalidOperationException(
                $"Configuration requires: {string.Join(", ", missing)}. Set them in docker/.env or environment.");
    }

    /// <summary>
    /// Checks if a PostgreSQL connection string points to localhost, 127.0.0.1, or ::1.
    /// Matches <c>Host=localhost</c>, <c>Server=127.0.0.1</c>, and <c>Data Source=::1</c> patterns case-insensitively.
    /// </summary>
    private static bool IsLocalhostConnection(string connectionString)
    {
        // Normalize to lowercase for case-insensitive matching
        var lower = connectionString.ToLowerInvariant();

        // Check for Host= or Server= patterns with localhost/IPs
        var hostKeyPatterns = new[] { "host=", "server=", "data source=" };
        foreach (var key in hostKeyPatterns)
        {
            var index = lower.IndexOf(key, StringComparison.Ordinal);
            if (index < 0) continue;

            var valueStart = index + key.Length;
            // Find the end of the value (next semicolon or end of string)
            var valueEnd = lower.IndexOf(';', valueStart);
            var value = valueEnd >= 0
                ? lower[valueStart..valueEnd].Trim()
                : lower[valueStart..].Trim();

            if (value is "localhost" or "127.0.0.1" or "::1")
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns the names of variables among <paramref name="vars"/> that are unset or empty in the environment.
    /// </summary>
    private static IEnumerable<string> Required(params string[] vars)
        => vars.Where(v => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(v)));
}
