using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using QuiverLauncher.Services;
using QuiverLauncher.Services.Mods;

namespace QuiverLauncher.Views;
/// <summary>Owns navigation, card geometry, and focus within the mods list.</summary>
public sealed class ModsNavigation(ModsView _view, LauncherSession _session, IModsFeatureHost Host) : IFeatureNavigationHandler
{
    private GamepadNavigationService _gamepadNavigation => Host.Navigation;
    private System.Collections.ObjectModel.ObservableCollection<ModListItem> ModListRows => _view.Model.Rows;

    private GamepadNavigationZone GetMainContentGamepadZone() => Host.MainContentZone;
    private bool TryApplyGamepadZoneTransition(GamepadZoneTransition transition) => Host.ApplyTransition(transition);
    private void ClearGamepadFocus() => Host.ClearFocus();
    public bool Navigate(NavigationDirection direction) => !_session.IsClosed && HandleModsGamepadNavigation(direction);
    public bool Confirm() => !_session.IsClosed && HandleModsGamepadConfirm();
    public bool Cancel() => !_session.IsClosed && HandleModsGamepadCancel();
    public bool Options() => false;
    public void RestoreFocus()
    {
        var index = _gamepadNavigation.ActiveZone switch
        {
            GamepadNavigationZone.ModsOverlayToolbar => _modsGamepadToolbarIndex,
            GamepadNavigationZone.ModsOverlayFilters => _modsGamepadFilterIndex,
            GamepadNavigationZone.ModsOverlaySourceFilters => _modsGamepadSourceFilterIndex,
            GamepadNavigationZone.ModsOverlayRowActions => _modsGamepadRowActionIndex,
            _ => _modsGamepadListIndex
        };
        EnterZone(new(_gamepadNavigation.ActiveZone, Math.Max(0, index)));
    }

    private void Post(Action action, DispatcherPriority priority) => Dispatcher.UIThread.Post(() =>
    {
        if (!_session.IsClosed && _view.IsVisible)
            action();
    }, priority);
    internal int _modsGamepadToolbarIndex = -1;
    internal int _modsGamepadFilterIndex = -1;
    internal int _modsGamepadSourceFilterIndex = -1;
    internal int _modsGamepadListIndex = -1;
    internal int _modsGamepadRowActionIndex = -1;
    internal void SelectInitialModsGamepadItem()
    {
        _gamepadNavigation.ActiveZone = GamepadNavigationZone.ModsOverlayToolbar;
        ApplyModsToolbarSelection(0);
    }

    internal bool HandleModsGamepadNavigation(NavigationDirection direction)
    {
        return _gamepadNavigation.ActiveZone switch
        {
            GamepadNavigationZone.ModsOverlayToolbar => HandleModsToolbarNavigation(direction),
            GamepadNavigationZone.ModsOverlayFilters => HandleModsFiltersNavigation(direction),
            GamepadNavigationZone.ModsOverlaySourceFilters => HandleModsSourceFiltersNavigation(direction),
            GamepadNavigationZone.ModsOverlayList => HandleModsListNavigation(direction),
            GamepadNavigationZone.ModsOverlayRowActions => HandleModsRowActionsNavigation(direction),
            _ => false,
        };
    }

    internal bool HandleModsToolbarNavigation(NavigationDirection direction)
    {
        var controls = CollectModsToolbarControls();
        if (controls.Count == 0)
            return false;
        var currentIndex = _modsGamepadToolbarIndex;
        var zoneTransition = _gamepadNavigation.TryGetZoneTransition(direction, GamepadNavigationZone.ModsOverlayToolbar, GetMainContentGamepadZone(), isListLayout: true, positions: null, currentIndex, controls.Count);
        if (zoneTransition.HasValue)
            return TryApplyGamepadZoneTransition(zoneTransition.Value);
        if (direction is not (NavigationDirection.Left or NavigationDirection.Right))
            return true;
        if (direction == NavigationDirection.Left && currentIndex <= 0)
            return TryApplyGamepadZoneTransition(new GamepadZoneTransition(GamepadNavigationZone.Sidebar, null));
        if (TryMoveXyFocusInRegion(_view, direction, controls, ApplyModsToolbarSelection))
            return true;
        var nextIndex = _gamepadNavigation.MoveHorizontalIndex(currentIndex, direction, controls.Count);
        ApplyModsToolbarSelection(nextIndex);
        return true;
    }

