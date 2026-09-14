using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using FluentAssertions;
using QuiverLauncher.Services;
using QuiverLauncher.Views;

namespace QuiverLauncher.Tests;

public class DesktopSidebarTests
{
    [AvaloniaTheory]
    [InlineData(false, 50)]
    [InlineData(true, 75)]
    [InlineData(false, 100)]
    [InlineData(true, 225)]
    public async Task Toggle_reclaims_space_keeps_focus_reachable_and_restores_across_sessions(bool osBar, int scale)
    {
        var root = Path.Combine(Path.GetTempPath(), "quiver-sidebar-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "settings.json");
        var store = new FileSettingsStore(path);
        store.Save(new AppSettings { FirstStartup = false, ShowOSTopBar = osBar, InterfaceScalePercent = scale, AppsPath = root });
        try
        {
            for (var session = 0; session < 3; session++)
            {
                var view = new MainView(new() { SettingsStore = new FileSettingsStore(path), EnableInput = false, EnableMusic = false, InitializeOnOpen = false });
                var window = new Window { Content = view, Width = 1200, Height = 800 };
                using var host = new DesktopHostController(window, view);
                try
                {
                    window.Show(); Settle(window);
                    var split = view.FindControl<SplitView>("MainSplitView")!;
                    var sidebar = view.FindControl<Border>("SidebarPanel")!;
                    var toggle = view.FindControl<Button>("DesktopSidebarToggleButton")!;
                    var content = view.FindControl<Grid>("MainGrid")!;
                    var navigation = (ShellChromeNavigation)typeof(MainView).GetField("_chromeNavigation", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view)!;
                    split.IsPaneOpen.Should().Be(session != 1);
                    toggle.IsEffectivelyVisible.Should().BeTrue();
                    navigation.CollectTopBarControls().First().Should().BeSameAs(toggle);
                    var before = content.Bounds.Width;
                    if (session == 2) continue;

                    var point = toggle.TranslatePoint(new Rect(toggle.Bounds.Size).Center, window)!.Value;
                    window.MouseDown(point, MouseButton.Left);
                    window.MouseUp(point, MouseButton.Left);
                    // The Fluent pane animates for up to 200 ms before layout reaches its final width.
                    await Task.Delay(300, TestContext.Current.CancellationToken);
                    Settle(window);
                    split.IsPaneOpen.Should().Be(session == 1);
                    content.Bounds.Width.Should().BeApproximately(before + (session == 0 ? 240 : -240), 1);
                    AutomationProperties.GetName(toggle).Should().Be(session == 0 ? "Show sidebar" : "Hide sidebar");
                    new FileSettingsStore(path).Current.DesktopSidebarCollapsed.Should().Be(session == 0);
                    sidebar.IsEnabled.Should().Be(session == 1);
                    if (session == 0)
                    {
                        navigation.CollectSidebarFocusableControls().Should().BeEmpty();
                        view.FindControl<Button>("LibraryNavButton")!.Focus().Should().BeFalse();
                        ((IFeatureNavigationHost)view).ApplyTransition(new(GamepadNavigationZone.Sidebar, 0));
                        ((IFeatureNavigationHost)view).Navigation.ActiveZone.Should().Be(GamepadNavigationZone.TopBar);
                        toggle.IsFocused.Should().BeTrue();
                        view.Shell.Mode = MainViewMode.AppCatalog;
                        ((IModsFeatureHost)view).RefreshShell(); Settle(window);
                        toggle.IsEffectivelyVisible.Should().BeTrue();
                        split.IsPaneOpen.Should().BeFalse();
                    }
                    else navigation.CollectSidebarFocusableControls().Should().NotBeEmpty();
                }
                finally { window.Close(); await view.ShutdownAsync(); }
            }
        }
        finally
        {
            GamepadTextInput.Reset();
            GamepadFocusChrome.SetKeyboardNavigationActive(false);
            GamepadFocusChrome.SetActive(false);
            Directory.Delete(root, true);
        }
    }

    private static void Settle(Window window)
    {
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
    }
}
