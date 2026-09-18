using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAssertions;
using QuiverLauncher.Models;
using QuiverLauncher.Services;
using QuiverLauncher.ViewModels;
using QuiverLauncher.Views;

namespace QuiverLauncher.Tests;

public class ForegroundScrollTests
{
    private sealed class Store : ISettingsStore
    {
        public AppSettings Current { get; } = new() { AppsPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")) };
        public AppSettings Load() => Current;
        public void Save(AppSettings settings) { }
    }

    private sealed class Host(bool controller) : IFeatureNavigationHost
    {
        public GamepadNavigationService Navigation { get; } = new();
        public GamepadNavigationZone MainContentZone { get; set; }
        public bool IsFocusActive => GamepadFocusChrome.ShouldShowGamepadChrome(controller, controller, !controller);
        public Button Sink { get; } = new() { Width = 1, Height = 1 };
        public Action Clear { get; set; } = () => { };
        public bool ApplyTransition(GamepadZoneTransition transition) => false;
        public void ClearFocus() => Clear();
        public void ClearSidebarFocus() { }
        public void FocusCard(bool stealFocus) { if (stealFocus) Sink.Focus(); }
    }

    private static Window Open(Control view, Host host)
    {
        var panel = new Grid();
        panel.Children.Add(view);
        panel.Children.Add(host.Sink);
        var window = new Window { Content = panel, Width = 1000, Height = 650 };
        window.Show();
        Flush(window);
        return window;
    }

