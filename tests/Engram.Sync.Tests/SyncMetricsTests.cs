using Xunit;

namespace Engram.Sync.Tests;

public sealed class SyncMetricsTests
{
    [Fact]
    public void RecordPush_IncrementsCounter()
    {
        var metrics = new SyncMetrics();
        Assert.Equal(0, metrics.TotalPushed);

        metrics.IncrementPushed();
        Assert.Equal(1, metrics.TotalPushed);

        metrics.IncrementPushed(5);
        Assert.Equal(6, metrics.TotalPushed);
    }

    [Fact]
    public void RecordPull_IncrementsCounter()
    {
        var metrics = new SyncMetrics();
        Assert.Equal(0, metrics.TotalPulled);

        metrics.IncrementPulled();
        Assert.Equal(1, metrics.TotalPulled);

        metrics.IncrementPulled(3);
        Assert.Equal(4, metrics.TotalPulled);
    }

    [Fact]
    public void RecordFailure_IncrementsFailureCount()
    {
        var metrics = new SyncMetrics();
        Assert.Equal(0, metrics.TotalFailures);

        metrics.IncrementFailures();
        Assert.Equal(1, metrics.TotalFailures);

        metrics.IncrementFailures(2);
        Assert.Equal(3, metrics.TotalFailures);
    }

    [Fact]
    public void GetSnapshot_ReturnsCurrentValues()
    {
        var metrics = new SyncMetrics();
        metrics.IncrementPushed(10);
        metrics.IncrementPulled(5);
        metrics.IncrementFailures(2);
        metrics.RecordError("test error");
        metrics.MarkSyncAt(new DateTime(2026, 5, 19, 12, 0, 0, DateTimeKind.Utc));

        var snapshot = metrics.GetSnapshot();

        Assert.Equal(10, snapshot.TotalPushed);
        Assert.Equal(5, snapshot.TotalPulled);
        Assert.Equal(2, snapshot.TotalFailures);
        Assert.Equal("test error", snapshot.LastError);
        Assert.Equal(new DateTime(2026, 5, 19, 12, 0, 0, DateTimeKind.Utc), snapshot.LastSyncAt);
    }

    [Fact]
    public void ThreadSafety_ParallelIncrements_NoRaceCondition()
    {
        var metrics = new SyncMetrics();
        const int iterations = 10000;

        Parallel.For(0, iterations, _ =>
        {
            metrics.IncrementPushed(1);
            metrics.IncrementPulled(1);
            metrics.IncrementFailures(1);
        });

        Assert.Equal(iterations, metrics.TotalPushed);
        Assert.Equal(iterations, metrics.TotalPulled);
        Assert.Equal(iterations, metrics.TotalFailures);
    }
}
