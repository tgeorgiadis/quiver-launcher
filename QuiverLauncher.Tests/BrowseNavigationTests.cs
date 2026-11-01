using System.Net;
using System.Reflection;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAssertions;
using QuiverLauncher.Services;
using QuiverLauncher.ViewModels;
using QuiverLauncher.Views;

namespace QuiverLauncher.Tests;

/// <summary>Browse driven only by the shell's controller/keyboard actions, against a stubbed quiverlauncher.com.</summary>
public class BrowseNavigationTests
{
    [AvaloniaFact]
    public async Task Controller_moves_through_browse_opens_details_adds_and_returns_to_the_card()
    {
        var root = Path.Combine(Path.GetTempPath(), "quiver-browse-nav-" + Guid.NewGuid().ToString("N"));
        var previousRoot = QuiverLauncherPaths.OverrideUserDataRoot;
        Directory.CreateDirectory(root);
        QuiverLauncherPaths.OverrideUserDataRoot = root;
        var store = new Store();
        store.Current.AppsPath = Path.Combine(root, "Apps");
        var site = new Site();
        using var http = new HttpClient(site);
        var manager = new GameManager(store, http) { UiThreadInvoker = action => Dispatcher.UIThread.InvokeAsync(action).GetTask() };
        var view = new MainView(new() { SettingsStore = store, GameManager = manager, InitializeOnOpen = false, EnableInput = false, EnableMusic = false });
        var window = new Window { Content = view, Width = 1280, Height = 860 };
        var browse = view.FindControl<BrowseView>("BrowsePanel")!;
        var details = view.FindControl<BrowseDetailsView>("BrowseDetailsPanel")!;
        var navigation = ((IFeatureNavigationHost)view).Navigation;
        bool Move(NavigationDirection direction) => (bool)Shell(view, "HandleGamepadNavigation", direction)!;
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            GamepadFocusChrome.SetKeyboardNavigationActive(true);
            view.FindControl<Button>("BrowseNavButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            navigation.ActiveZone.Should().Be(GamepadNavigationZone.BrowseFilters);
            await Until(() => browse.Model.Items.Count == 3);
            window.UpdateLayout();

            // Refresh asks the site again and is available to the controller as well as the mouse.
            browse.Navigation.FilterControls().Should().Contain(browse.BrowseRefreshButton);
            var requests = site.AppRequests;
            browse.BrowseRefreshButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => site.AppRequests > requests && !browse.Model.IsLoading && browse.Model.Items.Count == 3);
            window.UpdateLayout();
            browse.Navigation.SelectInitial();

            // The search box holds the highlight while the catalog loads; the first card takes it when the cards arrive.
            navigation.ActiveZone.Should().Be(GamepadNavigationZone.BrowseGrid);
            browse.Model.Items[0].IsGamepadFocused.Should().BeTrue();

            Move(NavigationDirection.Right).Should().BeTrue();
            browse.Model.Items.Select(i => i.IsGamepadFocused).Should().Equal(false, true, false);
            // At this width the filters sit under the search, four across, and the library option is on the title line
            // beside the sort: Up climbs line by line to it, and Down comes back the same way to the cards.
            Move(NavigationDirection.Up).Should().BeTrue();
            navigation.ActiveZone.Should().Be(GamepadNavigationZone.BrowseFilters);
            browse.BrowseRefreshButton.Classes.Should().Contain("gamepad-focused");
            Move(NavigationDirection.Up).Should().BeTrue();
            new Control[] { browse.BrowseTypeComboBox, browse.BrowsePlatformComboBox, browse.BrowseConsoleComboBox, browse.BrowseAiComboBox }
                .Should().ContainSingle(c => c.Classes.Contains("gamepad-focused"));
            Move(NavigationDirection.Up).Should().BeTrue();
            browse.BrowseSearchTextBox.Classes.Should().Contain("gamepad-focused");
            Move(NavigationDirection.Up).Should().BeTrue();
            navigation.ActiveZone.Should().Be(GamepadNavigationZone.BrowseToolbar);
            browse.HideOptionInTitle.Should().BeTrue();
            browse.BrowseHideLibraryCheckBox.Classes.Should().Contain("gamepad-focused");
            Move(NavigationDirection.Right).Should().BeTrue();
            browse.BrowseSortComboBox.Classes.Should().Contain("gamepad-focused");
            for (var i = 0; i < 4; i++) Move(NavigationDirection.Down).Should().BeTrue();
            navigation.ActiveZone.Should().Be(GamepadNavigationZone.BrowseGrid);
            Move(NavigationDirection.Left).Should().BeTrue();
            navigation.ActiveZone.Should().Be(GamepadNavigationZone.Sidebar);
            Move(NavigationDirection.Right).Should().BeTrue();
            navigation.ActiveZone.Should().Be(GamepadNavigationZone.BrowseGrid);
            browse.Model.Items[0].IsGamepadFocused.Should().BeTrue();

            // Confirm opens the details with the first action highlighted; it becomes Add once the app's repository is known.
            Shell(view, "HandleConfirmAction");
            view.Shell.BrowseDetailsOpen.Should().BeTrue();
            navigation.ActiveZone.Should().Be(GamepadNavigationZone.BrowseDetailsOverlay);
            await Until(() => details.BrowseDetailsAddButton.IsVisible);
            details.BrowseDetailsAddButton.Classes.Should().Contain("gamepad-focused");

            // Down reaches the tabs (Overview, Releases, Player feedback); Player feedback has the button to give feedback on the website. Up returns to Add.
            Move(NavigationDirection.Down).Should().BeTrue();
            details.BrowseDetailsOverviewTab.Classes.Should().Contain("gamepad-focused");
            Move(NavigationDirection.Right).Should().BeTrue();
            details.BrowseDetailsReleasesTab.Classes.Should().Contain("gamepad-focused");
            Move(NavigationDirection.Right).Should().BeTrue();
            Shell(view, "HandleConfirmAction");
            details.BrowseDetailsFeedback.IsVisible.Should().BeTrue();
            details.BrowseDetailsReviewButton.IsVisible.Should().BeTrue();
            // Up lands on whatever sits nearest above: from Overview, Add.
            Move(NavigationDirection.Left).Should().BeTrue();
            Move(NavigationDirection.Left).Should().BeTrue();
            Move(NavigationDirection.Up).Should().BeTrue();
            details.BrowseDetailsAddButton.Classes.Should().Contain("gamepad-focused");

            Shell(view, "HandleConfirmAction");
            await Until(() => details.BrowseDetailsRemoveButton.IsVisible);
            manager.LibraryApps.Should().ContainSingle().Which.Repository.Should().Be("HarbourMasters/2ship2harkinian");
            browse.Model.Items[0].InLibrary.Should().BeTrue();
            details.BrowseDetailsRemoveButton.Classes.Should().Contain("gamepad-focused");

            // Back closes the details and returns to the card that opened them.
            Shell(view, "HandleCancelAction");
            view.Shell.BrowseDetailsOpen.Should().BeFalse();
            navigation.ActiveZone.Should().Be(GamepadNavigationZone.BrowseGrid);
            browse.Model.Items[0].IsGamepadFocused.Should().BeTrue();

            // Y on the next card adds it straight from the catalog, without opening its page.
            browse.Model.Items[1].CanAdd.Should().BeTrue();
            Move(NavigationDirection.Right).Should().BeTrue();
            Shell(view, "HandleOptionsAction");
            await Until(() => browse.Model.Items[1].InLibrary);
            view.Shell.BrowseDetailsOpen.Should().BeFalse();
            manager.LibraryApps.Select(a => a.Repository).Should().Contain("sonicdcer/Starship");
            browse.Model.Items[1].CanAdd.Should().BeFalse();
        }
        finally
        {
            window.Close();
            await view.ShutdownAsync();
            manager.Dispose();
            GamepadFocusChrome.SetKeyboardNavigationActive(false);
            QuiverLauncherPaths.OverrideUserDataRoot = previousRoot;
            TestFixtures.CleanupDirectory(root);
        }
    }

    [AvaloniaFact]
    public async Task On_a_phone_the_catalog_is_laid_out_like_the_websites_narrow_view()
    {
        var root = Path.Combine(Path.GetTempPath(), "quiver-browse-phone-" + Guid.NewGuid().ToString("N"));
        var previousRoot = QuiverLauncherPaths.OverrideUserDataRoot;
        Directory.CreateDirectory(root);
        QuiverLauncherPaths.OverrideUserDataRoot = root;
        var store = new Store();
        store.Current.AppsPath = Path.Combine(root, "Apps");
        using var http = new HttpClient(new Site());
        var manager = new GameManager(store, http) { UiThreadInvoker = action => Dispatcher.UIThread.InvokeAsync(action).GetTask() };
        var view = new MainView(new() { SettingsStore = store, GameManager = manager, InitializeOnOpen = false, EnableInput = false, EnableMusic = false });
        var window = new Window { Content = view, Width = 392, Height = 840 };
        var browse = view.FindControl<BrowseView>("BrowsePanel")!;
        var navigation = ((IFeatureNavigationHost)view).Navigation;
        bool Move(NavigationDirection direction) => (bool)Shell(view, "HandleGamepadNavigation", direction)!;
        Rect Place(Control control) => new(control.TranslatePoint(default, browse)!.Value, control.Bounds.Size);
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            // The phone shell only runs on Android: here the sidebar is closed and the catalog laid out as on a phone.
            var split = view.FindControl<SplitView>("MainSplitView")!;
            split.DisplayMode = SplitViewDisplayMode.Overlay;
            split.IsPaneOpen = false;
            browse.ApplyMobileLayout();
            GamepadFocusChrome.SetKeyboardNavigationActive(true);
            view.FindControl<Button>("BrowseNavButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => browse.Model.Items.Count == 3);
            window.UpdateLayout();

            // The sort shares the title's line; the search spans the width, with the four filters two by two under it.
            Place(browse.BrowseSortComboBox).Right.Should().BeApproximately(browse.Bounds.Width, 1);
            browse.BrowseSortLabel.IsVisible.Should().BeFalse();
            var search = Place(browse.BrowseSearchTextBox);
            search.Width.Should().BeApproximately(browse.Bounds.Width, 1);
            var (type, platform, console, ai) = (Place(browse.BrowseTypeComboBox), Place(browse.BrowsePlatformComboBox),
                Place(browse.BrowseConsoleComboBox), Place(browse.BrowseAiComboBox));
            (type.Y, platform.Y, console.X, ai.X).Should().Be((platform.Y, type.Y, type.X, platform.X));
            console.Y.Should().BeGreaterThan(type.Bottom);
            (type.X, platform.Right).Should().Be((0, search.Right));

            // Two cards to a row, filling the width.
            var cards = browse.BrowseItemsControl.GetVisualDescendants().OfType<BrowseCard>().Select(Place).ToList();
            cards.Should().HaveCount(3);
            (cards[0].X, cards[1].Y, cards[1].Right).Should().Be((0, cards[0].Y, search.Right));
            cards[2].Y.Should().BeGreaterThan(cards[0].Bottom);

            // Up from a card reaches the line above it (the hide option, under the filters), then each filter line, then the search.
            Move(NavigationDirection.Right).Should().BeTrue();
            Move(NavigationDirection.Up).Should().BeTrue();
            navigation.ActiveZone.Should().Be(GamepadNavigationZone.BrowseFilters);
            browse.BrowseRefreshButton.Classes.Should().Contain("gamepad-focused");
            Move(NavigationDirection.Left).Should().BeTrue();
            browse.BrowseHideLibraryCheckBox.Classes.Should().Contain("gamepad-focused");
            Move(NavigationDirection.Up).Should().BeTrue();
            browse.BrowseConsoleComboBox.Classes.Should().Contain("gamepad-focused");
            Move(NavigationDirection.Right).Should().BeTrue();
            browse.BrowseAiComboBox.Classes.Should().Contain("gamepad-focused");
            Move(NavigationDirection.Up).Should().BeTrue();
            browse.BrowsePlatformComboBox.Classes.Should().Contain("gamepad-focused");
            Move(NavigationDirection.Up).Should().BeTrue();
            browse.BrowseSearchTextBox.Classes.Should().Contain("gamepad-focused");
            Move(NavigationDirection.Down).Should().BeTrue();
            browse.BrowseTypeComboBox.Classes.Should().Contain("gamepad-focused");
        }
        finally
        {
            window.Close();
            await view.ShutdownAsync();
            manager.Dispose();
            GamepadFocusChrome.SetKeyboardNavigationActive(false);
            QuiverLauncherPaths.OverrideUserDataRoot = previousRoot;
            TestFixtures.CleanupDirectory(root);
        }
    }

    [AvaloniaFact]
    public async Task On_a_handheld_held_sideways_the_catalog_controls_scroll_away_with_the_cards()
    {
        var root = Path.Combine(Path.GetTempPath(), "quiver-browse-handheld-" + Guid.NewGuid().ToString("N"));
        var previousRoot = QuiverLauncherPaths.OverrideUserDataRoot;
        Directory.CreateDirectory(root);
        QuiverLauncherPaths.OverrideUserDataRoot = root;
        var store = new Store();
        store.Current.AppsPath = Path.Combine(root, "Apps");
        using var http = new HttpClient(new Site());
        var manager = new GameManager(store, http) { UiThreadInvoker = action => Dispatcher.UIThread.InvokeAsync(action).GetTask() };
        var view = new MainView(new() { SettingsStore = store, GameManager = manager, InitializeOnOpen = false, EnableInput = false, EnableMusic = false });
        // A 1920×1080 handheld at its usual density, in landscape.
        var window = new Window { Content = view, Width = 853, Height = 480 };
        var browse = view.FindControl<BrowseView>("BrowsePanel")!;
        var navigation = ((IFeatureNavigationHost)view).Navigation;
        var scroller = browse.BrowseScrollViewer;
        bool Move(NavigationDirection direction) => (bool)Shell(view, "HandleGamepadNavigation", direction)!;
        double Top(Control control) => control.TranslatePoint(default, scroller)!.Value.Y;
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            var split = view.FindControl<SplitView>("MainSplitView")!;
            split.DisplayMode = SplitViewDisplayMode.Overlay;
            split.IsPaneOpen = false;
            browse.ApplyMobileLayout();
            GamepadFocusChrome.SetKeyboardNavigationActive(true);
            view.FindControl<Button>("BrowseNavButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => browse.Model.Items.Count == 3);
            window.UpdateLayout();

            // The title, search and filters start at the top of the scrolling area, above the cards.
            scroller.Offset.Y.Should().Be(0);
            Top(browse.BrowseToolbarPanel).Should().BeApproximately(0, 1);
            Top(browse.BrowseItemsControl).Should().BeGreaterThan(Top(browse.BrowseFiltersPanel) + browse.BrowseFiltersPanel.Bounds.Height - 1);

            // Scrolling down moves them up and out of the way with the cards (three cards only scroll part of the way here).
            var search = Top(browse.BrowseSearchTextBox);
            scroller.Offset = new Vector(0, scroller.Extent.Height - scroller.Viewport.Height);
            window.UpdateLayout();
            scroller.Offset.Y.Should().BeGreaterThan(browse.BrowseToolbarPanel.Bounds.Height);
            Top(browse.BrowseSearchTextBox).Should().BeApproximately(search - scroller.Offset.Y, 1);

            // Up from the first card with a controller brings the filters back into view.
            Move(NavigationDirection.Up).Should().BeTrue();
            navigation.ActiveZone.Should().Be(GamepadNavigationZone.BrowseFilters);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var focused = new Control[] { browse.BrowseTypeComboBox, browse.BrowsePlatformComboBox, browse.BrowseConsoleComboBox,
                browse.BrowseAiComboBox, browse.BrowseHideLibraryCheckBox, browse.BrowseRefreshButton }.Single(c => c.Classes.Contains("gamepad-focused"));
            Top(focused).Should().BeGreaterThanOrEqualTo(-1);
            (Top(focused) + focused.Bounds.Height).Should().BeLessThanOrEqualTo(scroller.Viewport.Height + 1);
        }
        finally
        {
            window.Close();
            await view.ShutdownAsync();
            manager.Dispose();
            GamepadFocusChrome.SetKeyboardNavigationActive(false);
            QuiverLauncherPaths.OverrideUserDataRoot = previousRoot;
            TestFixtures.CleanupDirectory(root);
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Switching_away_and_back_keeps_the_app_page_where_it_was_scrolled(bool controller)
    {
        var root = Path.Combine(Path.GetTempPath(), "quiver-browse-refocus-" + Guid.NewGuid().ToString("N"));
        var previousRoot = QuiverLauncherPaths.OverrideUserDataRoot;
        Directory.CreateDirectory(root);
        QuiverLauncherPaths.OverrideUserDataRoot = root;
        var store = new Store();
        store.Current.AppsPath = Path.Combine(root, "Apps");
        using var http = new HttpClient(new Site());
        var manager = new GameManager(store, http) { UiThreadInvoker = action => Dispatcher.UIThread.InvokeAsync(action).GetTask() };
        var view = new MainView(new() { SettingsStore = store, GameManager = manager, InitializeOnOpen = false, EnableInput = false, EnableMusic = false });
        // Short, so the page scrolls.
        var window = new Window { Content = view, Width = 1280, Height = 480 };
        var browse = view.FindControl<BrowseView>("BrowsePanel")!;
        var details = view.FindControl<BrowseDetailsView>("BrowseDetailsPanel")!;
        var scroll = details.BrowseDetailsScrollViewer;
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            GamepadFocusChrome.SetKeyboardNavigationActive(controller);
            view.FindControl<Button>("BrowseNavButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => browse.Model.Items.Count == 3);
            if (controller)
            {
                Shell(view, "HandleConfirmAction");
                await Until(() => details.BrowseDetailsAddButton.IsVisible);
            }
            else
            {
                typeof(MainView).GetMethod("OpenBrowseDetails", BindingFlags.Instance | BindingFlags.NonPublic, [typeof(BrowseItem), typeof(bool)])!
                    .Invoke(view, [browse.Model.Items[0], false]);
                await Until(() => details.BrowseDetailsAddButton.IsVisible);
                window.UpdateLayout();
                // Clicking a tab leaves it with keyboard focus, as on Windows.
                var tab = details.BrowseDetailsOverviewTab;
                var at = tab.TranslatePoint(new Point(tab.Bounds.Width / 2, tab.Bounds.Height / 2), window)!.Value;
                window.MouseDown(at, Avalonia.Input.MouseButton.Left);
                window.MouseUp(at, Avalonia.Input.MouseButton.Left);
                tab.IsFocused.Should().BeTrue();
            }
            await Until(() => { window.UpdateLayout(); return scroll.Extent.Height > scroll.Viewport.Height * 3; });
            if (controller)
            {
                // Down past the last row reads the page: nothing is highlighted, and the page scrolls.
                for (var i = 0; i < 20 && !details.BodyFocused; i++) Shell(view, "HandleGamepadNavigation", NavigationDirection.Down);
                details.BodyFocused.Should().BeTrue();
            }
            scroll.Offset = new Vector(0, scroll.Extent.Height - scroll.Viewport.Height);
            window.UpdateLayout();
            var offset = scroll.Offset;

            // Alt+Tab away and back: the window loses keyboard focus, then Avalonia refocuses what had it.
            var impl = typeof(TopLevel).GetProperty("PlatformImpl")!.GetValue(window)!;
            ((Action?)impl.GetType().GetProperty("Deactivated")!.GetValue(impl))?.Invoke();
            ((Action?)impl.GetType().GetProperty("LostFocus")!.GetValue(impl))!.Invoke();
            window.FocusManager!.GetFocusedElement().Should().BeNull();
            view.HandleDeactivated();
            window.Activate();
            Dispatcher.UIThread.RunJobs();
            view.HandleActivated();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            scroll.Offset.Should().Be(offset);
            if (!controller)
            {
                // Tab still brings the control it reaches into view.
                window.KeyPress(Avalonia.Input.Key.Tab, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.Tab, null);
                window.UpdateLayout();
                details.BrowseDetailsReleasesTab.IsFocused.Should().BeTrue();
                scroll.Offset.Y.Should().BeLessThan(offset.Y);
            }
        }
        finally
        {
            window.Close();
            await view.ShutdownAsync();
            manager.Dispose();
            GamepadFocusChrome.SetKeyboardNavigationActive(false);
            QuiverLauncherPaths.OverrideUserDataRoot = previousRoot;
            TestFixtures.CleanupDirectory(root);
        }
    }

    [AvaloniaFact]
    public async Task On_a_short_desktop_window_the_catalog_controls_scroll_with_the_cards_and_stay_above_them_when_taller()
    {
        var root = Path.Combine(Path.GetTempPath(), "quiver-browse-short-" + Guid.NewGuid().ToString("N"));
        var previousRoot = QuiverLauncherPaths.OverrideUserDataRoot;
        Directory.CreateDirectory(root);
        QuiverLauncherPaths.OverrideUserDataRoot = root;
        var store = new Store();
        store.Current.AppsPath = Path.Combine(root, "Apps");
        using var http = new HttpClient(new Site());
        var manager = new GameManager(store, http) { UiThreadInvoker = action => Dispatcher.UIThread.InvokeAsync(action).GetTask() };
        var view = new MainView(new() { SettingsStore = store, GameManager = manager, InitializeOnOpen = false, EnableInput = false, EnableMusic = false });
        // The Steam Deck's screen.
        var window = new Window { Content = view, Width = 1280, Height = 800 };
        var browse = view.FindControl<BrowseView>("BrowsePanel")!;
        var scroller = browse.BrowseScrollViewer;
        double Top(Control control) => control.TranslatePoint(default, scroller)!.Value.Y;
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            view.FindControl<Button>("BrowseNavButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => browse.Model.Items.Count == 3);
            window.UpdateLayout();

            // The title, search and filters (158 of the page's 690 points here) are the top of the scrolling area, so they
            // scroll up out of the way with the cards.
            browse.ControlsScroll.Should().BeTrue();
            Top(browse.BrowseToolbarPanel).Should().BeApproximately(0, 1);
            Top(browse.BrowseItemsControl).Should().BeGreaterThan(Top(browse.BrowseFiltersPanel) + browse.BrowseFiltersPanel.Bounds.Height - 1);
            // They line up with the cards, clear of the scroll bar.
            browse.BrowseFiltersPanel.Bounds.Width.Should().BeApproximately(browse.BrowseItemsControl.Bounds.Width, 1);

            // A taller window keeps them above the cards, as before.
            window.Height = 1040;
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            browse.ControlsScroll.Should().BeFalse();
            Top(browse.BrowseItemsControl).Should().BeApproximately(-scroller.Offset.Y, 1);
            browse.BrowseFiltersPanel.TranslatePoint(default, scroller)!.Value.Y.Should().BeLessThan(0);
            browse.BrowseFiltersPanel.Bounds.Width.Should().BeApproximately(browse.BrowseItemsControl.Bounds.Width, 1);
        }
        finally
        {
            window.Close();
            await view.ShutdownAsync();
            manager.Dispose();
            GamepadFocusChrome.SetKeyboardNavigationActive(false);
            QuiverLauncherPaths.OverrideUserDataRoot = previousRoot;
            TestFixtures.CleanupDirectory(root);
        }
    }

    private static object? Shell(MainView view, string method, params object[] args) =>
        typeof(MainView).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(view, args);

    private static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("The condition was not met in time.");
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(20);
        }
    }

    private sealed class Store : ISettingsStore
    {
        public AppSettings Current { get; } = new() { FirstStartup = false, LocalFirstCatalogMigrationComplete = true };
        public AppSettings Load() => Current;
        public void Save(AppSettings settings) { }
    }

    /// <summary>The parts of quiverlauncher.com Browse reads; GitHub and everything else is unavailable.</summary>
    private sealed class Site : HttpMessageHandler
    {
        public int AppRequests { get; private set; }
        private static string App(string slug, string project, string folder) => $$"""
            {"id":"{{slug}}","slug":"{{slug}}","name":"{{project}} game","projectName":"{{project}}","description":"Plays {{project}}.",
             "games":[{"id":"g","slug":"g","title":"{{project}} game"}],"consoles":["n64"],"tags":["n64"],
             "launcher":{"folderName":"{{folder}}","filesToAdd":[]},"projectType":"port","supportedOS":["windows","linux"],
             "recommended":2,"reportIssues":0,"reportBroken":0,"addedAt":1786576949000,"lastReleaseAt":1787680483000,
             "verified":{"version":"1.0.0","pinned":true} }
            """;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // Answer later, like the network, so Browse opens before its cards arrive.
            await Task.Delay(50, cancellationToken);
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/v1/apps") AppRequests++;
            var body = path switch
            {
                "/api/v1/facets" => """{"total":3,"consoles":[{"id":"n64","name":"Nintendo 64","brand":"Nintendo"}]}""",
                "/api/v1/apps" => $$"""{"items":[{{App("2ship", "2 Ship 2 Harkinian", "MajorasMask-2Ship")}},{{App("starship", "Starship", "StarFox64-Starship")}},{{App("zelda", "Zelda 64 Recompiled", "Zelda64")}}],"nextCursor":null,"isDone":true}""",
                "/api/v1/apps/2ship" => $$"""{"entry":{{App("2ship", "2 Ship 2 Harkinian", "MajorasMask-2Ship")}},"project":{"provider":"github","repository":"HarbourMasters/2ship2harkinian"},"withdrawn":[]}""",
                "/api/v1/apps/starship" => $$"""{"entry":{{App("starship", "Starship", "StarFox64-Starship")}},"project":{"provider":"github","repository":"sonicdcer/Starship"},"withdrawn":[]}""",
                // A README long enough to scroll the page well past its tabs.
                "/api/v1/apps/2ship/readme" => $$"""{"markdown":"{{string.Concat(Enumerable.Range(1, 60).Select(i => $"Paragraph {i} of the README.\\n\\n"))}}"}""",
                "/api/v1/apps/2ship/reviews" => """{"items":[{"author":"Player","result":"runs","body":"Smooth.","createdAt":1787680483000}],"nextCursor":null,"isDone":true}""",
                _ => null,
            };
            return body == null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
}
