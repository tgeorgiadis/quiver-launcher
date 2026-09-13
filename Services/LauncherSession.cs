namespace QuiverLauncher.Services;

/// <summary>Owns the lifetime of a hosted launcher and drains work before releasing dependencies.</summary>
public sealed class LauncherSession : IAsyncDisposable
{
    public CatalogMutationQueue CatalogMutations { get; } = new();
    private static readonly AsyncLocal<CancellationToken> OperationLifetime = new();
    internal static CancellationToken OperationCancellation => OperationLifetime.Value;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly HashSet<Task> _operations = [];
    private readonly List<Action> _cleanup = [];
    private readonly object _gate = new();
    private Task? _shutdown;
    private bool _initialized;
    public CancellationToken Token { get; }
    private volatile bool _isClosed;
    public bool IsClosed => _isClosed;

    public LauncherSession() => Token = _lifetime.Token;

    public async Task<T> RunAsync<T>(Func<Task<T>> operation)
    {
        T result = default!;
        await RunAsync(async () => { result = await operation(); });
        return result;
    }

    public bool TryInitialize()
    {
        lock (_gate)
        {
            if (_initialized || IsClosed) return false;
            return _initialized = true;
        }
    }

    public void OnShutdown(Action cleanup)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(IsClosed, this);
            _cleanup.Add(cleanup);
        }
    }

    public Task RunAsync(Func<Task> operation)
    {
        TaskCompletionSource completion;
        lock (_gate)
        {
            if (IsClosed) return Task.CompletedTask;
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _operations.Add(completion.Task);
        }
        // Register before calling user code: it may synchronously initiate shutdown.
        _ = RunOperationAsync(operation, completion);
        return completion.Task;
    }

    private async Task RunOperationAsync(Func<Task> operation, TaskCompletionSource completion)
    {
        var previous = OperationLifetime.Value;
        OperationLifetime.Value = Token;
        try
        {
            Token.ThrowIfCancellationRequested();
            await operation();
            completion.TrySetResult();
        }
        catch (OperationCanceledException) when (Token.IsCancellationRequested) { completion.TrySetResult(); }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
            _ = completion.Task.Exception;
        }
        finally
        {
            OperationLifetime.Value = previous;
            lock (_gate) _operations.Remove(completion.Task);
        }
    }

    public ValueTask DisposeAsync()
    {
        TaskCompletionSource completion;
        Task[] operations;
        lock (_gate)
        {
            if (_shutdown != null) return new ValueTask(_shutdown);
            _isClosed = true;
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _shutdown = completion.Task;
            operations = _operations.ToArray();
        }
        // Publish the shutdown task before invoking cancellation callbacks.
        _ = DrainAsync(operations, completion);
        return new ValueTask(completion.Task);
    }

    private async Task DrainAsync(Task[] operations, TaskCompletionSource completion)
    {
        List<Exception>? failures = null;
        try { _lifetime.Cancel(); }
        catch (Exception ex) { (failures ??= []).Add(ex); }
        try { await Task.WhenAll(operations).ConfigureAwait(false); }
        catch (Exception) { /* Callers report failures; shutdown must still release resources. */ }
        finally
        {
            foreach (var cleanup in _cleanup.AsEnumerable().Reverse())
            {
                try { cleanup(); }
                catch (Exception ex) { (failures ??= []).Add(ex); }
            }
            _lifetime.Dispose();
            if (failures != null) completion.TrySetException(new AggregateException(failures));
            else completion.TrySetResult();
        }
    }
}
