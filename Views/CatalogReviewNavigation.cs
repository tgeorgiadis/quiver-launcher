using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using QuiverLauncher.Services;

namespace QuiverLauncher.Views;
public sealed class CatalogReviewNavigation(CatalogReviewView view, IFeatureNavigationHost host) : IFeatureNavigationHandler
{
    private CatalogReviewView _view => view;
    private IFeatureNavigationHost _host => host;
    private GamepadNavigationService _gamepadNavigation => host.Navigation;
    private AppSettings _settings => view.SettingsModel.Current;
    private ResettableObservableCollection<CatalogSyncRowItem> CatalogSyncRows => view.Model.Rows;

    public bool Navigate(Services.NavigationDirection direction) => HandleCatalogReviewGamepadNavigation(direction);
    public bool Confirm()
    {
        if (_gamepadNavigation.ActiveZone == GamepadNavigationZone.CatalogReviewFilters)
            ActivateReviewFilterSelection();
        else if (_gamepadNavigation.ActiveZone == GamepadNavigationZone.CatalogReviewRowActions)
            ActivateCatalogReviewRowActionSelection();
        else
            ActivateReviewRowSelection();
        return true;
    }

    public bool Cancel()
    {
        switch (_gamepadNavigation.ActiveZone)
        {
            case GamepadNavigationZone.CatalogReviewRowActions:
                ApplyCatalogReviewRowSelection(_gamepadNavigation.CatalogReviewSelectedIndex);
                return true;
            case GamepadNavigationZone.CatalogReviewFilters:
                _gamepadNavigation.CatalogReviewFilterIndex = -1;
                SelectInitialCatalogReviewGamepadItem();
                return true;
            case GamepadNavigationZone.CatalogReviewList:
                _view.ReturnToSources();
                return true;
            default:
                return false;
        }
    }

    public bool Options() => false;
    public void RestoreFocus() => RestoreFocus(bringIntoView: true);
    public void RestoreFocus(bool bringIntoView) => SyncCatalogReviewGamepadSelection(bringIntoView);
    internal bool HandleCatalogReviewGamepadNavigation(Services.NavigationDirection direction)
    {
        if (_gamepadNavigation.ActiveZone == GamepadNavigationZone.CatalogReviewDetailsOverlay)
            return _view.Details.HandleCatalogReviewDetailsGamepadNavigation(direction);
        if (_gamepadNavigation.ActiveZone == GamepadNavigationZone.CatalogReviewFilters)
            return HandleCatalogReviewFiltersGamepadNavigation(direction);
        if (_gamepadNavigation.ActiveZone == GamepadNavigationZone.CatalogReviewRowActions)
            return HandleCatalogReviewRowActionsNavigation(direction);
        // Needs-review complete (or other empty filter): navigate the Back buttons.
        if (CatalogSyncRows.Count == 0)
            return HandleCatalogReviewEmptyActionsNavigation(direction);
        var rows = CatalogSyncRows.ToList();
        var currentIndex = _gamepadNavigation.CatalogReviewSelectedIndex;
        var isListLayout = !_settings.CatalogReviewUseGridView;
        var positions = !isListLayout && rows.Count > 0 ? CollectCatalogReviewCardPositions(rows) : null;
        var zoneTransition = _gamepadNavigation.TryGetZoneTransition(direction, _gamepadNavigation.ActiveZone, _host.MainContentZone, isListLayout, positions, currentIndex, rows.Count);
        if (zoneTransition.HasValue)
            return _host.ApplyTransition(zoneTransition.Value);
        if (_gamepadNavigation.ActiveZone != GamepadNavigationZone.CatalogReviewList)
            return false;
        if (isListLayout && direction == Services.NavigationDirection.Right)
        {
            ActivateReviewRowSelection();
            return true;
        }

        var nextIndex = isListLayout ? _gamepadNavigation.MoveListIndex(currentIndex, direction, rows.Count, wrap: false) : _gamepadNavigation.MoveLibraryIndex(currentIndex, direction, rows.Count, isListLayout: false, positions);
        ApplyCatalogReviewRowSelection(nextIndex);
        return true;
    }