    private static void Flush(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    private static void Refocus(IFeatureNavigationHandler handler, Host host, LauncherSession session)
    {
        var shell = new ShellViewModel
        {
            Mode = host.MainContentZone == GamepadNavigationZone.Library ? MainViewMode.Library : MainViewMode.AppCatalog,
            CatalogSubView = host.MainContentZone == GamepadNavigationZone.CatalogSources ? AppCatalogSubView.Sources : AppCatalogSubView.Review,
        };
        var router = new ShellNavigationRouter(shell, host.Navigation,
            new Dictionary<GamepadNavigationZone, Func<IFeatureNavigationHandler>> { [host.MainContentZone] = () => handler },
            () => false, () => { }, () => throw new Exception("Unexpected view re-entry"));
        using var music = new LauncherMusicService(action => action(), enabled: false);
        var refreshes = 0;
        using var foreground = new LauncherForegroundController(session, music, () => null,
            () => new AppSettings(), () => true, () => { },
            () => router.RestoreCurrentFocus(bringIntoView: false),
            () => { refreshes++; return Task.CompletedTask; }, (_, _) => { },
            action => { action(); return Task.CompletedTask; });
        foreground.Deactivated();
        foreground.Activated();
        refreshes.Should().Be(1);
    }

    private static Vector ScrollAway(ScrollViewer scroll, Window window)
    {
        scroll.Offset = new Vector(0, (scroll.Extent.Height - scroll.Viewport.Height) / 2);
        Flush(window);
        scroll.Offset.Y.Should().BeGreaterThan(100);
        return scroll.Offset;
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Library_activation_preserves_viewport_then_navigation_reveals_selection(bool grid, bool controller)
    {
        var store = new Store();
        store.Current.UseGridView = grid;
        using var manager = new GameManager(store);
        using var model = new LibraryViewModel(manager, new SettingsViewModel(store));
        await using var session = new LauncherSession();
        for (var i = 0; i < 80; i++) manager.Games.Add(new GameInfo { Name = $"App {i:D3}", Repository = "example/app" });
        var view = new LibraryView();
        view.Configure(model, session, null!, (_, _) => { });
        view.UpdateLayoutMode();
        view.LibraryContentPanel.IsVisible = true;
        view.EmptyLibraryPanel.IsVisible = false;
        var host = new Host(controller) { MainContentZone = GamepadNavigationZone.Library };
        var navigation = new LibraryNavigation(view, host, session, () => true, () => true, () => false, _ => { }, _ => Task.CompletedTask);
        view.Navigation = navigation;
        host.Clear = () => { foreach (var game in manager.Games) game.IsGamepadFocused = false; };
        var window = Open(view, host);
        try
        {
            navigation.ApplyLibraryGamepadSelection(0);
            Flush(window);
            var offset = ScrollAway(view.LibraryContentPanel, window);
            Refocus(navigation, host, session);
            Flush(window);
            view.LibraryContentPanel.Offset.Should().Be(offset);
            host.Navigation.LibrarySelectedIndex.Should().Be(0);
            manager.Games[0].IsGamepadFocused.Should().BeTrue();
            navigation.Navigate(NavigationDirection.Down).Should().BeTrue();
            Flush(window);
            view.LibraryContentPanel.Offset.Y.Should().BeLessThan(offset.Y);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Catalog_sources_preserve_scroll_and_action_selection(bool actions, bool controller)
    {
        await using var session = new LauncherSession();
        var model = new CatalogViewModel();
        for (var i = 0; i < 50; i++) model.Sources.Add(new CatalogSourceListItem { SourceId = i.ToString(), Name = $"Source {i:D3}" });
        var host = new Host(controller) { MainContentZone = GamepadNavigationZone.CatalogSources };
        var view = new CatalogSourcesView();
        view.Configure(model, session, host, () => true, _ => Task.CompletedTask);
        host.Clear = () => { foreach (var source in model.Sources) source.IsGamepadFocused = false; };
        var window = Open(view, host);
        try
        {
            view.Navigation.ApplyCatalogGamepadSelection(0);
            Flush(window);
            if (actions) view.Navigation.ApplyCatalogSourceCardActionSelection(1);
            Flush(window);
            var zone = host.Navigation.ActiveZone;
            var scroll = view.GetVisualDescendants().OfType<ScrollViewer>().First(s => s.Extent.Height > s.Viewport.Height + 100);
            var offset = ScrollAway(scroll, window);
            Refocus(view.Navigation, host, session);
            Flush(window);
            scroll.Offset.Should().Be(offset);
            host.Navigation.ActiveZone.Should().Be(zone);
            host.Navigation.CatalogSelectedIndex.Should().Be(0);
            if (actions) host.Navigation.CatalogSourceCardActionIndex.Should().Be(1);
            model.Sources[0].IsGamepadFocused.Should().BeTrue();
            view.Navigation.Navigate(actions ? NavigationDirection.Right : NavigationDirection.Down).Should().BeTrue();
            Flush(window);
            scroll.Offset.Y.Should().BeLessThan(offset.Y);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task Catalog_apps_preserve_scroll_and_action_selection(bool grid, bool actions, bool controller)
    {
        await using var session = new LauncherSession();
        var store = new Store();
        store.Current.CatalogReviewUseGridView = grid;
        var host = new Host(controller) { MainContentZone = GamepadNavigationZone.CatalogReviewList };
        var view = new CatalogReviewView();
        view.Configure(view.Model, null!, new SettingsViewModel(store), session, host, null!, () => true, _ => { });
        var rows = Enumerable.Range(0, 100).Select(i => new CatalogSyncRowItem
        {
            IdentityKey = "app/" + i, Status = CatalogSyncStatus.InExternalOnly,
            External = new GameInfo { Name = $"App {i:D3}", Repository = "example/app" },
        }).ToArray();
        view.Model.Rows.UpdateWith(rows);
        var list = view.Navigation.GetActiveCatalogReviewItemsControl()!;
        ((Panel)list.Parent!).Children.Remove(list);
        view.Content = list;
        list.IsVisible = true;
        host.Clear = () => { foreach (var row in rows) row.IsGamepadFocused = false; view.Navigation.ClearCatalogReviewRowActionsGamepadFocus(); };
        var window = Open(view, host);
        try
        {
            view.Navigation.ApplyCatalogReviewRowSelection(0);
            Flush(window);
            if (actions) view.Navigation.ApplyCatalogReviewRowActionSelection(0);
            Flush(window);
            var zone = host.Navigation.ActiveZone;
            var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().First();
            var offset = ScrollAway(scroll, window);
            Refocus(view.Navigation, host, session);
            Flush(window);
            scroll.Offset.Should().Be(offset);
            host.Navigation.ActiveZone.Should().Be(zone);
            host.Navigation.CatalogReviewSelectedIndex.Should().Be(0);
            if (actions) host.Navigation.CatalogReviewRowActionIndex.Should().Be(0);
            rows[0].IsGamepadFocused.Should().BeTrue();
            if (actions)
            {
                view.Navigation.Navigate(NavigationDirection.Right).Should().BeTrue();
                Flush(window);
                scroll.Offset.Y.Should().BeLessThan(offset.Y);
            }
            view.Navigation.Navigate(NavigationDirection.Down).Should().BeTrue();
            Flush(window);
            scroll.Offset.Y.Should().BeLessThan(offset.Y);
        }
        finally { window.Close(); }
    }
}