    internal bool HandleModsFiltersNavigation(NavigationDirection direction)
    {
        var controls = CollectModsFilterControls();
        if (controls.Count == 0)
            return false;
        var currentIndex = _modsGamepadFilterIndex;
        var zoneTransition = _gamepadNavigation.TryGetZoneTransition(direction, GamepadNavigationZone.ModsOverlayFilters, GetMainContentGamepadZone(), isListLayout: true, positions: null, currentIndex, controls.Count);
        if (zoneTransition.HasValue)
            return TryApplyGamepadZoneTransition(zoneTransition.Value);
        if (direction is not (NavigationDirection.Left or NavigationDirection.Right))
            return true;
        if (direction == NavigationDirection.Left && currentIndex <= 0)
            return TryApplyGamepadZoneTransition(new GamepadZoneTransition(GamepadNavigationZone.Sidebar, null));
        if (TryMoveXyFocusInRegion(_view, direction, controls, ApplyModsFiltersSelection))
            return true;
        var nextIndex = _gamepadNavigation.MoveHorizontalIndex(currentIndex, direction, controls.Count);
        ApplyModsFiltersSelection(nextIndex);
        return true;
    }

    internal bool HandleModsSourceFiltersNavigation(NavigationDirection direction)
    {
        var controls = CollectModsSourceFilterControls();
        if (controls.Count == 0)
        {
            return TryApplyGamepadZoneTransition(direction == NavigationDirection.Up ? new GamepadZoneTransition(GamepadNavigationZone.ModsOverlayFilters, null) : new GamepadZoneTransition(GamepadNavigationZone.ModsOverlayList, 0));
        }

        var currentIndex = _modsGamepadSourceFilterIndex;
        var zoneTransition = _gamepadNavigation.TryGetZoneTransition(direction, GamepadNavigationZone.ModsOverlaySourceFilters, GetMainContentGamepadZone(), isListLayout: true, positions: null, currentIndex, ModListRows.Count);
        if (zoneTransition.HasValue)
            return TryApplyGamepadZoneTransition(zoneTransition.Value);
        if (direction is not (NavigationDirection.Left or NavigationDirection.Right))
            return true;
        if (direction == NavigationDirection.Left && currentIndex <= 0)
            return TryApplyGamepadZoneTransition(new GamepadZoneTransition(GamepadNavigationZone.Sidebar, null));
        if (TryMoveXyFocusInRegion(_view, direction, controls, ApplyModsSourceFiltersSelection))
            return true;
        var nextIndex = _gamepadNavigation.MoveHorizontalIndex(currentIndex, direction, controls.Count);
        ApplyModsSourceFiltersSelection(nextIndex);
        return true;
    }

    internal bool HandleModsListNavigation(NavigationDirection direction)
    {
        var currentIndex = _modsGamepadListIndex;
        var positions = ModListRows.Count > 0 ? CollectModCardPositions() : null;
        var zoneTransition = _gamepadNavigation.TryGetZoneTransition(direction, GamepadNavigationZone.ModsOverlayList, GetMainContentGamepadZone(), isListLayout: false, positions, currentIndex, ModListRows.Count);
        if (zoneTransition.HasValue)
            return TryApplyGamepadZoneTransition(zoneTransition.Value);
        if (ModListRows.Count == 0)
            return true;
        var nextIndex = _gamepadNavigation.MoveLibraryIndex(currentIndex, direction, ModListRows.Count, isListLayout: false, positions);
        // Rightmost card: Right enters the card action buttons.
        if (direction == NavigationDirection.Right && nextIndex == currentIndex)
        {
            ApplyModsRowActionSelection(0);
            return true;
        }

        if (nextIndex == currentIndex && direction is NavigationDirection.Left or NavigationDirection.Up)
        {
            var blockedTransition = _gamepadNavigation.TryGetBlockedMoveZoneTransition(direction, GamepadNavigationZone.ModsOverlayList, GetMainContentGamepadZone(), isListLayout: false, currentIndex, ModListRows.Count);
            if (blockedTransition.HasValue)
                return TryApplyGamepadZoneTransition(blockedTransition.Value);
        }

        ApplyModsListSelection(nextIndex);
        if (direction == NavigationDirection.Down && _view.ShouldPrefetchMoreModsForGamepad(nextIndex))
            _ = _session.RunAsync(() => _view.LoadMoreModsAsync());
        return true;
    }

    internal List<(double X, double Y)> CollectModCardPositions()
    {
        var positions = new List<(double X, double Y)>();
        foreach (var item in ModListRows)
        {
            var card = FindModListRowBorder(item);
            positions.Add(GetControlCenter(card) ?? (0, positions.Count * 220));
        }

        return positions;
    }

