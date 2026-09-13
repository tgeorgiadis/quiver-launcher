using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using QuiverLauncher.Models;
using QuiverLauncher.Services;

namespace QuiverLauncher.Views;
public sealed class CatalogSourcesNavigation(CatalogSourcesView view, IFeatureNavigationHost host, Func<bool> isActive) : IFeatureNavigationHandler
{
    private CatalogSourcesView _view => view;
    private IFeatureNavigationHost _host => host;
    private Func<bool> _isActive => isActive;
    private GamepadNavigationService _gamepadNavigation => host.Navigation;
    private System.Collections.ObjectModel.ObservableCollection<CatalogSourceListItem> CatalogSources => view.Model.Sources;

    public bool Navigate(Services.NavigationDirection direction) => _gamepadNavigation.ActiveZone switch
    {
        GamepadNavigationZone.CatalogSourcesToolbar => HandleCatalogSourcesToolbarNavigation(direction),
        GamepadNavigationZone.CatalogSourcesFilters => HandleCatalogSourcesFiltersNavigation(direction),
        GamepadNavigationZone.CatalogSourceCardActions => HandleCatalogSourceCardActionsNavigation(direction),
        _ => HandleCatalogSourcesCardNavigation(direction),
    };
    public bool Confirm()
    {
        switch (_gamepadNavigation.ActiveZone)
        {
            case GamepadNavigationZone.CatalogSourcesToolbar:
                ActivateCatalogSourcesToolbarSelection();
                break;
            case GamepadNavigationZone.CatalogSourcesFilters:
                ActivateCatalogSourcesFilterSelection();
                break;
            case GamepadNavigationZone.CatalogSourceCardActions:
                if (CatalogSources.Count == 0)
                {
                    ApplyCatalogSourcesFilterSelection(Math.Max(0, _gamepadNavigation.CatalogSourcesFilterIndex));
                    ActivateCatalogSourcesFilterSelection();
                }
                else
                    ActivateCatalogSourceCardActionSelection();
                break;
            default:
                var index = _gamepadNavigation.ClampIndex(_gamepadNavigation.CatalogSelectedIndex, CatalogSources.Count);
                if (index < 0)
                    return false;
                var actionIndex = GetDefaultCatalogSourceCardActionIndex(CollectCatalogSourceCardActionControls(CatalogSources[index]));
                if (actionIndex >= 0)
                    ApplyCatalogSourceCardActionSelection(actionIndex);
                break;
        }

        return true;
    }

    public bool Cancel()
    {
        switch (_gamepadNavigation.ActiveZone)
        {
            case GamepadNavigationZone.CatalogSourceCardActions:
                if (CatalogSources.Count == 0)
                    ApplyCatalogSourcesFilterSelection(Math.Max(0, _gamepadNavigation.CatalogSourcesFilterIndex));
                else
                    ApplyCatalogGamepadSelection(_gamepadNavigation.CatalogSelectedIndex);
                return true;
            case GamepadNavigationZone.CatalogSourcesToolbar:
            case GamepadNavigationZone.CatalogSourcesFilters:
                ClearCatalogSourcesToolbarGamepadFocus();
                ClearCatalogSourcesFiltersGamepadFocus();
                SelectInitialCatalogGamepadItem();
                return true;
            default:
                return false;
        }
    }

