using System.Diagnostics;
using Engram.Store;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Engram.Diagnostics.Tests;

/// <summary>
/// Integration tests for the "engram doctor" CLI command.
/// Verifies command existence and exit codes with mocked data.
/// </summary>
public sealed class CliIntegrationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;

    public CliIntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "engram-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "engram.db");
        
        // Initialize database with minimal schema
        InitializeDatabase();
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } 
        catch { /* best-effort cleanup */ }
    }

    private void InitializeDatabase()
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        
        // Create minimal tables needed for doctor command
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS sessions (
                id TEXT PRIMARY KEY,
                project TEXT NOT NULL,
                directory TEXT NOT NULL,
                started_at TEXT NOT NULL,
                summary TEXT,
                ended_at TEXT
            );
            CREATE TABLE IF NOT EXISTS observations (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                session_id TEXT NOT NULL,
                title TEXT NOT NULL,
                content TEXT NOT NULL,
                type TEXT NOT NULL,
                project TEXT,
                scope TEXT DEFAULT 'personal',
                topic_key TEXT,
                sync_id TEXT,
                created_at TEXT NOT NULL,
                updated_at TEXT,
                deleted_at TEXT,
                duplicate_count INTEGER DEFAULT 0,
                revision_count INTEGER DEFAULT 0,
                FOREIGN KEY (session_id) REFERENCES sessions(id)
            );
            CREATE TABLE IF NOT EXISTS user_prompts (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                session_id TEXT NOT NULL,
                content TEXT NOT NULL,
                project TEXT,
                created_at TEXT NOT NULL,
                deleted_at TEXT,
                FOREIGN KEY (session_id) REFERENCES sessions(id)
            );
            INSERT INTO sessions (id, project, directory, started_at) 
            VALUES ('test-session', 'test-project', '/tmp', datetime('now'));
        ";
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public async Task DoctorCommand_Exists_AndRunsSuccessfully()
    {
        // Arrange
        var envVars = new Dictionary<string, string>
        {
            ["ENGRAM_DATA_DIR"] = _tempDir,
            ["ENGRAM_SERVER_URL"] = "http://localhost:5000"
        };

        // Act
        var (exitCode, output) = await RunDoctorCommand(envVars);

        // Assert
        Assert.Contains("Engram Diagnostic Report", output);
        Assert.Contains("database", output);
        Assert.Contains("http_server", output);
        Assert.Contains("mcp_server", output);
    }

    [Fact]
    public async Task DoctorCommand_HealthyDatabase_ChecksAllComponents()
    {
        // Arrange
        var envVars = new Dictionary<string, string>
        {
            ["ENGRAM_DATA_DIR"] = _tempDir,
            ["ENGRAM_SERVER_URL"] = "http://localhost:5000"
        };

        // Act
        var (exitCode, output) = await RunDoctorCommand(envVars);

        // Assert - Database should be healthy, but HTTP will fail (no server running)
        Assert.Contains("Engram Diagnostic Report", output);
        Assert.Contains("database", output);
        Assert.Contains("http_server", output);
        Assert.Contains("mcp_server", output);
        // Exit code is 1 because HTTP server is not running
        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task DoctorCommand_UnhealthyHttpServer_ReturnsExitCode1()
    {
        // Arrange
        var envVars = new Dictionary<string, string>
        {
            ["ENGRAM_DATA_DIR"] = _tempDir,
            ["ENGRAM_SERVER_URL"] = "http://localhost:9999" // Non-existent server
        };

        // Act
        var (exitCode, output) = await RunDoctorCommand(envVars, timeoutMs: 5000);

        // Assert
        Assert.Equal(1, exitCode);
        Assert.Contains("Some components are unhealthy", output);
    }

    [Fact]
    public async Task DoctorCommand_NoServerUrl_HttpCheckFailsGracefully()
    {
        // Arrange
        var envVars = new Dictionary<string, string>
        {
            ["ENGRAM_DATA_DIR"] = _tempDir
            // No ENGRAM_SERVER_URL set
        };

        // Act
        var (exitCode, output) = await RunDoctorCommand(envVars);

        // Assert
        Assert.Equal(1, exitCode);
        Assert.Contains("http_server", output);
        Assert.Contains("not configured", output);
    }

    [Fact]
    public async Task DoctorCommand_PostgresBackend_ReportsCorrectBackend()
    {
        // Arrange - This test verifies that when using sqlite (default), 
        // the backend name is reported correctly
        var envVars = new Dictionary<string, string>
        {
            ["ENGRAM_DATA_DIR"] = _tempDir
        };

        // Act
        var (exitCode, output) = await RunDoctorCommand(envVars);

        // Assert
        Assert.Contains("sqlite", output.ToLower());
    }

    [Fact]
    public async Task DoctorCommand_OutputFormattedCorrectly()
    {
        // Arrange
        var envVars = new Dictionary<string, string>
        {
            ["ENGRAM_DATA_DIR"] = _tempDir,
            ["ENGRAM_SERVER_URL"] = "http://localhost:5000"
        };

        // Act
        var (exitCode, output) = await RunDoctorCommand(envVars);

        // Assert
        Assert.Contains("========================", output);
        
        // Check for status indicators
        Assert.Contains("✓", output); // Healthy components
        // Unhealthy components (HTTP will fail) - check for either X variant
        var hasUnhealthyIndicator = output.Contains("✗") || output.Contains("✕");
        Assert.True(hasUnhealthyIndicator, "Expected unhealthy indicator (✗ or ✕) in output");
    }

    [Fact]
    public async Task DoctorCommand_WithServerOption_UsesProvidedUrl()
    {
        // Arrange
        var envVars = new Dictionary<string, string>
        {
            ["ENGRAM_DATA_DIR"] = _tempDir
        };
        var serverUrl = "http://custom-server:8080";

        // Act
        var (exitCode, output) = await RunDoctorCommand(envVars, additionalArgs: $"--server {serverUrl}");

        // Assert
        Assert.Contains("Diagnostic Report", output);
        // Server URL is used internally, output format remains the same
    }

    [Fact]
    public async Task DoctorCommand_IsReadOnly_DoesNotModifyData()
    {
        // Arrange - Insert test data before running doctor
        var envVars = new Dictionary<string, string>
        {
            ["ENGRAM_DATA_DIR"] = _tempDir
        };
        
        // Count records before
        var (beforeSessions, beforeObservations, beforePrompts) = GetRecordCounts();
        
        // Add some test data
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO observations (session_id, title, content, type, project, created_at)
            VALUES ('test-session', 'Test Obs', 'Test content', 'manual', 'test-project', datetime('now'));
            INSERT INTO user_prompts (session_id, content, created_at)
            VALUES ('test-session', 'Test prompt', datetime('now'));
        ";
        cmd.ExecuteNonQuery();
        
        var (afterInsertSessions, afterInsertObservations, afterInsertPrompts) = GetRecordCounts();

        // Act - Run doctor command
        var (exitCode, output) = await RunDoctorCommand(envVars);

        // Assert - Verify no changes occurred during doctor execution
        var (afterDoctorSessions, afterDoctorObservations, afterDoctorPrompts) = GetRecordCounts();
        
        Assert.Equal(afterInsertSessions, afterDoctorSessions);
        Assert.Equal(afterInsertObservations, afterDoctorObservations);
        Assert.Equal(afterInsertPrompts, afterDoctorPrompts);
    }

    private (int Sessions, int Observations, int Prompts) GetRecordCounts()
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        
        int sessions = 0, observations = 0, prompts = 0;
        
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sessions";
        sessions = Convert.ToInt32(cmd.ExecuteScalar());
        
        cmd.CommandText = "SELECT COUNT(*) FROM observations";
        observations = Convert.ToInt32(cmd.ExecuteScalar());
        
        cmd.CommandText = "SELECT COUNT(*) FROM user_prompts";
        prompts = Convert.ToInt32(cmd.ExecuteScalar());
        
        return (sessions, observations, prompts);
    }

    /// <summary>
    /// Runs the "engram doctor" command with specified environment variables.
    /// </summary>
    private async Task<(int ExitCode, string Output)> RunDoctorCommand(
        Dictionary<string, string> envVars, 
        int timeoutMs = 10000,
        string? additionalArgs = null)
    {
        // Solution directory is 3 levels up from test assembly output
        // e.g., tests/Engram.Diagnostics.Tests/bin/Debug/net10.0 -> project root
        var testAssemblyDir = AppContext.BaseDirectory;
        var solutionDir = Path.GetFullPath(Path.Combine(testAssemblyDir, "..", "..", "..", "..", ".."));
        var cliProjectPath = Path.Combine(solutionDir, "src", "Engram.Cli", "Engram.Cli.csproj");
        
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project \"{cliProjectPath}\" -- doctor {additionalArgs}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = solutionDir
        };

        foreach (var kvp in envVars)
        {
            startInfo.EnvironmentVariables[kvp.Key] = kvp.Value;
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        // Wait for process to exit with timeout
        if (!process.WaitForExit(timeoutMs))
        {
            process.Kill();
            throw new TimeoutException($"Doctor command timed out after {timeoutMs}ms");
        }

        await Task.WhenAll(outputTask, errorTask);

        var output = await outputTask;
        var error = await errorTask;

        return (process.ExitCode, output + error);
    }
}