    internal bool HandleModsRowActionsNavigation(NavigationDirection direction)
    {
        if (ModListRows.Count == 0)
            return false;
        var rowIndex = _gamepadNavigation.ClampIndex(_modsGamepadListIndex, ModListRows.Count);
        if (rowIndex < 0 || rowIndex >= ModListRows.Count)
            return false;
        var actions = CollectModsRowActionControls(rowIndex);
        if (actions.Count == 0)
            return false;
        var currentIndex = _gamepadNavigation.ClampIndex(_modsGamepadRowActionIndex, actions.Count);
        if (direction is NavigationDirection.Up or NavigationDirection.Down)
        {
            ClearModsRowActionGamepadFocus();
            var positions = CollectModCardPositions();
            var nextRow = _gamepadNavigation.MoveLibraryIndex(rowIndex, direction, ModListRows.Count, isListLayout: false, positions);
            ApplyModsListSelection(nextRow);
            if (direction == NavigationDirection.Down && _view.ShouldPrefetchMoreModsForGamepad(nextRow))
                _ = _session.RunAsync(() => _view.LoadMoreModsAsync());
            return true;
        }

        // Left from the first action returns to the card (do not wrap to the far-right button).
        if (direction == NavigationDirection.Left && currentIndex <= 0)
        {
            ClearModsRowActionGamepadFocus();
            ApplyModsListSelection(rowIndex);
            return true;
        }

        if (direction is not (NavigationDirection.Left or NavigationDirection.Right))
            return false;
        var nextIndex = _gamepadNavigation.MoveHorizontalIndex(currentIndex, direction, actions.Count);
        ApplyModsRowActionSelection(nextIndex);
        return true;
    }

    internal bool HandleModsGamepadConfirm()
    {
        switch (_gamepadNavigation.ActiveZone)
        {
            case GamepadNavigationZone.ModsOverlayToolbar:
                ActivateFocusedControl(CollectModsToolbarControls(), _modsGamepadToolbarIndex);
                return true;
            case GamepadNavigationZone.ModsOverlayFilters:
                ActivateFocusedControl(CollectModsFilterControls(), _modsGamepadFilterIndex);
                return true;
            case GamepadNavigationZone.ModsOverlaySourceFilters:
                ActivateFocusedControl(CollectModsSourceFilterControls(), _modsGamepadSourceFilterIndex);
                return true;
            case GamepadNavigationZone.ModsOverlayList:
            {
                var index = _gamepadNavigation.ClampIndex(_modsGamepadListIndex, ModListRows.Count);
                if (index < 0 || index >= ModListRows.Count)
                    return true;
                var actions = CollectModsRowActionControls(index);
                if (actions.Count == 0)
                    return true;
                // Enter the action strip; do not fire Install/Update/etc until Confirm again.
                ApplyModsRowActionSelection(0);
                return true;
            }

            case GamepadNavigationZone.ModsOverlayRowActions:
                ActivateFocusedControl(CollectModsRowActionControls(_modsGamepadListIndex), _modsGamepadRowActionIndex);
                return true;
            default:
                return false;
        }
    }

    internal bool HandleModsGamepadCancel()
    {
        if (_gamepadNavigation.ActiveZone == GamepadNavigationZone.ModsOverlayRowActions)
        {
            ClearModsRowActionGamepadFocus();
            _gamepadNavigation.ActiveZone = GamepadNavigationZone.ModsOverlayList;
            ApplyModsListSelection(_modsGamepadListIndex);
            return true;
        }

        _view.CloseModsOverlay();
        return true;
    }

    internal static void ActivateFocusedControl(IReadOnlyList<Control> controls, int index)
    {
        if (index < 0 || index >= controls.Count)
            return;
        var control = controls[index];
        if (control is Button button)
            GamepadControlActivation.ActivateButton(button);
        else if (control is ComboBox comboBox)
            GamepadComboBoxNavigation.Open(comboBox);
        else if (control is TextBox textBox)
            GamepadControlActivation.ActivateTextBox(textBox);
        else
            control.Focus();
    }

    internal List<Control> CollectModsToolbarControls()
    {
        var list = new List<Control>();
        void Add(Control? c)
        {
            if (c != null && c.IsVisible && c.IsEnabled)
                list.Add(c);
        }

        Add(_view.ModsRefreshButton);
        Add(_view.ModsUpdateAllButton);
        Add(_view.ModsOpenFolderButton);
        Add(_view.ModsCloseButton);
        return list;
    }