    internal bool HandleCatalogReviewEmptyActionsNavigation(Services.NavigationDirection direction)
    {
        var controls = CollectCatalogReviewEmptyActionControls();
        if (controls.Count == 0)
        {
            // Nothing to focus in the body — Up returns to the filter strip (tags if present).
            if (direction == Services.NavigationDirection.Up)
                return NavigateToCatalogReviewFiltersFromList() || true;
            if (direction == Services.NavigationDirection.Left)
                return _host.ApplyTransition(new GamepadZoneTransition(GamepadNavigationZone.Sidebar, null));
            return true;
        }

        var currentIndex = _gamepadNavigation.CatalogReviewSelectedIndex;
        if (direction == Services.NavigationDirection.Up)
            return NavigateToCatalogReviewFiltersFromList() || true;
        if (direction == Services.NavigationDirection.Left && currentIndex <= 0)
        {
            return _host.ApplyTransition(new GamepadZoneTransition(GamepadNavigationZone.Sidebar, null));
        }

        if (direction is Services.NavigationDirection.Left or Services.NavigationDirection.Right)
        {
            var nextIndex = _gamepadNavigation.MoveHorizontalIndex(currentIndex, direction, controls.Count);
            ApplyCatalogReviewEmptyActionSelection(nextIndex);
            return true;
        }

        // Down stays on the empty actions.
        return true;
    }

    internal bool HandleCatalogReviewRowActionsNavigation(Services.NavigationDirection direction)
    {
        var rows = CatalogSyncRows.ToList();
        if (rows.Count == 0)
            return false;
        var rowIndex = _gamepadNavigation.ClampIndex(_gamepadNavigation.CatalogReviewSelectedIndex, rows.Count);
        if (rowIndex < 0 || rowIndex >= rows.Count)
            return false;
        // Leave the action strip back to the row list.
        if (direction is Services.NavigationDirection.Up or Services.NavigationDirection.Down)
        {
            ClearCatalogReviewRowActionsGamepadFocus();
            var nextRow = _gamepadNavigation.MoveListIndex(rowIndex, direction, rows.Count, wrap: false);
            ApplyCatalogReviewRowSelection(nextRow);
            return true;
        }

        var controls = CollectCatalogReviewRowActionControls(rows[rowIndex]);
        if (controls.Count == 0)
        {
            DeferCatalogRowInput(rows[rowIndex], () => HandleCatalogReviewRowActionsNavigation(direction));
            return true;
        }
        var currentIndex = ResolveMobileActionFocus(controls,
            _gamepadNavigation.ClampIndex(_gamepadNavigation.CatalogReviewRowActionIndex, controls.Count));
        _gamepadNavigation.CatalogReviewRowActionIndex = currentIndex;
        // Left from the first action returns to the row (do not wrap to the far-right button).
        if (direction == Services.NavigationDirection.Left && currentIndex <= 0)
        {
            ClearCatalogReviewRowActionsGamepadFocus();
            ApplyCatalogReviewRowSelection(rowIndex);
            return true;
        }

        if (direction is not (Services.NavigationDirection.Left or Services.NavigationDirection.Right))
            return false;
        var actionRoot = controls[0].Parent as Control ?? controls[0];
        if (TryMoveXyFocusInRegion(actionRoot, direction, controls, ApplyCatalogReviewRowActionSelection))
            return true;
        var nextIndex = _gamepadNavigation.MoveHorizontalIndex(currentIndex, direction, controls.Count);
        ApplyCatalogReviewRowActionSelection(nextIndex);
        return true;
    }

