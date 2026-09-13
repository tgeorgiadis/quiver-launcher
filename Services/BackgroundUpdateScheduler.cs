using Avalonia.Threading;

namespace QuiverLauncher.Services;

public sealed class BackgroundUpdateScheduler : IDisposable
{
    private readonly DispatcherTimer _timer = new();
    private readonly LauncherSession _session;
    private readonly Func<Task> _check;
    private bool _disposed;
    public BackgroundUpdateScheduler(LauncherSession session, Func<Task> check)
    {
        _session = session; _check = check;
        _timer.Tick += Tick;
    }
    public void Configure(AppSettings settings)
    {
        _timer.Stop();
        if (_disposed || _session.IsClosed || !settings.BackgroundUpdateCheckEnabled) return;
        _timer.Interval = TimeSpan.FromMinutes(BackgroundUpdateCheckIntervals.Normalize(settings.BackgroundUpdateCheckIntervalMinutes));
        _timer.Start();
    }
    private void Tick(object? sender, EventArgs e)
    {
        if (!_disposed && !_session.IsClosed) _ = _session.RunAsync(_check);
    }
    public void Stop() => _timer.Stop();
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _timer.Tick -= Tick;
    }
}
