using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Threading;
using QuiverLauncher.ViewModels;

namespace QuiverLauncher.Services;

/// <summary>Scales client content without overriding the platform's DPI or recreating controls.</summary>
internal sealed class DesktopInterfaceScaling : IDisposable
{
    // Responsive dialogs already constrain their content and keep actions outside
    // their scroll area. An outer scroller would measure them at infinite width.
    internal static readonly AttachedProperty<bool> ManagesScrollingProperty =
        AvaloniaProperty.RegisterAttached<Window, bool>("ManagesScrolling", typeof(DesktopInterfaceScaling));
    private static readonly ConditionalWeakTable<Window, DesktopInterfaceScaling> Owners = new();
    private readonly Window _window;
    private readonly SettingsViewModel _settings;
    private readonly LayoutTransformControl _content;
    private readonly Func<Size>? _availableArea;
    private readonly HashSet<ScaledDialog> _dialogs = [];
    private bool _queued, _disposed, _applying;
    internal int AppliedPercent { get; private set; } = 100;
    private static bool _initialized;

    internal static void InitializePopups()
    {
        if (_initialized || PlatformCapabilities.IsMobile) return;
        _initialized = true;
        // Includes internally created ContextMenu/Flyout/ToolTip popups, which cannot
        // all be reached by a style selector. Nested popups inherit their parent's
        // transform once through Avalonia's popup host.
        Popup.ChildProperty.Changed.AddClassHandler<Popup>((popup, _) => EnablePopupScaling(popup));
        Popup.PlacementTargetProperty.Changed.AddClassHandler<Popup>((popup, _) => EnablePopupScaling(popup));
    }

    private static void EnablePopupScaling(Popup popup)
    {
        if (!PlatformCapabilities.IsMobile)
            popup.SetCurrentValue(Popup.InheritsTransformProperty, true);
    }

    public DesktopInterfaceScaling(Window window, SettingsViewModel settings, Func<Size>? availableArea = null)
    {
        InitializePopups();
        _window = window;
        _settings = settings;
        _availableArea = availableArea;
        if (Owners.TryGetValue(window, out var previous)) previous.Dispose();
        Owners.Add(window, this);
        _content = Wrap(window);
        settings.PropertyChanged += SettingsChanged;
        window.PropertyChanged += WindowChanged;
        window.PositionChanged += PositionChanged;
        window.Opened += Opened;
        window.Closed += Closed;
        window.Screens.Changed += ScreensChanged;
        Apply();
    }

    private static LayoutTransformControl Wrap(Window window)
    {
        if (window.Content is LayoutTransformControl existing) return existing;
        var content = window.Content as Control;
        window.Content = null;
        var wrapper = new LayoutTransformControl { Child = content, LayoutTransform = new ScaleTransform(1, 1) };
        window.Content = wrapper;
        return wrapper;
    }