    internal bool HandleCatalogReviewFiltersGamepadNavigation(Services.NavigationDirection direction)
    {
        var ranges = GetCatalogReviewFilterRanges();
        var controls = CollectCatalogReviewFilterControls();
        if (controls.Count == 0 || ranges.Total <= 0)
            return false;
        var currentIndex = _gamepadNavigation.ClampIndex(_gamepadNavigation.CatalogReviewFilterIndex, controls.Count);
        var row = ranges.ResolveRow(currentIndex);
        var localIndex = ranges.LocalIndex(currentIndex);
        var rows = Enum.GetValues<CatalogReviewFilterGamepadLayout.Row>().Where(r => ranges.RowCount(r) > 0).ToList();
        var rowIndex = rows.IndexOf(row);
        if (row == CatalogReviewFilterGamepadLayout.Row.Notice)
        {
            var next = direction is Services.NavigationDirection.Right or Services.NavigationDirection.Down
                ? localIndex + 1 : localIndex - 1;
            if (next >= 0 && next < ranges.NoticeCount)
            {
                var targetIndex = ranges.NoticeStart + next;
                var currentPosition = controls[currentIndex].TranslatePoint(default, _view);
                var targetPosition = controls[targetIndex].TranslatePoint(default, _view);
                // When actions wrap, Up/Down follows the visible second row too.
                if (direction is Services.NavigationDirection.Left or Services.NavigationDirection.Right ||
                    (currentPosition.HasValue && targetPosition.HasValue &&
                     Math.Abs(currentPosition.Value.Y - targetPosition.Value.Y) > 1))
                {
                    ApplyCatalogReviewFilterSelection(targetIndex);
                    return true;
                }
            }
        }
        if (direction == Services.NavigationDirection.Up)
        {
            if (rowIndex > 0)
            {
                ApplyCatalogReviewFilterSelection(ranges.AbsoluteIndex(rows[rowIndex - 1], 0));
                return true;
            }
            return _host.ApplyTransition(new GamepadZoneTransition(GamepadNavigationZone.TopBar, null));
        }

        if (direction == Services.NavigationDirection.Down)
        {
            if (rowIndex >= 0 && rowIndex < rows.Count - 1)
            {
                ApplyCatalogReviewFilterSelection(ranges.AbsoluteIndex(rows[rowIndex + 1], 0));
                return true;
            }
            if (CatalogSyncRows.Count == 0)
            {
                var emptyActions = CollectCatalogReviewEmptyActionControls();
                if (emptyActions.Count > 0)
                {
                    ApplyCatalogReviewEmptyActionSelection(0);
                    return true;
                }

                return true;
            }

            return _host.ApplyTransition(new GamepadZoneTransition(GamepadNavigationZone.CatalogReviewList, 0));
        }

        if (direction is not (Services.NavigationDirection.Left or Services.NavigationDirection.Right))
            return true;
        var rowCount = ranges.RowCount(row);
        if (rowCount <= 0)
            return true;
        if (CatalogReviewFilterGamepadLayout.ShouldLeaveToSidebarOnLeft(localIndex, direction))
        {
            return _host.ApplyTransition(new GamepadZoneTransition(GamepadNavigationZone.Sidebar, null));
        }

        var rowStart = ranges.AbsoluteIndex(row, 0);
        if (TryMoveXyFocusInRegion(_view.Surface, direction, controls.Skip(rowStart).Take(rowCount).ToList(), index => ApplyCatalogReviewFilterSelection(rowStart + index)))
            return true;
        int nextLocal;
        if (row == CatalogReviewFilterGamepadLayout.Row.Tags)
        {
            // Tag chips clamp at ends (no wrap) so Right stops on the last tag.
            nextLocal = CatalogReviewFilterGamepadLayout.MoveHorizontalClamped(localIndex, direction, rowCount);
        }
        else
        {
            nextLocal = _gamepadNavigation.MoveHorizontalIndex(localIndex, direction, rowCount);
        }

        ApplyCatalogReviewFilterSelection(ranges.AbsoluteIndex(row, nextLocal));
        return true;
    }

    internal bool NavigateToCatalogReviewFiltersFromList()
    {
        var index = GetCatalogReviewFilterIndexFromList();
        if (index < 0)
            return false;
        ApplyCatalogReviewFilterSelection(index);
        return true;
    }

    internal List<Control> CollectCatalogReviewFilterChipControls()
    {
        var controls = new List<Control>();
        void Add(Control? control)
        {
            if (control != null && control.IsEffectivelyVisible && control.IsEnabled)
                controls.Add(control);
        }

        if (PlatformCapabilities.IsMobile)
        {
            Add(_view.CatalogSearchTextBox);
            Add(_view.CatalogReviewBulkButton);
            Add(_view.CatalogReviewTagsToggle);
            Add(_view.CatalogReviewFiltersToggle);
        }

        Add(_view.CatalogFilterAllButton);
        Add(_view.CatalogFilterNeedsReviewButton);
        Add(_view.CatalogFilterNotInLibraryButton);
        Add(_view.CatalogFilterChangedButton);
        Add(_view.CatalogFilterUpToDateButton);
        Add(_view.CatalogFilterHiddenButton);
        if (!PlatformCapabilities.IsMobile)
        {
            Add(_view.CatalogSearchTextBox);
            Add(_view.CatalogReviewTagsToggle);
            Add(_view.CatalogReviewPlatformButton);
            Add(_view.CatalogReviewSortByComboBox);
            Add(_view.CatalogReviewListViewButton);
            Add(_view.CatalogReviewGridViewButton);
        }

        return controls;
    }

    internal List<Control> CollectCatalogReviewTagChipControls()
    {
        var controls = new List<Control>();
        if (_view.CatalogReviewFiltersExtra != null && !_view.CatalogReviewFiltersExtra.IsEffectivelyVisible)
            return controls;
        var itemsControl = _view.FindControl<ItemsControl>("CatalogTagChipsItemsControl");
        if (itemsControl == null || !itemsControl.IsEffectivelyVisible)
            return controls;
        foreach (var button in itemsControl.GetVisualDescendants().OfType<Button>())
        {
            if (button.IsEffectivelyVisible && button.IsEnabled)
                controls.Add(button);
        }

        return controls;
    }

