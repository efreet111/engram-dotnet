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