    internal List<Control> CollectModsFilterControls()
    {
        var list = new List<Control>();
        void Add(Control? c)
        {
            if (c != null && c.IsVisible && c.IsEnabled)
                list.Add(c);
        }

        Add(_view.ModsTabBrowseButton);
        Add(_view.ModsTabInstalledButton);
        Add(_view.ModsSearchTextBox);
        Add(_view.ModsSortByComboBox);
        return list;
    }

    internal List<Control> CollectModsSourceFilterControls()
    {
        var list = new List<Control>();
        if (_view.ModsSourceFilterPanel == null)
            return list;
        foreach (var button in _view.ModsSourceFilterPanel.Children.OfType<Button>())
        {
            if (button.IsVisible && button.IsEnabled)
                list.Add(button);
        }

        return list;
    }

    internal List<Control> CollectModsRowActionControls(int rowIndex)
    {
        var list = new List<Control>();
        if (rowIndex < 0 || rowIndex >= ModListRows.Count || _view.ModsItemsControl == null)
            return list;
        var container = _view.ModsItemsControl.ContainerFromIndex(rowIndex);
        if (container == null)
            return list;
        foreach (var button in container.GetVisualDescendants().OfType<Button>().Where(b => b.IsVisible && b.IsEnabled && b.Classes.Contains("options")))
        {
            list.Add(button);
        }

        return list;
    }