    internal List<Control> CollectCatalogReviewBulkActionControls()
    {
        var controls = new List<Control>();
        void Add(Control? control)
        {
            if (control != null && control.IsEffectivelyVisible && control.IsEnabled)
                controls.Add(control);
        }

        if (_view.CatalogReviewBulkPanel == null || _view.CatalogReviewBulkPanel.IsEffectivelyVisible)
        {
            Add(_view.CatalogSyncAddAllButton);
            Add(_view.CatalogSyncReplaceAllButton);
            Add(_view.CatalogSyncAcknowledgeButton);
        }


        return controls;
    }

    internal List<Control> CollectCatalogReviewNoticeControls() =>
        new Control[] { _view.CatalogPlatformRetryButton, _view.CatalogPlatformDetailsButton, _view.CatalogShowAllPendingButton }
            .Where(control => control.IsEffectivelyVisible && control.IsEnabled).ToList();

    internal CatalogReviewFilterGamepadLayout.Ranges GetCatalogReviewFilterRanges() => CatalogReviewFilterGamepadLayout.FromCounts(CollectCatalogReviewFilterChipControls().Count, CollectCatalogReviewTagChipControls().Count, CollectCatalogReviewBulkActionControls().Count, CollectCatalogReviewNoticeControls().Count);
    internal List<Control> CollectCatalogReviewFilterControls()
    {
        // Status strip, then tag chips (when open), then bulk actions.
        var controls = CollectCatalogReviewFilterChipControls();
        controls.AddRange(CollectCatalogReviewTagChipControls());
        controls.AddRange(CollectCatalogReviewBulkActionControls());
        controls.AddRange(CollectCatalogReviewNoticeControls());
        return controls;
    }

    internal int GetCatalogReviewFilterIndexFromList() => GetCatalogReviewFilterRanges().PreferredIndexFromList;
    internal List<Control> CollectCatalogReviewEmptyActionControls()
    {
        var controls = new List<Control>();
        void Add(Control? control)
        {
            if (control != null && control.IsVisible && control.IsEnabled)
                controls.Add(control);
        }

        var emptyPanel = _view.FindControl<StackPanel>("CatalogSyncNeedsReviewEmptyPanel");
        if (emptyPanel == null || !emptyPanel.IsVisible)
            return controls;
        Add(_view.FindControl<Button>("CatalogSyncBackToLibraryButton"));
        Add(_view.FindControl<Button>("CatalogSyncBackToSourcesButton"));
        return controls;
    }

    internal List<Control> CollectCatalogReviewRowActionControls(CatalogSyncRowItem row, bool realize = true)
    {
        if (realize)
        {
            EnsureCatalogReviewRowRealized(row);
            // After passive restoration the action row may still be virtualized.
            // Explicit input needs its controls now, not on the next layout pass.
            if (FindCatalogSyncRowBorder(row) == null)
                GetActiveCatalogReviewItemsControl()?.UpdateLayout();
        }
        var controls = new List<Control>();
        var border = FindCatalogSyncRowBorder(row);
        if (border == null)
            return controls;
        foreach (var button in border.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible && b.IsEnabled && b.Classes.Contains("options")))
        {
            controls.Add(button);
        }

