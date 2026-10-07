using System.Net;
using System.Reflection;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAssertions;
using QuiverLauncher.Services;
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
        using var http = new HttpClient(new Site());
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

            // The search box holds the highlight while the catalog loads; the first card takes it when the cards arrive.
            navigation.ActiveZone.Should().Be(GamepadNavigationZone.BrowseGrid);
            browse.Model.Items[0].IsGamepadFocused.Should().BeTrue();

            Move(NavigationDirection.Right).Should().BeTrue();
            browse.Model.Items.Select(i => i.IsGamepadFocused).Should().Equal(false, true, false);
            // At this width the filters sit under the search, four across, with the library option below them:
            // Up climbs line by line to the sort, and Down comes back the same way to the cards.
            Move(NavigationDirection.Up).Should().BeTrue();
            navigation.ActiveZone.Should().Be(GamepadNavigationZone.BrowseFilters);
            browse.BrowseHideLibraryCheckBox.Classes.Should().Contain("gamepad-focused");
            Move(NavigationDirection.Up).Should().BeTrue();
            browse.BrowseTypeComboBox.Classes.Should().Contain("gamepad-focused");
            Move(NavigationDirection.Up).Should().BeTrue();
            browse.BrowseSearchTextBox.Classes.Should().Contain("gamepad-focused");
            Move(NavigationDirection.Up).Should().BeTrue();
            navigation.ActiveZone.Should().Be(GamepadNavigationZone.BrowseToolbar);
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
            var body = path switch
            {
                "/api/v1/facets" => """{"total":3,"consoles":[{"id":"n64","name":"Nintendo 64","brand":"Nintendo"}]}""",
                "/api/v1/apps" => $$"""{"items":[{{App("2ship", "2 Ship 2 Harkinian", "MajorasMask-2Ship")}},{{App("starship", "Starship", "StarFox64-Starship")}},{{App("zelda", "Zelda 64 Recompiled", "Zelda64")}}],"nextCursor":null,"isDone":true}""",
                "/api/v1/apps/2ship" => $$"""{"entry":{{App("2ship", "2 Ship 2 Harkinian", "MajorasMask-2Ship")}},"project":{"provider":"github","repository":"HarbourMasters/2ship2harkinian"},"withdrawn":[]}""",
                "/api/v1/apps/starship" => $$"""{"entry":{{App("starship", "Starship", "StarFox64-Starship")}},"project":{"provider":"github","repository":"sonicdcer/Starship"},"withdrawn":[]}""",
                "/api/v1/apps/2ship/reviews" => """{"items":[{"author":"Player","result":"runs","body":"Smooth.","createdAt":1787680483000}],"nextCursor":null,"isDone":true}""",
                _ => null,
            };
            return body == null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
}
