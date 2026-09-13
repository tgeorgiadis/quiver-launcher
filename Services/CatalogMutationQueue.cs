namespace QuiverLauncher.Services;

/// <summary>FIFO session writes. Once accepted, a save is drained even during shutdown.</summary>
public sealed class CatalogMutationQueue
{
    private Task _tail = Task.CompletedTask;
    private readonly object _gate = new();
    private int _pending;
    public int PendingCount => Volatile.Read(ref _pending);

    public Task Enqueue(Func<Task> operation)
    {
        lock (_gate)
        {
            var previous = _tail;
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _tail = completion.Task;
            Interlocked.Increment(ref _pending);
            return RunAsync(previous, operation, completion);
        }
    }

    private async Task RunAsync(Task previous, Func<Task> operation, TaskCompletionSource completion)
    {
        // Return to input/rendering before starting any persistence or comparison work.
        await Task.Yield();
        await previous;
        try { await operation(); }
        finally
        {
            Interlocked.Decrement(ref _pending);
            completion.TrySetResult(); // One failed write must not poison subsequent writes.
        }
    }
}
