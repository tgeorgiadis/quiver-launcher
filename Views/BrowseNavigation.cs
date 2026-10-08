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
    // The grid's cards: the games a search matched (a row above), then the apps.
    private List<IBrowseCard> Cards => [.. view.Model.Games, .. view.Model.Items];
    internal int CardIndexOf(IBrowseCard card) => Cards.IndexOf(card);
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
                var cards = Cards;
                var index = Service.ClampIndex(CardIndex, cards.Count);
                if (index < 0) return false;
                if (cards[index] is BrowseGame game) view.OpenGame(game);
                else view.OpenDetails((BrowseItem)cards[index]);
                return true;
        }
    }

    public bool Cancel()
    {
        if (!isActive()) return false;
        if (Service.ActiveZone is not (GamepadNavigationZone.BrowseToolbar or GamepadNavigationZone.BrowseFilters) || Cards.Count == 0)
            return false;
        SelectCard(CardIndex < 0 ? 0 : CardIndex);
        return true;
    }

    /// <summary>
    /// Y (Options) on a highlighted card adds its app to the library, like the card's Add button; for an app already in the
    /// library it shows the app there (Open in Library).
    /// </summary>
    public bool Options()
    {
        if (!isActive() || Service.ActiveZone != GamepadNavigationZone.BrowseGrid) return false;
        var cards = Cards;
        var index = Service.ClampIndex(CardIndex, cards.Count);
        if (index < 0 || cards[index] is not BrowseItem item) return false;
        if (item.CanAdd) view.RequestAdd(item);
        else if (item.InLibrary) view.RequestOpenInLibrary(item);
        else return false;
        return true;
    }
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
        _selectFirstCardWhenLoaded = Cards.Count == 0;
        if (Cards.Count == 0) ApplyFilterSelection(0, bringIntoView);
        else SelectCard(CardIndex < 0 ? 0 : CardIndex, bringIntoView: bringIntoView);
    }

    /// <summary>Keeps the highlight on a card that still exists after the cards change.</summary>
    internal void SyncSelection()
    {
        if (!host.IsFocusActive || !isActive()) return;
        if (_selectFirstCardWhenLoaded && Cards.Count > 0 && Service.ActiveZone == GamepadNavigationZone.BrowseFilters && !GamepadTextInput.IsEditing)
        {
            _selectFirstCardWhenLoaded = false;
            SelectCard(0);
            return;
        }
        if (Service.ActiveZone != GamepadNavigationZone.BrowseGrid) return;
        if (Cards.Count == 0)
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

    // "Hide apps in my library" belongs to whichever line it sits on: the title line beside the sort, or its own under the filters.
    internal List<Control> ToolbarControls() => view.HideOptionInTitle
        ? Visible(view.BrowseCatalogTabButton, view.BrowseCustomListTabButton, view.BrowseHideLibraryCheckBox, view.BrowseSortComboBox)
        : Visible(view.BrowseCatalogTabButton, view.BrowseCustomListTabButton, view.BrowseSortComboBox);

    internal List<Control> FilterControls() => view.HideOptionInTitle
        ? Visible(view.BrowseSearchTextBox, view.BrowseTypeComboBox, view.BrowsePlatformComboBox,
            view.BrowseConsoleComboBox, view.BrowseAiComboBox, view.BrowseRetryButton, view.BrowseClearFiltersButton)
        : Visible(view.BrowseSearchTextBox, view.BrowseTypeComboBox, view.BrowsePlatformComboBox,
            view.BrowseConsoleComboBox, view.BrowseAiComboBox, view.BrowseHideLibraryCheckBox, view.BrowseRetryButton, view.BrowseClearFiltersButton);

    private static List<Control> Visible(params Control[] controls) => controls.Where(c => c.IsVisible && c.IsEnabled).ToList();

    private bool MoveInRow(NavigationDirection direction, GamepadNavigationZone zone, List<Control> controls, int index, Action<int> apply)
    {
        if (controls.Count == 0)
            return direction == NavigationDirection.Down && zone == GamepadNavigationZone.BrowseToolbar
                ? host.ApplyTransition(new GamepadZoneTransition(GamepadNavigationZone.BrowseFilters, null))
                : false;
        // The controls can sit on more than one line (the filters under the search, the library option below them): Up and Down move between those first.
        if (NearestOnNextLine(controls, index, direction) is { } line)
        {
            apply(line);
            return true;
        }
        // While searching with no list tabs the title row has nothing to select: Up goes to the top bar.
        if (zone == GamepadNavigationZone.BrowseFilters && direction == NavigationDirection.Up && ToolbarControls().Count == 0)
            return host.ApplyTransition(new GamepadZoneTransition(GamepadNavigationZone.TopBar, null));
        var transition = Service.TryGetZoneTransition(direction, zone, host.MainContentZone, isListLayout: true, positions: null, index, Cards.Count);
        if (transition.HasValue)
            return host.ApplyTransition(transition.Value);
        if (direction is not (NavigationDirection.Left or NavigationDirection.Right))
            return true;
        apply(Service.MoveHorizontalIndex(index, direction, controls.Count));
        return true;
    }

    /// <summary>The control on the nearest line above or below, closest across; null on the first or last line.</summary>
    private int? NearestOnNextLine(List<Control> controls, int index, NavigationDirection direction) =>
        index >= 0 && index < controls.Count && Centre(controls[index]) is { } from
            ? NearestOnNextLine(controls, from, controls[index].Bounds.Height / 2, direction)
            : null;

    private Point? Centre(Control c) => c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), view);

    private int? NearestOnNextLine(List<Control> controls, Point from, double lineGap, NavigationDirection direction)
    {
        if (direction is not (NavigationDirection.Up or NavigationDirection.Down)) return null;
        int? best = null;
        double bestDy = double.MaxValue, bestDx = double.MaxValue;
        for (var i = 0; i < controls.Count; i++)
        {
            if (Centre(controls[i]) is not { } to) continue;
            var dy = direction == NavigationDirection.Down ? to.Y - from.Y : from.Y - to.Y;
            if (dy < lineGap) continue;
            var dx = Math.Abs(to.X - from.X);
            if (dy < bestDy - 1 || (Math.Abs(dy - bestDy) <= 1 && dx < bestDx))
                (best, bestDy, bestDx) = (i, dy, dx);
        }
        return best;
    }

    private bool MoveInGrid(NavigationDirection direction)
    {
        var current = CardIndex;
        var positions = CardPositions();
        var count = Cards.Count;
        var transition = Service.TryGetZoneTransition(direction, GamepadNavigationZone.BrowseGrid, host.MainContentZone, isListLayout: false, positions, current, count);
        if (transition.HasValue)
        {
            // Up from the cards goes to the control just above the card.
            if (transition.Value.Zone == GamepadNavigationZone.BrowseFilters && current >= 0 && current < count &&
                NearestOnNextLine(FilterControls(), new Point(positions[current].X, positions[current].Y), 1, NavigationDirection.Up) is { } above)
                FilterIndex = above;
            return host.ApplyTransition(transition.Value);
        }
        if (count == 0) return false;
        var next = Service.MoveCatalogIndex(current, direction, count, positions);
        if (next == current && direction is NavigationDirection.Left or NavigationDirection.Up)
        {
            var blocked = Service.TryGetBlockedMoveZoneTransition(direction, GamepadNavigationZone.BrowseGrid, host.MainContentZone, isListLayout: false, current, count);
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
            SelectCard(Service.MoveCatalogIndex(from, NavigationDirection.Down, Cards.Count, CardPositions()));
        }
    }

    private static void Activate(List<Control> controls, int index)
    {
        if (index < 0 || index >= controls.Count) return;
        switch (controls[index])
        {
            case ComboBox combo: GamepadComboBoxNavigation.Open(combo); break;
            case CheckBox check: check.IsChecked = check.IsChecked != true; break;
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
        var cards = Cards;
        if (cards.Count == 0)
        {
            ApplyFilterSelection(Math.Max(0, FilterIndex), bringIntoView);
            return;
        }
        index = Service.ClampIndex(index, cards.Count);
        Service.ActiveZone = GamepadNavigationZone.BrowseGrid;
        host.ClearSidebarFocus();
        host.ClearFocus();
        host.FocusCard(stealFocus);
        CardIndex = index;
        var card = cards[index];
        card.IsGamepadFocused = true;
        if (!bringIntoView) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (isActive() && Cards.Contains(card))
                FindCard(card)?.BringIntoView();
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
        foreach (var card in Cards)
            card.IsGamepadFocused = false;
        // Every control, including hidden ones, so one that reappears doesn't still look highlighted.
        Control[] controls = [view.BrowseCatalogTabButton, view.BrowseCustomListTabButton, view.BrowseSortComboBox, view.BrowseSearchTextBox,
            view.BrowseTypeComboBox, view.BrowsePlatformComboBox, view.BrowseConsoleComboBox, view.BrowseAiComboBox,
            view.BrowseHideLibraryCheckBox, view.BrowseRetryButton, view.BrowseClearFiltersButton];
        foreach (var control in controls)
            control.Classes.Set("gamepad-focused", false);
        var focus = TopLevel.GetTopLevel(view)?.FocusManager;
        if (!GamepadTextInput.IsEditing && focus?.GetFocusedElement() is Control focused && controls.Contains(focused))
            focus.Focus(null);
    }

    private List<(double X, double Y)> CardPositions()
    {
        var positions = new List<(double X, double Y)>();
        foreach (var item in Cards)
        {
            var card = FindCard(item);
            var topLeft = card?.TranslatePoint(new Point(0, 0), view);
            positions.Add(topLeft is { } p ? (p.X + card!.Bounds.Width / 2, p.Y + card.Bounds.Height / 2) : (0, positions.Count * 300));
        }
        return positions;
    }

    private Border? FindCard(IBrowseCard card) =>
        (card is BrowseGame ? view.BrowseGamesControl : view.BrowseItemsControl)
            .GetVisualDescendants().OfType<Border>().FirstOrDefault(b => ReferenceEquals(b.DataContext, card));

    public bool SynchronizePointer(object? source)
    {
        if (GamepadPointerFocusSync.Hit(Service, ToolbarControls(), GamepadNavigationZone.BrowseToolbar, ToolbarIndex, ApplyToolbarSelection, source) ||
            GamepadPointerFocusSync.Hit(Service, FilterControls(), GamepadNavigationZone.BrowseFilters, FilterIndex, ApplyFilterSelection, source))
            return true;
        return GamepadPointerFocusSync.Card(Service, Cards, GamepadNavigationZone.BrowseGrid, CardIndex, i => SelectCard(i, stealFocus: false), source);
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
                if (Cards.Count == 0) ApplyFilterSelection(Math.Max(0, FilterIndex));
                else SelectCard(transition.SelectedIndex ?? Math.Max(0, CardIndex));
                return true;
            default:
                return false;
        }
    }
}
