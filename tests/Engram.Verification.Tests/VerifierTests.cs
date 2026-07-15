using Engram.Store;
using Engram.Verification;
using Xunit;

namespace Engram.Verification.Tests;

public class CycleTrackerTests
{
    [Fact]
    public async Task GetCurrentCycle_NewChange_ReturnsZero()
    {
        using var store = CreateStore();
        var tracker = new CycleTracker(store);

        var cycle = await tracker.GetCurrentCycleAsync("test-change");

        Assert.Equal(0, cycle);
    }

    [Fact]
    public async Task IncrementCycle_IncreasesCount()
    {
        using var store = CreateStore();
        var tracker = new CycleTracker(store);

        // Create session first (FK constraint on observations)
        await store.CreateSessionAsync("test-session", "engram", "");

        var cycle1 = await tracker.IncrementCycleAsync("test-change", "test-session");
        var cycle2 = await tracker.IncrementCycleAsync("test-change", "test-session");

        Assert.Equal(2, cycle2);
    }

    [Fact]
    public async Task ResetCycle_ClearsCount()
    {
        using var store = CreateStore();
        var tracker = new CycleTracker(store);

        await store.CreateSessionAsync("test-session", "engram", "");
        await tracker.IncrementCycleAsync("test-change", "test-session");
        await tracker.ResetCycleAsync("test-change");

        var cycle = await tracker.GetCurrentCycleAsync("test-change");
        Assert.Equal(0, cycle);
    }

    [Fact]
    public void ShouldEscalate_AtMaxCycles_ReturnsTrue()
    {
        var tracker = new CycleTracker(CreateStore());
        Assert.True(tracker.ShouldEscalate(3));
        Assert.False(tracker.ShouldEscalate(2));
    }

    [Fact]
    public void MaxCycles_DefaultIsThree()
    {
        var tracker = new CycleTracker(CreateStore());
        Assert.Equal(3, tracker.MaxCycles);
    }

    private static IStore CreateStore()
    {
        var cfg = new StoreConfig
        {
            DataDir = Path.Combine(Path.GetTempPath(), $"engram-test-{Guid.NewGuid()}")
        };
        return new SqliteStore(cfg);
    }
}

public class LlmVerifierConstructorTests
{
    [Fact]
    public void LlmVerifier_Constructor_MissingApiKey_Throws()
    {
        // Save and clear the env var
        var original = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        try
        {
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", null);

            var ex = Assert.Throws<InvalidOperationException>(() => new LlmVerifier());
            Assert.Contains("ANTHROPIC_API_KEY", ex.Message);
        }
        finally
        {
            // Restore original value
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", original);
        }
    }

    [Fact]
    public void LlmVerifier_Constructor_WithApiKey_Creates()
    {
        // Save and set the env var
        var original = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        try
        {
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", "test-key");

            using var verifier = new LlmVerifier();
            Assert.NotNull(verifier);
        }
        finally
        {
            // Restore original value
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", original);
        }
    }
}

public class NoOpVerifierTests
{
    [Fact]
    public async Task NoOpVerifier_VerifyAsync_ReturnsEmptyReport()
    {
        var verifier = new NoOpVerifier();
        var spec = new SpecParseResult
        {
            Objective = "Test",
            Requirements = new List<Requirement>()
        };

        var report = await verifier.VerifyAsync(spec, "", 42);

        Assert.Equal(42, report.Cycle);
        Assert.Equal(0, report.Total);
        Assert.Equal(0, report.Passed);
        Assert.Equal(0, report.Failed);
        Assert.Equal(100.0, report.CoveragePct);
        Assert.Equal(100.0, report.PassPct);
        Assert.False(report.Escalate);
        Assert.Empty(report.Items);
        Assert.Contains("ANTHROPIC_API_KEY", report.Summary);
    }
}

public class TraceabilityMatrixTests
{
    [Fact]
    public async Task BuildMatrix_NoObservations_AllMissing()
    {
        using var store = CreateStore();
        var builder = new TraceabilityMatrixBuilder(store);

        var spec = new SpecParseResult
        {
            Objective = "Test",
            Requirements = new List<Requirement>
            {
                new() { Id = "RF-001", Type = "RF", Description = "Do something" }
            }
        };

        var matrix = await builder.BuildMatrixAsync(spec, "test-project");

        Assert.Equal(1, matrix.Total);
        Assert.Equal(0, matrix.Covered);
        Assert.Equal(1, matrix.Missing);
        Assert.Equal(0, matrix.CoveragePct);
    }

    [Fact]
    public async Task BuildMatrix_EmptyRequirements_ReturnsEmpty()
    {
        using var store = CreateStore();
        var builder = new TraceabilityMatrixBuilder(store);

        var spec = new SpecParseResult
        {
            Objective = "Test",
            Requirements = new List<Requirement>()
        };

        var matrix = await builder.BuildMatrixAsync(spec, "test-project");

        Assert.Equal(0, matrix.Total);
    }

    private static IStore CreateStore()
    {
        var cfg = new StoreConfig
        {
            DataDir = Path.Combine(Path.GetTempPath(), $"engram-test-{Guid.NewGuid()}")
        };
        return new SqliteStore(cfg);
    }
}
