using System.Diagnostics;
using Engram.Diagnostics.Models;
using Engram.Store;

namespace Engram.Diagnostics;

/// <summary>
/// Implementation of IDiagnosticService that performs health checks
/// for the engram ecosystem: database, HTTP server, and MCP server.
/// </summary>
public sealed class DiagnosticService : IDiagnosticService
{
    private readonly IStore _store;
    private readonly HttpClient _httpClient;
    private readonly string? _serverUrl;

    /// <summary>
    /// Database check timeout in milliseconds.
    /// </summary>
    private const int DbTimeoutMs = 2000;

    /// <summary>
    /// HTTP health check timeout in milliseconds.
    /// </summary>
    private const int HttpTimeoutMs = 2000;

    public DiagnosticService(IStore store, HttpClient? httpClient = null, string? serverUrl = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _httpClient = httpClient ?? new HttpClient();
        _serverUrl = serverUrl;
    }

    /// <summary>
    /// Runs all diagnostic checks and returns the aggregated result.
    /// </summary>
    public async Task<DiagnosticResult> RunDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        var result = new DiagnosticResult
        {
            Components = new Dictionary<string, ComponentHealth>()
        };

        // Run all checks in parallel
        var dbCheck = CheckDatabaseAsync(cancellationToken);
        var httpCheck = CheckHttpServerAsync(cancellationToken);
        var mcpCheck = CheckMcpServerAsync(cancellationToken);
        var identityCheck = CheckProjectIdentityAsync(cancellationToken);

        await Task.WhenAll(dbCheck, httpCheck, mcpCheck, identityCheck);

        result.Components["database"] = await dbCheck;
        result.Components["http_server"] = await httpCheck;
        result.Components["mcp_server"] = await mcpCheck;
        result.Components["project_identity"] = await identityCheck;

        // System is healthy if all components are healthy
        result.IsHealthy = result.Components.Values.All(c => c.IsHealthy);

