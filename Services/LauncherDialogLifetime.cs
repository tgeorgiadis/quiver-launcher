using Avalonia.Controls;

namespace QuiverLauncher.Services;

/// <summary>Closes only this session's dialogs when its host is replaced or destroyed.</summary>
public sealed class LauncherDialogLifetime : IDisposable
{
    private readonly HashSet<Window> _windows = [];
    private readonly Action<Action> _dispatch;
    private readonly CancellationTokenRegistration _registration;
    private bool _closed;
    public LauncherDialogLifetime(CancellationToken lifetime, Action<Action> dispatch)
    {
        _dispatch = dispatch;
        _registration = lifetime.Register(() => dispatch(CloseAll));
    }
    public void Track(Window window)
    {
        if (_closed) { _dispatch(window.Close); return; }
        if (!_windows.Add(window)) return;
        window.Closed += Closed;
    }
    private void Closed(object? sender, EventArgs e)
    {
        if (sender is not Window window) return;
        window.Closed -= Closed;
        _windows.Remove(window);
    }
    private void CloseAll()
    {
        _closed = true;
        foreach (var window in _windows.ToArray())
        {
            window.Close();
            window.Closed -= Closed;
        }
        _windows.Clear();
    }
    public void Dispose() { _registration.Dispose(); _dispatch(CloseAll); }
}