    public bool Options() => false;
    public void RestoreFocus() => SyncCatalogGamepadSelection();
    internal bool HandleCatalogSourcesCardNavigation(Services.NavigationDirection direction)
    {
        var currentIndex = _gamepadNavigation.CatalogSelectedIndex;
        if (CatalogSources.Count > 0)
        {
            var positions = CollectCatalogCardPositions();
            var zoneTransition = _gamepadNavigation.TryGetZoneTransition(direction, _gamepadNavigation.ActiveZone, _host.MainContentZone, isListLayout: false, positions, currentIndex, CatalogSources.Count);
            if (zoneTransition.HasValue)
                return _host.ApplyTransition(zoneTransition.Value);
            if (_gamepadNavigation.ActiveZone != GamepadNavigationZone.CatalogSources)
                return false;
            var nextIndex = _gamepadNavigation.MoveCatalogIndex(currentIndex, direction, CatalogSources.Count, positions);
            if (nextIndex == currentIndex && direction is Services.NavigationDirection.Left or Services.NavigationDirection.Up)
            {
                var blockedTransition = _gamepadNavigation.TryGetBlockedMoveZoneTransition(direction, _gamepadNavigation.ActiveZone, _host.MainContentZone, isListLayout: false, currentIndex, CatalogSources.Count);
                if (blockedTransition.HasValue)
                    return _host.ApplyTransition(blockedTransition.Value);
            }

            ApplyCatalogGamepadSelection(nextIndex);
            return true;
        }

        var emptyTransition = _gamepadNavigation.TryGetZoneTransition(direction, _gamepadNavigation.ActiveZone, _host.MainContentZone, isListLayout: false, positions: null, currentIndex, itemCount: 0);
        if (emptyTransition.HasValue)
            return _host.ApplyTransition(emptyTransition.Value);
        return false;
    }

    internal bool HandleCatalogSourcesToolbarNavigation(Services.NavigationDirection direction)
    {
        var controls = CollectCatalogSourcesToolbarControls();
        if (controls.Count == 0)
            return false;
        if (TryMoveXyFocusInRegion(_view.Surface, direction, controls, ApplyCatalogSourcesToolbarSelection))
            return true;
        var currentIndex = _gamepadNavigation.CatalogSourcesToolbarSelectedIndex;
        var zoneTransition = _gamepadNavigation.TryGetZoneTransition(direction, GamepadNavigationZone.CatalogSourcesToolbar, _host.MainContentZone, isListLayout: true, positions: null, currentIndex, controls.Count);
        if (zoneTransition.HasValue)
            return _host.ApplyTransition(zoneTransition.Value);
        if (direction is not (Services.NavigationDirection.Left or Services.NavigationDirection.Right))
            return false;
        var nextIndex = _gamepadNavigation.MoveHorizontalIndex(currentIndex, direction, controls.Count);
        ApplyCatalogSourcesToolbarSelection(nextIndex);
        return true;
    }

    internal bool HandleCatalogSourcesFiltersNavigation(Services.NavigationDirection direction)
    {
        var controls = CollectCatalogSourcesFilterControls();
        if (controls.Count == 0)
            return false;
        var currentIndex = _gamepadNavigation.CatalogSourcesFilterIndex;
        var zoneTransition = _gamepadNavigation.TryGetZoneTransition(direction, GamepadNavigationZone.CatalogSourcesFilters, _host.MainContentZone, isListLayout: true, positions: null, currentIndex, CatalogSources.Count);
        if (zoneTransition.HasValue)
            return _host.ApplyTransition(zoneTransition.Value);
        if (direction is not (Services.NavigationDirection.Left or Services.NavigationDirection.Right))
            return false;
        if (TryMoveXyFocusInRegion(controls[0].Parent as Control ?? _view, direction, controls, ApplyCatalogSourcesFilterSelection))
        {
            return true;
        }

        var nextIndex = _gamepadNavigation.MoveHorizontalIndex(currentIndex, direction, controls.Count);
        ApplyCatalogSourcesFilterSelection(nextIndex);
        return true;
    }

