using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace QuiverLauncher.Services;

public enum ContextMenuKeyboardCancelResult
{
    None,
    InvokeCancel,
    SwallowEscape,
}

public sealed class GamepadContextMenuNavigation
{
    private static GamepadContextMenuNavigation? _instance;

    private ContextMenu? _activeMenu;
    private readonly Stack<MenuItem> _submenuStack = new();
    private List<MenuItem> _menuItems = [];
    private int _focusedItemIndex;
    private InputService? _inputService;
    private readonly EventHandler<KeyEventArgs> _contextMenuKeyDownHandler;

    public static GamepadContextMenuNavigation Instance => _instance ??= new GamepadContextMenuNavigation();

    public bool HasActiveContextMenu => _activeMenu != null;

    /// <summary>
    /// Resolves a key press to a <see cref="GamepadAction"/> using the app's keyboard bindings.
    /// </summary>
    public Func<Key, KeyModifiers, GamepadAction?>? ResolveKeyboardAction { get; set; }

    private bool HasConnectedGamepad => _inputService?.HasConnectedGamepad == true;

    public GamepadContextMenuNavigation()
    {
        _contextMenuKeyDownHandler = OnContextMenuKeyDown;
    }

    public void Configure(InputService inputService)
    {
        _inputService = inputService;
    }

    public static void Attach(ContextMenu menu)
    {
        Instance.PrepareMenu(menu);
    }

    /// <summary>
    /// Decides how a context-menu key press should interact with Cancel / Avalonia Escape dismiss.
    /// </summary>
    public static ContextMenuKeyboardCancelResult ResolveKeyboardCancel(
        Key key,
        KeyModifiers modifiers,
        Func<Key, KeyModifiers, GamepadAction?>? resolveAction)
    {
        if (resolveAction == null)
            return ContextMenuKeyboardCancelResult.None;

        var action = resolveAction(key, modifiers);
        if (action == GamepadAction.Cancel)
            return ContextMenuKeyboardCancelResult.InvokeCancel;

        // Block Avalonia's hardcoded Escape dismiss when Escape is not the Cancel binding.
        if (key == Key.Escape)
            return ContextMenuKeyboardCancelResult.SwallowEscape;

        return ContextMenuKeyboardCancelResult.None;
    }

    public void PrepareMenu(ContextMenu menu)
    {
        // Only one gamepad-navigable menu at a time — close any prior picker/options menu.
        if (_activeMenu != null && !ReferenceEquals(_activeMenu, menu))
        {
            var previous = _activeMenu;
            try
            {
                previous.Close();
            }
            catch
            {
                UnregisterContextMenu(previous);
            }
        }

        RegisterContextMenu(menu);
        AttachKeyboardCancelHandler(menu);

        menu.Opened += OnMenuOpened;
        menu.Closed += OnMenuClosed;

        void OnMenuOpened(object? sender, EventArgs e)
        {
            RefreshContextMenu(menu);
        }

        void OnMenuClosed(object? sender, EventArgs e)
        {
            menu.Opened -= OnMenuOpened;
            menu.Closed -= OnMenuClosed;
            DetachKeyboardCancelHandler(menu);
            UnregisterContextMenu(menu);
        }
    }

    private void AttachKeyboardCancelHandler(ContextMenu menu)
    {
        menu.RemoveHandler(InputElement.KeyDownEvent, _contextMenuKeyDownHandler);
        menu.AddHandler(InputElement.KeyDownEvent, _contextMenuKeyDownHandler, RoutingStrategies.Tunnel);
    }

    private void DetachKeyboardCancelHandler(ContextMenu menu)
    {
        menu.RemoveHandler(InputElement.KeyDownEvent, _contextMenuKeyDownHandler);
    }