    private void SettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(SettingsViewModel.InterfaceScalePercent)) Refresh();
    }
    private void WindowChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property.Name is "RenderScaling" or "WindowState" or "FrameSize" or "WindowDecorations" or "ClientSize") Refresh();
    }
    private void PositionChanged(object? sender, PixelPointEventArgs e) => Refresh();
    private void ScreensChanged(object? sender, EventArgs e) => Refresh();
    private void Opened(object? sender, EventArgs e) => Refresh();
    private void Closed(object? sender, EventArgs e) => Dispose();

    internal void Refresh()
    {
        if (_queued || _disposed || _applying) return;
        _queued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _queued = false;
            if (!_disposed) Apply();
        }, DispatcherPriority.Loaded);
    }

    private Size AvailableClientArea()
    {
        if (_availableArea != null) return _availableArea();
        return AvailableClientArea(_window);
    }

    private static Size AvailableClientArea(Window window)
    {
        var screen = window.Screens.ScreenFromWindow(window) ?? window.Screens.Primary;
        if (screen == null) return new Size(750, 490);
        var pixels = window.WindowState == WindowState.FullScreen ? screen.Bounds : screen.WorkingArea;
        var dpi = window.IsVisible ? window.RenderScaling : screen.Scaling;
        var frame = window.FrameSize ?? window.ClientSize;
        return new Size(Math.Max(0, pixels.Width / dpi - Math.Max(0, frame.Width - window.ClientSize.Width)),
            Math.Max(0, pixels.Height / dpi - Math.Max(0, frame.Height - window.ClientSize.Height)));
    }

    private void Apply()
    {
        _applying = true;
        try
        {
            var available = AvailableClientArea();
            AppliedPercent = InterfaceScale.Fit(_settings.InterfaceScalePercent, available);
            var scale = AppliedPercent / 100d;
            var transform = (ScaleTransform)_content.LayoutTransform!;
            transform.ScaleX = transform.ScaleY = scale;
            _window.MinWidth = 750 * scale;
            _window.MinHeight = 490 * scale;
            if (_window.WindowState == WindowState.Normal)
            {
                _window.Width = Math.Max(_window.MinWidth, Math.Min(available.Width, _window.Width));
                _window.Height = Math.Max(_window.MinHeight, Math.Min(available.Height, _window.Height));
                KeepOnScreen(_window);
            }
            _settings.SetAppliedInterfaceScale(AppliedPercent);
            foreach (var dialog in _dialogs.ToArray()) dialog.Apply();
        }
        finally { _applying = false; }
    }

    private static void KeepOnScreen(Window window)
    {
        if (!window.IsVisible) return;
        var screen = window.Screens.ScreenFromWindow(window) ?? window.Screens.Primary;
        if (screen == null) return;
        var work = screen.WorkingArea;
        var frame = window.FrameSize ?? window.ClientSize;
        var dpi = window.RenderScaling;
        window.Position = new PixelPoint(
            Math.Clamp(window.Position.X, work.X, Math.Max(work.X, work.Right - (int)Math.Ceiling(frame.Width * dpi))),
            Math.Clamp(window.Position.Y, work.Y, Math.Max(work.Y, work.Bottom - (int)Math.Ceiling(frame.Height * dpi))));
    }

    /// <summary>Call before ShowDialog; native file pickers are intentionally excluded.</summary>
    internal static void PrepareDialog(Window dialog, Window owner)
    {
        if (!Owners.TryGetValue(owner, out var scaling) || scaling._disposed) return;
        if (scaling._dialogs.Any(d => ReferenceEquals(d.Window, dialog))) return;
        var registration = new ScaledDialog(dialog, scaling);
        scaling._dialogs.Add(registration);
        registration.Apply();
    }

    private sealed class ScaledDialog : IDisposable
    {
        public Window Window { get; }
        private readonly DesktopInterfaceScaling _owner;
        private readonly LayoutTransformControl _content;
        private readonly double _width, _height, _minWidth, _minHeight, _maxWidth, _maxHeight;
        public ScaledDialog(Window window, DesktopInterfaceScaling owner)
        {
            Window = window;
            _owner = owner;
            _width = window.Width; _height = window.Height;
            _minWidth = window.MinWidth; _minHeight = window.MinHeight;
            _maxWidth = window.MaxWidth; _maxHeight = window.MaxHeight;
            // Scrolling keeps fixed-size custom dialogs usable on smaller displays.
            _content = Wrap(window);
            if (!window.GetValue(ManagesScrollingProperty))
            {
                var child = _content.Child;
                _content.Child = null;
                _content.Child = new ScrollViewer
                {
                    Content = child,
                    // Height-sized dialogs need a finite width during measurement so text
                    // wraps and SizeToContent can calculate the full required height.
                    HorizontalScrollBarVisibility = window.SizeToContent == SizeToContent.Height
                        ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                };
            }
            window.Closed += Closed;
            window.Opened += Opened;
            Owners.Add(window, owner);
        }
        private void Closed(object? sender, EventArgs e) => Dispose();
        private void Opened(object? sender, EventArgs e) => Apply();
        public void Apply()
        {
            var area = _owner._availableArea?.Invoke() ?? AvailableClientArea(Window.IsVisible ? Window : _owner._window);
            var scale = _owner.AppliedPercent / 100d;
            var transform = (ScaleTransform)_content.LayoutTransform!;
            transform.ScaleX = transform.ScaleY = scale;
            Window.MinWidth = Math.Min(_minWidth * scale, area.Width);
            Window.MinHeight = Math.Min(_minHeight * scale, area.Height);
            Window.MaxWidth = Math.Min(_maxWidth * scale, area.Width);
            Window.MaxHeight = Math.Min(_maxHeight * scale, area.Height);
            if (!double.IsNaN(_width)) Window.Width = Math.Min(_width * scale, area.Width);
            if (!double.IsNaN(_height)) Window.Height = Math.Min(_height * scale, area.Height);
            KeepOnScreen(Window);
        }
        public void Dispose()
        {
            Window.Closed -= Closed;
            Window.Opened -= Opened;
            Owners.Remove(Window);
            _owner._dialogs.Remove(this);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _settings.PropertyChanged -= SettingsChanged;
        _window.PropertyChanged -= WindowChanged;
        _window.PositionChanged -= PositionChanged;
        _window.Opened -= Opened;
        _window.Closed -= Closed;
        _window.Screens.Changed -= ScreensChanged;
        foreach (var dialog in _dialogs.ToArray()) dialog.Dispose();
        if (Owners.TryGetValue(_window, out var current) && ReferenceEquals(current, this)) Owners.Remove(_window);
    }
}
