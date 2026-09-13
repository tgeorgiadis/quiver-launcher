using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FluentAssertions;
using QuiverLauncher.Models;
using QuiverLauncher.Services;
using QuiverLauncher.ViewModels;
using QuiverLauncher.Views;

namespace QuiverLauncher.Tests;

public class LibraryNavigationTests
{
    [AvaloniaFact]
    public async Task Empty_library_buttons_are_selectable_and_activate_both_actions()
    {
        var store = new Store();
        using var manager = new GameManager(store);
        using var model = new LibraryViewModel(manager, new SettingsViewModel(store));
        await using var session = new LauncherSession();
        var view = new LibraryView();
        view.Configure(model, session, null!, (_, _) => { });
        var host = new Host();
        var navigation = new LibraryNavigation(view, host, session, () => true, () => true, () => false, _ => { }, _ => throw new Exception("Unexpected app launch"));
        view.Navigation = navigation;
        var window = new Avalonia.Controls.Window { Content = view, Width = 900, Height = 650 };
        var requests = new List<LibraryActionKind>();
        view.NavigationRequested += (action, _) => requests.Add(action);
        try
        {
            view.UpdateEmptyState(true);
            window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            navigation.RestoreFocus();
            host.Navigation.ActiveZone.Should().Be(GamepadNavigationZone.Library);
            view.EmptyLibraryAddButton.Classes.Should().Contain("gamepad-focused");
            navigation.Confirm().Should().BeTrue();
            requests.Should().Equal(LibraryActionKind.EmptyLibraryAddApp);
            navigation.Navigate(NavigationDirection.Right).Should().BeTrue();
            view.EmptyLibraryAddButton.Classes.Should().NotContain("gamepad-focused");
            view.EmptyLibraryBrowseButton.Classes.Should().Contain("gamepad-focused");
            navigation.Confirm().Should().BeTrue();
            requests.Should().Equal(LibraryActionKind.EmptyLibraryAddApp, LibraryActionKind.EmptyLibraryBrowseCatalog);
            navigation.Navigate(NavigationDirection.Up).Should().BeTrue();
            host.Transitions.Last().Zone.Should().Be(GamepadNavigationZone.TopBar);
            navigation.ApplyLibraryGamepadSelection(0);
            navigation.Navigate(NavigationDirection.Left).Should().BeTrue();
            host.Transitions.Last().Zone.Should().Be(GamepadNavigationZone.Sidebar);
            navigation.Options().Should().BeFalse();
        }
        finally { window.Close(); }
    }

    private sealed class Store : ISettingsStore
    {
        public AppSettings Current { get; } = new() { UseGridView = false, AppsPath = Path.Combine(Path.GetTempPath(), "quiver-navigation-tests", Guid.NewGuid().ToString("N")) };
        public AppSettings Load() => Current;
        public void Save(AppSettings settings) { }
    }
    private sealed class Host : IFeatureNavigationHost
    {
        public GamepadNavigationService Navigation { get; } = new();
        public GamepadNavigationZone MainContentZone => GamepadNavigationZone.Library;
        public bool IsFocusActive => true;
        public List<GamepadZoneTransition> Transitions { get; } = [];
        public Action Clear { get; set; } = () => { };
        public bool ApplyTransition(GamepadZoneTransition transition) { Transitions.Add(transition); return true; }
        public void ClearFocus() => Clear();
        public void ClearSidebarFocus() { }
        public void FocusCard(bool stealFocus) { }
    }
    [AvaloniaFact]
    public async Task List_movement_confirms_selected_app_and_requests_cross_zone_transition()
    {
        var store = new Store();
        using var manager = new GameManager(store);
        using var model = new LibraryViewModel(manager, new SettingsViewModel(store));
        var session = new LauncherSession();
        var view = new LibraryView();
        view.Configure(model, session, null!, (_, _) => { });
        manager.Games.Add(new GameInfo { Name = "First" });
        manager.Games.Add(new GameInfo { Name = "Second" });
        var host = new Host();
        host.Clear = () => { foreach (var game in manager.Games) game.IsGamepadFocused = false; };
        GameInfo? confirmed = null;
        var navigation = new LibraryNavigation(view, host, session, () => true, () => true, () => false, _ => { }, game => { confirmed = game; return Task.CompletedTask; });
        navigation.RestoreFocus();
        navigation.Navigate(NavigationDirection.Down).Should().BeTrue();
        manager.Games[0].IsGamepadFocused.Should().BeFalse();
        manager.Games[1].IsGamepadFocused.Should().BeTrue();
        navigation.Confirm().Should().BeTrue();
        confirmed.Should().BeSameAs(manager.Games[1]);
        navigation.Navigate(NavigationDirection.Left).Should().BeTrue();
        host.Transitions.Should().ContainSingle();
        await session.DisposeAsync();
        navigation.Confirm().Should().BeFalse();
        Dispatcher.UIThread.RunJobs();
    }
    [AvaloniaFact]
    public async Task Search_and_nested_overlay_keep_focus_during_library_rebuild()
    {
        var store = new Store();
        using var manager = new GameManager(store);
        using var model = new LibraryViewModel(manager, new SettingsViewModel(store));
        var session = new LauncherSession();
        var view = new LibraryView();
        view.Configure(model, session, null!, (_, _) => { });
        manager.Games.Add(new GameInfo { Name = "App", IsGamepadFocused = true });
        var host = new Host();
        host.Navigation.ActiveZone = GamepadNavigationZone.TopBar;
        var overlayOpen = false;
        var searchFocused = true;
        var navigation = new LibraryNavigation(view, host, session, () => true, () => !overlayOpen, () => searchFocused, _ => { }, _ => throw new Exception("Unexpected launch"));
        navigation.RestoreFocus();
        manager.Games[0].IsGamepadFocused.Should().BeFalse();
        host.Navigation.ActiveZone.Should().Be(GamepadNavigationZone.TopBar);
        searchFocused = false;
        overlayOpen = true;
        navigation.RestoreFocus();
        navigation.Navigate(NavigationDirection.Down).Should().BeFalse();
        navigation.Confirm().Should().BeFalse();
        host.Navigation.ActiveZone.Should().Be(GamepadNavigationZone.TopBar);
        overlayOpen = false;
        navigation.RestoreFocus();
        manager.Games[0].IsGamepadFocused.Should().BeTrue();
        await session.DisposeAsync();
    }
}