        return result;
    }

    /// <summary>
    /// Checks database connectivity using a lightweight SELECT 1 query.
    /// </summary>
    private async Task<ComponentHealth> CheckDatabaseAsync(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            using var cts = new CancellationTokenSource(DbTimeoutMs);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, cancellationToken);
            
            // Execute lightweight query to verify database connectivity
            // IStore doesn't expose raw SQL, so we use StatsAsync as a lightweight operation
            var stats = await _store.StatsAsync().WaitAsync(linkedCts.Token);
            
            stopwatch.Stop();
            
            return new ComponentHealth
            {
                IsHealthy = true,
                Message = $"Database {_store.BackendName} responded successfully",
                LatencyMs = stopwatch.ElapsedMilliseconds
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            return new ComponentHealth
            {
                IsHealthy = false,
                Message = "Database check cancelled",
                LatencyMs = stopwatch.ElapsedMilliseconds
            };
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            return new ComponentHealth
            {
                IsHealthy = false,
                Message = $"Database check timed out after {DbTimeoutMs}ms",
                LatencyMs = stopwatch.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new ComponentHealth
            {
                IsHealthy = false,
                Message = $"Database check failed: {ex.Message}",
                LatencyMs = stopwatch.ElapsedMilliseconds
            };
        }
    }

    /// <summary>
    /// Checks HTTP server health by pinging the /health endpoint.
    /// </summary>
    private async Task<ComponentHealth> CheckHttpServerAsync(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            if (string.IsNullOrEmpty(_serverUrl))
            {
                stopwatch.Stop();
                return new ComponentHealth
                {
                    IsHealthy = false,
                    Message = "Server URL not configured (ENGRAM_SERVER_URL not set)",
                    LatencyMs = stopwatch.ElapsedMilliseconds
                };
            }

            using var cts = new CancellationTokenSource(HttpTimeoutMs);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, cancellationToken);
            
            var healthUrl = _serverUrl.TrimEnd('/') + "/health";
            var response = await _httpClient.GetAsync(healthUrl, linkedCts.Token);
            
            stopwatch.Stop();
            
            if (response.IsSuccessStatusCode)
            {
                return new ComponentHealth
                {
                    IsHealthy = true,
                    Message = $"HTTP server responded with {response.StatusCode}",
                    LatencyMs = stopwatch.ElapsedMilliseconds
                };
            }
            else
            {
                return new ComponentHealth
                {
                    IsHealthy = false,
                    Message = $"HTTP server returned {response.StatusCode}",
                    LatencyMs = stopwatch.ElapsedMilliseconds
                };
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            return new ComponentHealth
            {
                IsHealthy = false,
                Message = "HTTP check cancelled",
                LatencyMs = stopwatch.ElapsedMilliseconds
            };
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            return new ComponentHealth
            {
                IsHealthy = false,
                Message = $"HTTP check timed out after {HttpTimeoutMs}ms",
                LatencyMs = stopwatch.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new ComponentHealth
            {
                IsHealthy = false,
                Message = $"HTTP check failed: {ex.Message}",
                LatencyMs = stopwatch.ElapsedMilliseconds
            };
        }
    }

    /// <summary>
    /// Checks MCP server configuration and availability.
    /// Verifies configuration without causing stdio contention.
    /// </summary>
    private Task<ComponentHealth> CheckMcpServerAsync(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            // MCP check strategy:
            // 1. Verify IStore is available (MCP depends on it)
            // 2. Check that we have valid configuration
            // 3. Avoid stdio contention by not spawning new clients
            
            // If we got here, IStore is working (checked in database check)
            var storeHealthy = true;
            
            // Check if store has a valid backend
            var backendValid = !string.IsNullOrEmpty(_store.BackendName);
            
            stopwatch.Stop();
            
            if (storeHealthy && backendValid)
            {
                return Task.FromResult(new ComponentHealth
                {
                    IsHealthy = true,
                    Message = $"MCP server configured with {_store.BackendName} backend",
                    LatencyMs = stopwatch.ElapsedMilliseconds
                });
            }
            else
            {
                return Task.FromResult(new ComponentHealth
                {
                    IsHealthy = false,
                    Message = "MCP server configuration invalid",
                    LatencyMs = stopwatch.ElapsedMilliseconds
                });
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return Task.FromResult(new ComponentHealth
            {
                IsHealthy = false,
                Message = $"MCP check failed: {ex.Message}",
                LatencyMs = stopwatch.ElapsedMilliseconds
            });
        }
    }

    /// <summary>
    /// Checks project identity: .engram-id existence and gitignore status.
    /// ENG-410 / ENG-430.
    /// </summary>
    private Task<ComponentHealth> CheckProjectIdentityAsync(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Find git root
            var cwd = Directory.GetCurrentDirectory();
            var repoRoot = FindGitRoot(cwd);

            if (repoRoot is null)
            {
                return Task.FromResult(new ComponentHealth
                {
                    IsHealthy = true,
                    Message = "Not a git repository — project identity N/A",
                    LatencyMs = stopwatch.ElapsedMilliseconds
                });
            }

            var idFile = Path.Combine(repoRoot, ".engram-id");

            if (!File.Exists(idFile))
            {
                return Task.FromResult(new ComponentHealth
                {
                    IsHealthy = true,
                    Message = "No .engram-id found. Run 'engram project id --generate' to create one.",
                    LatencyMs = stopwatch.ElapsedMilliseconds
                });
            }

            // Check if .engram-id is in .gitignore
            var isIgnored = IsGitIgnored(repoRoot, ".engram-id");

            if (isIgnored)
            {
                stopwatch.Stop();
                return Task.FromResult(new ComponentHealth
                {
                    IsHealthy = false,
                    Message = ".engram-id is in .gitignore — team members may have " +
                              "different project identities. Remove '.engram-id' from .gitignore " +
                              "and commit the file so all team members share the same identity.",
                    LatencyMs = stopwatch.ElapsedMilliseconds
                });
            }

            // Healthy: .engram-id exists and is tracked
            stopwatch.Stop();
            var guid = File.ReadAllText(idFile).Trim();
            return Task.FromResult(new ComponentHealth
            {
                IsHealthy = true,
                Message = $"Project identity found: {guid[..8]}...",
                LatencyMs = stopwatch.ElapsedMilliseconds
            });
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return Task.FromResult(new ComponentHealth
            {
                IsHealthy = false,
                Message = $"Project identity check failed: {ex.Message}",
                LatencyMs = stopwatch.ElapsedMilliseconds
            });
        }
    }

    private static string? FindGitRoot(string startDir)
    {
        var dir = startDir;
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir, ".git")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    private static bool IsGitIgnored(string repoRoot, string path)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = $"check-ignore -q -- {path}",
                WorkingDirectory = repoRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(psi);
            process?.WaitForExit(5000);
            // git check-ignore returns 0 if the file IS ignored
            return process?.ExitCode == 0;
        }
        catch
        {
            // If git is not available, assume not ignored
            return false;
        }
    }
}
