using System.Threading.Channels;

namespace Engram.Mcp;

/// <summary>
/// Serializes all MCP write operations through a bounded channel to prevent
/// SQLite concurrency issues (SQLITE_BUSY, database is locked).
///
/// Read operations bypass the queue and call the store directly.
///
/// Mirrors the Go original's write queue in internal/mcp/mcp.go.
/// </summary>
public sealed class WriteQueue : IDisposable
{
    // Each job is a Func<Task> that executes the operation and completes the TCS
    private readonly Channel<Func<Task>> _queue;
    private readonly Task _worker;
    private readonly CancellationTokenSource _cts = new();
    private readonly TimeSpan _shutdownTimeout = TimeSpan.FromSeconds(5);
    private int _disposed; // 0 = not disposed, 1 = disposed

    public int PendingCount => _queue.Reader.Count;

    public WriteQueue()
    {
        _queue = Channel.CreateBounded<Func<Task>>(new BoundedChannelOptions(32)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });

        _worker = Task.Run(async () => await WorkerLoopAsync(), _cts.Token);
    }

    /// <summary>
    /// Enqueues a write operation and returns a Task that completes when the write finishes.
    /// The operation receives a CancellationToken that is triggered when the queue is disposed.
    /// </summary>
    public Task<T> EnqueueAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Register cancellation — if caller cancels before the job runs,
        // we skip execution and cancel the TCS
        if (ct.CanBeCanceled)
        {
            ct.Register(static state =>
            {
                var source = (TaskCompletionSource<T>)state!;
                source.TrySetCanceled();
            }, tcs);
        }

        // Enqueue a job that executes the operation and completes the TCS
        _queue.Writer.TryWrite(async () =>
        {
            try
            {
                if (ct.IsCancellationRequested)
                {
                    tcs.TrySetCanceled(ct);
                    return;
                }

                var result = await operation(ct);
                tcs.TrySetResult(result);
            }
            catch (OperationCanceledException)
            {
                tcs.TrySetCanceled(ct);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });

        return tcs.Task;
    }

    private async Task WorkerLoopAsync()
    {
        try
        {
            await foreach (var job in _queue.Reader.ReadAllAsync(_cts.Token))
            {
                try
                {
                    await job();
                }
                catch (Exception ex)
                {
                    // Individual job errors are handled inside the job closure
                    // and propagated to the caller via TaskCompletionSource
                    Console.Error.WriteLine($"[write-queue] unhandled job error: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Channel reader cancelled during shutdown — expected
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[write-queue] worker error: {ex.Message}");
        }
    }

    public void Dispose()
    {
        // Idempotent: only dispose once
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        // Signal the worker to stop
        _cts.Cancel();
        _queue.Writer.Complete();

        try
        {
            // Wait for the worker to finish (with timeout)
            _worker.Wait(_shutdownTimeout);
        }
        catch (AggregateException ex) when (ex.InnerException is OperationCanceledException)
        {
            // Expected — worker was cancelled
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[write-queue] shutdown error: {ex.Message}");
        }

        _cts.Dispose();
    }
}
