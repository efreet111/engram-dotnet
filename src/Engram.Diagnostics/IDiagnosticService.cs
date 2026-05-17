namespace Engram.Diagnostics;

/// <summary>
/// Provides diagnostic health checks for the engram ecosystem.
/// </summary>
public interface IDiagnosticService
{
    /// <summary>
    /// Runs all diagnostic checks and returns the aggregated result.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for async operations.</param>
    /// <returns>Diagnostic result with health status of all components.</returns>
    Task<Models.DiagnosticResult> RunDiagnosticsAsync(CancellationToken cancellationToken = default);
}
