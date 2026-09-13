using System;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace QuiverLauncher.Services;

/// <summary>
/// Keeps MenuItem submenus inside the Quiver window and overlaps them with the parent
/// so the pointer never crosses a dead gap.
/// Fluent's default HorizontalOffset (-4) overlaps when the popup opens to the right,
/// but opens a gap when the submenu is placed on the left.
/// </summary>
public static class MenuSubmenuOverlap
{
    public const double OverlapPixels = 5;
    public const double FallbackSubmenuWidth = 180;

    public static readonly AttachedProperty<bool> EnableProperty =
        AvaloniaProperty.RegisterAttached<MenuItem, bool>("Enable", typeof(MenuSubmenuOverlap));

    private static readonly ConditionalWeakTable<MenuItem, PopupHook> Hooks = new();
    private static readonly AttachedProperty<TopLevel?> HostProperty =
        AvaloniaProperty.RegisterAttached<MenuItem, TopLevel?>("Host", typeof(MenuSubmenuOverlap), inherits: true);

    internal static void SetFlyoutHost(MenuFlyout flyout, TopLevel? host)
    {
        foreach (var item in flyout.Items.OfType<MenuItem>())
            if (host == null) item.ClearValue(HostProperty);
            else item.SetValue(HostProperty, host);
    }

    static MenuSubmenuOverlap()
    {
        EnableProperty.Changed.AddClassHandler<MenuItem>(OnEnableChanged);
    }

    public static bool GetEnable(MenuItem element) => element.GetValue(EnableProperty);

    public static void SetEnable(MenuItem element, bool value) => element.SetValue(EnableProperty, value);

    /// <summary>
    /// Negative offset pulls a right-side popup into the parent; positive pulls a left-side popup in.
    /// </summary>
    public static double ChooseHorizontalOffset(
        double parentLeft,
        double parentRight,
        double popupLeft,
        double popupRight,
        double overlapPixels)
    {
        var parentCenter = (parentLeft + parentRight) / 2;
        var popupCenter = (popupLeft + popupRight) / 2;
        return popupCenter < parentCenter ? overlapPixels : -overlapPixels;
    }

    /// <summary>
    /// True when a right-opening submenu would extend past the host window's right edge.
    /// </summary>
    public static bool ShouldOpenToTheLeft(double itemRight, double popupWidth, double windowRight) =>
        itemRight + popupWidth > windowRight;

    internal static double CorrectHorizontalOffset(double currentOffset, double parentLeft, double parentRight,
        double popupLeft, double popupRight, double scale)
    {
        var left = (popupLeft + popupRight) / 2 < (parentLeft + parentRight) / 2;
        var correction = left ? parentLeft + OverlapPixels * scale - popupRight
            : parentRight - OverlapPixels * scale - popupLeft;
        return currentOffset + correction / scale;
    }

