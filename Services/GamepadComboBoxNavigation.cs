using Avalonia.Controls;
using Avalonia.Threading;

namespace QuiverLauncher.Services;

public sealed class GamepadComboBoxNavigation
{
    private static GamepadComboBoxNavigation? _instance;

    private ComboBox? _activeComboBox;
    private List<ComboBoxItem> _items = [];
    private int _focusedItemIndex = -1;
    private int _originalSelectedIndex = -1;

    public static GamepadComboBoxNavigation Instance => _instance ??= new GamepadComboBoxNavigation();

    public bool HasActiveComboBox => _activeComboBox != null;

    public static void Attach(ComboBox comboBox)
    {
        // Mouse / native open must register too — otherwise Up/Down stay on the dialog.
        comboBox.DropDownOpened += (_, _) => Instance.RegisterOpen(comboBox, openIfNeeded: false);
        comboBox.DropDownClosed += (_, _) => Instance.Close(comboBox);
    }

    public static void Open(ComboBox comboBox) => Instance.RegisterOpen(comboBox, openIfNeeded: true);

    public void RegisterOpen(ComboBox comboBox, bool openIfNeeded = true)
    {
        _activeComboBox = comboBox;
        _items = CollectNavigableItems(comboBox);
        _originalSelectedIndex = comboBox.SelectedIndex;

        var selectedItem = comboBox.SelectedItem as ComboBoxItem;
        _focusedItemIndex = selectedItem != null ? _items.IndexOf(selectedItem) : 0;
        if (_focusedItemIndex < 0)
            _focusedItemIndex = _items.Count > 0 ? 0 : -1;

        if (openIfNeeded && !comboBox.IsDropDownOpen)
            comboBox.IsDropDownOpen = true;

        // Apply immediately so keyboard/gamepad hover is visible; post again after the
        // popup finishes layout (items may not be realized yet on the first pass).
        FocusCurrentItem();
        Dispatcher.UIThread.Post(FocusCurrentItem, DispatcherPriority.Loaded);
    }

    public void Close(ComboBox comboBox)
    {
        if (!ReferenceEquals(_activeComboBox, comboBox))
            return;

        ClearItemHighlights();
        _activeComboBox = null;
        _items = [];
        _focusedItemIndex = -1;
        _originalSelectedIndex = -1;
    }

    public bool TryHandleNavigation(NavigationDirection direction)
    {
        if (_activeComboBox == null || _items.Count == 0)
            return false;

        if (_items.Count == 1)
            return true;

        _focusedItemIndex = MoveItemIndex(_focusedItemIndex, direction, _items.Count);
        FocusCurrentItem();
        return true;
    }

    public bool TryHandleConfirm()
    {
        if (_activeComboBox == null || _items.Count == 0)
            return false;

        if (_focusedItemIndex >= 0 && _focusedItemIndex < _items.Count)
            _activeComboBox.SelectedItem = _items[_focusedItemIndex];

        _activeComboBox.IsDropDownOpen = false;
        Close(_activeComboBox);
        return true;
    }

    public bool TryHandleCancel()
    {
        if (_activeComboBox == null)
            return false;

        if (_originalSelectedIndex >= 0 && _originalSelectedIndex < _activeComboBox.Items.Count)
            _activeComboBox.SelectedIndex = _originalSelectedIndex;

        _activeComboBox.IsDropDownOpen = false;
        Close(_activeComboBox);
        return true;
    }

    public static List<ComboBoxItem> CollectNavigableItems(ComboBox comboBox)
    {
        return comboBox.Items
            .OfType<ComboBoxItem>()
            .Where(item => item.IsVisible && item.IsEnabled)
            .ToList();
    }

    public static int MoveItemIndex(int currentIndex, NavigationDirection direction, int count)
    {
        if (count <= 0)
            return -1;

        if (currentIndex < 0 || currentIndex >= count)
            return 0;

        if (count == 1)
            return currentIndex;

        return direction switch
        {
            NavigationDirection.Down or NavigationDirection.Right => (currentIndex + 1) % count,
            NavigationDirection.Up or NavigationDirection.Left => (currentIndex - 1 + count) % count,
            _ => currentIndex,
        };
    }

    private void FocusCurrentItem()
    {
        ClearItemHighlights();

        var focused = GetFocusedItem();
        if (focused == null)
            return;

        // Highlight only — do not change SelectedItem until Confirm (A / Enter).
        // Cancel restores _originalSelectedIndex if the user browsed away.
        focused.Classes.Set("gamepad-focused", true);
        focused.Focus();
    }

    private void ClearItemHighlights()
    {
        foreach (var item in _items)
            item.Classes.Set("gamepad-focused", false);
    }

    private ComboBoxItem? GetFocusedItem()
    {
        if (_focusedItemIndex < 0 || _focusedItemIndex >= _items.Count)
            return null;

        return _items[_focusedItemIndex];
    }
}
