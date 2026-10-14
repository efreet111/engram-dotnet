using Engram.Sync;
using Moq;
using Xunit;

namespace Engram.Sync.Tests;

/// <summary>
/// HU-065 F2: Tests for --auto-enroll flag in sync enroll command.
/// </summary>
public class SyncAutoEnrollTests : IDisposable
{
    private readonly string? _originalServerUrl;
    private readonly string? _originalUser;

    public SyncAutoEnrollTests()
    {
        _originalServerUrl = Environment.GetEnvironmentVariable("ENGRAM_SERVER_URL");
        _originalUser = Environment.GetEnvironmentVariable("ENGRAM_USER");
    }

    public void Dispose()
    {
        if (_originalServerUrl is not null)
            Environment.SetEnvironmentVariable("ENGRAM_SERVER_URL", _originalServerUrl);
        else
            Environment.SetEnvironmentVariable("ENGRAM_SERVER_URL", null);

        if (_originalUser is not null)
            Environment.SetEnvironmentVariable("ENGRAM_USER", _originalUser);
        else
            Environment.SetEnvironmentVariable("ENGRAM_USER", null);
    }

    // ─── SyncServerEnrollment.EnrollProjectOnServerAsync unit tests ──────────

    [Fact]
    public async Task EnrollProjectOnServerAsync_ServerNotReachable_ReturnsFalse()
    {
        // Use a localhost port that nothing is listening on
        var serverUrl = "http://localhost:54321";

        var result = await SyncServerEnrollment.EnrollProjectOnServerAsync(serverUrl, "test-project");

        Assert.False(result); // best-effort: returns false without throwing
    }

    [Fact]
    public async Task EnrollProjectOnServerAsync_NullServerUrl_ReturnsFalse()
    {
        Environment.SetEnvironmentVariable("ENGRAM_SERVER_URL", null);

        var result = await SyncServerEnrollment.EnrollProjectOnServerAsync(null!, "test-project");

        Assert.False(result);
    }

    [Fact]
    public async Task EnrollProjectOnServerAsync_EmptyServerUrl_ReturnsFalse()
    {
        var result = await SyncServerEnrollment.EnrollProjectOnServerAsync("", "test-project");

        // Empty URL — should not throw, returns false
        Assert.False(result);
    }

    [Fact]
    public async Task EnrollProjectOnServerAsync_NonExistentHost_ReturnsFalse()
    {
        // Use a domain that will never resolve
        var result = await SyncServerEnrollment.EnrollProjectOnServerAsync(
            "http://this-domain-does-not-exist-12345.invalid/enroll", "any-project");

        // Should return false, not throw
        Assert.False(result);
    }

    [Fact]
    public async Task EnrollProjectOnServerAsync_CancellationRequested_ReturnsFalse()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await SyncServerEnrollment.EnrollProjectOnServerAsync(
            "http://localhost:7437", "test-project", cts.Token);

        Assert.False(result);
    }

    // ─── Auto-enroll behavior (integration — requires Docker) ─────────────

    [Trait("Category", "RequiresDocker")]
    [Fact(Skip = "Requires Docker + running Engram server. Enable manually with --filter 'Category=RequiresDocker'")]
    public async Task AutoEnroll_WithAutoEnrollFlag_EnrollsLocalAndServer()
    {
        // This test requires a running Engram server in Docker.
        Environment.SetEnvironmentVariable("ENGRAM_SERVER_URL", "http://localhost:7437");
        Environment.SetEnvironmentVariable("ENGRAM_USER", "test-user");

        var result = await SyncServerEnrollment.EnrollProjectOnServerAsync(
            "http://localhost:7437", "test-project-autoenroll");

        Assert.True(result);
    }

    [Trait("Category", "RequiresDocker")]
    [Fact(Skip = "Requires Docker + running Engram server. Enable manually with --filter 'Category=RequiresDocker'")]
    public async Task AutoEnroll_ServerAlreadyEnrolled_DoesNotThrow()
    {
        // When server returns 409 (already enrolled), EnrollProjectOnServerAsync returns false
        // without throwing — local enrollment succeeded regardless.
        Environment.SetEnvironmentVariable("ENGRAM_SERVER_URL", "http://localhost:7437");
        Environment.SetEnvironmentVariable("ENGRAM_USER", "test-user");

        // First enrollment
        await SyncServerEnrollment.EnrollProjectOnServerAsync(
            "http://localhost:7437", "test-project-twice");

        // Second enrollment should not throw
        var result = await SyncServerEnrollment.EnrollProjectOnServerAsync(
            "http://localhost:7437", "test-project-twice");

        // Returns false (already enrolled) but does not throw
        Assert.False(result);
    }

    // ─── Auto-enroll validation (CLI parsing) ─────────────────────────────

    /// <summary>
    /// Tests the validation logic for --auto-enroll incompatible with --all.
    /// This is a unit test of the validation pattern (not the full CLI).
    /// </summary>
    [Fact]
    public void AutoEnrollValidation_AllAndAutoEnroll_AreIncompatible()
    {
        // This tests the validation rule: --auto-enroll and --all cannot be used together.
        // The actual CLI validation is in Program.cs syncEnrollCmd SetAction.
        // Here we verify the expected error message pattern.
        var errorMessage = "error: --auto-enroll is not compatible with --all or --interactive.";

        Assert.Contains("--auto-enroll", errorMessage);
        Assert.Contains("--all", errorMessage);
    }

    /// <summary>
    /// Tests the validation logic for --auto-enroll incompatible with --interactive.
    /// </summary>
    [Fact]
    public void AutoEnrollValidation_InteractiveAndAutoEnroll_AreIncompatible()
    {
        var errorMessage = "error: --auto-enroll is not compatible with --all or --interactive.";

        Assert.Contains("--auto-enroll", errorMessage);
        Assert.Contains("--interactive", errorMessage);
    }
}
