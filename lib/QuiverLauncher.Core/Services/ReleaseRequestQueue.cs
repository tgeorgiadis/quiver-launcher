namespace QuiverLauncher.Core.Services;

/// <summary>Bounded provider queue; interactive checks overtake queued background work.</summary>
internal sealed class ReleaseRequestQueue
{
    private sealed record Waiter(Func<bool> Priority, TaskCompletionSource Ready);
    private readonly object _gate = new();
    private readonly List<Waiter> _waiting = [];
    private const int MaxConcurrency = 8;
    private int _active;

    public async Task EnterAsync(Func<bool> priority, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Waiter waiter;
        lock (_gate)
        {
            if (_active < MaxConcurrency) { _active++; return; }
            waiter = new(priority, new(TaskCreationOptions.RunContinuationsAsynchronously));
            _waiting.Add(waiter);
        }
        using var registration = token.Register(() =>
        {
            lock (_gate)
                if (_waiting.Remove(waiter)) waiter.Ready.TrySetCanceled(token);
        });
        await waiter.Ready.Task.ConfigureAwait(false);
    }

    public void Release()
    {
        lock (_gate)
        {
            if (_waiting.Count == 0) { _active--; return; }
            var next = _waiting.FirstOrDefault(w => w.Priority()) ?? _waiting[0];
            _waiting.Remove(next);
            next.Ready.SetResult();
        }
    }
}
