using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using QuiverLauncher.Services;
using QuiverLauncher.ViewModels;
using NavigationDirection = QuiverLauncher.Services.NavigationDirection;

namespace QuiverLauncher.Views;
/// <summary>Owns navigation among the sidebar, top bar, and shared banners.</summary>
public sealed class ShellChromeNavigation : IFeatureNavigationHandler
{
    private readonly UserControl _root;
    private readonly LauncherSession _session;
    private readonly IFeatureNavigationHost _host;
    private readonly LibraryToolbarView _toolbar;
    private readonly LauncherBannerView _banners;
    private readonly ShellViewModel Shell;
    private readonly Func<bool> _isSearchOpen;
    private readonly Action _wireEdges;
    private GamepadNavigationService _gamepadNavigation => _host.Navigation;

    private readonly Border SidebarPanel;
    private readonly Button ContinueButton;
    private readonly Button LibraryNavButton;
    private readonly Button AppCatalogNavButton;
    private readonly LibraryFiltersView LibraryFiltersPanel;
    private readonly Button GitHubFooterButton;
    private readonly Button DiscordFooterButton;
    private readonly Button KofiFooterButton;
    private readonly Button MobileSearchToggleButton;
    private readonly Button MobileAddButton;
    private readonly Button MobileSortButton;
    private readonly Button MobileCatalogBackButton;
    private readonly Button MobileCatalogSortButton;
    private readonly Button CatalogReviewBackButton;
    private readonly Button CheckForUpdatesButton;
    private readonly Button SettingsButton;
    private readonly Button MinimizeButton;
    private readonly Button ToggleMaximizeButton;
    private readonly Button CloseLauncherButton;
    private readonly Grid MobileSecondaryTopBar;
    private readonly Grid DesktopInlineTopBar;
    private readonly Grid HeaderLayoutGrid;
    public ShellChromeNavigation(UserControl root, LauncherSession session, IFeatureNavigationHost host, ShellViewModel shell, Func<bool> isSearchOpen, Action wireEdges)
    {
        _root = root;
        _session = session;
        _host = host;
        _toolbar = root.FindControl<LibraryToolbarView>("LibraryToolbar")!;
        _banners = root.FindControl<LauncherBannerView>("Banners")!;
        Shell = shell;
        _isSearchOpen = isSearchOpen;
        _wireEdges = wireEdges;
        SidebarPanel = root.FindControl<Border>("SidebarPanel")!;
        ContinueButton = root.FindControl<Button>("ContinueButton")!;
        LibraryNavButton = root.FindControl<Button>("LibraryNavButton")!;
        AppCatalogNavButton = root.FindControl<Button>("AppCatalogNavButton")!;
        LibraryFiltersPanel = root.FindControl<LibraryFiltersView>("LibraryFiltersPanel")!;
        GitHubFooterButton = root.FindControl<Button>("GitHubFooterButton")!;
        DiscordFooterButton = root.FindControl<Button>("DiscordFooterButton")!;
        KofiFooterButton = root.FindControl<Button>("KofiFooterButton")!;
        MobileSearchToggleButton = root.FindControl<Button>("MobileSearchToggleButton")!;
        MobileAddButton = root.FindControl<Button>("MobileAddButton")!;
        MobileSortButton = root.FindControl<Button>("MobileSortButton")!;
        MobileCatalogBackButton = root.FindControl<Button>("MobileCatalogBackButton")!;
        MobileCatalogSortButton = root.FindControl<Button>("MobileCatalogSortButton")!;
        CatalogReviewBackButton = root.FindControl<Button>("CatalogReviewBackButton")!;
        CheckForUpdatesButton = root.FindControl<Button>("CheckForUpdatesButton")!;
        SettingsButton = root.FindControl<Button>("SettingsButton")!;
        MinimizeButton = root.FindControl<Button>("MinimizeButton")!;
        ToggleMaximizeButton = root.FindControl<Button>("ToggleMaximizeButton")!;
        CloseLauncherButton = root.FindControl<Button>("CloseLauncherButton")!;
        MobileSecondaryTopBar = root.FindControl<Grid>("MobileSecondaryTopBar")!;
        DesktopInlineTopBar = root.FindControl<Grid>("DesktopInlineTopBar")!;
        HeaderLayoutGrid = root.FindControl<Grid>("HeaderLayoutGrid")!;
    }