    internal bool HandleCatalogSourceCardActionsNavigation(Services.NavigationDirection direction)
    {
        if (CatalogSources.Count == 0)
        {
            ApplyCatalogSourcesFilterSelection(_gamepadNavigation.CatalogSourcesFilterIndex < 0 ? 0 : _gamepadNavigation.CatalogSourcesFilterIndex);
            return true;
        }

        var cardIndex = _gamepadNavigation.ClampIndex(_gamepadNavigation.CatalogSelectedIndex, CatalogSources.Count);
        if (cardIndex < 0 || cardIndex >= CatalogSources.Count)
            return false;
        var controls = CollectCatalogSourceCardActionControls(CatalogSources[cardIndex]);
        if (controls.Count == 0)
            return false;
        var currentIndex = _gamepadNavigation.CatalogSourceCardActionIndex;
        var zoneTransition = _gamepadNavigation.TryGetZoneTransition(direction, GamepadNavigationZone.CatalogSourceCardActions, _host.MainContentZone, isListLayout: true, positions: null, currentIndex, controls.Count);
        if (zoneTransition.HasValue)
            return _host.ApplyTransition(zoneTransition.Value);
        if (direction is not (Services.NavigationDirection.Left or Services.NavigationDirection.Right))
            return false;
        var nextIndex = _gamepadNavigation.MoveHorizontalIndex(currentIndex, direction, controls.Count);
        ApplyCatalogSourceCardActionSelection(nextIndex);
        return true;
    }

    internal List<Control> CollectCatalogSourcesToolbarControls()
    {
        var controls = new List<Control>();
        void Add(Control? control)
        {
            if (control != null && control.IsVisible && control.IsEnabled)
                controls.Add(control);
        }

        Add(_view.AddCatalogSourceButton);
        Add(_view.RefreshCatalogSourcesButton);
        return controls;
    }

    internal List<Control> CollectCatalogSourcesFilterControls()
    {
        var controls = new List<Control>();
        void Add(Control? control)
        {
            if (control != null && control.IsVisible && control.IsEnabled)
                controls.Add(control);
        }

        Add(_view.CatalogSourceFilterAllButton);
        Add(_view.CatalogSourceFilterEnabledButton);
        Add(_view.CatalogSourceFilterDisabledButton);
        return controls;
    }

    internal List<Control> CollectCatalogSourceCardActionControls(CatalogSourceListItem source)
    {
        var controls = new List<Control>();
        var border = FindCatalogCardBorder(source);
        if (border == null)
            return controls;
        var checkBox = border.GetVisualDescendants().OfType<CheckBox>().FirstOrDefault(c => c.IsVisible && c.IsEnabled);
        if (checkBox != null)
            controls.Add(checkBox);
        foreach (var button in border.GetVisualDescendants().OfType<Button>().Where(b => b.IsVisible && b.IsEnabled && b.Classes.Contains("options")))
        {
            controls.Add(button);
        }

        return controls;
    }

    /// <summary>
    /// Confirm on a source card drills into actions on the first Button (Review), not Enabled.
    /// Enabled remains reachable via Left/Right within card actions.
    /// Note: Avalonia CheckBox inherits Button, so exclude ToggleButton.
    /// </summary>
    internal static int GetDefaultCatalogSourceCardActionIndex(IReadOnlyList<Control> controls)
    {
        for (var i = 0; i < controls.Count; i++)
        {
            if (controls[i] is Button and not ToggleButton)
                return i;
        }

        return controls.Count > 0 ? 0 : -1;
    }

    internal void ApplyCatalogSourcesToolbarSelection(int index)
    {
        var controls = CollectCatalogSourcesToolbarControls();
        index = _gamepadNavigation.ClampIndex(index, controls.Count);
        _gamepadNavigation.CatalogSourcesToolbarSelectedIndex = index;
        _gamepadNavigation.ActiveZone = GamepadNavigationZone.CatalogSourcesToolbar;
        _host.ClearFocus();
        ClearStyledControlsGamepadFocusClasses(controls);
        if (index < 0 || index >= controls.Count)
            return;
        if (controls[index] is StyledElement styled)
            styled.Classes.Set("gamepad-focused", true);
        controls[index].Focus();
        Dispatcher.UIThread.Post(() =>
        {
            if (_isActive())
                controls[index].BringIntoView();
        }, DispatcherPriority.Loaded);
    }