    private void OnContextMenuKeyDown(object? sender, KeyEventArgs e)
    {
        switch (ResolveKeyboardCancel(e.Key, e.KeyModifiers, ResolveKeyboardAction))
        {
            case ContextMenuKeyboardCancelResult.InvokeCancel:
                if (TryHandleCancel())
                    e.Handled = true;
                break;

            case ContextMenuKeyboardCancelResult.SwallowEscape:
                e.Handled = true;
                break;
        }
    }

    public void RegisterContextMenu(ContextMenu menu)
    {
        RefreshContextMenu(menu);
    }

    public void RefreshContextMenu(ContextMenu menu)
    {
        if (!ReferenceEquals(_activeMenu, menu))
            _activeMenu = menu;

        _submenuStack.Clear();
        CloseAllSubmenus(menu);
        _menuItems = CollectNavigableMenuItems(menu);
        _focusedItemIndex = _menuItems.Count > 0 ? 0 : -1;

        // Only auto-focus the first item when a gamepad is connected.
        if (HasConnectedGamepad)
            Dispatcher.UIThread.Post(FocusCurrentItem, DispatcherPriority.Loaded);
    }

    public void UnregisterContextMenu(ContextMenu menu)
    {
        if (!ReferenceEquals(_activeMenu, menu))
            return;

        DetachKeyboardCancelHandler(menu);
        CloseAllSubmenus(menu);
        _submenuStack.Clear();
        _activeMenu = null;
        _menuItems = [];
        _focusedItemIndex = -1;
    }

    public bool TryHandleNavigation(NavigationDirection direction)
    {
        if (_activeMenu == null)
            return false;

        EnsureMenuItems();
        if (_menuItems.Count == 0)
            return false;

        if (direction is NavigationDirection.Right)
        {
            var focused = GetFocusedItem();
            if (focused != null && HasOpenableChildren(focused))
            {
                OpenSubmenu(focused);
                return true;
            }

            // No submenu: stay put (do not treat Right as next sibling).
            return true;
        }

        if (direction is NavigationDirection.Left)
        {
            if (_submenuStack.Count > 0)
            {
                CloseCurrentSubmenu();
                return true;
            }

            return true;
        }

        if (_menuItems.Count == 1)
            return true;

        _focusedItemIndex = MoveMenuItemIndex(_focusedItemIndex, direction, _menuItems.Count);
        FocusCurrentItem();
        return true;
    }

    public bool TryHandleConfirm()
    {
        if (_activeMenu == null)
            return false;

        EnsureMenuItems();
        var item = GetFocusedItem();
        if (item == null)
            return false;

        if (HasOpenableChildren(item))
        {
            OpenSubmenu(item);
            return true;
        }

        GamepadControlActivation.ActivateMenuItem(item);

        // Nested submenu items may not close the root menu via Parent alone; ensure it dismisses.
        if (_activeMenu != null && _activeMenu.IsOpen)
        {
            var menu = _activeMenu;
            CloseAllSubmenus(menu);
            menu.Close();
        }

        return true;
    }

    public bool TryHandleCancel()
    {
        if (_activeMenu == null)
            return false;

        if (_submenuStack.Count > 0)
        {
            CloseCurrentSubmenu();
            return true;
        }

        EnsureMenuItems();

        var cancelIndex = FindCancelMenuItemIndex(_menuItems);
        if (cancelIndex >= 0)
        {
            GamepadControlActivation.ActivateMenuItem(_menuItems[cancelIndex]);
            return true;
        }

        _activeMenu.Close();
        return true;
    }

    /// <summary>
    /// Dismiss the open context menu with the Options button (toggle close).
    /// Unlike Cancel, this never activates a Cancel menu item — it just closes.
    /// </summary>
    public bool TryHandleOptionsDismiss()
    {
        if (_activeMenu == null)
            return false;

        var menu = _activeMenu;
        CloseAllSubmenus(menu);
        _submenuStack.Clear();
        menu.Close();
        // Ensure navigation state clears even if Closed did not fire (menu never shown).
        UnregisterContextMenu(menu);
        return true;
    }