    internal Border? FindModListRowBorder(ModListItem item)
    {
        return _view.ModsItemsControl?.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => ReferenceEquals(b.DataContext, item) && b.Classes.Contains("catalog-focus-card"));
    }

    internal void ApplyModsToolbarSelection(int index)
    {
        var controls = CollectModsToolbarControls();
        index = _gamepadNavigation.ClampIndex(index, controls.Count);
        _modsGamepadToolbarIndex = index;
        _gamepadNavigation.ActiveZone = GamepadNavigationZone.ModsOverlayToolbar;
        ClearGamepadFocus();
        ClearModsListGamepadFocus();
        ClearStyledControlsGamepadFocusClasses(controls);
        if (index < 0 || index >= controls.Count)
            return;
        if (controls[index] is StyledElement styled)
            styled.Classes.Set("gamepad-focused", true);
        GamepadControlActivation.ApplyGamepadHighlightFocus(controls[index]);
    }

    internal void ApplyModsFiltersSelection(int index)
    {
        var controls = CollectModsFilterControls();
        index = _gamepadNavigation.ClampIndex(index, controls.Count);
        _modsGamepadFilterIndex = index;
        _gamepadNavigation.ActiveZone = GamepadNavigationZone.ModsOverlayFilters;
        ClearGamepadFocus();
        ClearModsListGamepadFocus();
        ClearStyledControlsGamepadFocusClasses(controls);
        if (index < 0 || index >= controls.Count)
            return;
        if (controls[index] is StyledElement styled)
            styled.Classes.Set("gamepad-focused", true);
        GamepadControlActivation.ApplyGamepadHighlightFocus(controls[index]);
    }

    internal void ApplyModsSourceFiltersSelection(int index)
    {
        var controls = CollectModsSourceFilterControls();
        index = _gamepadNavigation.ClampIndex(index, controls.Count);
        _modsGamepadSourceFilterIndex = index;
        _gamepadNavigation.ActiveZone = GamepadNavigationZone.ModsOverlaySourceFilters;
        ClearGamepadFocus();
        ClearModsListGamepadFocus();
        ClearStyledControlsGamepadFocusClasses(controls);
        if (index < 0 || index >= controls.Count)
            return;
        if (controls[index] is StyledElement styled)
            styled.Classes.Set("gamepad-focused", true);
        GamepadControlActivation.ApplyGamepadHighlightFocus(controls[index]);
    }

    internal void ApplyModsListSelection(int index, bool stealFocus = true)
    {
        index = _gamepadNavigation.ClampIndex(index, ModListRows.Count);
        _modsGamepadListIndex = index;
        _modsGamepadRowActionIndex = -1;
        _gamepadNavigation.ActiveZone = GamepadNavigationZone.ModsOverlayList;
        ClearModsToolbarGamepadFocus();
        ClearModsFiltersGamepadFocus();
        ClearModsSourceFiltersGamepadFocus();
        ClearModsRowActionGamepadFocus();
        ClearModsListGamepadFocus();
        ClearGamepadFocus();
        if (index < 0 || index >= ModListRows.Count)
            return;
        Host.FocusCard(stealFocus);
        var row = ModListRows[index];
        row.IsGamepadFocused = true;
        Post(() => FindModListRowBorder(row)?.BringIntoView(), DispatcherPriority.Loaded);
    }

    internal void ApplyModsRowActionSelection(int index)
    {
        var listIndex = _gamepadNavigation.ClampIndex(_modsGamepadListIndex, ModListRows.Count);
        if (listIndex < 0 || listIndex >= ModListRows.Count)
            return;
        var actions = CollectModsRowActionControls(listIndex);
        index = _gamepadNavigation.ClampIndex(index, actions.Count);
        _gamepadNavigation.ActiveZone = GamepadNavigationZone.ModsOverlayRowActions;
        // Assign action index after clearing focus classes so Left/Right use a stable index.
        ClearModsToolbarGamepadFocus();
        ClearModsFiltersGamepadFocus();
        ClearModsSourceFiltersGamepadFocus();
        ClearStyledControlsGamepadFocusClasses(actions);
        ClearFocusIfOnControls(actions);
        _modsGamepadListIndex = listIndex;
        _modsGamepadRowActionIndex = index;
        if (index < 0 || index >= actions.Count)
            return;
        // Keep the card highlighted while moving Left/Right across its action buttons.
        ClearModsListGamepadFocus();
        ModListRows[listIndex].IsGamepadFocused = true;
        if (actions[index] is StyledElement styled)
            styled.Classes.Set("gamepad-focused", true);
        GamepadControlActivation.ApplyGamepadHighlightFocus(actions[index]);
        Post(() => actions[index].BringIntoView(), DispatcherPriority.Loaded);
    }

    internal void ClearModsToolbarGamepadFocus()
    {
        var controls = CollectModsToolbarControls();
        ClearStyledControlsGamepadFocusClasses(controls);
        ClearFocusIfOnControls(controls);
    }

    internal void ClearModsFiltersGamepadFocus()
    {
        var controls = CollectModsFilterControls();
        ClearStyledControlsGamepadFocusClasses(controls);
        ClearFocusIfOnControls(controls);
    }

    internal void ClearModsSourceFiltersGamepadFocus()
    {
        var controls = CollectModsSourceFilterControls();
        ClearStyledControlsGamepadFocusClasses(controls);
        ClearFocusIfOnControls(controls);
    }

    internal void ClearModsListGamepadFocus()
    {
        foreach (var row in ModListRows)
            row.IsGamepadFocused = false;
    }

    internal void ClearModsRowActionGamepadFocus()
    {
        if (_modsGamepadListIndex < 0)
        {
            _modsGamepadRowActionIndex = -1;
            return;
        }

        var controls = CollectModsRowActionControls(_modsGamepadListIndex);
        ClearStyledControlsGamepadFocusClasses(controls);
        ClearFocusIfOnControls(controls);
        _modsGamepadRowActionIndex = -1;
    }

    internal void ClearModsGamepadFocus()
    {
        ClearModsToolbarGamepadFocus();
        ClearModsFiltersGamepadFocus();
        ClearModsSourceFiltersGamepadFocus();
        ClearModsListGamepadFocus();
        ClearModsRowActionGamepadFocus();
    }

    private static void ClearStyledControlsGamepadFocusClasses(IReadOnlyList<Control> controls)
    {
        foreach (var control in controls)
            control.Classes.Remove("gamepad-focused");
    }

    private void ClearFocusIfOnControls(IReadOnlyList<Control> controls)
    {
        var manager = TopLevel.GetTopLevel(_view)?.FocusManager;
        if (manager?.GetFocusedElement()is Control focused && controls.Contains(focused))
            manager.Focus(null);
    }

    private (double X, double Y)? GetControlCenter(Control? control)
    {
        if (control == null)
            return null;
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), _view);
        return point.HasValue ? (point.Value.X, point.Value.Y) : null;
    }

    private bool TryMoveXyFocusInRegion(Control? root, NavigationDirection direction, IReadOnlyList<Control> controls, Action<int> apply)
    {
        var previous = TopLevel.GetTopLevel(_view)?.FocusManager?.GetFocusedElement();
        var previousIndex = GamepadControlActivation.IndexOfControlContainingFocus(controls, previous);
        if (!XyFocusNavigation.TryMove(_view, direction, root))
            return false;
        var focused = TopLevel.GetTopLevel(_view)?.FocusManager?.GetFocusedElement();
        var index = GamepadControlActivation.IndexOfControlContainingFocus(controls, focused);
        if (index < 0 || index == previousIndex)
            return false;
        apply(index);
        return true;
    }

    public bool SynchronizePointer(object? source)
    {
        if (GamepadPointerFocusSync.Hit(_gamepadNavigation, CollectModsToolbarControls(), GamepadNavigationZone.ModsOverlayToolbar, _modsGamepadToolbarIndex, ApplyModsToolbarSelection, source) || GamepadPointerFocusSync.Hit(_gamepadNavigation, CollectModsFilterControls(), GamepadNavigationZone.ModsOverlayFilters, _modsGamepadFilterIndex, ApplyModsFiltersSelection, source) || GamepadPointerFocusSync.Hit(_gamepadNavigation, CollectModsSourceFilterControls(), GamepadNavigationZone.ModsOverlaySourceFilters, _modsGamepadSourceFilterIndex, ApplyModsSourceFiltersSelection, source))
        {
            return true;
        }

        var mods = ModListRows.ToList();
        var rowIndex = GamepadPointerFocusSync.IndexOfDataContext(mods, source as Visual);
        if (rowIndex >= 0)
        {
            var actions = CollectModsRowActionControls(rowIndex);
            var actionIndex = GamepadControlActivation.IndexOfControlContainingFocus(actions, source);
            if (actionIndex >= 0)
            {
                if (_gamepadNavigation.ActiveZone == GamepadNavigationZone.ModsOverlayRowActions && _modsGamepadListIndex == rowIndex && _modsGamepadRowActionIndex == actionIndex)
                {
                    return true;
                }

                GamepadTextInput.Reset();
                _modsGamepadListIndex = rowIndex;
                ApplyModsRowActionSelection(actionIndex);
                return true;
            }

            return GamepadPointerFocusSync.Card(_gamepadNavigation, mods, GamepadNavigationZone.ModsOverlayList, _modsGamepadListIndex, index => ApplyModsListSelection(index, stealFocus: false), source);
        }

        return false;
    }

    public void LeaveZone(GamepadNavigationZone nextZone)
    {
        if (nextZone is not (GamepadNavigationZone.ModsOverlayToolbar or GamepadNavigationZone.ModsOverlayFilters or GamepadNavigationZone.ModsOverlaySourceFilters or GamepadNavigationZone.ModsOverlayList or GamepadNavigationZone.ModsOverlayRowActions))
            ClearModsGamepadFocus();
    }

    public bool EnterZone(GamepadZoneTransition transition)
    {
        switch (transition.Zone)
        {
            case GamepadNavigationZone.ModsOverlayToolbar:
                ApplyModsToolbarSelection(transition.SelectedIndex ?? Math.Max(0, _modsGamepadToolbarIndex));
                return true;
            case GamepadNavigationZone.ModsOverlayFilters:
                ApplyModsFiltersSelection(transition.SelectedIndex ?? Math.Max(0, _modsGamepadFilterIndex));
                return true;
            case GamepadNavigationZone.ModsOverlaySourceFilters:
                if (CollectModsSourceFilterControls().Count == 0)
                {
                    // Down from filters passes SelectedIndex; Up from list passes null.
                    if (transition.SelectedIndex.HasValue && ModListRows.Count > 0)
                        ApplyModsListSelection(transition.SelectedIndex.Value);
                    else
                        ApplyModsFiltersSelection(Math.Max(0, _modsGamepadFilterIndex));
                    return true;
                }

                ApplyModsSourceFiltersSelection(transition.SelectedIndex ?? Math.Max(0, _modsGamepadSourceFilterIndex));
                return true;
            case GamepadNavigationZone.ModsOverlayList:
                if (ModListRows.Count == 0)
                {
                    if (CollectModsSourceFilterControls().Count > 0)
                        ApplyModsSourceFiltersSelection(Math.Max(0, _modsGamepadSourceFilterIndex));
                    else
                        ApplyModsFiltersSelection(Math.Max(0, _modsGamepadFilterIndex));
                    return true;
                }

                ApplyModsListSelection(transition.SelectedIndex ?? 0);
                return true;
            case GamepadNavigationZone.ModsOverlayRowActions:
                ApplyModsRowActionSelection(transition.SelectedIndex ?? Math.Max(0, _modsGamepadRowActionIndex));
                return true;
            default:
                return false;
        }
    }
}