    internal void ApplyCatalogSourcesFilterSelection(int index)
    {
        var controls = CollectCatalogSourcesFilterControls();
        index = _gamepadNavigation.ClampIndex(index, controls.Count);
        _gamepadNavigation.CatalogSourcesFilterIndex = index;
        _gamepadNavigation.ActiveZone = GamepadNavigationZone.CatalogSourcesFilters;
        _host.ClearFocus();
        ClearStyledControlsGamepadFocusClasses(controls);
        if (index < 0 || index >= controls.Count)
            return;
        if (controls[index] is StyledElement styled)
            styled.Classes.Set("gamepad-focused", true);
        controls[index].Focus();
        Dispatcher.UIThread.Post(() =>
        {
            if (_isActive())
                controls[index].BringIntoView();
        }, DispatcherPriority.Loaded);
    }

    internal void ApplyCatalogSourceCardActionSelection(int index)
    {
        if (CatalogSources.Count == 0)
            return;
        var cardIndex = _gamepadNavigation.ClampIndex(_gamepadNavigation.CatalogSelectedIndex, CatalogSources.Count);
        var controls = CollectCatalogSourceCardActionControls(CatalogSources[cardIndex]);
        index = _gamepadNavigation.ClampIndex(index, controls.Count);
        _gamepadNavigation.CatalogSourceCardActionIndex = index;
        _gamepadNavigation.ActiveZone = GamepadNavigationZone.CatalogSourceCardActions;
        _host.ClearFocus();
        ClearCatalogSourceCardActionsGamepadFocusClasses(controls);
        if (index < 0 || index >= controls.Count)
            return;
        CatalogSources[cardIndex].IsGamepadFocused = true;
        if (controls[index] is StyledElement styled)
            styled.Classes.Set("gamepad-focused", true);
        controls[index].Focus();
        Dispatcher.UIThread.Post(() =>
        {
            if (_isActive())
                controls[index].BringIntoView();
        }, DispatcherPriority.Loaded);
    }

    internal void ClearCatalogSourcesToolbarGamepadFocus()
    {
        var controls = CollectCatalogSourcesToolbarControls();
        ClearStyledControlsGamepadFocusClasses(controls);
        ClearFocusIfOnControls(controls);
    }

    internal void ClearCatalogSourcesFiltersGamepadFocus()
    {
        var controls = CollectCatalogSourcesFilterControls();
        ClearStyledControlsGamepadFocusClasses(controls);
        ClearFocusIfOnControls(controls);
    }

    internal void ClearCatalogSourceCardActionsGamepadFocus()
    {
        if (CatalogSources.Count == 0)
            return;
        var cardIndex = _gamepadNavigation.ClampIndex(_gamepadNavigation.CatalogSelectedIndex, CatalogSources.Count);
        if (cardIndex < 0 || cardIndex >= CatalogSources.Count)
            return;
        var controls = CollectCatalogSourceCardActionControls(CatalogSources[cardIndex]);
        ClearCatalogSourceCardActionsGamepadFocusClasses(controls);
        ClearFocusIfOnControls(controls);
    }

    internal static void ClearCatalogSourceCardActionsGamepadFocusClasses(IReadOnlyList<Control> controls)
    {
        ClearStyledControlsGamepadFocusClasses(controls);
    }

    internal void SelectInitialCatalogGamepadItem()
    {
        if (!_host.IsFocusActive)
        {
            _host.ClearFocus();
            return;
        }

        if (CatalogSources.Count == 0)
        {
            ApplyCatalogSourcesToolbarSelection(0);
            return;
        }

        ApplyCatalogGamepadSelection(_gamepadNavigation.CatalogSelectedIndex < 0 ? 0 : _gamepadNavigation.CatalogSelectedIndex);
    }