        return controls;
    }

    internal void ClearCatalogReviewRowActionsGamepadFocus()
    {
        // The selected index may already refer to a different row after filtering.
        // Clear every realized action, including hidden/recycled controls.
        var controls = _view.GetVisualDescendants().OfType<Button>()
            .Where(b => b.Classes.Contains("options")).Cast<Control>().ToList();
        ClearStyledControlsGamepadFocusClasses(controls);
        ClearFocusIfOnControls(controls);
        _gamepadNavigation.CatalogReviewRowActionIndex = -1;
    }

    internal void ApplyCatalogReviewRowActionSelection(int index) => ApplyCatalogReviewRowActionSelection(index, bringIntoView: true);

    internal void ApplyCatalogReviewRowActionSelection(int index, bool bringIntoView)
    {
        var rows = CatalogSyncRows.ToList();
        if (rows.Count == 0)
            return;
        var rowIndex = _gamepadNavigation.ClampIndex(_gamepadNavigation.CatalogReviewSelectedIndex, rows.Count);
        if (rowIndex < 0 || rowIndex >= rows.Count)
            return;
        var controls = CollectCatalogReviewRowActionControls(rows[rowIndex], realize: bringIntoView);
        index = _gamepadNavigation.ClampIndex(index, controls.Count);
        _gamepadNavigation.ActiveZone = GamepadNavigationZone.CatalogReviewRowActions;
        // ClearGamepadFocus → ClearCatalogReviewRowActionsGamepadFocus resets action index to -1.
        // Assign the new index after that clear, or Left wraps as if index were 0 → last button forever.
        _host.ClearFocus();
        ClearCatalogReviewRowActionsGamepadFocus();
        ClearStyledControlsGamepadFocusClasses(controls);
        _gamepadNavigation.CatalogReviewRowActionIndex = index;
        if (index < 0 || index >= controls.Count)
            return;
        rows[rowIndex].IsGamepadFocused = true;
        if (controls[index] is StyledElement styled)
            styled.Classes.Set("gamepad-focused", true);
        if (!bringIntoView)
        {
            // Confirm uses the saved index; native focus must not reveal this control.
            _host.FocusCard(true);
            return;
        }
        controls[index].Focus();
        Dispatcher.UIThread.Post(() =>
        {
            if (_view.IsActive)
                controls[index].BringIntoView();
        }, DispatcherPriority.Loaded);
    }

    internal void ApplyCatalogReviewFilterSelection(int index) => ApplyCatalogReviewFilterSelection(index, bringIntoView: true);

    internal void ApplyCatalogReviewFilterSelection(int index, bool bringIntoView)
    {
        var controls = CollectCatalogReviewFilterControls();
        index = _gamepadNavigation.ClampIndex(index, controls.Count);
        _gamepadNavigation.CatalogReviewFilterIndex = index;
        _gamepadNavigation.ActiveZone = GamepadNavigationZone.CatalogReviewFilters;
        ClearCatalogReviewEmptyActionGamepadFocus();
        ClearCatalogReviewRowActionsGamepadFocus();
        _host.ClearFocus();
        ClearStyledControlsGamepadFocusClasses(controls);
        if (index < 0 || index >= controls.Count)
            return;
        if (controls[index] is StyledElement styled)
            styled.Classes.Set("gamepad-focused", true);
        if (!bringIntoView)
        {
            // Confirm uses the saved index; native focus must not reveal this control.
            _host.FocusCard(true);
            return;
        }
        GamepadControlActivation.ApplyGamepadHighlightFocus(controls[index]);
        Dispatcher.UIThread.Post(() =>
        {
            if (_view.IsActive)
                controls[index].BringIntoView();
        }, DispatcherPriority.Loaded);
    }

    internal void ApplyCatalogReviewEmptyActionSelection(int index) => ApplyCatalogReviewEmptyActionSelection(index, bringIntoView: true);

    internal void ApplyCatalogReviewEmptyActionSelection(int index, bool bringIntoView)
    {
        var controls = CollectCatalogReviewEmptyActionControls();
        index = _gamepadNavigation.ClampIndex(index, controls.Count);
        _gamepadNavigation.CatalogReviewSelectedIndex = index;
        _gamepadNavigation.CatalogReviewRowActionIndex = -1;
        _gamepadNavigation.ActiveZone = GamepadNavigationZone.CatalogReviewList;
        ClearCatalogReviewFilterGamepadFocus();
        ClearCatalogReviewRowActionsGamepadFocus();
        _host.ClearFocus();
        ClearStyledControlsGamepadFocusClasses(controls);
        if (index < 0 || index >= controls.Count)
            return;
        if (controls[index] is StyledElement styled)
            styled.Classes.Set("gamepad-focused", true);
        if (!bringIntoView)
        {
            // Confirm uses the saved index; native focus must not reveal this control.
            _host.FocusCard(true);
            return;
        }
        controls[index].Focus();
        Dispatcher.UIThread.Post(() =>
        {
            if (_view.IsActive)
                controls[index].BringIntoView();
        }, DispatcherPriority.Loaded);
    }

    internal void ClearCatalogReviewFilterGamepadFocus()
    {
        var controls = CollectCatalogReviewFilterControls();
        ClearStyledControlsGamepadFocusClasses(controls);
        ClearFocusIfOnControls(controls);
    }

    internal void ClearCatalogReviewEmptyActionGamepadFocus()
    {
        var controls = CollectCatalogReviewEmptyActionControls();
        ClearStyledControlsGamepadFocusClasses(controls);
        ClearFocusIfOnControls(controls);
    }

    internal void ApplyCatalogReviewRowSelection(int index, bool stealFocus = true, bool bringIntoView = true)
    {
        var rows = CatalogSyncRows.ToList();
        index = _gamepadNavigation.ClampIndex(index, rows.Count);
        _gamepadNavigation.CatalogReviewSelectedIndex = index;
        _gamepadNavigation.ActiveZone = GamepadNavigationZone.CatalogReviewList;
        ClearCatalogReviewFilterGamepadFocus();
        ClearCatalogReviewEmptyActionGamepadFocus();
        ClearCatalogReviewRowActionsGamepadFocus();
        _host.ClearFocus();
        if (index < 0 || index >= rows.Count)
            return;
        // Rows are not Focusable. Park after ClearFocusIfOnControls so the next
        // arrow does not Tab to Continue.
        _host.FocusCard(stealFocus);
        rows[index].IsGamepadFocused = true;
        if (!bringIntoView) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (_view.IsActive)
                BringCatalogReviewRowIntoView(rows[index]);
        }, DispatcherPriority.Loaded);
    }

    internal void SelectInitialCatalogReviewGamepadItem()
    {
        if (!_host.IsFocusActive)
        {
            _host.ClearFocus();
            return;
        }

        if (CatalogSyncRows.Count == 0)
        {
            var emptyActions = CollectCatalogReviewEmptyActionControls();
            if (emptyActions.Count > 0)
            {
                ApplyCatalogReviewEmptyActionSelection(0);
                return;
            }

            _host.ClearFocus();
            _gamepadNavigation.CatalogReviewSelectedIndex = -1;
            ApplyCatalogReviewFilterSelection(0);
            return;
        }

        ApplyCatalogReviewRowSelection(_gamepadNavigation.CatalogReviewSelectedIndex < 0 ? 0 : _gamepadNavigation.CatalogReviewSelectedIndex);
    }

    internal void SyncCatalogReviewGamepadSelection(bool bringIntoView = true)
    {
        if (!_host.IsFocusActive)
        {
            _host.ClearFocus();
            return;
        }

        if (!_view.IsActive || !_view.IsActive)
        {
            return;
        }

        if (CatalogSyncRows.Count == 0)
        {
            ClearCatalogReviewRowActionsGamepadFocus();
            _host.ClearFocus();
            _gamepadNavigation.CatalogReviewRowActionIndex = -1;
            var emptyActions = CollectCatalogReviewEmptyActionControls();
            if (emptyActions.Count > 0)
            {
                ApplyCatalogReviewEmptyActionSelection(0, bringIntoView: bringIntoView);
                return;
            }

            _gamepadNavigation.CatalogReviewSelectedIndex = -1;
            ApplyCatalogReviewFilterSelection(0, bringIntoView: bringIntoView);
            return;
        }

        if (!bringIntoView && _gamepadNavigation.ActiveZone == GamepadNavigationZone.CatalogReviewFilters)
        {
            ApplyCatalogReviewFilterSelection(_gamepadNavigation.CatalogReviewFilterIndex, bringIntoView: false);
            return;
        }

        var wasInRowActions = _gamepadNavigation.ActiveZone == GamepadNavigationZone.CatalogReviewRowActions;
        var actionIndex = _gamepadNavigation.CatalogReviewRowActionIndex;
        var clamped = _gamepadNavigation.ClampIndex(_gamepadNavigation.CatalogReviewSelectedIndex, CatalogSyncRows.Count);
        // After Add/Ignore/etc the row list is rebuilt. Keep the same list index (now the
        // next app) and restore either the row ring or the action-strip focus.
        if (wasInRowActions)
        {
            if (!bringIntoView)
            {
                _gamepadNavigation.CatalogReviewSelectedIndex = clamped;
                // Do not realize virtualized offscreen rows by scrolling to them.
                if (CollectCatalogReviewRowActionControls(CatalogSyncRows[clamped], realize: false).Count > 0)
                    ApplyCatalogReviewRowActionSelection(actionIndex, bringIntoView: false);
                else
                {
                    _host.ClearFocus();
                    _host.FocusCard(true);
                    _gamepadNavigation.CatalogReviewRowActionIndex = actionIndex;
                    CatalogSyncRows[clamped].IsGamepadFocused = true;
                }
                return;
            }
            _gamepadNavigation.CatalogReviewSelectedIndex = clamped;
            _host.ClearFocus();
            _host.FocusCard(true);
            CatalogSyncRows[clamped].IsGamepadFocused = true;
            EnsureCatalogReviewRowRealized(CatalogSyncRows[clamped]);
            Dispatcher.UIThread.Post(() =>
            {
                if (!_host.IsFocusActive || !_view.IsActive || CatalogSyncRows.Count == 0 ||
                    _gamepadNavigation.ActiveZone != GamepadNavigationZone.CatalogReviewRowActions)
                {
                    return;
                }

                var rowIndex = _gamepadNavigation.ClampIndex(_gamepadNavigation.CatalogReviewSelectedIndex, CatalogSyncRows.Count);
                if (rowIndex < 0)
                    return;
                EnsureCatalogReviewRowRealized(CatalogSyncRows[rowIndex]);
                var controls = CollectCatalogReviewRowActionControls(CatalogSyncRows[rowIndex]);
                if (controls.Count == 0)
                {
                    ApplyCatalogReviewRowSelection(rowIndex, bringIntoView: bringIntoView);
                    return;
                }

                var nextAction = _gamepadNavigation.ClampIndex(actionIndex < 0 ? 0 : actionIndex, controls.Count);
                ApplyCatalogReviewRowActionSelection(nextAction < 0 ? 0 : nextAction, bringIntoView: bringIntoView);
            }, DispatcherPriority.Loaded);
            return;
        }

        ApplyCatalogReviewRowSelection(clamped, bringIntoView: bringIntoView);
    }

    internal ItemsControl? GetActiveCatalogReviewItemsControl() => _settings.CatalogReviewUseGridView ? _view.CatalogReviewGridItemsControl : _view.CatalogSyncRowsItemsControl;
    internal List<(double X, double Y)> CollectCatalogReviewCardPositions(IReadOnlyList<CatalogSyncRowItem> rows)
    {
        var positions = new List<(double X, double Y)>();
        foreach (var row in rows)
        {
            var card = FindCatalogSyncRowBorder(row);
            positions.Add(GetControlCenter(card) ?? (0, positions.Count * 160));
        }

        return positions;
    }

    internal void EnsureCatalogReviewRowRealized(CatalogSyncRowItem row)
    {
        if (_settings.CatalogReviewUseGridView)
            _view.CatalogReviewGridItemsControl?.ScrollIntoView(row);
        else
            _view.CatalogSyncRowsItemsControl?.ScrollIntoView(row);
    }

    internal void BringCatalogReviewRowIntoView(CatalogSyncRowItem row)
    {
        EnsureCatalogReviewRowRealized(row);
        FindCatalogSyncRowBorder(row)?.BringIntoView();
    }

    internal Border? FindCatalogSyncRowBorder(CatalogSyncRowItem row)
    {
        var host = GetActiveCatalogReviewItemsControl();
        if (host == null)
            return null;
        return host.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Classes.Contains("catalog-focus-card") && ReferenceEquals(b.DataContext, row));
    }

    internal void ActivateReviewRowSelection()
    {
        if (CatalogSyncRows.Count == 0)
        {
            ActivateCatalogReviewEmptyActionSelection();
            return;
        }

        var rows = CatalogSyncRows.ToList();
        var index = _gamepadNavigation.ClampIndex(_gamepadNavigation.CatalogReviewSelectedIndex, rows.Count);
        if (index < 0 || index >= rows.Count)
            return;
        var controls = CollectCatalogReviewRowActionControls(rows[index]);
        if (controls.Count == 0)
        {
            DeferCatalogRowInput(rows[index], ActivateReviewRowSelection);
            return;
        }
        // Enter the action strip; do not fire Add/Ignore/etc until Confirm again.
        ApplyCatalogReviewRowActionSelection(0);
    }

    internal void ActivateCatalogReviewEmptyActionSelection()
    {
        var controls = CollectCatalogReviewEmptyActionControls();
        var index = _gamepadNavigation.ClampIndex(_gamepadNavigation.CatalogReviewSelectedIndex, controls.Count);
        if (index < 0 || index >= controls.Count)
            return;
        if (controls[index] is Button button)
            GamepadControlActivation.ActivateButton(button);
    }

    internal void ActivateCatalogReviewRowActionSelection()
    {
        var rows = CatalogSyncRows.ToList();
        if (rows.Count == 0)
            return;
        var rowIndex = _gamepadNavigation.ClampIndex(_gamepadNavigation.CatalogReviewSelectedIndex, rows.Count);
        if (rowIndex < 0 || rowIndex >= rows.Count)
            return;
        var controls = CollectCatalogReviewRowActionControls(rows[rowIndex]);
        if (controls.Count == 0)
        {
            DeferCatalogRowInput(rows[rowIndex], ActivateCatalogReviewRowActionSelection);
            return;
        }
        var index = ResolveMobileActionFocus(controls,
            _gamepadNavigation.ClampIndex(_gamepadNavigation.CatalogReviewRowActionIndex, controls.Count));
        _gamepadNavigation.CatalogReviewRowActionIndex = index;
        if (index < 0 || index >= controls.Count)
            return;
        if (controls[index] is Button button)
            GamepadControlActivation.ActivateButton(button);
    }

    private void DeferCatalogRowInput(CatalogSyncRowItem row, Action input)
    {
        // ScrollIntoView may create a grid container whose template is not ready yet.
        // Finish this explicit input after layout, only if the selection is still current.
        var zone = _gamepadNavigation.ActiveZone;
        var index = _gamepadNavigation.CatalogReviewSelectedIndex;
        Dispatcher.UIThread.Post(() =>
        {
            if (!_host.IsFocusActive || !_view.IsActive || _gamepadNavigation.ActiveZone != zone ||
                _gamepadNavigation.CatalogReviewSelectedIndex != index || index < 0 ||
                index >= CatalogSyncRows.Count || !ReferenceEquals(CatalogSyncRows[index], row)) return;
            if (CollectCatalogReviewRowActionControls(row, realize: false).Count > 0)
                input();
        }, DispatcherPriority.Loaded);
    }

    internal static int ResolveMobileActionFocus(IReadOnlyList<Control> controls, int fallback)
    {
        // An adaptive strip can move focus as its actions change or overflow.
        // Its current focused action wins over the index saved before layout.
        for (var i = 0; i < controls.Count; i++)
            if (controls[i].IsFocused && controls[i].GetVisualAncestors().OfType<MobileActionRow>().Any()) return i;
        return fallback;
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

    internal void ActivateReviewFilterSelection()
    {
        var controls = CollectCatalogReviewFilterControls();
        var index = _gamepadNavigation.ClampIndex(_gamepadNavigation.CatalogReviewFilterIndex, controls.Count);
        if (index < 0 || index >= controls.Count)
            return;
        var control = controls[index];
        if (control is ComboBox comboBox)
        {
            comboBox.Focus();
            if (!comboBox.IsDropDownOpen)
                GamepadComboBoxNavigation.Open(comboBox);
            return;
        }

        if (control is Button button)
            GamepadControlActivation.ActivateButton(button);
        else if (control is TextBox textBox)
            GamepadControlActivation.ActivateTextBox(textBox);
    }

    public bool SynchronizePointer(object? source)
    {
        if (GamepadPointerFocusSync.Hit(_gamepadNavigation, CollectCatalogReviewFilterControls(), GamepadNavigationZone.CatalogReviewFilters, _gamepadNavigation.CatalogReviewFilterIndex, ApplyCatalogReviewFilterSelection, source) || GamepadPointerFocusSync.Hit(_gamepadNavigation, CollectCatalogReviewEmptyActionControls(), GamepadNavigationZone.CatalogReviewList, _gamepadNavigation.CatalogReviewSelectedIndex, ApplyCatalogReviewEmptyActionSelection, source))
        {
            return true;
        }

        var rows = CatalogSyncRows.ToList();
        var rowIndex = GamepadPointerFocusSync.IndexOfDataContext(rows, source as Visual);
        if (rowIndex >= 0)
        {
            var actions = CollectCatalogReviewRowActionControls(rows[rowIndex]);
            var actionIndex = GamepadControlActivation.IndexOfControlContainingFocus(actions, source);
            if (actionIndex >= 0)
            {
                if (_gamepadNavigation.ActiveZone == GamepadNavigationZone.CatalogReviewRowActions && _gamepadNavigation.CatalogReviewSelectedIndex == rowIndex && _gamepadNavigation.CatalogReviewRowActionIndex == actionIndex)
                {
                    return true;
                }

                GamepadTextInput.Reset();
                _gamepadNavigation.CatalogReviewSelectedIndex = rowIndex;
                ApplyCatalogReviewRowActionSelection(actionIndex);
                return true;
            }

            return GamepadPointerFocusSync.Card(_gamepadNavigation, rows, GamepadNavigationZone.CatalogReviewList, _gamepadNavigation.CatalogReviewSelectedIndex, index => ApplyCatalogReviewRowSelection(index, stealFocus: false), source);
        }

        return false;
    }

    public void LeaveZone(GamepadNavigationZone nextZone)
    {
        var zone = _gamepadNavigation.ActiveZone;
        if (zone == nextZone)
            return;
        if (zone == GamepadNavigationZone.CatalogReviewFilters)
            ClearCatalogReviewFilterGamepadFocus();
        if (zone == GamepadNavigationZone.CatalogReviewRowActions)
            ClearCatalogReviewRowActionsGamepadFocus();
    }

    public bool EnterZone(GamepadZoneTransition transition)
    {
        switch (transition.Zone)
        {
            case GamepadNavigationZone.CatalogReviewRowActions:
                ApplyCatalogReviewRowActionSelection(transition.SelectedIndex ?? (_gamepadNavigation.CatalogReviewRowActionIndex < 0 ? 0 : _gamepadNavigation.CatalogReviewRowActionIndex));
                return true;
            case GamepadNavigationZone.CatalogReviewFilters:
                ApplyCatalogReviewFilterSelection(transition.SelectedIndex ?? GetCatalogReviewFilterIndexFromList());
                return true;
            case GamepadNavigationZone.CatalogReviewList:
                if (CatalogSyncRows.Count == 0)
                {
                    var emptyActions = CollectCatalogReviewEmptyActionControls();
                    if (emptyActions.Count > 0)
                    {
                        ApplyCatalogReviewEmptyActionSelection(transition.SelectedIndex ?? 0);
                        return true;
                    }

                    return NavigateToCatalogReviewFiltersFromList() || true;
                }

                ApplyCatalogReviewRowSelection(transition.SelectedIndex ?? 0);
                return true;
            default:
                return false;
        }
    }
}
