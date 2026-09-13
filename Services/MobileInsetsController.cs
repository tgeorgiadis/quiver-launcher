using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Platform;
using Avalonia.Media;
using Avalonia.Threading;

namespace QuiverLauncher.Services;

/// <summary>Owns safe-area and input-pane subscriptions for an attached mobile root.</summary>
public sealed class MobileInsetsController : IDisposable
{
    private readonly UserControl _root;
    private readonly Action _layoutChanged;
    private IInsetsManager? _insets;
    private IInputPane? _androidInputPane;
    private Thickness _lastSafeArea;
    private double _imeBottomInset;
    private bool _disposed;
    private int _attachment;

    public MobileInsetsController(UserControl root, Action layoutChanged)
    {
        _root = root;
        _layoutChanged = layoutChanged;
    }

    public void Refresh() => ApplySafeAreaPadding(_lastSafeArea);
    public void Detach()
    {
        ++_attachment;
        if (_insets != null) _insets.SafeAreaChanged -= OnAndroidSafeAreaChanged;
        _insets = null;
        DetachAndroidInputPane();
    }
    public void Dispose() { if (_disposed) return; _disposed = true; Detach(); }
    private static double ToDip(double value, double scale, double dipCeiling) =>
        scale > 1 && value > dipCeiling ? value / scale : value;
        public void Attach()
        {
            if (_disposed || !OperatingSystem.IsAndroid())
                return;

            Detach();
            var topLevel = TopLevel.GetTopLevel(_root);
            var insets = topLevel?.InsetsManager;
            if (insets == null)
                return;

            TopLevel.SetAutoSafeAreaPadding(_root, false);
            if (topLevel != null)
                TopLevel.SetAutoSafeAreaPadding(topLevel, false);

            var barColor = Color.Parse("#0e0e10");
            topLevel!.Background = new SolidColorBrush(barColor);
            _root.Background = topLevel.Background;
            insets.DisplayEdgeToEdgePreference = true;
            insets.SystemBarColor = barColor;
            ApplySafeAreaPadding(insets);
            _insets = insets;
            insets.SafeAreaChanged += OnAndroidSafeAreaChanged;
            AttachAndroidInputPane(topLevel);
        }

        private void AttachAndroidInputPane(TopLevel? topLevel)
        {
            var pane = topLevel?.InputPane;
            if (ReferenceEquals(_androidInputPane, pane))
                return;

            DetachAndroidInputPane();
            if (pane == null)
                return;

            _androidInputPane = pane;
            _androidInputPane.StateChanged += OnAndroidInputPaneStateChanged;
            ApplyImeBottomInset(_androidInputPane);
        }

        private void DetachAndroidInputPane()
        {
            if (_androidInputPane == null)
                return;

            _androidInputPane.StateChanged -= OnAndroidInputPaneStateChanged;
            _androidInputPane = null;
        }

        private void OnAndroidInputPaneStateChanged(object? sender, InputPaneStateEventArgs e)
        {
            if (e.NewState != InputPaneState.Open)
            {
                _imeBottomInset = 0;
            }
            else
            {
                var topLevel = TopLevel.GetTopLevel(_root);
                var clientHeight = topLevel?.ClientSize.Height ?? _root.Bounds.Height;
                var scale = topLevel?.RenderScaling ?? 1;
                var rect = e.EndRect.Height > 0 ? e.EndRect : _androidInputPane?.OccludedRect ?? default;
                _imeBottomInset = MobileImeOverlap.BottomPadding(
                    clientHeight,
                    rect.Y,
                    rect.Height,
                    scale);
            }

            ApplySafeAreaPadding(_lastSafeArea);
            var attachment = _attachment;
            Dispatcher.UIThread.Post(() =>
            {
                if (!_disposed && attachment == _attachment) ScrollFocusedInputIntoView();
            }, DispatcherPriority.Loaded);
        }

        private void ApplyImeBottomInset(IInputPane pane)
        {
            var topLevel = TopLevel.GetTopLevel(_root);
            var clientHeight = topLevel?.ClientSize.Height ?? _root.Bounds.Height;
            var scale = topLevel?.RenderScaling ?? 1;
            _imeBottomInset = pane.State == InputPaneState.Open
                ? MobileImeOverlap.BottomPadding(
                    clientHeight,
                    pane.OccludedRect.Y,
                    pane.OccludedRect.Height,
                    scale)
                : 0;
            ApplySafeAreaPadding(_lastSafeArea);
        }

        private void ScrollFocusedInputIntoView()
        {
            if (!PlatformCapabilities.IsMobile)
                return;

            var focused = TopLevel.GetTopLevel(_root)?.FocusManager?.GetFocusedElement();
            if (focused is Control control)
                control.BringIntoView();
        }

        private void OnAndroidSafeAreaChanged(object? sender, SafeAreaChangedArgs e)
        {
            ApplySafeAreaPadding(e.SafeAreaPadding);
        }

        private void ApplySafeAreaPadding(IInsetsManager insets)
        {
            ApplySafeAreaPadding(insets.SafeAreaPadding);
        }

        public void ApplySafeAreaPadding(Thickness safeArea)
        {
            if (_disposed) return;
            _lastSafeArea = safeArea;
            // Portrait: status + nav bars only. Landscape: also inset left/right so the
            // 3-button system nav does not cover cards.
            // Avalonia 12 on some devices reports these in pixels; DIP values stay small.
            var scale = TopLevel.GetTopLevel(_root)?.RenderScaling ?? 1;
            var landscape = _root.Bounds.Width > _root.Bounds.Height && _root.Bounds.Width > 1;
            var navBottom = ToDip(safeArea.Bottom, scale, 80);
            var bottom = Math.Max(navBottom, _imeBottomInset);
            if (_imeBottomInset > 0)
                bottom += 8;
            var next = new Thickness(
                landscape ? ToDip(safeArea.Left, scale, 64) : 0,
                ToDip(safeArea.Top, scale, 64),
                landscape ? ToDip(safeArea.Right, scale, 80) : 0,
                bottom);
            if (_root.Padding != next)
                _root.Padding = next;

            _layoutChanged();
        }

}