    private void OpenSubmenu(MenuItem parent)
    {
        parent.IsSubMenuOpen = true;
        _submenuStack.Push(parent);
        _menuItems = CollectNavigableChildMenuItems(parent);
        _focusedItemIndex = _menuItems.Count > 0 ? 0 : -1;
        Dispatcher.UIThread.Post(FocusCurrentItem, DispatcherPriority.Loaded);
    }

    private void CloseCurrentSubmenu()
    {
        if (_submenuStack.Count == 0)
            return;

        var parent = _submenuStack.Pop();
        parent.IsSubMenuOpen = false;

        if (_submenuStack.Count > 0)
        {
            var currentParent = _submenuStack.Peek();
            _menuItems = CollectNavigableChildMenuItems(currentParent);
            _focusedItemIndex = Math.Max(0, _menuItems.IndexOf(parent));
            if (_focusedItemIndex < 0)
                _focusedItemIndex = 0;
        }
        else if (_activeMenu != null)
        {
            _menuItems = CollectNavigableMenuItems(_activeMenu);
            _focusedItemIndex = Math.Max(0, _menuItems.IndexOf(parent));
            if (_focusedItemIndex < 0)
                _focusedItemIndex = 0;
        }

        Dispatcher.UIThread.Post(FocusCurrentItem, DispatcherPriority.Loaded);
    }

    private void EnsureMenuItems()
    {
        if (_activeMenu == null || _menuItems.Count > 0)
            return;

        _menuItems = _submenuStack.Count > 0
            ? CollectNavigableChildMenuItems(_submenuStack.Peek())
            : CollectNavigableMenuItems(_activeMenu);
        _focusedItemIndex = _menuItems.Count > 0 ? 0 : -1;
    }

    public static bool HasOpenableChildren(MenuItem item) =>
        CollectNavigableChildMenuItems(item).Count > 0;

    public static List<MenuItem> CollectNavigableMenuItems(ContextMenu menu)
    {
        return menu.Items
            .OfType<MenuItem>()
            .Where(item => item.IsVisible && item.IsEnabled)
            .ToList();
    }

    public static List<MenuItem> CollectNavigableChildMenuItems(MenuItem parent)
    {
        return parent.Items
            .OfType<MenuItem>()
            .Where(item => item.IsVisible && item.IsEnabled)
            .ToList();
    }

    public static int MoveMenuItemIndex(int currentIndex, NavigationDirection direction, int count)
    {
        if (count <= 0)
            return -1;

        if (currentIndex < 0 || currentIndex >= count)
            return 0;

        if (count == 1)
            return currentIndex;

        return direction switch
        {
            NavigationDirection.Down => (currentIndex + 1) % count,
            NavigationDirection.Up => (currentIndex - 1 + count) % count,
            // Left/Right are handled by TryHandleNavigation for submenu expand/collapse.
            _ => currentIndex,
        };
    }

    public static int FindCancelMenuItemIndex(IReadOnlyList<MenuItem> items)
    {
        for (var i = 0; i < items.Count; i++)
        {
            var label = GetMenuItemLabel(items[i]);
            if (string.Equals(label, "cancel", StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    private void FocusCurrentItem()
    {
        GetFocusedItem()?.Focus();
    }

    private MenuItem? GetFocusedItem()
    {
        if (_focusedItemIndex < 0 || _focusedItemIndex >= _menuItems.Count)
            return null;

        return _menuItems[_focusedItemIndex];
    }

    private static void CloseAllSubmenus(ContextMenu menu)
    {
        foreach (var item in menu.Items.OfType<MenuItem>())
            CloseSubmenusRecursive(item);
    }

    private static void CloseSubmenusRecursive(MenuItem item)
    {
        item.IsSubMenuOpen = false;
        foreach (var child in item.Items.OfType<MenuItem>())
            CloseSubmenusRecursive(child);
    }

    private static string GetMenuItemLabel(MenuItem item)
    {
        return item.Header?.ToString()?.Trim() ?? string.Empty;
    }
}
