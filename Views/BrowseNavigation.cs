using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using QuiverLauncher.Services;
using QuiverLauncher.ViewModels;

namespace QuiverLauncher.Views;

/// <summary>
/// Controller and keyboard movement on the App Catalog: the title row (list tabs and sort), the
/// search and filter row, then the cards.
/// Moving down past the last row of cards loads the next page.
/// </summary>
public sealed class BrowseNavigation(BrowseView view, IFeatureNavigationHost host, Func<bool> isActive) : IFeatureNavigationHandler
{
    private GamepadNavigationService Service => host.Navigation;
    private IList<BrowseItem> Items => view.Model.Items;
    internal int ToolbarIndex { get; private set; } = -1;
    internal int FilterIndex { get; private set; } = -1;
    internal int CardIndex { get; private set; } = -1;
    // Browse opened before its cards arrived: move to the first card once they do, unless the player moved.
    private bool _selectFirstCardWhenLoaded;

    public bool Navigate(NavigationDirection direction)
    {
        _selectFirstCardWhenLoaded = false;
        return Service.ActiveZone switch
        {
            GamepadNavigationZone.BrowseToolbar => MoveInRow(direction, GamepadNavigationZone.BrowseToolbar, ToolbarControls(), ToolbarIndex, ApplyToolbarSelection),
            GamepadNavigationZone.BrowseFilters => MoveInRow(direction, GamepadNavigationZone.BrowseFilters, FilterControls(), FilterIndex, ApplyFilterSelection),
            _ => MoveInGrid(direction),
        };
    }

    public bool Confirm()
    {
        // A zone left over from before Browse was shown must not open an app.
        if (!isActive()) return false;
        _selectFirstCardWhenLoaded = false;
        switch (Service.ActiveZone)
        {
            case GamepadNavigationZone.BrowseToolbar:
                Activate(ToolbarControls(), ToolbarIndex);
                return true;
            case GamepadNavigationZone.BrowseFilters:
                Activate(FilterControls(), FilterIndex);
                return true;
            default:
                var index = Service.ClampIndex(CardIndex, Items.Count);
                if (index < 0) return false;
                view.OpenDetails(Items[index]);
                return true;
        }
    }

    public bool Cancel()
    {
        if (!isActive()) return false;
        if (Service.ActiveZone is not (GamepadNavigationZone.BrowseToolbar or GamepadNavigationZone.BrowseFilters) || Items.Count == 0)
            return false;
        SelectCard(CardIndex < 0 ? 0 : CardIndex);
        return true;
    }

    public bool Options() => false;
    public void RestoreFocus() => RestoreFocus(bringIntoView: true);
    public void RestoreFocus(bool bringIntoView)
    {
        if (!host.IsFocusActive || !isActive()) return;
        switch (Service.ActiveZone)
        {
            case GamepadNavigationZone.BrowseToolbar: ApplyToolbarSelection(Math.Max(0, ToolbarIndex), bringIntoView); break;
            case GamepadNavigationZone.BrowseFilters: ApplyFilterSelection(Math.Max(0, FilterIndex), bringIntoView); break;
            default: SelectInitial(bringIntoView); break;
        }
    }

    /// <summary>Highlights the first card, or the search box while there are none.</summary>
    public void SelectInitial(bool bringIntoView = true)
    {
        if (!host.IsFocusActive)
        {
            host.ClearFocus();
            return;
        }
        _selectFirstCardWhenLoaded = Items.Count == 0;
        if (Items.Count == 0) ApplyFilterSelection(0, bringIntoView);
        else SelectCard(CardIndex < 0 ? 0 : CardIndex, bringIntoView: bringIntoView);
    }

    /// <summary>Keeps the highlight on a card that still exists after the cards change.</summary>
    internal void SyncSelection()
    {
        if (!host.IsFocusActive || !isActive()) return;
        if (_selectFirstCardWhenLoaded && Items.Count > 0 && Service.ActiveZone == GamepadNavigationZone.BrowseFilters && !GamepadTextInput.IsEditing)
        {
            _selectFirstCardWhenLoaded = false;
            SelectCard(0);
            return;
        }
        if (Service.ActiveZone != GamepadNavigationZone.BrowseGrid) return;
        if (Items.Count == 0)
        {
            CardIndex = -1;
            // Searching or filtering away every card keeps the player on the controls they used.
            ApplyFilterSelection(Math.Max(0, FilterIndex));
            return;
        }
        SelectCard(CardIndex < 0 ? 0 : CardIndex, bringIntoView: false);
    }

    /// <summary>"Try again" hides while it loads: keep the search box highlighted, then move to the cards.</summary>
    internal void AfterRetry()
    {
        if (Service.ActiveZone != GamepadNavigationZone.BrowseFilters || !host.IsFocusActive) return;
        _selectFirstCardWhenLoaded = true;
        Dispatcher.UIThread.Post(() => ApplyFilterSelection(0), DispatcherPriority.Loaded);
    }

