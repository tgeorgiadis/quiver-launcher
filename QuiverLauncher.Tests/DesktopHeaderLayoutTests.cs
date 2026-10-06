using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using System.Reflection;
using FluentAssertions;
using QuiverLauncher.Services;
using QuiverLauncher.Views;

namespace QuiverLauncher.Tests;

public class DesktopHeaderLayoutTests
{
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Library_up_visits_details_then_retry_and_down_reverses_the_path(bool keyboard)
    {
        var view = CreateView();
        var window = new Window { Content = view, Width = 1200, Height = 720 };
        try
        {
            window.Show();
            view.Shell.LastLauncherCheckNote = "Update check incomplete";
            view.Shell.UpdateCheckDetails = "Example app: Repository not found.";
            Settle(window);
            GamepadFocusChrome.SetKeyboardNavigationActive(keyboard);
            GamepadFocusChrome.SetActive(true, view);
            var navigation = (ShellChromeNavigation)typeof(MainView).GetField("_chromeNavigation", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view)!;
            var host = (IFeatureNavigationHost)view;
            var strip = view.FindControl<UpdateCheckStatusView>("UpdateCheckStatus")!;
            var details = strip.FindControl<Expander>("CheckDetailsExpander")!;
            host.Navigation.ActiveZone = GamepadNavigationZone.Library;
            navigation.EnterZone(new(GamepadNavigationZone.TopBar, null)).Should().BeTrue();
            details.IsFocused.Should().BeTrue();
            details.Classes.Should().Contain("gamepad-focused");
            navigation.ActivateTopBarSelection();
            details.IsExpanded.Should().BeTrue();
            navigation.ActivateTopBarSelection();
            details.IsExpanded.Should().BeFalse();
            navigation.Navigate(Services.NavigationDirection.Up).Should().BeTrue();
            var controls = navigation.CollectTopBarControls();
            var retry = controls[host.Navigation.TopBarSelectedIndex];
            retry.Should().BeOfType<Button>().Which.Content.Should().Be("Retry");
            retry.IsFocused.Should().BeTrue();
            navigation.Navigate(Services.NavigationDirection.Down).Should().BeTrue();
            details.IsFocused.Should().BeTrue();
            navigation.Navigate(Services.NavigationDirection.Down).Should().BeTrue();
            host.Navigation.ActiveZone.Should().Be(GamepadNavigationZone.Library);
            view.Shell.UpdateCheckDetails = "";
            Settle(window);
            navigation.EnterZone(new(GamepadNavigationZone.TopBar, null));
            navigation.CollectTopBarControls().Should().NotContain(details);
        }
        finally
        {
            GamepadFocusChrome.SetKeyboardNavigationActive(false);
            GamepadFocusChrome.SetActive(false, view);
            window.Close();
            await view.ShutdownAsync();
        }
    }

    [AvaloniaFact]
    public async Task Directional_navigation_visits_both_rows_before_leaving_header()
    {
        var view = CreateView();
        var window = new Window { Content = view, Width = 750, Height = 720 };
        try
        {
            window.Show(); Settle(window);
            GamepadFocusChrome.SetKeyboardNavigationActive(true);
            GamepadFocusChrome.SetActive(true, view);
            var navigation = (ShellChromeNavigation)typeof(MainView).GetField("_chromeNavigation", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view)!;
            var host = (IFeatureNavigationHost)view;
            var controls = navigation.CollectTopBarControls();
            navigation.ApplyTopBarGamepadSelection(controls.IndexOf(view.FindControl<Button>("SettingsButton")!));
            navigation.Navigate(Services.NavigationDirection.Down).Should().BeTrue();
            var selected = controls[host.Navigation.TopBarSelectedIndex];
            view.FindControl<LibraryToolbarView>("LibraryToolbar")!.NavigationControls().Should().Contain(selected);
            host.Navigation.ActiveZone.Should().Be(GamepadNavigationZone.TopBar);
            navigation.Navigate(Services.NavigationDirection.Up).Should().BeTrue();
            var header = view.FindControl<Grid>("HeaderLayoutGrid")!;
            Bounds(controls[host.Navigation.TopBarSelectedIndex], header).Top.Should().BeLessThan(Bounds(selected, header).Top);

            var search = view.FindControl<LibraryToolbarView>("LibraryToolbar")!.FindControl<TextBox>("LibrarySearchTextBox")!;
            GamepadTextInput.SkipNativeFocusOverride = () => true;
            navigation.ApplyTopBarGamepadSelection(controls.IndexOf(search));
            GamepadTextInput.IsEditing.Should().BeFalse();
            navigation.Navigate(Services.NavigationDirection.Up).Should().BeTrue();
            Bounds(controls[host.Navigation.TopBarSelectedIndex], header).Top.Should().BeLessThan(Bounds(search, header).Top);
            navigation.ApplyTopBarGamepadSelection(controls.IndexOf(search));
            navigation.Navigate(Services.NavigationDirection.Down).Should().BeTrue();
            host.Navigation.ActiveZone.Should().NotBe(GamepadNavigationZone.TopBar);
        }
        finally
        {
            GamepadTextInput.Reset(); GamepadFocusChrome.SetKeyboardNavigationActive(false); GamepadFocusChrome.SetActive(false);
            window.Close(); await view.ShutdownAsync();
        }
    }

