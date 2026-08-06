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
    /// Requires <c>ENGRAM_PG_CONNECTION</c> (non-localhost) and <c>ENGRAM_USER</c>.
    /// </summary>
    RemoteServer,

    /// <summary>
    /// Large team offline-first — SQLite backend + SyncManager enabled.
    /// Requires <c>ENGRAM_SERVER_URL</c> and <c>ENGRAM_USER</c>.
    /// </summary>
    OfflineFirst,

    /// <summary>
    /// Desktop app with full stack — PostgreSQL backend + SyncManager enabled.
    /// Requires <c>ENGRAM_PG_CONNECTION</c>, <c>ENGRAM_SERVER_URL</c>, and <c>ENGRAM_USER</c>.
    /// </summary>
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
            "desktop"        => DeployProfile.Desktop,
            _ => throw new InvalidOperationException(
                $"Unknown profile '{raw}'. Use local, remote-server, offline-first, or desktop."),
        };
    }
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
        DeployProfile.Desktop     => new() { ["ENGRAM_DB_TYPE"] = "postgres", ["ENGRAM_SYNC_ENABLED"] = "true",
                                              ["ENGRAM_SYNC_POLL_SECONDS"] = "30", ["ENGRAM_SYNC_TARGET"] = "cloud" },
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
///   <item><b>RemoteServer/Desktop/OfflineFirst profiles</b>: <c>ENGRAM_USER</c> strongly recommended</item>
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
    public static void Validate(StoreConfig cfg)
    {
        var missing = new List<string>();

        // PostgreSQL backend requires connection string
        if (cfg.IsPostgres && string.IsNullOrWhiteSpace(cfg.PgConnectionString))
            missing.Add("ENGRAM_PG_CONNECTION");

        // Sync requires server URL
        if (cfg.IsSyncEnabled && string.IsNullOrWhiteSpace(cfg.RemoteUrl))
            missing.Add("ENGRAM_SERVER_URL");

        // RemoteServer profile: reject localhost connection strings (security gate)
        if (cfg.Profile is DeployProfile.RemoteServer && !string.IsNullOrWhiteSpace(cfg.PgConnectionString))
        {
            if (IsLocalhostConnection(cfg.PgConnectionString))
                missing.Add("ENGRAM_PG_CONNECTION (localhost not allowed for remote-server profile)");
        }

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
