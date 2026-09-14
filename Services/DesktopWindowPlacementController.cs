using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using QuiverLauncher.ViewModels;

namespace QuiverLauncher.Services;

/// <summary>Tracks only settled, visible desktop geometry, independently of transient window states.</summary>
internal sealed class DesktopWindowPlacementController : IDisposable
{
    private readonly Window _window;
    private readonly SettingsViewModel _settings;
    private readonly DispatcherTimer _saveTimer;
    private DesktopWindowPlacement? _placement, _persisted, _initialPlacement;
    private bool _ready, _suspended, _queued, _disposed;

    internal DesktopWindowPlacementController(Window window, SettingsViewModel settings)
    {
        _window = window;
        _settings = settings;
        _persisted = settings.Current.DesktopWindowPlacement;
        _initialPlacement = _persisted is { IsValid: true } ? _persisted : null;
        var frame = window.FrameSize ?? window.ClientSize;
        var extra = new Size(Math.Max(0, frame.Width - window.ClientSize.Width),
            Math.Max(0, frame.Height - window.ClientSize.Height));
        _placement = DesktopPlacementGeometry.Restore(_persisted,
            window.Screens.All.Select(s => new PlacementScreen(s.Bounds, s.WorkingArea, s.Scaling, s.IsPrimary)).ToArray(),
            settings.InterfaceScalePercent, extra);
        if (_placement != null)
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Position = new PixelPoint(_placement.X, _placement.Y);
            window.Width = _placement.Width;
            window.Height = _placement.Height;
        }
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _saveTimer.Tick += SaveTick;
        window.Opened += Opened;
        window.PositionChanged += PositionChanged;
        window.PropertyChanged += Changed;
    }

    internal void ApplyStartupState()
    {
        // Capture the normal size before maximizing; Avalonia retains it for Restore Down.
        _placement ??= new DesktopWindowPlacement(_window.Width, _window.Height,
            _window.Position.X, _window.Position.Y, false);
        _window.WindowState = _settings.Current.StartFullscreen
            ? SteamDeckEnvironment.DesktopFullscreenWindowState()
            : _placement.Maximized ? WindowState.Maximized : WindowState.Normal;
        if (_window.WindowState != WindowState.Normal && _initialPlacement != null)
            _placement = _initialPlacement;
    }

    private void Opened(object? sender, EventArgs e) => Resume();

    internal void Resume()
    {
        _suspended = false;
        Dispatcher.UIThread.Post(CorrectInitialBounds, DispatcherPriority.Loaded);
        // Scaling/layout run at Loaded priority. Do not capture the intermediate startup sizes.
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed || _suspended) return;
            _ready = true;
            Capture();
        }, DispatcherPriority.Background);
    }

    private void PositionChanged(object? sender, PixelPointEventArgs e) => QueueCapture();

    private void CorrectInitialBounds()
    {
        if (_disposed || _suspended || !_window.IsVisible || _window.WindowState != WindowState.Normal || _initialPlacement == null) return;
        // Before Show, some backends estimate a full OS title bar even with custom chrome.
        // Refine the initial clamp using the real frame, so small screens do not lose height
        // on every restart. A maximized startup defers this until the first Restore Down.
        var frame = _window.FrameSize ?? _window.ClientSize;
        var restored = DesktopPlacementGeometry.Restore(_initialPlacement,
            _window.Screens.All.Select(s => new PlacementScreen(s.Bounds, s.WorkingArea, s.Scaling, s.IsPrimary)).ToArray(),
            _settings.InterfaceScalePercent, new Size(Math.Max(0, frame.Width - _window.ClientSize.Width),
                Math.Max(0, frame.Height - _window.ClientSize.Height)));
        _initialPlacement = null;
        if (restored == null) return;
        _window.Width = restored.Width;
        _window.Height = restored.Height;
        _window.Position = new PixelPoint(restored.X, restored.Y);
    }

    private void Changed(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WindowStateProperty && _ready && !_suspended && _window.IsVisible && _placement != null)
        {
            // Remember the state immediately, even if minimize/close follows before layout runs.
            // Bounds still wait for layout so a maximize resize cannot overwrite Restore Down.
            if (_window.WindowState is WindowState.Normal or WindowState.Maximized)
                Remember(_placement with { Maximized = _window.WindowState == WindowState.Maximized });
            if (_window.WindowState == WindowState.Normal && _initialPlacement != null)
                Dispatcher.UIThread.Post(CorrectInitialBounds, DispatcherPriority.Loaded);
        }
        if (e.Property.Name is "ClientSize" or "Width" or "Height" or "WindowState") QueueCapture();
    }

    private void QueueCapture()
    {
        if (!_ready || _suspended || _disposed || _queued) return;
        _queued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _queued = false;
            if (!_disposed) Capture();
        }, DispatcherPriority.Background);
    }

    private void Capture()
    {
        if (!_ready || _suspended || !_window.IsVisible) return;
        var next = _placement;
        if (_window.WindowState == WindowState.Normal)
        {
            var size = _window.ClientSize;
            next = new DesktopWindowPlacement(size.Width, size.Height, _window.Position.X, _window.Position.Y, false);
        }
        else if (_window.WindowState == WindowState.Maximized && next != null)
            next = next with { Maximized = true };
        // Minimized/fullscreen bounds never replace the last normal bounds or maximized flag.
        Remember(next);
    }

    private void Remember(DesktopWindowPlacement? next)
    {
        if (next is not { IsValid: true } || next == _placement) return;
        _placement = next;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SaveTick(object? sender, EventArgs e) => Flush();

    internal void Flush()
    {
        Capture();
        _saveTimer.Stop();
        if (_placement is not { IsValid: true } || _placement == _persisted) return;
        try
        {
            _settings.SaveWindowPlacement(_placement);
            _persisted = _placement;
        }
        catch (Exception ex) { CrashLog.Log("Saving desktop window placement", ex); }
    }

    internal void Suspend()
    {
        Flush();
        _suspended = true;
        _ready = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _saveTimer.Stop();
        _saveTimer.Tick -= SaveTick;
        _window.Opened -= Opened;
        _window.PositionChanged -= PositionChanged;
        _window.PropertyChanged -= Changed;
    }
}
