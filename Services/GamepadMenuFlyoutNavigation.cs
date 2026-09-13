using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;

namespace QuiverLauncher.Services;

public sealed class GamepadMenuFlyoutNavigation
{
    private static GamepadMenuFlyoutNavigation? _instance;
    private readonly HashSet<MenuFlyout> _attached = [];
    private MenuFlyout? _activeFlyout;
    private List<MenuItem> _items = [];
    private int _focusedItemIndex = -1;

    public static GamepadMenuFlyoutNavigation Instance => _instance ??= new GamepadMenuFlyoutNavigation();

    public bool HasActiveMenuFlyout => _activeFlyout != null;

    public static void Attach(MenuFlyout? flyout)
    {
        if (flyout == null)
            return;

        var instance = Instance;
        if (!instance._attached.Add(flyout))
            return;

        flyout.Opened += (_, _) => instance.RegisterOpen(flyout);
        flyout.Closed += (_, _) => instance.Close(flyout);
    }

    public static void Toggle(Button button)
    {
        if (button.Flyout is not FlyoutBase flyout)
            return;

        if (flyout.IsOpen)
        {
            flyout.Hide();
            return;
        }

        if (flyout is MenuFlyout menuFlyout)
        {
            Attach(menuFlyout);
            flyout.ShowAt(button);
            Instance.RegisterOpen(menuFlyout);
            return;
        }

        flyout.ShowAt(button);
    }

    public void RegisterOpen(MenuFlyout flyout)
    {
        MenuSubmenuOverlap.SetFlyoutHost(flyout, flyout.Target is { } target ? TopLevel.GetTopLevel(target) : null);
        _activeFlyout = flyout;
        _items = CollectNavigableItems(flyout);
        _focusedItemIndex = FindPreferredItemIndex(_items);
        FocusCurrentItem();
        Dispatcher.UIThread.Post(FocusCurrentItem, DispatcherPriority.Loaded);
    }

    public void Close(MenuFlyout flyout)
    {
        MenuSubmenuOverlap.SetFlyoutHost(flyout, null);
        if (!ReferenceEquals(_activeFlyout, flyout))
            return;

        ClearItemHighlights();
        _activeFlyout = null;
        _items = [];
        _focusedItemIndex = -1;
    }

    public bool TryHandleNavigation(NavigationDirection direction)
    {
        if (_activeFlyout == null || _items.Count == 0)
            return false;

        if (_items.Count == 1)
            return true;

        _focusedItemIndex = MoveItemIndex(_focusedItemIndex, direction, _items.Count);
        FocusCurrentItem();
        return true;
    }

    public bool TryHandleConfirm()
    {
        if (_activeFlyout == null || _items.Count == 0)
            return false;

        var item = GetFocusedItem();
        if (item == null)
            return false;

        var flyout = _activeFlyout;
        GamepadControlActivation.ActivateMenuItem(item);
        if (flyout.IsOpen)
            flyout.Hide();
        Close(flyout);
        return true;
    }

    public bool TryHandleCancel()
    {
        if (_activeFlyout == null)
            return false;

        var flyout = _activeFlyout;
        if (flyout.IsOpen)
            flyout.Hide();
        Close(flyout);
        return true;
    }

    public static List<MenuItem> CollectNavigableItems(MenuFlyout flyout)
    {
        return flyout.Items
            .OfType<MenuItem>()
            .Where(item => item.IsVisible && item.IsEnabled)
            .ToList();
    }

    public static int MoveItemIndex(int currentIndex, NavigationDirection direction, int count) =>
        GamepadComboBoxNavigation.MoveItemIndex(currentIndex, direction, count);

    public static int FindPreferredItemIndex(IReadOnlyList<MenuItem> items)
    {
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i].FontWeight == Avalonia.Media.FontWeight.Bold ||
                items[i].FontWeight == Avalonia.Media.FontWeight.SemiBold)
            {
                return i;
            }
        }

        return items.Count > 0 ? 0 : -1;
    }

    private void FocusCurrentItem()
    {
        ClearItemHighlights();

        var focused = GetFocusedItem();
        if (focused == null)
            return;

        MenuSubmenuHover.CancelPointerDeadlines(focused);
        focused.Classes.Set("gamepad-focused", true);
        focused.Focus();
    }

    private void ClearItemHighlights()
    {
        foreach (var item in _items)
            item.Classes.Set("gamepad-focused", false);
    }

    private MenuItem? GetFocusedItem()
    {
        if (_focusedItemIndex < 0 || _focusedItemIndex >= _items.Count)
            return null;

        return _items[_focusedItemIndex];
    }
}
