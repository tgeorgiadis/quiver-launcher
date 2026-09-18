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
    public void Header_uses_hysteresis_and_ignores_queued_work_after_disposal()
    {
        var title = new Border { Width = 100, Height = 40 };
        var tools = new Border { Height = 32 };
        var actions = new Border { Width = 100, Height = 32 };
        var header = new Grid { ColumnDefinitions = new("Auto,*,Auto,Auto"), RowDefinitions = new("Auto,Auto"), Children = { title, tools, actions } };
        Grid.SetColumn(title, 1); Grid.SetColumn(tools, 2); Grid.SetColumn(actions, 3);
        var window = new Window { Content = header, Width = 410, Height = 120 };
        using var layout = new DesktopHeaderLayout(header, title, tools, actions, () => 200);
        try
        {
            window.Show(); Settle(window);
            Grid.GetRow(tools).Should().Be(1);
            window.Width = 420; Settle(window);
            Grid.GetRow(tools).Should().Be(1);
            window.Width = 430; Settle(window);
            Grid.GetRow(tools).Should().Be(0);
            window.Width = 410; Settle(window);
            Grid.GetRow(tools).Should().Be(1);
            window.Width = 500; layout.Refresh(); layout.Dispose(); layout.Dispose(); Settle(window);
            Grid.GetRow(tools).Should().Be(1);
        }
        finally { window.Close(); }
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

    [AvaloniaTheory]
    [InlineData(750)]
    [InlineData(900)]
    [InlineData(1024)]
    [InlineData(1280)]
    [InlineData(1600)]
    public async Task Header_controls_fit_all_shell_modes(double width)
    {
        var view = CreateView();
        var window = new Window { Content = view, Width = width, Height = 720 };
        try
        {
            window.Show();
            view.FindControl<LibraryToolbarView>("LibraryToolbar")!.SelectSort("NotInstalled");
            foreach (var showOsBar in new[] { false, true })
            foreach (var mode in new[] { "library", "sources", "review", "updates", "mods" })
            foreach (var settingsOpen in new[] { false, true })
            {
                view.SettingsModel.ShowOSTopBar = showOsBar;
                view.Shell.Mode = mode is "sources" or "review" ? MainViewMode.AppCatalog : MainViewMode.Library;
                view.Shell.CatalogSubView = mode == "review" ? AppCatalogSubView.Review : AppCatalogSubView.Sources;
                view.Shell.AppUpdatesOpen = mode == "updates";
                view.Shell.ModsOpen = mode == "mods";
                view.Shell.SettingsOpen = settingsOpen;
                ((IModsFeatureHost)view).RefreshShell();
                if (mode == "review")
                {
                    view.FindControl<TextBlock>("HeaderTitleText")!.Text = "Review: A very long community catalog source name that should be truncated";
                }
                Settle(window);
                var header = view.FindControl<Grid>("HeaderLayoutGrid")!;
                var title = view.FindControl<Grid>("HeaderTitleColumn")!;
                var actions = view.FindControl<StackPanel>("HeaderFixedActions")!;
                AssertInside(actions, header);
                Bounds(title, header).Right.Should().BeLessThanOrEqualTo(Bounds(actions, header).Left + 1);
                foreach (var name in new[] { "MinimizeButton", "ToggleMaximizeButton", "CloseLauncherButton", "SettingsButton", "CheckForUpdatesButton" })
                    AssertInside(view.FindControl<Button>(name)!, header);
                var tools = view.FindControl<Grid>("DesktopInlineTopBar")!;
                if (mode is "library" or "review")
                {
                    AssertInside(tools, header);
                    if (Grid.GetRow(tools) == 1)
                        Bounds(tools, header).Top.Should().BeGreaterThanOrEqualTo(Bounds(actions, header).Bottom);
                    else
                        Bounds(title, header).Right.Should().BeLessThanOrEqualTo(Bounds(tools, header).Left + 1);
                }
                else
                    Grid.GetRow(tools).Should().Be(0);
                if (mode == "library")
                {
                    var toolbar = view.FindControl<LibraryToolbarView>("LibraryToolbar")!;
                    foreach (var control in toolbar.NavigationControls())
                        AssertInside(control, header);
                    var search = toolbar.FindControl<TextBox>("LibrarySearchTextBox")!;
                    var add = toolbar.FindControl<Button>("AddNewEntryButton")!;
                    var sort = toolbar.FindControl<ComboBox>("SortByComboBox")!;
                    Bounds(search, header).Right.Should().BeLessThanOrEqualTo(Bounds(add, header).Left);
                    Bounds(add, header).Right.Should().BeLessThanOrEqualTo(Bounds(sort, header).Left);
                    if (width == 750) Grid.GetRow(tools).Should().Be(1);
                    if (width == 1600) Grid.GetRow(tools).Should().Be(0);
                }
            }
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
    private static void AssertInside(Control control, Control root)
    {
        var bounds = Bounds(control, root);
        bounds.Width.Should().BeGreaterThan(0, control.Name);
        bounds.Left.Should().BeGreaterThanOrEqualTo(-1, control.Name);
        bounds.Right.Should().BeLessThanOrEqualTo(root.Bounds.Width + 1, control.Name);
        bounds.Bottom.Should().BeLessThanOrEqualTo(root.Bounds.Height + 1, control.Name);
    }
    private static void Settle(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
    private static MainView CreateView() => new(new() { SettingsStore = new Store(), EnableInput = false, EnableMusic = false, InitializeOnOpen = false });
    private sealed class Store : ISettingsStore
    {
        public AppSettings Current { get; } = new() { FirstStartup = false, AppsPath = Path.Combine(Path.GetTempPath(), "quiver-header-tests", Guid.NewGuid().ToString("N")) };
        public AppSettings Load() => Current;
        public void Save(AppSettings settings) { }
    }
}
