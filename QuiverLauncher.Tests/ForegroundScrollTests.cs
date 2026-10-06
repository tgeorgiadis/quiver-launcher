using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
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
        var shell = new ShellViewModel();
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
}
