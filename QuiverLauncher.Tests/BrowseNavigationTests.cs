using System.Net;
using System.Reflection;
using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
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
            Move(NavigationDirection.Up).Should().BeTrue();
            navigation.ActiveZone.Should().Be(GamepadNavigationZone.BrowseFilters);
            browse.BrowseSearchTextBox.Classes.Should().Contain("gamepad-focused");
            Move(NavigationDirection.Up).Should().BeTrue();
            navigation.ActiveZone.Should().Be(GamepadNavigationZone.BrowseToolbar);
            browse.BrowseSortComboBox.Classes.Should().Contain("gamepad-focused");
            Move(NavigationDirection.Down).Should().BeTrue();
            Move(NavigationDirection.Down).Should().BeTrue();
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
            details.BrowseDetailsReviewButton.IsVisible.Should().BeTrue();

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
                "/api/v1/apps/2ship/reviews" => """{"items":[{"author":"Player","result":"runs","body":"Smooth.","createdAt":1787680483000}],"nextCursor":null,"isDone":true}""",
                _ => null,
            };
            return body == null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
}