    internal void SyncCatalogGamepadSelection()
    {
        if (!_host.IsFocusActive)
        {
            _host.ClearFocus();
            return;
        }

        if (!_isActive())
        {
            return;
        }

        if (CatalogSources.Count == 0)
        {
            _gamepadNavigation.CatalogSelectedIndex = -1;
            _gamepadNavigation.CatalogSourceCardActionIndex = -1;
            if (_gamepadNavigation.ActiveZone == GamepadNavigationZone.CatalogSourcesToolbar)
            {
                ApplyCatalogSourcesToolbarSelection(_gamepadNavigation.CatalogSourcesToolbarSelectedIndex < 0 ? 0 : _gamepadNavigation.CatalogSourcesToolbarSelectedIndex);
            }
            else
            {
                // Empty filter (e.g. enabled last Disabled source) — re-home to filter chips
                // so Confirm is not stuck in CatalogSourceCardActions with nothing to activate.
                ApplyCatalogSourcesFilterSelection(_gamepadNavigation.CatalogSourcesFilterIndex < 0 ? 0 : _gamepadNavigation.CatalogSourcesFilterIndex);
            }

            return;
        }

        // Keep toolbar/filter focus after list rebuilds (e.g. Refresh All Sources)
        // so focus does not jump to a source under a pending Yes/No prompt.
        if (_gamepadNavigation.ActiveZone == GamepadNavigationZone.CatalogSourcesToolbar)
        {
            ApplyCatalogSourcesToolbarSelection(_gamepadNavigation.CatalogSourcesToolbarSelectedIndex < 0 ? 0 : _gamepadNavigation.CatalogSourcesToolbarSelectedIndex);
            return;
        }

        if (_gamepadNavigation.ActiveZone == GamepadNavigationZone.CatalogSourcesFilters)
        {
            ApplyCatalogSourcesFilterSelection(_gamepadNavigation.CatalogSourcesFilterIndex < 0 ? 0 : _gamepadNavigation.CatalogSourcesFilterIndex);
            return;
        }

        var clamped = _gamepadNavigation.ClampIndex(_gamepadNavigation.CatalogSelectedIndex, CatalogSources.Count);
        ApplyCatalogGamepadSelection(clamped);
    }

    internal void ApplyCatalogGamepadSelection(int index, bool stealFocus = true)
    {
        if (CatalogSources.Count == 0)
        {
            ApplyCatalogSourcesToolbarSelection(_gamepadNavigation.CatalogSourcesToolbarSelectedIndex < 0 ? 0 : _gamepadNavigation.CatalogSourcesToolbarSelectedIndex);
            return;
        }

        index = _gamepadNavigation.ClampIndex(index, CatalogSources.Count);
        _gamepadNavigation.CatalogSelectedIndex = index;
        _gamepadNavigation.ActiveZone = GamepadNavigationZone.CatalogSources;
        _host.ClearSidebarFocus();
        ClearCatalogSourcesToolbarGamepadFocus();
        _host.ClearFocus();
        ClearCatalogSourceCardActionsGamepadFocus();
        _host.FocusCard(stealFocus);
        if (index < 0 || index >= CatalogSources.Count)
            return;
        CatalogSources[index].IsGamepadFocused = true;
        var source = CatalogSources[index];
        Dispatcher.UIThread.Post(() =>
        {
            if (_isActive() && CatalogSources.Contains(source))
                FindCatalogCardBorder(source)?.BringIntoView();
        }, DispatcherPriority.Loaded);
    }

    internal List<(double X, double Y)> CollectCatalogCardPositions()
    {
        var positions = new List<(double X, double Y)>();
        foreach (var source in CatalogSources)
        {
            var card = FindCatalogCardBorder(source);
            positions.Add(GetControlCenter(card) ?? (0, positions.Count * 160));
        }

        return positions;
    }

