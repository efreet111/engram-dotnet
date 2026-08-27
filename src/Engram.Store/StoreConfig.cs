namespace Engram.Store;

public enum StoreDbType { Sqlite, Postgres }

public class StoreConfig
{
    public string DataDir { get; init; } =
        Environment.GetEnvironmentVariable("ENGRAM_DATA_DIR")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".engram");

    public string DbPath => Path.Combine(DataDir, "engram.db");

    public int Port { get; init; } = int.TryParse(Environment.GetEnvironmentVariable("ENGRAM_PORT"), out var port)
        ? port : 7437;

    /// <summary>
    /// Deployment profile that sets default values for database type, sync behavior,
    /// and other configuration. Parsed from <c>ENGRAM_PROFILE</c> environment variable.
    /// Defaults to <see cref="DeployProfile.Local"/> when unset or empty.
    /// </summary>
    public DeployProfile Profile { get; init; } = DeployProfileExtensions.FromEnvironment();

    public string? Project { get; init; } = Environment.GetEnvironmentVariable("ENGRAM_PROJECT");

    public TimeSpan DedupeWindow { get; init; } = TimeSpan.FromMinutes(15);

    public int MaxObservationLength { get; init; } = 100_000;

    /// <summary>
    /// Maximum title length before truncation (ENG-475 follow-up).
    /// Titles exceeding this limit are truncated and appended with "…" to make data loss visible.
    /// Prevents PostgreSQL B-tree index overflow (idx_obs_dedupe).
    /// </summary>
    public int MaxTitleLength { get; init; } = 200;

    public string? JwtSecret { get; init; } = Environment.GetEnvironmentVariable("ENGRAM_JWT_SECRET");

    public string? CorsOrigins { get; init; } = Environment.GetEnvironmentVariable("ENGRAM_CORS_ORIGINS");

    /// <summary>
    /// Remote server URL for team/centralized mode (env: ENGRAM_SERVER_URL).
    /// When set, the MCP client acts as an HTTP proxy instead of using a local SQLite store.
    /// Example: http://10.0.0.5:7437
    /// </summary>
    public string? RemoteUrl { get; init; } = Environment.GetEnvironmentVariable("ENGRAM_SERVER_URL");

    /// <summary>
    /// Identifies the developer using this client (env: ENGRAM_USER).
    /// Used to namespace memories in the shared server.
    /// Falls back to the OS user name if the environment variable is not set.
    /// Example: victor.silgado
    /// </summary>
    public string User { get; init; } = 
        Environment.GetEnvironmentVariable("ENGRAM_USER") ?? Environment.UserName;

    /// <summary>
    /// Database backend type (env: ENGRAM_DB_TYPE). Values: "sqlite" (default) or "postgres".
    /// </summary>
    public StoreDbType DbType { get; init; } = ParseDbType(
        Environment.GetEnvironmentVariable("ENGRAM_DB_TYPE"));

    /// <summary>
    /// PostgreSQL connection string (env: ENGRAM_PG_CONNECTION).
    /// Required when DbType == Postgres.
    /// </summary>
    public string? PgConnectionString { get; init; } =
        Environment.GetEnvironmentVariable("ENGRAM_PG_CONNECTION");

    /// <summary>
    /// True when using PostgreSQL as the local backend.
    /// </summary>
    public bool IsPostgres => DbType == StoreDbType.Postgres;

    /// <summary>
    /// True when the client operates as a pure thin client: it has a remote server
    /// (via <c>ENGRAM_SERVER_URL</c>) and is NOT a sync profile (<c>offline-first</c>
    /// or <c>desktop</c>), so every read/write is delegated to the remote server via
    /// <c>HttpStore</c> instead of using a local store.
    /// </summary>
    /// <remarks>
    /// ADR-013 separates "thin client" (delegate everything to a remote server) from
    /// "sync enabled" (local store + background sync). The <c>offline-first</c> and
    /// <c>desktop</c> profiles keep a local store and sync via <c>SyncManager</c>, so
    /// they must never be treated as thin clients even though they set
    /// <c>ENGRAM_SERVER_URL</c> for sync.
    /// </remarks>
    public bool IsThinClient =>
        !string.IsNullOrWhiteSpace(RemoteUrl)
        && Profile is not (DeployProfile.OfflineFirst or DeployProfile.Desktop);

    /// <summary>
    /// Deprecated alias for <see cref="IsThinClient"/>. Kept for backward compatibility
    /// during the ADR-013 transition; use <see cref="IsThinClient"/> instead.
    /// </summary>
    [Obsolete("Use IsThinClient instead.")]
    public bool IsRemote => IsThinClient;

    /// <summary>
    /// True when sync is enabled via ENGRAM_SYNC_ENABLED env var.
    /// Honors the same merge precedence as SyncManagerConfig: explicit env var > profile default > false.
    /// </summary>
    public bool IsSyncEnabled
    {
        get
        {
            var raw = Environment.GetEnvironmentVariable("ENGRAM_SYNC_ENABLED");
            if (raw is not null)
                return raw.Trim().ToLowerInvariant() is not ("false" or "0");
            // Profile defaults may enable sync
            if (Profile is DeployProfile.OfflineFirst or DeployProfile.Desktop)
                return true;
            return false;
        }
    }

    private static StoreDbType ParseDbType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return StoreDbType.Sqlite;
        return value.Trim().ToLowerInvariant() switch
        {
            "postgres" or "postgresql" or "pg" => StoreDbType.Postgres,
            _ => StoreDbType.Sqlite,
        };
    }

    /// <summary>
    /// Creates a <see cref="StoreConfig"/> from environment variables using the profile-based
    /// merge pattern: <c>explicit env var > profile default > hardcoded default</c>.
    /// </summary>
    public static StoreConfig FromEnvironment()
    {
        var profile = DeployProfileExtensions.FromEnvironment();
        var defaults = ProfileDefaults.For(profile);

        string? Resolve(string key, string? hc = null) =>
            Environment.GetEnvironmentVariable(key)
            ?? (defaults.TryGetValue(key, out var d) ? d : null)
            ?? hc;

        return new StoreConfig
        {
            Profile = profile,
            DbType = ParseDbType(Resolve("ENGRAM_DB_TYPE", "sqlite")),
            PgConnectionString = Resolve("ENGRAM_PG_CONNECTION"),
            RemoteUrl = Resolve("ENGRAM_SERVER_URL"),
            User = Resolve("ENGRAM_USER") ?? Environment.UserName,
        };
    }
}