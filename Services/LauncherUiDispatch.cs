namespace QuiverLauncher.Services;

/// <summary>A replaceable UI callback lease that never restores an already closed owner.</summary>
public sealed class LauncherUiDispatch : IDisposable
{
    private readonly GameManager _manager;
    private readonly LauncherSession _session;
    private readonly Func<Action, Task> _dispatch;
    private readonly Func<Action, Task>? _previous;
    private bool _disposed;
    public LauncherUiDispatch(GameManager manager, LauncherSession session, Func<Action, Task> dispatch)
    {
        _manager = manager; _session = session; _dispatch = dispatch;
        _previous = manager.UiThreadInvoker;
        manager.UiThreadInvoker = InvokeAsync;
    }
    private Task InvokeAsync(Action action) => _disposed || _session.IsClosed ? Task.CompletedTask
        : _dispatch(() => { if (!_disposed && !_session.IsClosed) action(); });
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_manager.UiThreadInvoker != InvokeAsync) return;
        var previous = _previous;
        while (previous?.Target is LauncherUiDispatch owner && (owner._disposed || owner._session.IsClosed))
            previous = owner._previous;
        _manager.UiThreadInvoker = previous;
    }
}