    internal Border? FindCatalogCardBorder(CatalogSourceListItem source)
    {
        var panel = _view.Surface;
        if (panel == null)
            return null;
        return panel.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => ReferenceEquals(b.DataContext, source));
    }

    internal void ActivateCatalogSourcesToolbarSelection()
    {
        var controls = CollectCatalogSourcesToolbarControls();
        var index = _gamepadNavigation.ClampIndex(_gamepadNavigation.CatalogSourcesToolbarSelectedIndex, controls.Count);
        if (index < 0 || index >= controls.Count)
            return;
        if (controls[index] is Button button)
            GamepadControlActivation.ActivateButton(button);
    }

    internal void ActivateCatalogSourcesFilterSelection()
    {
        var controls = CollectCatalogSourcesFilterControls();
        var index = _gamepadNavigation.ClampIndex(_gamepadNavigation.CatalogSourcesFilterIndex, controls.Count);
        if (index < 0 || index >= controls.Count)
            return;
        if (controls[index] is Button button)
            GamepadControlActivation.ActivateButton(button);
    }

    internal void ActivateCatalogSourceCardActionSelection()
    {
        if (CatalogSources.Count == 0)
            return;
        var cardIndex = _gamepadNavigation.ClampIndex(_gamepadNavigation.CatalogSelectedIndex, CatalogSources.Count);
        var controls = CollectCatalogSourceCardActionControls(CatalogSources[cardIndex]);
        var index = _gamepadNavigation.ClampIndex(_gamepadNavigation.CatalogSourceCardActionIndex, controls.Count);
        if (index < 0 || index >= controls.Count)
            return;
        if (controls[index] is CheckBox checkBox)
        {
            GamepadControlActivation.ActivateCheckBox(checkBox);
            return;
        }

        if (controls[index] is Button button)
            GamepadControlActivation.ActivateButton(button);
    }

    internal bool TryMoveXyFocusInRegion(Control? searchRoot, Services.NavigationDirection direction, IReadOnlyList<Control> controls, Action<int> applySelection)
    {
        if (searchRoot == null || controls.Count == 0)
            return false;
        var previous = TopLevel.GetTopLevel(_view)?.FocusManager?.GetFocusedElement();
        var previousIndex = GamepadControlActivation.IndexOfControlContainingFocus(controls, previous);
        if (!XyFocusNavigation.TryMove(_view, direction, searchRoot))
            return false;
        var focused = TopLevel.GetTopLevel(_view)?.FocusManager?.GetFocusedElement();
        var index = GamepadControlActivation.IndexOfControlContainingFocus(controls, focused);
        if (index < 0 || index == previousIndex)
            return false;
        applySelection(index);
        return true;
    }

    internal static void ClearStyledControlsGamepadFocusClasses(IReadOnlyList<Control> controls)
    {
        foreach (var control in controls)
        {
            if (control is StyledElement styled)
                styled.Classes.Set("gamepad-focused", false);
        }
    }

    internal void ClearFocusIfOnControls(IReadOnlyList<Control> controls)
    {
        var focusManager = TopLevel.GetTopLevel(_view)?.FocusManager;
        if (focusManager?.GetFocusedElement()is Control focusedControl && controls.Contains(focusedControl))
        {
            focusManager.Focus(null);
        }
    }

    internal (double X, double Y)? GetControlCenter(Control? control)
    {
        if (control == null)
            return null;
        var topLeft = control.TranslatePoint(new Point(0, 0), _view);
        if (!topLeft.HasValue)
            return null;
        var bounds = control.Bounds;
        return (topLeft.Value.X + bounds.Width / 2, topLeft.Value.Y + bounds.Height / 2);
    }

    public bool SynchronizePointer(object? source)
    {
        if (GamepadPointerFocusSync.Hit(_gamepadNavigation, CollectCatalogSourcesToolbarControls(), GamepadNavigationZone.CatalogSourcesToolbar, _gamepadNavigation.CatalogSourcesToolbarSelectedIndex, ApplyCatalogSourcesToolbarSelection, source) || GamepadPointerFocusSync.Hit(_gamepadNavigation, CollectCatalogSourcesFilterControls(), GamepadNavigationZone.CatalogSourcesFilters, _gamepadNavigation.CatalogSourcesFilterIndex, ApplyCatalogSourcesFilterSelection, source))
        {
            return true;
        }

        var sources = CatalogSources.ToList();
        var sourceIndex = GamepadPointerFocusSync.IndexOfDataContext(sources, source as Visual);
        if (sourceIndex >= 0)
        {
            var actions = CollectCatalogSourceCardActionControls(sources[sourceIndex]);
            var actionIndex = GamepadControlActivation.IndexOfControlContainingFocus(actions, source);
            if (actionIndex >= 0)
            {
                if (_gamepadNavigation.ActiveZone == GamepadNavigationZone.CatalogSourceCardActions && _gamepadNavigation.CatalogSelectedIndex == sourceIndex && _gamepadNavigation.CatalogSourceCardActionIndex == actionIndex)
                {
                    return true;
                }

                GamepadTextInput.Reset();
                ApplyCatalogGamepadSelection(sourceIndex, stealFocus: false);
                ApplyCatalogSourceCardActionSelection(actionIndex);
                return true;
            }

            return GamepadPointerFocusSync.Card(_gamepadNavigation, sources, GamepadNavigationZone.CatalogSources, _gamepadNavigation.CatalogSelectedIndex, index => ApplyCatalogGamepadSelection(index, stealFocus: false), source);
        }

        return false;
    }

    public void LeaveZone(GamepadNavigationZone nextZone)
    {
        var zone = _gamepadNavigation.ActiveZone;
        if (zone == nextZone)
            return;
        if (zone == GamepadNavigationZone.CatalogSourcesToolbar)
            ClearCatalogSourcesToolbarGamepadFocus();
        if (zone == GamepadNavigationZone.CatalogSourcesFilters)
            ClearCatalogSourcesFiltersGamepadFocus();
        if (zone == GamepadNavigationZone.CatalogSourceCardActions)
            ClearCatalogSourceCardActionsGamepadFocus();
    }

    public bool EnterZone(GamepadZoneTransition transition)
    {
        switch (transition.Zone)
        {
            case GamepadNavigationZone.CatalogSources:
                if (CatalogSources.Count == 0)
                    ApplyCatalogSourcesToolbarSelection(transition.SelectedIndex ?? 0);
                else
                    ApplyCatalogGamepadSelection(transition.SelectedIndex ?? 0);
                return true;
            case GamepadNavigationZone.CatalogSourcesToolbar:
                _host.ClearFocus();
                _gamepadNavigation.ActiveZone = GamepadNavigationZone.CatalogSourcesToolbar;
                ApplyCatalogSourcesToolbarSelection(_gamepadNavigation.CatalogSourcesToolbarSelectedIndex < 0 ? 0 : _gamepadNavigation.CatalogSourcesToolbarSelectedIndex);
                return true;
            case GamepadNavigationZone.CatalogSourcesFilters:
                _host.ClearFocus();
                _gamepadNavigation.ActiveZone = GamepadNavigationZone.CatalogSourcesFilters;
                ApplyCatalogSourcesFilterSelection(_gamepadNavigation.CatalogSourcesFilterIndex < 0 ? 0 : _gamepadNavigation.CatalogSourcesFilterIndex);
                return true;
            case GamepadNavigationZone.CatalogSourceCardActions:
                ApplyCatalogSourceCardActionSelection(transition.SelectedIndex ?? (_gamepadNavigation.CatalogSourceCardActionIndex < 0 ? 0 : _gamepadNavigation.CatalogSourceCardActionIndex));
                return true;
            default:
                return false;
        }
    }
}