    [AvaloniaFact]
    public async Task Narrow_window_buttons_remain_clickable()
    {
        var view = CreateView();
        var window = new Window { Content = view, Width = 750, Height = 720 };
        using var host = new DesktopHostController(window, view);
        void Click(string name)
        {
            var button = view.FindControl<Button>(name)!;
            var point = button.TranslatePoint(new Rect(button.Bounds.Size).Center, window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            if (name != "CloseLauncherButton") window.MouseUp(point, MouseButton.Left);
        }
        try
        {
            window.Show(); Settle(window);
            Click("MinimizeButton");
            window.WindowState.Should().Be(WindowState.Minimized);
            window.WindowState = WindowState.Normal; Settle(window);
            Click("ToggleMaximizeButton");
            window.WindowState.Should().Be(WindowState.Maximized);
            Click("ToggleMaximizeButton");
            window.WindowState.Should().Be(WindowState.Normal);
            view.SettingsModel.Current.CloseToTray = false;
            Click("CloseLauncherButton");
            window.IsVisible.Should().BeFalse();
        }
        finally { window.Close(); await view.ShutdownAsync(); }
    }

    [AvaloniaFact]
    public async Task Resize_preserves_search_editing_selection_and_sort()
    {
        var view = CreateView();
        var window = new Window { Content = view, Width = 1600, Height = 720 };
        try
        {
            window.Show(); Settle(window);
            var toolbar = view.FindControl<LibraryToolbarView>("LibraryToolbar")!;
            var search = toolbar.FindControl<TextBox>("LibrarySearchTextBox")!;
            var parent = search.Parent;
            search.Text = "dummy search";
            toolbar.SelectSort("NameDesc");
            GamepadTextInput.BeginEdit(search);
            search.SelectionStart = 2; search.SelectionEnd = 7;
            foreach (var width in new[] { 750, 1600, 900, 1280, 750, 1600 })
            {
                window.Width = width; Settle(window);
                search.Parent.Should().BeSameAs(parent);
                search.IsFocused.Should().BeTrue();
                GamepadTextInput.IsEditing.Should().BeTrue();
                search.IsReadOnly.Should().BeFalse();
                search.SelectedText.Should().Be("mmy s");
                search.Text.Should().Be("dummy search");
                view.Library.SortBy.Should().Be("NameDesc");
            }
            window.KeyTextInput("replacement");
            search.Text.Should().Be("dureplacementearch");
        }
        finally { GamepadTextInput.Reset(); window.Close(); await view.ShutdownAsync(); }
    }

    private static Rect Bounds(Control control, Control root) => new(control.TranslatePoint(default, root)!.Value, control.Bounds.Size);
    private static void Settle(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
    private static MainView CreateView() => new(new() { SettingsStore = new Store(), EnableInput = false, EnableMusic = false, InitializeOnOpen = false });
    private sealed class Store : ISettingsStore
    {
        public AppSettings Current { get; } = new() { FirstStartup = false, AppsPath = Path.Combine(Path.GetTempPath(), "quiver-header-tests", Guid.NewGuid().ToString("N")) };
        public AppSettings Load() => Current;
        public void Save(AppSettings settings) { }
    }
}