    /// <summary>"Clear filters" disappears once used; stay on the filter row.</summary>
    internal void KeepFilterFocusAfterClear()
    {
        if (Service.ActiveZone == GamepadNavigationZone.BrowseFilters && host.IsFocusActive)
            Dispatcher.UIThread.Post(() => ApplyFilterSelection(Math.Max(0, FilterIndex - 1)), DispatcherPriority.Loaded);
    }

    internal List<Control> ToolbarControls() => Visible(view.BrowseCatalogTabButton, view.BrowseCustomListTabButton, view.BrowseSortComboBox);

    internal List<Control> FilterControls() => Visible(view.BrowseSearchTextBox, view.BrowseTypeComboBox, view.BrowsePlatformComboBox,
        view.BrowseConsoleComboBox, view.BrowseAiComboBox, view.BrowseRetryButton, view.BrowseClearFiltersButton);

    private static List<Control> Visible(params Control[] controls) => controls.Where(c => c.IsVisible && c.IsEnabled).ToList();

    private bool MoveInRow(NavigationDirection direction, GamepadNavigationZone zone, List<Control> controls, int index, Action<int> apply)
    {
        if (controls.Count == 0)
            return direction == NavigationDirection.Down && zone == GamepadNavigationZone.BrowseToolbar
                ? host.ApplyTransition(new GamepadZoneTransition(GamepadNavigationZone.BrowseFilters, null))
                : false;
        // While searching with no list tabs the title row has nothing to select: Up goes to the top bar.
        if (zone == GamepadNavigationZone.BrowseFilters && direction == NavigationDirection.Up && ToolbarControls().Count == 0)
            return host.ApplyTransition(new GamepadZoneTransition(GamepadNavigationZone.TopBar, null));
        var transition = Service.TryGetZoneTransition(direction, zone, host.MainContentZone, isListLayout: true, positions: null, index, Items.Count);
        if (transition.HasValue)
            return host.ApplyTransition(transition.Value);
        if (direction is not (NavigationDirection.Left or NavigationDirection.Right))
            return true;
        apply(Service.MoveHorizontalIndex(index, direction, controls.Count));
        return true;
    }

    private bool MoveInGrid(NavigationDirection direction)
    {
        var current = CardIndex;
        var positions = CardPositions();
        var transition = Service.TryGetZoneTransition(direction, GamepadNavigationZone.BrowseGrid, host.MainContentZone, isListLayout: false, positions, current, Items.Count);
        if (transition.HasValue)
            return host.ApplyTransition(transition.Value);
        if (Items.Count == 0) return false;
        var next = Service.MoveCatalogIndex(current, direction, Items.Count, positions);
        if (next == current && direction is NavigationDirection.Left or NavigationDirection.Up)
        {
            var blocked = Service.TryGetBlockedMoveZoneTransition(direction, GamepadNavigationZone.BrowseGrid, host.MainContentZone, isListLayout: false, current, Items.Count);
            if (blocked.HasValue)
                return host.ApplyTransition(blocked.Value);
        }
        if (next == current && direction == NavigationDirection.Down && view.Model.CanLoadMore)
            LoadMoreThenMoveDown(current);
        SelectCard(next);
        return true;
    }

