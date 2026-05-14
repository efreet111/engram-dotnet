namespace Engram.Store;

public sealed class RetentionConfig
{
    private static readonly Dictionary<string, TimeSpan> DefaultTtl = new(StringComparer.OrdinalIgnoreCase)
    {
        ["tool_use"]    = TimeSpan.FromDays(30),
        ["file_change"] = TimeSpan.FromDays(30),
        ["command"]     = TimeSpan.FromDays(30),
        ["bugfix"]      = TimeSpan.FromDays(90),
        ["pattern"]     = TimeSpan.FromDays(90),
        ["learning"]    = TimeSpan.FromDays(60),
        ["discovery"]   = TimeSpan.FromDays(60),
    };
    
    // Types that NEVER expire
    private static readonly HashSet<string> NoExpiry = ["decision", "architecture", "session_summary"];
    
    public IReadOnlyDictionary<string, TimeSpan> TtlByType { get; }
    
    public RetentionConfig()
    {
        var ttl = new Dictionary<string, TimeSpan>(StringComparer.OrdinalIgnoreCase);
        foreach (var (type, defaultTtl) in DefaultTtl)
        {
            var envVar = Environment.GetEnvironmentVariable($"ENGRAM_TTL_{type.ToUpperInvariant()}");
            ttl[type] = ParseTtl(envVar, defaultTtl);
        }
        TtlByType = ttl;
    }
    
    public bool ShouldExpire(string type) => !NoExpiry.Contains(type);
    
    public TimeSpan? GetTtl(string type) =>
        TtlByType.TryGetValue(type, out var ttl) ? ttl : null;
    
    private static TimeSpan ParseTtl(string? value, TimeSpan defaultVal)
    {
        if (string.IsNullOrWhiteSpace(value)) return defaultVal;
        value = value.Trim().ToLowerInvariant();
        if (value.EndsWith('d') && int.TryParse(value.TrimEnd('d'), out var days))
            return TimeSpan.FromDays(days);
        if (value.EndsWith('h') && int.TryParse(value.TrimEnd('h'), out var hours))
            return TimeSpan.FromHours(hours);
        return defaultVal;
    }
}
