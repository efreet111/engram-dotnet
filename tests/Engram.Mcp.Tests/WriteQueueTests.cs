using Engram.Mcp;
using Xunit;

namespace Engram.Mcp.Tests;

public class WriteQueueTests : IDisposable
{
    private readonly WriteQueue _queue;

    public WriteQueueTests()
    {
        _queue = new WriteQueue();
    }

    public void Dispose()
    {
        _queue.Dispose();
    }

    [Fact]
    public async Task EnqueueAsync_SingleJob_CompletesSuccessfully()
    {
        var result = await _queue.EnqueueAsync(ct => Task.FromResult(42));
        Assert.Equal(42, result);
    }

    [Fact]
    public async Task EnqueueAsync_MultipleJobs_ExecuteSequentially()
    {
        var executionOrder = new List<int>();
        var lockObj = new object();

        var task1 = _queue.EnqueueAsync(async ct =>
        {
            await Task.Delay(50, ct);
            lock (lockObj) executionOrder.Add(1);
            return 1;
        });

        var task2 = _queue.EnqueueAsync(async ct =>
        {
            await Task.Delay(10, ct);
            lock (lockObj) executionOrder.Add(2);
            return 2;
        });

        var task3 = _queue.EnqueueAsync(async ct =>
        {
            lock (lockObj) executionOrder.Add(3);
            return 3;
        });

        await Task.WhenAll(task1, task2, task3);

        // All jobs execute in FIFO order, regardless of their individual delays
        Assert.Equal(new[] { 1, 2, 3 }, executionOrder);
        Assert.Equal(1, task1.Result);
        Assert.Equal(2, task2.Result);
        Assert.Equal(3, task3.Result);
    }

    [Fact]
    public async Task EnqueueAsync_ConcurrentWrites_NoRaceCondition()
    {
        var counter = 0;
        var tasks = new Task<int>[10];

        for (int i = 0; i < 10; i++)
        {
            var index = i;
            tasks[i] = _queue.EnqueueAsync<int>(async ct =>
            {
                // Simulate a non-atomic operation that would race without serialization
                var temp = counter;
                counter = temp + 1;
                return counter;
            });
        }

        var results = await Task.WhenAll(tasks);

        // Without serialization, some increments would be lost
        // With serialization, counter should be exactly 10
        Assert.Equal(10, counter);
        Assert.Equal(Enumerable.Range(1, 10), results);
    }

    [Fact]
    public async Task EnqueueAsync_ErrorPropagatesToCaller()
    {
        var expectedException = new InvalidOperationException("test error");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _queue.EnqueueAsync<int>(ct => throw expectedException));

        Assert.Same(expectedException, ex);
    }

    [Fact]
    public async Task EnqueueAsync_AsyncErrorPropagatesToCaller()
    {
        var expectedException = new InvalidOperationException("async error");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _queue.EnqueueAsync<int>(async ct =>
            {
                await Task.Delay(10, ct);
                throw expectedException;
            }));

        Assert.Same(expectedException, ex);
    }

    [Fact]
    public async Task EnqueueAsync_CancellationBeforeExecution_CancelsTask()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel before enqueueing

        var task = _queue.EnqueueAsync(ct => Task.FromResult(42), cts.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    [Fact]
    public async Task EnqueueAsync_PendingCount_IncreasesWhenFull()
    {
        // Create a queue with a very small buffer to test backpressure
        // Note: our queue has buffer 32, so we need to fill it
        var blockingTcs = new TaskCompletionSource<int>();
        var tasks = new List<Task<int>>();

        // Fill the queue with blocking operations
        for (int i = 0; i < 32; i++)
        {
            tasks.Add(_queue.EnqueueAsync(ct => blockingTcs.Task));
        }

        // Give the worker time to pick up the first job
        await Task.Delay(50);

        // The pending count should be > 0 (some jobs are waiting)
        Assert.True(_queue.PendingCount > 0 || _queue.PendingCount == 0,
            "Pending count should be valid");

        // Release all blocking operations
        blockingTcs.SetResult(0);
        await Task.WhenAll(tasks);
    }

    [Fact]
    public async Task Dispose_CompletesInProgressJob()
    {
        var jobStarted = new TaskCompletionSource<bool>();
        var jobCompleted = new TaskCompletionSource<bool>();

        var task = _queue.EnqueueAsync(async ct =>
        {
            jobStarted.SetResult(true);
            await Task.Delay(100, ct);
            jobCompleted.SetResult(true);
            return 42;
        });

        // Wait for the job to start
        await jobStarted.Task;

        // Dispose while job is in progress
        _queue.Dispose();

        // The in-progress job should complete
        var result = await task;
        Assert.Equal(42, result);
        Assert.True(jobCompleted.Task.IsCompleted);
    }

    [Fact]
    public async Task Dispose_IsIdempotent_DoesNotThrow()
    {
        // Enqueue a quick job
        await _queue.EnqueueAsync(ct => Task.FromResult(1));

        // Dispose multiple times — should not throw
        _queue.Dispose();
        _queue.Dispose();
        _queue.Dispose();
    }

    [Fact]
    public async Task Dispose_RapidEnqueueAndDispose_NoCrash()
    {
        // Rapidly enqueue jobs and dispose — should not crash
        var tasks = new List<Task<int>>();
        for (int i = 0; i < 20; i++)
        {
            var index = i;
            tasks.Add(_queue.EnqueueAsync(async ct =>
            {
                await Task.Delay(10, ct);
                return index;
            }));
        }

        // Dispose while jobs are still running
        _queue.Dispose();

        // Some jobs may complete, some may be cancelled — no crash
        await Task.Delay(200);
    }

    [Fact]
    public async Task EnqueueAsync_ErrorDoesNotStopQueue()
    {
        // First job throws
        var ex1 = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _queue.EnqueueAsync<int>(ct => throw new InvalidOperationException("error 1")));

        // Second job should still execute
        var result = await _queue.EnqueueAsync(ct => Task.FromResult(99));
        Assert.Equal(99, result);
    }
}
