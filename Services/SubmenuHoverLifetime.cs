namespace QuiverLauncher.Services;

/// <summary>One cancellable hover deadline. A previous opening can never close a new one.</summary>
internal sealed class SubmenuHoverLifetime(Func<Action, TimeSpan, IDisposable> schedule, Func<bool> isOpen,
    Func<bool> containsPointer, Action close) : IDisposable
{
    internal static readonly TimeSpan CloseDelay = TimeSpan.FromMilliseconds(400);
    private IDisposable? _pending;
    private int _generation;
    public void Cancel()
    {
        ++_generation;
        _pending?.Dispose();
        _pending = null;
    }
    public void Leave()
    {
        Cancel();
        if (!isOpen()) return;
        var generation = _generation;
        _pending = schedule(() =>
        {
            if (generation != _generation) return;
            _pending = null;
            if (isOpen() && !containsPointer()) close();
        }, CloseDelay);
    }
    public void Dispose() => Cancel();
}