    private void LoadMoreThenMoveDown(int from)
    {
        _ = LoadAsync();
        async Task LoadAsync()
        {
            if (!await view.LoadMoreAsync()) return;
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Loaded);
            if (!isActive() || Service.ActiveZone != GamepadNavigationZone.BrowseGrid || CardIndex != from) return;
            SelectCard(Service.MoveCatalogIndex(from, NavigationDirection.Down, Items.Count, CardPositions()));
        }
    }

    private static void Activate(List<Control> controls, int index)
    {
        if (index < 0 || index >= controls.Count) return;
        switch (controls[index])
        {
            case ComboBox combo: GamepadComboBoxNavigation.Open(combo); break;
            case TextBox text: GamepadControlActivation.ActivateTextBox(text); break;
            case Button button: GamepadControlActivation.ActivateButton(button); break;
        }
    }

    internal void ApplyToolbarSelection(int index) => ApplyToolbarSelection(index, bringIntoView: true);
    private void ApplyToolbarSelection(int index, bool bringIntoView)
    {
        var controls = ToolbarControls();
        // The sort hides while searching; with no list tabs either, the search row takes over.
        if (controls.Count == 0) ApplyFilterSelection(Math.Max(0, FilterIndex), bringIntoView);
        else ToolbarIndex = ApplyRowSelection(GamepadNavigationZone.BrowseToolbar, controls, index, bringIntoView);
    }

    internal void ApplyFilterSelection(int index) => ApplyFilterSelection(index, bringIntoView: true);
    private void ApplyFilterSelection(int index, bool bringIntoView)
    {
        var controls = FilterControls();
        FilterIndex = ApplyRowSelection(GamepadNavigationZone.BrowseFilters, controls, index, bringIntoView);
    }

    private int ApplyRowSelection(GamepadNavigationZone zone, List<Control> controls, int index, bool bringIntoView)
    {
        index = Service.ClampIndex(index, controls.Count);
        Service.ActiveZone = zone;
        host.ClearFocus();
        if (index < 0) return index;
        var control = controls[index];
        control.Classes.Set("gamepad-focused", true);
        if (!bringIntoView)
        {
            // Confirm uses the saved index; native focus must not reveal this control.
            host.FocusCard(true);
            return index;
        }
        // A text box is only highlighted until confirmed, so Steam's keyboard doesn't open.
        GamepadControlActivation.ApplyGamepadHighlightFocus(control);
        Dispatcher.UIThread.Post(() =>
        {
            if (isActive()) control.BringIntoView();
        }, DispatcherPriority.Loaded);
        return index;
    }

    internal void SelectCard(int index, bool stealFocus = true, bool bringIntoView = true)
    {
        if (Items.Count == 0)
        {
            ApplyFilterSelection(Math.Max(0, FilterIndex), bringIntoView);
            return;
        }
        index = Service.ClampIndex(index, Items.Count);
        Service.ActiveZone = GamepadNavigationZone.BrowseGrid;
        host.ClearSidebarFocus();
        host.ClearFocus();
        host.FocusCard(stealFocus);
        CardIndex = index;
        var item = Items[index];
        item.IsGamepadFocused = true;
        if (!bringIntoView) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (isActive() && Items.Contains(item))
                FindCard(item)?.BringIntoView();
        }, DispatcherPriority.Loaded);
    }

    /// <summary>A clicked card becomes the highlighted one, so the controller continues from it.</summary>
    internal void TrackPointerCard(int index)
    {
        if (host.IsFocusActive && index >= 0)
            SelectCard(index, stealFocus: false, bringIntoView: false);
    }

    /// <summary>Removes every Browse highlight; the shell calls this before highlighting anything.</summary>
    internal void ClearHighlights()
    {
        foreach (var item in Items)
            item.IsGamepadFocused = false;
        // Every control, including hidden ones, so one that reappears doesn't still look highlighted.
        Control[] controls = [view.BrowseCatalogTabButton, view.BrowseCustomListTabButton, view.BrowseSortComboBox, view.BrowseSearchTextBox,
            view.BrowseTypeComboBox, view.BrowsePlatformComboBox, view.BrowseConsoleComboBox, view.BrowseAiComboBox,
            view.BrowseRetryButton, view.BrowseClearFiltersButton];
        foreach (var control in controls)
            control.Classes.Set("gamepad-focused", false);
        var focus = TopLevel.GetTopLevel(view)?.FocusManager;
        if (!GamepadTextInput.IsEditing && focus?.GetFocusedElement() is Control focused && controls.Contains(focused))
            focus.Focus(null);
    }

    private List<(double X, double Y)> CardPositions()
    {
        var positions = new List<(double X, double Y)>();
        foreach (var item in Items)
        {
            var card = FindCard(item);
            var topLeft = card?.TranslatePoint(new Point(0, 0), view);
            positions.Add(topLeft is { } p ? (p.X + card!.Bounds.Width / 2, p.Y + card.Bounds.Height / 2) : (0, positions.Count * 300));
        }
        return positions;
    }

    private Border? FindCard(BrowseItem item) =>
        view.BrowseItemsControl.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => ReferenceEquals(b.DataContext, item));

    public bool SynchronizePointer(object? source)
    {
        if (GamepadPointerFocusSync.Hit(Service, ToolbarControls(), GamepadNavigationZone.BrowseToolbar, ToolbarIndex, ApplyToolbarSelection, source) ||
            GamepadPointerFocusSync.Hit(Service, FilterControls(), GamepadNavigationZone.BrowseFilters, FilterIndex, ApplyFilterSelection, source))
            return true;
        return GamepadPointerFocusSync.Card(Service, Items.ToList(), GamepadNavigationZone.BrowseGrid, CardIndex, i => SelectCard(i, stealFocus: false), source);
    }

    public void LeaveZone(GamepadNavigationZone nextZone)
    {
        if (nextZone is not (GamepadNavigationZone.BrowseToolbar or GamepadNavigationZone.BrowseFilters or GamepadNavigationZone.BrowseGrid))
            ClearHighlights();
    }

    public bool EnterZone(GamepadZoneTransition transition)
    {
        switch (transition.Zone)
        {
            case GamepadNavigationZone.BrowseToolbar:
                ApplyToolbarSelection(Math.Max(0, ToolbarIndex));
                return true;
            case GamepadNavigationZone.BrowseFilters:
                ApplyFilterSelection(Math.Max(0, FilterIndex));
                return true;
            case GamepadNavigationZone.BrowseGrid:
                if (Items.Count == 0) ApplyFilterSelection(Math.Max(0, FilterIndex));
                else SelectCard(transition.SelectedIndex ?? Math.Max(0, CardIndex));
                return true;
            default:
                return false;
        }
    }
}
