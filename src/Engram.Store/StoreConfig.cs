namespace Engram.Store;

public class StoreConfig
{
    public string DataDir { get; init; } =
        Environment.GetEnvironmentVariable("ENGRAM_DATA_DIR")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".engram");

    public string DbPath => Path.Combine(DataDir, "engram.db");

    public int Port { get; init; } = int.TryParse(Environment.GetEnvironmentVariable("ENGRAM_PORT"), out var port)
        ? port : 7437;

    public string? Project { get; init; } = Environment.GetEnvironmentVariable("ENGRAM_PROJECT");

    public TimeSpan DedupeWindow { get; init; } = TimeSpan.FromMinutes(15);

    public int MaxObservationLength { get; init; } = 100_000;

    public string? JwtSecret { get; init; } = Environment.GetEnvironmentVariable("ENGRAM_JWT_SECRET");

    public string? CorsOrigins { get; init; } = Environment.GetEnvironmentVariable("ENGRAM_CORS_ORIGINS");

    /// <summary>
    /// Remote server URL for team/centralized mode (env: ENGRAM_URL).
    /// When set, the MCP client acts as an HTTP proxy instead of using a local SQLite store.
    /// Example: http://10.0.0.5:7437
    /// </summary>
    public string? RemoteUrl { get; init; } = Environment.GetEnvironmentVariable("ENGRAM_URL");

    /// <summary>
    /// Identifies the developer using this client (env: ENGRAM_USER).
    /// Used to namespace memories in the shared server — stored as a project prefix "user/project".
    /// Set by IT per machine. Example: victor.silgado
    /// </summary>
    public string? User { get; init; } = Environment.GetEnvironmentVariable("ENGRAM_USER");

    /// <summary>
    /// True when the client is configured to operate in team/centralized mode.
    /// </summary>
    public bool IsRemote => !string.IsNullOrWhiteSpace(RemoteUrl);

    public static StoreConfig FromEnvironment() => new();
}