    private void Post(Action action, DispatcherPriority priority)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!_session.IsClosed)
                action();
        }, priority);
    }

    public bool Navigate(NavigationDirection direction) => !_session.IsClosed && (_gamepadNavigation.ActiveZone switch
    {
        GamepadNavigationZone.Sidebar => HandleSidebarGamepadNavigation(direction),
        GamepadNavigationZone.TopBar => HandleTopBarGamepadNavigation(direction),
        GamepadNavigationZone.AnnouncementBanner => _banners.HandleAnnouncementBannerGamepadNavigation(direction),
        _ => false,
    });
    public bool Confirm()
    {
        if (_session.IsClosed)
            return false;
        switch (_gamepadNavigation.ActiveZone)
        {
            case GamepadNavigationZone.Sidebar:
                ActivateSidebarSelection();
                return true;
            case GamepadNavigationZone.TopBar:
                ActivateTopBarSelection();
                return true;
            case GamepadNavigationZone.AnnouncementBanner:
                _banners.ActivateTopBannerGamepadSelection();
                return true;
            default:
                return false;
        }
    }

    public bool Cancel() => false;
    public bool Options() => !_session.IsClosed && _gamepadNavigation.ActiveZone == GamepadNavigationZone.Sidebar && TryOpenFocusedDisplayFilterOverflowMenu();
    public void RestoreFocus()
    {
        if (_session.IsClosed)
            return;
        switch (_gamepadNavigation.ActiveZone)
        {
            case GamepadNavigationZone.Sidebar:
                ApplySidebarGamepadSelection(Math.Max(0, _gamepadNavigation.SidebarSelectedIndex));
                break;
            case GamepadNavigationZone.TopBar:
                ApplyTopBarGamepadSelection(Math.Max(0, _gamepadNavigation.TopBarSelectedIndex));
                break;
            case GamepadNavigationZone.AnnouncementBanner:
                _banners.ApplyAnnouncementBannerGamepadSelection();
                break;
        }
    }

    internal bool TryOpenFocusedDisplayFilterOverflowMenu()
    {
        var controls = CollectSidebarFocusableControls();
        var index = _gamepadNavigation.ClampIndex(_gamepadNavigation.SidebarSelectedIndex, controls.Count);
        if (index < 0 || index >= controls.Count)
            return false;
        if (controls[index] is not Button row || !row.Classes.Contains("display-filter-row"))
            return false;
        var overflow = Views.LibraryFiltersView.FindDisplayFilterOverflowButton(row);
        if (overflow == null)
            return false;
        LibraryFiltersPanel.OpenDisplayFilterOverflowMenu(overflow);
        return true;
    }

    internal void RestoreSidebarDisplayFilterFocus(string filterId)
    {
        if (!_host.IsFocusActive)
            return;
        _gamepadNavigation.ActiveZone = GamepadNavigationZone.Sidebar;
        Post(() =>
        {
            if (!_host.IsFocusActive || _gamepadNavigation.ActiveZone != GamepadNavigationZone.Sidebar)
                return;
            if (!TrySelectSidebarDisplayFilterRow(filterId))
            {
                Post(() => TrySelectSidebarDisplayFilterRow(filterId), DispatcherPriority.Background);
            }
        }, DispatcherPriority.Loaded);
    }

    internal void SelectSidebarDisplayFilterRow(string filterId) => TrySelectSidebarDisplayFilterRow(filterId);
    internal bool TrySelectSidebarDisplayFilterRow(string filterId)
    {
        var controls = CollectSidebarFocusableControls();
        var index = controls.FindIndex(c => c is Button b && b.Classes.Contains("display-filter-row") && b.Tag is string id && string.Equals(id, filterId, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            return false;
        ApplySidebarGamepadSelection(index);
        return true;
    }

    internal bool TryMoveXyFocusInRegion(Control? searchRoot, NavigationDirection direction, IReadOnlyList<Control> controls, Action<int> applySelection)
    {
        if (searchRoot == null || controls.Count == 0)
            return false;
        var previous = TopLevel.GetTopLevel(_root)?.FocusManager?.GetFocusedElement();
        var previousIndex = GamepadControlActivation.IndexOfControlContainingFocus(controls, previous);
        if (!XyFocusNavigation.TryMove(_root, direction, searchRoot))
            return false;
        var focused = TopLevel.GetTopLevel(_root)?.FocusManager?.GetFocusedElement();
        var index = GamepadControlActivation.IndexOfControlContainingFocus(controls, focused);
        if (index < 0 || index == previousIndex)
            return false;
        applySelection(index);
        return true;
    }

    internal bool HandleSidebarGamepadNavigation(NavigationDirection direction)
    {
        var controls = CollectSidebarFocusableControls();
        if (controls.Count == 0)
            return false;
        if (TryMoveXyFocusInRegion(SidebarPanel, direction, controls, ApplySidebarGamepadSelection))
            return true;
        var currentIndex = _gamepadNavigation.ClampIndex(_gamepadNavigation.SidebarSelectedIndex, controls.Count);
        var footerStartIndex = FindSidebarFooterStartIndex(controls);
        // Footer icons are a horizontal strip: Left/Right only between them.
        if (footerStartIndex >= 0 && currentIndex >= footerStartIndex)
        {
            var footerCount = controls.Count - footerStartIndex;
            var footerLocalIndex = currentIndex - footerStartIndex;
            if (direction is NavigationDirection.Left or NavigationDirection.Right)
            {
                // Right from the last footer button leaves the sidebar.
                if (direction == NavigationDirection.Right && footerLocalIndex >= footerCount - 1)
                {
                    var leaveSidebar = _gamepadNavigation.TryGetZoneTransition(direction, GamepadNavigationZone.Sidebar, _host.MainContentZone, isListLayout: true, positions: null, currentIndex, controls.Count);
                    if (leaveSidebar.HasValue)
                        return _host.ApplyTransition(leaveSidebar.Value);
                    return true;
                }

                var nextLocal = _gamepadNavigation.MoveHorizontalIndex(footerLocalIndex, direction, footerCount);
                ApplySidebarGamepadSelection(footerStartIndex + nextLocal);
                return true;
            }

            if (direction == NavigationDirection.Up)
            {
                if (footerStartIndex > 0)
                    ApplySidebarGamepadSelection(footerStartIndex - 1);
                return true;
            }

            if (direction == NavigationDirection.Down)
            {
                // Stay on the current footer icon; wrap from the last one to the top of the sidebar.
                if (currentIndex >= controls.Count - 1 && footerStartIndex > 0)
                    ApplySidebarGamepadSelection(0);
                return true;
            }

            return true;
        }

        var zoneTransition = _gamepadNavigation.TryGetZoneTransition(direction, GamepadNavigationZone.Sidebar, _host.MainContentZone, isListLayout: true, positions: null, currentIndex, controls.Count);
        if (zoneTransition.HasValue)
            return _host.ApplyTransition(zoneTransition.Value);
        if (direction is not (NavigationDirection.Up or NavigationDirection.Down))
            return true;
        // Enter the footer strip on its first button (GitHub), not Discord.
        if (direction == NavigationDirection.Down && footerStartIndex >= 0 && currentIndex == footerStartIndex - 1)
        {
            ApplySidebarGamepadSelection(footerStartIndex);
            return true;
        }

        var nextIndex = _gamepadNavigation.MoveListIndex(currentIndex, direction, controls.Count);
        // Wrapping Up from the first sidebar item lands on the last control (Discord).
        // Prefer the start of the footer strip so Up/Down never step GitHub ↔ Discord.
        if (direction == NavigationDirection.Up && currentIndex <= 0 && footerStartIndex >= 0 && nextIndex >= footerStartIndex)
        {
            nextIndex = footerStartIndex;
        }

        ApplySidebarGamepadSelection(nextIndex);
        return true;
    }

    internal int FindSidebarFooterStartIndex(IReadOnlyList<Control> controls)
    {
        for (var i = 0; i < controls.Count; i++)
        {
            if (ReferenceEquals(controls[i], GitHubFooterButton) || ReferenceEquals(controls[i], DiscordFooterButton) || ReferenceEquals(controls[i], KofiFooterButton))
            {
                return i;
            }
        }

        return -1;
    }

    internal bool HandleTopBarGamepadNavigation(NavigationDirection direction)
    {
        var controls = CollectTopBarControls();
        if (controls.Count == 0)
            return false;
        var currentIndex = _gamepadNavigation.ClampIndex(_gamepadNavigation.TopBarSelectedIndex, controls.Count);
        var current = currentIndex >= 0 && currentIndex < controls.Count ? controls[currentIndex] : null;
        if (TryMoveUpdateStatus(current, direction, controls)) return true;
        var skipXy = GamepadTextInput.ShouldSkipXyFocusOnHighlight(current);
        if (!PlatformCapabilities.IsMobile && current != null &&
            !((Shell.ModDetailsOpen || Shell.CatalogDetailsOpen) && direction == NavigationDirection.Down) &&
            TryMoveBetweenHeaderRows(current, direction, controls))
            return true;
        // Details overlay owns Down: do not XYFocus onto Changelog or stop on the banner.
        // Highlight-only TextBoxes (Search) have no reliable native focus; XY would
        // land on Library. Walk Search → Add instead.
        if (!skipXy && !((Shell.ModDetailsOpen || Shell.CatalogDetailsOpen) && direction == NavigationDirection.Down) && TryMoveXyFocusInRegion(GetActiveTopBarRoot(), direction, controls, ApplyTopBarGamepadSelection))
            return true;
        if (direction == NavigationDirection.Down && _banners.IsAnnouncementBannerVisible && !Shell.ModDetailsOpen && !Shell.CatalogDetailsOpen)
        {
            ClearTopBarGamepadFocus();
            _banners.ApplyAnnouncementBannerGamepadSelection(0);
            return true;
        }

        if (direction == NavigationDirection.Down && !Shell.ModDetailsOpen && !Shell.CatalogDetailsOpen &&
            _root.FindControl<UpdateCheckStatusView>("UpdateCheckStatus") is { } status &&
            controls.FirstOrDefault(c => status.IsVisualAncestorOf(c)) is { } statusEntry)
        {
            ApplyTopBarGamepadSelection(controls.IndexOf(statusEntry));
            return true;
        }
        var zoneTransition = _gamepadNavigation.TryGetZoneTransition(direction, GamepadNavigationZone.TopBar, _host.MainContentZone, isListLayout: true, positions: null, _gamepadNavigation.TopBarSelectedIndex, controls.Count);
        if (zoneTransition.HasValue)
            return _host.ApplyTransition(zoneTransition.Value);
        if (direction is NavigationDirection.Left or NavigationDirection.Right)
        {
            var nextIndex = _gamepadNavigation.MoveHorizontalIndex(_gamepadNavigation.TopBarSelectedIndex, direction, controls.Count);
            ApplyTopBarGamepadSelection(nextIndex);
            return true;
        }

        // Consume Up (and any other non-transition direction) so InputService's
        // Avalonia focus walk does not move keyboard focus onto Sort By while
        // the gamepad selection ring stays on Check for Updates / Settings / etc.
        return true;
    }

    internal List<Control> CollectSidebarFocusableControls()
    {
        var controls = new List<Control>();
        if (!SidebarPanel.IsEnabled) return controls;
        void Add(Control? control)
        {
            if (control != null && control.IsVisible && control.IsEnabled)
                controls.Add(control);
        }

        Add(ContinueButton);
        Add(LibraryNavButton);
        Add(AppCatalogNavButton);
        controls.AddRange(LibraryFiltersPanel.CollectNavigationControls());
        Add(GitHubFooterButton);
        Add(DiscordFooterButton);
        Add(KofiFooterButton);
        return controls;
    }

    internal List<Control> CollectTopBarControls()
    {
        var controls = new List<Control>();
        void Add(Control? control)
        {
            if (control != null && control.IsVisible && control.IsEnabled)
                controls.Add(control);
        }

        if (!PlatformCapabilities.IsMobile) Add(_root.FindControl<Button>("DesktopSidebarToggleButton"));
        if (Shell.Mode == MainViewMode.Library && !Shell.AppUpdatesOpen && !Shell.ModsOpen)
        {
            if (PlatformCapabilities.IsMobile)
            {
                Add(MobileSearchToggleButton);
                if (_isSearchOpen())
                {
                    controls.AddRange(_toolbar.NavigationControls(searchOnly: true));
                }

                Add(MobileAddButton);
                Add(MobileSortButton);
            }
            else
            {
                controls.AddRange(_toolbar.NavigationControls());
            }
        }
        else if (Shell.Mode == MainViewMode.AppCatalog && Shell.CatalogSubView == AppCatalogSubView.Review)
        {
            if (PlatformCapabilities.IsMobile)
            {
                Add(MobileCatalogBackButton);
                Add(MobileCatalogSortButton);
            }
            else
            {
                Add(CatalogReviewBackButton);
            }
        }

        Add(CheckForUpdatesButton);
        if (_root.FindControl<UpdateCheckStatusView>("UpdateCheckStatus") is { } status)
        {
            foreach (var button in status.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible &&
                !b.GetVisualAncestors().OfType<Expander>().Any())) Add(button);
            if (status.FindControl<Expander>("CheckDetailsExpander") is { IsEffectivelyVisible: true } details) Add(details);
        }
        if (_root.FindControl<AndroidLauncherUpdateView>("AndroidUpdateBanner") is {} updates)
            foreach (var button in updates.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible)) Add(button);
        Add(SettingsButton);
        Add(MinimizeButton);
        Add(ToggleMaximizeButton);
        Add(CloseLauncherButton);
        return controls;
    }

    private bool TryMoveUpdateStatus(Control? current, NavigationDirection direction, IReadOnlyList<Control> controls)
    {
        if (current == null ||
            _root.FindControl<UpdateCheckStatusView>("UpdateCheckStatus") is not { } status) return false;
        var rows = controls.Where(c => status.IsVisualAncestorOf(c)).ToList();
        if (rows.Count == 0) return false;
        var details = rows.OfType<Expander>().FirstOrDefault();
        var actions = rows.Where(c => c is Button).ToList();
        if (direction is NavigationDirection.Left or NavigationDirection.Right)
        {
            if (current == details) return true;
            var index = actions.IndexOf(current);
            if (index < 0) return false;
            var next = Math.Clamp(index + (direction == NavigationDirection.Right ? 1 : -1), 0, actions.Count - 1);
            ApplyTopBarGamepadSelection(controls.ToList().IndexOf(actions[next]));
            return true;
        }
        Control? target = null;
        if (current == details)
        {
            if (direction == NavigationDirection.Down)
                return _host.ApplyTransition(new(_host.MainContentZone, null));
            target = actions.FirstOrDefault();
        }
        else if (actions.Contains(current))
        {
            if (direction == NavigationDirection.Up) target = CheckForUpdatesButton;
            else if (details != null) target = details;
            else return _host.ApplyTransition(new(_host.MainContentZone, null));
        }
        if (target == null) return false;
        ApplyTopBarGamepadSelection(controls.ToList().IndexOf(target));
        return true;
    }

    internal Control? GetActiveTopBarRoot()
    {
        if (PlatformCapabilities.IsMobile)
            return (Control? )MobileSecondaryTopBar ?? DesktopInlineTopBar;
        return HeaderLayoutGrid;
    }

    private bool TryMoveBetweenHeaderRows(Control current, NavigationDirection direction, IReadOnlyList<Control> controls)
    {
        if (direction is not (NavigationDirection.Up or NavigationDirection.Down) || Grid.GetRow(DesktopInlineTopBar) != 1)
            return false;
        var origin = current.TranslatePoint(new Rect(current.Bounds.Size).Center, HeaderLayoutGrid);
        if (origin == null)
            return false;
        var next = -1;
        var distance = double.PositiveInfinity;
        for (var i = 0; i < controls.Count; i++)
        {
            var candidate = controls[i];
            var center = candidate.TranslatePoint(new Rect(candidate.Bounds.Size).Center, HeaderLayoutGrid);
            if (center == null || (direction == NavigationDirection.Down ? center.Value.Y <= origin.Value.Y + 8 : center.Value.Y >= origin.Value.Y - 8))
                continue;
            var delta = Math.Abs(center.Value.X - origin.Value.X);
            if (delta < distance) { next = i; distance = delta; }
        }
        if (next < 0)
            return false;
        ApplyTopBarGamepadSelection(next);
        return true;
    }

    internal void ApplySidebarGamepadSelection(int index)
    {
        if (!SidebarPanel.IsEnabled)
        {
            ApplyTopBarGamepadSelection(0);
            return;
        }
        var controls = CollectSidebarFocusableControls();
        index = _gamepadNavigation.ClampIndex(index, controls.Count);
        _gamepadNavigation.SidebarSelectedIndex = index;
        _gamepadNavigation.ActiveZone = GamepadNavigationZone.Sidebar;
        _host.ClearFocus();
        ClearSidebarGamepadFocusClasses(controls);
        if (index < 0 || index >= controls.Count)
            return;
        if (controls[index] is StyledElement styled)
            styled.Classes.Set("gamepad-focused", true);
        controls[index].Focus();
        _wireEdges();
        Post(() => controls[index].BringIntoView(), DispatcherPriority.Loaded);
    }

    internal void ApplyTopBarGamepadSelection(int index)
    {
        var controls = CollectTopBarControls();
        index = _gamepadNavigation.ClampIndex(index, controls.Count);
        _gamepadNavigation.TopBarSelectedIndex = index;
        _gamepadNavigation.ActiveZone = GamepadNavigationZone.TopBar;
        _host.ClearFocus();
        ClearTopBarGamepadFocusClasses(controls);
        if (index < 0 || index >= controls.Count)
            return;
        if (controls[index] is StyledElement styled)
            styled.Classes.Set("gamepad-focused", true);
        GamepadControlActivation.ApplyGamepadHighlightFocus(controls[index]);
        _wireEdges();
    }

    internal void ClearSidebarGamepadFocus(bool stealFocus = true)
    {
        var controls = CollectSidebarFocusableControls();
        ClearSidebarGamepadFocusClasses(controls);
        if (stealFocus)
            ClearFocusIfOnControls(controls);
    }

    internal void ClearTopBarGamepadFocus(bool stealFocus = true)
    {
        var controls = CollectTopBarControls();
        ClearTopBarGamepadFocusClasses(controls);
        if (stealFocus)
            ClearFocusIfOnControls(controls);
    }

    internal void ClearSidebarGamepadFocusClasses(IReadOnlyList<Control> controls)
    {
        foreach (var control in controls)
        {
            if (control is StyledElement styled)
                styled.Classes.Set("gamepad-focused", false);
        }
    }

    internal void ClearTopBarGamepadFocusClasses(IReadOnlyList<Control> controls)
    {
        foreach (var control in controls)
        {
            if (control is StyledElement styled)
                styled.Classes.Set("gamepad-focused", false);
        }
    }

    internal void ClearFocusIfOnControls(IReadOnlyList<Control> controls)
    {
        var focusManager = TopLevel.GetTopLevel(_root)?.FocusManager;
        if (focusManager?.GetFocusedElement()is Control focusedControl && controls.Contains(focusedControl))
        {
            focusManager.Focus(null);
        }
    }

    internal void ActivateSidebarSelection()
    {
        var controls = CollectSidebarFocusableControls();
        var index = _gamepadNavigation.ClampIndex(_gamepadNavigation.SidebarSelectedIndex, controls.Count);
        if (index < 0 || index >= controls.Count)
            return;
        var control = controls[index];
        if (control is Button button)
        {
            GamepadControlActivation.ActivateButton(button);
            return;
        }

        control.Focus();
    }

    internal void ActivateTopBarSelection()
    {
        var controls = CollectTopBarControls();
        var index = _gamepadNavigation.ClampIndex(_gamepadNavigation.TopBarSelectedIndex, controls.Count);
        if (index < 0 || index >= controls.Count)
            return;
        var control = controls[index];
        if (control is Expander expander)
        {
            expander.IsExpanded = !expander.IsExpanded;
            return;
        }
        if (control is ComboBox comboBox)
        {
            comboBox.Focus();
            if (!comboBox.IsDropDownOpen)
            {
                comboBox.IsDropDownOpen = true;
                GamepadComboBoxNavigation.Open(comboBox);
            }

            return;
        }

        if (control is TextBox textBox)
        {
            GamepadControlActivation.ActivateTextBox(textBox);
            return;
        }

        if (control is Button button)
            GamepadControlActivation.ActivateButton(button);
    }

    public bool SynchronizePointer(object? source)
    {
        if (GamepadPointerFocusSync.Hit(_gamepadNavigation, _banners.CollectTopBannerGamepadControls(), GamepadNavigationZone.AnnouncementBanner, _banners._topBannerGamepadIndex, index => _banners.ApplyAnnouncementBannerGamepadSelection(index), source) || GamepadPointerFocusSync.Hit(_gamepadNavigation, CollectTopBarControls(), GamepadNavigationZone.TopBar, _gamepadNavigation.TopBarSelectedIndex, ApplyTopBarGamepadSelection, source) || GamepadPointerFocusSync.Hit(_gamepadNavigation, CollectSidebarFocusableControls(), GamepadNavigationZone.Sidebar, _gamepadNavigation.SidebarSelectedIndex, ApplySidebarGamepadSelection, source))
        {
            return true;
        }

        return false;
    }

    public void LeaveZone(GamepadNavigationZone nextZone)
    {
        var zone = _gamepadNavigation.ActiveZone;
        if (zone == nextZone)
            return;
        if (zone == GamepadNavigationZone.Sidebar)
            ClearSidebarGamepadFocus();
        if (zone == GamepadNavigationZone.TopBar)
            ClearTopBarGamepadFocus();
        if (zone == GamepadNavigationZone.AnnouncementBanner)
            _banners.ClearAnnouncementBannerGamepadFocus();
    }

    public bool EnterZone(GamepadZoneTransition transition)
    {
        switch (transition.Zone)
        {
            case GamepadNavigationZone.Sidebar:
                _host.ClearFocus();
                _gamepadNavigation.ActiveZone = GamepadNavigationZone.Sidebar;
                _gamepadNavigation.LibrarySelectedIndex = -1;
                _gamepadNavigation.CatalogSelectedIndex = -1;
                _gamepadNavigation.CatalogReviewSelectedIndex = -1;
                ApplySidebarGamepadSelection(_gamepadNavigation.SidebarSelectedIndex < 0 ? 0 : _gamepadNavigation.SidebarSelectedIndex);
                return true;
            case GamepadNavigationZone.TopBar:
                if (_gamepadNavigation.ActiveZone == GamepadNavigationZone.Library)
                {
                    var controls = CollectTopBarControls();
                    if (_root.FindControl<UpdateCheckStatusView>("UpdateCheckStatus") is { } status)
                    {
                        var entry = controls.LastOrDefault(c => status.IsVisualAncestorOf(c));
                        if (entry != null)
                        {
                            ApplyTopBarGamepadSelection(controls.IndexOf(entry));
                            return true;
                        }
                    }
                }
                // Coming up from content: stop on the banner first when it is visible.
                if (_banners.IsAnnouncementBannerVisible && _gamepadNavigation.ActiveZone is not (GamepadNavigationZone.TopBar or GamepadNavigationZone.AnnouncementBanner))
                {
                    _banners.ApplyAnnouncementBannerGamepadSelection(0);
                    return true;
                }

                _host.ClearFocus();
                _gamepadNavigation.ActiveZone = GamepadNavigationZone.TopBar;
                _gamepadNavigation.LibrarySelectedIndex = -1;
                _gamepadNavigation.CatalogReviewSelectedIndex = -1;
                ApplyTopBarGamepadSelection(_gamepadNavigation.TopBarSelectedIndex < 0 ? 0 : _gamepadNavigation.TopBarSelectedIndex);
                return true;
            case GamepadNavigationZone.AnnouncementBanner:
                _banners.ApplyAnnouncementBannerGamepadSelection(0);
                return true;
            default:
                return false;
        }
    }
}
