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

    public static StoreConfig FromEnvironment() => new();
}