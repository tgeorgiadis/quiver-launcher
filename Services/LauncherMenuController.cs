using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace QuiverLauncher.Services;

/// <summary>Owns shell context menus and mobile submenu activation for one view lifetime.</summary>
public sealed class LauncherMenuController(Control view, Action preserveFocus, Action restoreFocus) : IDisposable
{
    private readonly Dictionary<ContextMenu, EventHandler<RoutedEventArgs>> _menus = [];
    private readonly HashSet<MenuItem> _touchItems = [];
    private MenuItem? _activatedItem;
    private bool _disposed;

    public void Open(Control anchor, ContextMenu menu)
    {
        if (_disposed || menu.IsOpen) return;
        if (double.IsNaN(menu.MaxHeight) || menu.MaxHeight <= 0)
            menu.MaxHeight = Math.Max(240, view.Bounds.Height > 0 ? view.Bounds.Height - 120 : 560);
        _activatedItem = null;
        GamepadContextMenuNavigation.Attach(menu);
        preserveFocus();
        if (PlatformCapabilities.IsMobile)
            foreach (var item in menu.Items.OfType<MenuItem>()) AttachTouch(item, false);
        menu.PlacementTarget = anchor;
        menu.Placement = PlacementMode.BottomEdgeAlignedRight;
        void Closed(object? sender, EventArgs e)
        {
            menu.Closed -= Closed;
            _menus.Remove(menu);
            _activatedItem = null;
            foreach (var item in menu.Items.OfType<MenuItem>()) DetachTouch(item);
            if (_disposed) return;
            var focus = TopLevel.GetTopLevel(view)?.FocusManager;
            if (ReferenceEquals(focus?.GetFocusedElement(), anchor)) focus.Focus(null);
            restoreFocus();
        }
        _menus.Add(menu, Closed);
        menu.Closed += Closed;
        menu.Open(anchor);
    }

    private void AttachTouch(MenuItem item, bool insideSubmenu)
    {
        _touchItems.Add(item);
        if (insideSubmenu)
        {
            item.RemoveHandler(InputElement.PointerPressedEvent, PointerPressed);
            item.AddHandler(InputElement.PointerPressedEvent, PointerPressed, RoutingStrategies.Bubble, handledEventsToo: true);
            item.RemoveHandler(InputElement.TappedEvent, Tapped);
            item.AddHandler(InputElement.TappedEvent, Tapped, RoutingStrategies.Bubble, handledEventsToo: true);
        }
        else if (item.ItemCount > 0)
        {
            item.SubmenuOpened -= SubmenuOpened;
            item.SubmenuOpened += SubmenuOpened;
        }
        foreach (var child in item.Items.OfType<MenuItem>()) AttachTouch(child, true);
    }

    private void DetachTouch(MenuItem item)
    {
        item.RemoveHandler(InputElement.PointerPressedEvent, PointerPressed);
        item.RemoveHandler(InputElement.TappedEvent, Tapped);
        item.SubmenuOpened -= SubmenuOpened;
        _touchItems.Remove(item);
        foreach (var child in item.Items.OfType<MenuItem>()) DetachTouch(child);
    }

    private void SubmenuOpened(object? sender, RoutedEventArgs e)
    {
        if (!_disposed && sender is MenuItem item)
            foreach (var child in item.Items.OfType<MenuItem>()) AttachTouch(child, true);
    }
    private void PointerPressed(object? sender, PointerPressedEventArgs e) => Activate(sender, e);
    private void Tapped(object? sender, TappedEventArgs e) => Activate(sender, e);
    private void Activate(object? sender, RoutedEventArgs e)
    {
        if (_disposed || sender is not MenuItem { IsEnabled: true, ItemCount: 0 } item || ReferenceEquals(item, _activatedItem)) return;
        _activatedItem = item;
        GamepadControlActivation.ActivateMenuItem(item);
        e.Handled = true;
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var pair in _menus.ToArray())
        {
            pair.Key.Closed -= pair.Value;
            pair.Key.Close();
        }
        _menus.Clear();
        foreach (var item in _touchItems.ToArray()) DetachTouch(item);
        _activatedItem = null;
    }
}