    private static void OnEnableChanged(MenuItem item, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.GetNewValue<bool>())
            Attach(item);
        else
            Detach(item);
    }

    private static void Attach(MenuItem item)
    {
        item.AttachedToVisualTree -= OnAttached;
        item.AttachedToVisualTree += OnAttached;
        item.DetachedFromVisualTree -= OnDetached;
        item.DetachedFromVisualTree += OnDetached;
        item.TemplateApplied -= OnTemplateApplied;
        item.TemplateApplied += OnTemplateApplied;
        HookPopup(item, FindSubmenuPopup(item));
    }

    private static void Detach(MenuItem item)
    {
        item.AttachedToVisualTree -= OnAttached;
        item.DetachedFromVisualTree -= OnDetached;
        item.TemplateApplied -= OnTemplateApplied;
        if (Hooks.TryGetValue(item, out var hook))
            hook.Unhook();
    }

    private static void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is MenuItem item && GetEnable(item)) HookPopup(item, FindSubmenuPopup(item));
    }
    private static void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is MenuItem item && Hooks.TryGetValue(item, out var hook)) hook.Unhook();
    }

    private static void OnTemplateApplied(object? sender, TemplateAppliedEventArgs e)
    {
        if (sender is not MenuItem item)
            return;

        HookPopup(item, e.NameScope.Find<Popup>("PART_Popup"));
    }

    private static void HookPopup(MenuItem item, Popup? popup)
    {
        var hook = Hooks.GetValue(item, _ => new PopupHook(item));
        hook.Replace(popup, (_, _) => SchedulePlacementAndOverlap(item, popup));
    }

    private static Popup? FindSubmenuPopup(MenuItem item) =>
        item.GetVisualDescendants().OfType<Popup>().FirstOrDefault(popup => popup.Name == "PART_Popup");

    private static void SchedulePlacementAndOverlap(MenuItem item, Popup? popup)
    {
        if (popup == null)
            return;

        if (Hooks.TryGetValue(item, out var hook))
            hook.Schedule(() => ApplyPlacementAndOverlap(item, popup));
    }

    private static void ApplyPlacementAndOverlap(MenuItem item, Popup popup)
    {
        if (!popup.IsOpen || item.ItemCount == 0)
            return;

        if (ApplyWindowConstrainedPlacement(item, popup))
        {
            if (Hooks.TryGetValue(item, out var hook)) hook.Schedule(() => ApplyOverlap(item, popup));
            return;
        }

        ApplyOverlap(item, popup);
    }

    /// <returns>True when placement changed and overlap should wait for the next layout.</returns>
    private static bool ApplyWindowConstrainedPlacement(MenuItem item, Popup popup)
    {
        var host = GetApplicationTopLevel(item);
        if (host == null)
            return false;

        PixelPoint itemRight;
        PixelPoint windowRight;
        try
        {
            itemRight = item.PointToScreen(new Point(item.Bounds.Width, 0));
            windowRight = host.PointToScreen(new Point(host.ClientSize.Width, 0));
        }
        catch (InvalidOperationException)
        {
            return false;
        }

        var scale = host.RenderScaling;
        var popupWidth = MeasurePopupWidth(popup, scale);
        var desired = ShouldOpenToTheLeft(itemRight.X, popupWidth, windowRight.X)
            ? PlacementMode.LeftEdgeAlignedTop
            : PlacementMode.RightEdgeAlignedTop;

        if (popup.Placement == desired)
            return false;

        popup.Placement = desired;
        return true;
    }

    private static double MeasurePopupWidth(Popup popup, double scale)
    {
        if (popup.Child is Visual child && child.Bounds.Width > 0)
        {
            try
            {
                return child.PointToScreen(new Point(child.Bounds.Width, 0)).X
                    - child.PointToScreen(new Point(0, 0)).X;
            }
            catch (InvalidOperationException)
            {
                return child.Bounds.Width * scale;
            }
        }

        return FallbackSubmenuWidth * scale;
    }

    private static TopLevel? GetApplicationTopLevel(MenuItem item)
    {
        if (item.GetValue(HostProperty) is { } suppliedHost) return suppliedHost;
        var contextMenu = item.FindLogicalAncestorOfType<ContextMenu>()
            ?? item.GetVisualAncestors().OfType<ContextMenu>().FirstOrDefault();
        if (contextMenu?.PlacementTarget is Control target)
        {
            var fromTarget = TopLevel.GetTopLevel(target);
            if (fromTarget != null)
                return fromTarget;
        }

        // Follow logical popup placement targets for hosted flyout presenters.
        foreach (var owner in item.GetLogicalAncestors().OfType<Popup>().Reverse())
            if (owner.PlacementTarget is Control placementTarget && TopLevel.GetTopLevel(placementTarget) is Window window)
                return window;
        return TopLevel.GetTopLevel(item) is Window host ? host : null;
    }

    private static void ApplyOverlap(MenuItem item, Popup popup)
    {
        if (!popup.IsOpen || item.ItemCount == 0)
            return;

        var popupVisual = popup.Child as Visual ?? popup;
        if (item.Bounds.Width <= 0 || popupVisual.Bounds.Width <= 0)
            return;

        PixelPoint parentTopLeft;
        PixelPoint parentBottomRight;
        PixelPoint popupTopLeft;
        PixelPoint popupBottomRight;
        try
        {
            parentTopLeft = item.PointToScreen(new Point(0, 0));
            parentBottomRight = item.PointToScreen(new Point(item.Bounds.Width, item.Bounds.Height));
            popupTopLeft = popupVisual.PointToScreen(new Point(0, 0));
            popupBottomRight = popupVisual.PointToScreen(new Point(popupVisual.Bounds.Width, popupVisual.Bounds.Height));
        }
        catch (InvalidOperationException)
        {
            return;
        }

        var offset = CorrectHorizontalOffset(popup.HorizontalOffset,
            parentTopLeft.X,
            parentBottomRight.X,
            popupTopLeft.X,
            popupBottomRight.X,
            TopLevel.GetTopLevel(item)?.RenderScaling ?? 1);

        if (Math.Abs(popup.HorizontalOffset - offset) > 0.5)
            popup.HorizontalOffset = offset;
    }

    private sealed class PopupHook(MenuItem item)
    {
        private Popup? _popup;
        private EventHandler? _openedHandler;
        private MenuSubmenuHover? _hover;
        private TopLevel? _host;
        private int _generation;
        private bool _scheduled;

        public void Schedule(Action action)
        {
            if (_scheduled) return;
            _scheduled = true;
            var generation = _generation;
            Dispatcher.UIThread.Post(() =>
            {
                if (generation != _generation) return;
                _scheduled = false;
                if (_popup?.IsOpen == true && GetEnable(item)) action();
            }, DispatcherPriority.Loaded);
        }
        private void Closed(object? sender, EventArgs e) { ++_generation; _scheduled = false; DetachHost(); }
        private void Opened(object? sender, EventArgs e)
        {
            DetachHost();
            _host = GetApplicationTopLevel(item);
            if (_host != null) _host.PropertyChanged += HostChanged;
            if (_host is WindowBase window) window.PositionChanged += PositionChanged;
        }
        private void PositionChanged(object? sender, PixelPointEventArgs e) => SchedulePlacementAndOverlap(item, _popup);
        private void HostChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property.Name is "Bounds" or "ClientSize" or "RenderScaling" or "Position")
                SchedulePlacementAndOverlap(item, _popup);
        }
        private void ChildSizeChanged(object? sender, SizeChangedEventArgs e) => SchedulePlacementAndOverlap(item, _popup);
        private void DetachHost()
        {
            if (_host != null) _host.PropertyChanged -= HostChanged;
            if (_host is WindowBase window) window.PositionChanged -= PositionChanged;
            _host = null;
        }

        public void Replace(Popup? popup, EventHandler openedHandler)
        {
            if (ReferenceEquals(_popup, popup) && _openedHandler != null)
                return;

            Unhook();
            _popup = popup;
            _openedHandler = openedHandler;
            if (_popup != null)
            {
                _hover = new MenuSubmenuHover(item, _popup);
                _popup.Opened += _openedHandler;
                _popup.Opened += Opened;
                _popup.Closed += Closed;
                if (_popup.Child != null) _popup.Child.SizeChanged += ChildSizeChanged;
            }
        }

        public void Unhook()
        {
            ++_generation;
            _scheduled = false;
            _hover?.Dispose();
            _hover = null;
            DetachHost();
            if (_popup != null)
            {
                _popup.Opened -= Opened;
                _popup.Closed -= Closed;
                if (_popup.Child != null) _popup.Child.SizeChanged -= ChildSizeChanged;
            }
            if (_popup != null && _openedHandler != null)
                _popup.Opened -= _openedHandler;

            _popup = null;
            _openedHandler = null;
        }
    }
}
