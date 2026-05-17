namespace Engram.Diagnostics.Models;

/// <summary>
/// Represents the health status of a single component.
/// </summary>
public class ComponentHealth
{
    /// <summary>
    /// Gets or sets whether the component is healthy.
    /// </summary>
    public bool IsHealthy { get; set; }

    /// <summary>
    /// Gets or sets a message describing the component status.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the latency in milliseconds for the health check.
    /// </summary>
    public long LatencyMs { get; set; }
}

/// <summary>
/// Represents the overall diagnostic result for the engram ecosystem.
/// </summary>
public class DiagnosticResult
{
    /// <summary>
    /// Gets or sets whether the entire system is healthy.
    /// </summary>
    public bool IsHealthy { get; set; }

    /// <summary>
    /// Gets or sets the health status of individual components.
    /// Keys: "database", "http_server", "mcp_server".
    /// </summary>
    public Dictionary<string, ComponentHealth> Components { get; set; } = new();
}
