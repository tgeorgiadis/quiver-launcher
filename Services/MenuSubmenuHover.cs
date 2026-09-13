using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using System.Runtime.CompilerServices;

namespace QuiverLauncher.Services;

/// <summary>Shares mouse hover ownership across a MenuItem's separate popup roots.</summary>
internal sealed class MenuSubmenuHover : IDisposable
{
    private static readonly ConditionalWeakTable<MenuItem, MenuSubmenuHover> Owners = new();
    private readonly MenuItem _item;
    private readonly Popup _popup;
    private readonly Control? _surface;
    private readonly SubmenuHoverLifetime _lifetime;
    private bool _mouseHover;
    private bool _overPopup;
    private bool _disposed;

    public MenuSubmenuHover(MenuItem item, Popup popup, Func<Action, TimeSpan, IDisposable>? schedule = null)
    {
        _item = item;
        _popup = popup;
        _surface = popup.Child;
        if (Owners.TryGetValue(item, out var previous)) previous.Dispose();
        Owners.Add(item, this);
        _lifetime = new(schedule ?? ((action, delay) => DispatcherTimer.RunOnce(action, delay)),
            () => !_disposed && item.IsSubMenuOpen && popup.IsOpen, ContainsPointer, () => item.Close());
        item.PointerEntered += ParentEntered;
        item.PointerMoved += ParentMoved;
        item.AddHandler(MenuItem.PointerExitedItemEvent, ItemExited);
        item.AddHandler(InputElement.KeyDownEvent, KeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        item.PropertyChanged += ItemChanged;
        popup.Opened += Opened;
        popup.Closed += Closed;
        if (_surface != null)
        {
            _surface.PointerEntered += PopupEntered;
            _surface.PointerExited += PopupExited;
        }
    }

    private IEnumerable<MenuSubmenuHover> Ancestors()
    {
        foreach (var item in _item.GetLogicalAncestors().OfType<MenuItem>())
            if (Owners.TryGetValue(item, out var owner)) yield return owner;
    }
    private bool ContainsPointer() => _item.IsPointerOver || _overPopup || _popup.IsPointerOverPopup ||
        _item.GetLogicalDescendants().OfType<MenuItem>().Any(item => item.IsSubMenuOpen &&
            Owners.TryGetValue(item, out var owner) && (owner._overPopup || owner._popup.IsPointerOverPopup || item.IsPointerOver));

    private void Enter()
    {
        _lifetime.Cancel();
        foreach (var owner in Ancestors()) owner._lifetime.Cancel();
    }
    private void Leave()
    {
        _lifetime.Leave();
        foreach (var owner in Ancestors()) owner._lifetime.Leave();
    }
    private void ParentEntered(object? sender, PointerEventArgs e)
    {
        _mouseHover = e.Pointer.Type == PointerType.Mouse;
        if (e.Pointer.Type == PointerType.Mouse) Enter();
    }
    private void ParentMoved(object? sender, PointerEventArgs e) => _mouseHover = e.Pointer.Type == PointerType.Mouse;
    private void ItemExited(object? sender, RoutedEventArgs e)
    {
        if (!_mouseHover || !ReferenceEquals(e.Source, _item) || !_item.IsSubMenuOpen || !_popup.IsOpen) return;
        // Handle before this bubbling event reaches Avalonia's menu handler, which otherwise
        // schedules an independent, uncancellable close using only IsPointerOverSubMenu.
        e.Handled = true;
        Leave();
    }
    private void PopupEntered(object? sender, PointerEventArgs e)
    {
        if (e.Pointer.Type != PointerType.Mouse) return;
        _overPopup = true;
        Enter();
    }
    private void PopupExited(object? sender, PointerEventArgs e)
    {
        if (e.Pointer.Type != PointerType.Mouse) return;
        _overPopup = false;
        Leave();
    }
    private void KeyDown(object? sender, KeyEventArgs e)
    {
        // Pointer deadlines must not close a submenu now being navigated with keys.
        Enter();
    }
    private void ItemChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == MenuItem.IsSubMenuOpenProperty && !_item.IsSubMenuOpen) Reset();
    }
    private void Opened(object? sender, EventArgs e) => Reset();
    private void Closed(object? sender, EventArgs e) => Reset();
    private void Reset() { _overPopup = false; _lifetime.Cancel(); }

    internal static void CancelPointerDeadlines(MenuItem item)
    {
        if (Owners.TryGetValue(item, out var owner)) owner.Enter();
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Dispose();
        _item.PointerEntered -= ParentEntered;
        _item.PointerMoved -= ParentMoved;
        _item.RemoveHandler(MenuItem.PointerExitedItemEvent, ItemExited);
        _item.RemoveHandler(InputElement.KeyDownEvent, KeyDown);
        _item.PropertyChanged -= ItemChanged;
        _popup.Opened -= Opened;
        _popup.Closed -= Closed;
        if (_surface != null)
        {
            _surface.PointerEntered -= PopupEntered;
            _surface.PointerExited -= PopupExited;
        }
        Owners.Remove(_item);
    }
}
