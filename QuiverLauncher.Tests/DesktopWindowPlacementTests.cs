using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FluentAssertions;
using QuiverLauncher.Services;
using QuiverLauncher.ViewModels;

namespace QuiverLauncher.Tests;

public class DesktopWindowPlacementTests
{
    private static readonly PlacementScreen Primary = new(new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1040), 1, true);

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("42")]
    [InlineData("{\"Width\":\"bad\"}")]
    [InlineData("{\"Width\":-2,\"Height\":720,\"X\":0,\"Y\":0,\"Maximized\":false}")]
    [InlineData("{\"Width\":1e999,\"Height\":720,\"X\":0,\"Y\":0,\"Maximized\":false}")]
    [InlineData("{\"Width\":1280,\"Height\":720,\"X\":99999999999,\"Y\":0,\"Maximized\":false}")]
    [InlineData("{\"Width\":1280,\"Height\":720,\"X\":0,\"Y\":0,\"Maximized\":\"yes\"}")]
    public void Invalid_placement_does_not_discard_other_saved_settings(string placement)
    {
        var path = Path.Combine(Path.GetTempPath(), $"quiver-placement-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{\"ShowOSTopBar\":true,\"InterfaceScalePercent\":150,\"DesktopWindowPlacement\":" + placement + "}");
            var settings = new FileSettingsStore(path).Current;
            settings.DesktopWindowPlacement.Should().BeNull();
            settings.ShowOSTopBar.Should().BeTrue();
            settings.InterfaceScalePercent.Should().Be(150);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Negative_monitor_coordinates_and_greatest_overlap_are_preserved()
    {
        var left = new PlacementScreen(new PixelRect(-1920, 0, 1920, 1080), new PixelRect(-1920, 0, 1920, 1040), 1, false);
        var saved = new DesktopWindowPlacement(1000, 650, -1300, 100, true);
        DesktopPlacementGeometry.Restore(saved, [Primary, left], 100, default).Should().Be(saved);
        var straddling = saved with { X = -800 };
        DesktopPlacementGeometry.Restore(straddling, [Primary, left], 100, default)!.X.Should().Be(-1000);
    }

    [Fact]
    public void Disconnected_monitor_centres_on_primary_and_clamps_size_with_DPI_and_interface_scale()
    {
        var saved = new DesktopWindowPlacement(1280, 720, -2000, 0, false);
        var restored = DesktopPlacementGeometry.Restore(saved, [Primary], 100, default)!;
        restored.X.Should().Be(320);
        restored.Y.Should().Be(160);
        var highDpi = Primary with { Scaling = 2 };
        restored = DesktopPlacementGeometry.Restore(saved with { X = 100 }, [highDpi], 225, new Size(8, 30))!;
        restored.Width.Should().Be(952);
        restored.Height.Should().Be(490);
        restored.X.Should().Be(0);
        restored.Y.Should().Be(0);
        DesktopPlacementGeometry.Restore(saved with { Width = double.NaN }, [Primary], 100, default).Should().BeNull();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Host_saves_and_reloads_normal_and_maximized_placement_across_window_instances(bool topBar)
    {
        var path = Path.Combine(Path.GetTempPath(), $"quiver-placement-{Guid.NewGuid():N}.json");
        var store = new FileSettingsStore(path);
        store.Current.FirstStartup = false;
        store.Current.ShowOSTopBar = topBar;
        store.Current.EnableGamepadInput = false;
        store.Current.AppsPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        DesktopWindowPlacement? saved = null;
        try
        {
            for (var session = 0; session < 3; session++)
            {
                if (session > 0) store = new FileSettingsStore(path);
                var view = new MainView(new() { SettingsStore = store, InitializeOnOpen = false, EnableInput = false, EnableMusic = false });
                var window = new Window { Content = view, Width = 1280, Height = 720, WindowStartupLocation = WindowStartupLocation.CenterScreen };
                using var host = new DesktopHostController(window, view);
                try
                {
                    if (session == 0) window.WindowStartupLocation.Should().Be(WindowStartupLocation.CenterScreen);
                    else
                    {
                        window.WindowStartupLocation.Should().Be(WindowStartupLocation.Manual);
                        window.Width.Should().Be(saved!.Width);
                        window.Height.Should().Be(saved.Height);
                        window.Position.Should().Be(new PixelPoint(saved.X, saved.Y));
                        window.WindowState.Should().Be(session == 2 ? WindowState.Maximized : WindowState.Normal);
                    }
                    window.Show();
                    Dispatcher.UIThread.RunJobs();
                    if (session == 0)
                    {
                        window.Width = 1000; window.Height = 650; window.Position = new PixelPoint(80, 90);
                        Dispatcher.UIThread.RunJobs();
                    }
                    if (session == 1) { window.WindowState = WindowState.Maximized; Dispatcher.UIThread.RunJobs(); }
                    host.RequestExit();
                    var reloaded = new FileSettingsStore(path).Current.DesktopWindowPlacement;
                    if (session == 0) reloaded.Should().Be(new DesktopWindowPlacement(1000, 650, 80, 90, false));
                    else reloaded.Should().Be(saved! with { Maximized = true });
                    saved = reloaded;
                }
                finally { window.Close(); await view.ShutdownAsync(); }
            }
        }
        finally { File.Delete(path); }
    }

    [AvaloniaTheory]
    [InlineData("minimized")]
    [InlineData("fullscreen")]
    [InlineData("tray")]
    [InlineData("launch")]
    [InlineData("launch-tray")]
    public async Task Lifecycle_keeps_last_visible_bounds(string action)
    {
        var store = new Store();
        var view = new MainView(new() { SettingsStore = store, InitializeOnOpen = false, EnableInput = false, EnableMusic = false });
        var window = new Window { Content = view, Width = 1000, Height = 650 };
        using var host = new DesktopHostController(window, view);
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            window.Position = new PixelPoint(70, 80); Dispatcher.UIThread.RunJobs();
            window.WindowState = WindowState.Maximized; Dispatcher.UIThread.RunJobs();
            switch (action)
            {
                case "minimized": window.WindowState = WindowState.Minimized; break;
                case "fullscreen": window.WindowState = WindowState.FullScreen; break;
                case "tray": host.HideToTray(); window.Width = 400; window.Position = default; break;
                case "launch":
                case "launch-tray":
                    store.Current.CloseAfterLaunch = true;
                    store.Current.CloseToTray = action == "launch-tray";
                    host.CloseAfterLaunch(true);
                    break;
            }
            Dispatcher.UIThread.RunJobs();
            if (action != "launch") host.RequestExit();
            store.Current.DesktopWindowPlacement.Should().Be(new DesktopWindowPlacement(1000, 650, 70, 80, true));
        }
        finally { host.Dispose(); window.Close(); await view.ShutdownAsync(); }
    }

    [AvaloniaFact]
    public void Initial_frame_estimates_do_not_permanently_shrink_saved_bounds()
    {
        foreach (var maximized in new[] { false, true })
        {
            var saved = new DesktopWindowPlacement(1000, 650, 40, 50, maximized);
            var store = new Store();
            store.Current.DesktopWindowPlacement = saved;
            var window = new Window { Width = 1280, Height = 720 };
            using var controller = new DesktopWindowPlacementController(window, new SettingsViewModel(store));
            try
            {
                // Simulate the scaling controller's clamp using an overestimated frame before Show.
                window.Width = 900; window.Height = 600;
                controller.ApplyStartupState();
                window.Show(); Dispatcher.UIThread.RunJobs();
                controller.Flush();
                store.Current.DesktopWindowPlacement.Should().Be(saved);
                if (maximized) { window.WindowState = WindowState.Normal; Dispatcher.UIThread.RunJobs(); }
                window.Width.Should().Be(1000);
                window.Height.Should().Be(650);
            }
            finally { window.Close(); }
        }
    }

    [AvaloniaFact]
    public async Task Closing_to_tray_flushes_and_restoring_keeps_maximized_state()
    {
        var store = new Store();
        store.Current.CloseToTray = true;
        var view = new MainView(new() { SettingsStore = store, InitializeOnOpen = false, EnableInput = false, EnableMusic = false });
        var window = new Window { Content = view, Width = 1000, Height = 650 };
        using var host = new DesktopHostController(window, view);
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            window.Position = new PixelPoint(70, 80); Dispatcher.UIThread.RunJobs();
            window.WindowState = WindowState.Maximized;
            window.Close();
            window.IsVisible.Should().BeFalse();
            store.Current.DesktopWindowPlacement.Should().Be(new DesktopWindowPlacement(1000, 650, 70, 80, true));
            host.RestoreFromTray(); Dispatcher.UIThread.RunJobs();
            window.IsVisible.Should().BeTrue();
            window.WindowState.Should().Be(WindowState.Maximized);
            window.WindowState = WindowState.Normal; Dispatcher.UIThread.RunJobs();
            window.WindowState = WindowState.Maximized;
            window.WindowState = WindowState.Minimized;
            host.RequestExit();
            store.Current.DesktopWindowPlacement!.Maximized.Should().BeTrue();
        }
        finally { host.Dispose(); window.Close(); await view.ShutdownAsync(); }
    }

    [AvaloniaFact]
    public void Fullscreen_startup_takes_precedence_without_overwriting_windowed_bounds()
    {
        var saved = new DesktopWindowPlacement(1000, 650, 40, 50, true);
        var store = new Store();
        store.Current.DesktopWindowPlacement = saved;
        store.Current.StartFullscreen = true;
        var window = new Window { Width = 1280, Height = 720 };
        using var controller = new DesktopWindowPlacementController(window, new SettingsViewModel(store));
        try
        {
            controller.ApplyStartupState();
            window.WindowState.Should().Be(SteamDeckEnvironment.DesktopFullscreenWindowState());
            window.Show(); Dispatcher.UIThread.RunJobs();
            controller.Flush();
            store.Current.DesktopWindowPlacement.Should().Be(saved);
            window.WindowState = WindowState.Normal;
            window.Width.Should().Be(saved.Width);
            window.Height.Should().Be(saved.Height);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Movement_is_debounced_and_failed_saves_can_retry_without_refreshing_settings()
    {
        var store = new Store();
        var model = new SettingsViewModel(store);
        var notifications = 0;
        model.PropertyChanged += (_, _) => notifications++;
        var window = new Window { Width = 1000, Height = 650 };
        using var controller = new DesktopWindowPlacementController(window, model);
        try
        {
            controller.ApplyStartupState(); window.Show(); Dispatcher.UIThread.RunJobs();
            controller.Flush(); store.Saves = 0;
            for (var i = 1; i <= 5; i++)
            {
                window.Position = new PixelPoint(10 * i, 10 * i);
                Dispatcher.UIThread.RunJobs();
            }
            store.Saves.Should().Be(0);
            await Task.Delay(650, TestContext.Current.CancellationToken);
            Dispatcher.UIThread.RunJobs();
            store.Saves.Should().Be(1);
            notifications.Should().Be(0);
            store.Fail = true;
            window.Width = 1100; Dispatcher.UIThread.RunJobs();
            controller.Invoking(c => c.Flush()).Should().NotThrow();
            store.Fail = false;
            controller.Flush();
            store.Saves.Should().Be(2);
        }
        finally { window.Close(); }
    }

    private sealed class Store : ISettingsStore
    {
        public AppSettings Current { get; private set; } = new() { FirstStartup = false, EnableGamepadInput = false,
            AppsPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")) };
        public int Saves;
        public bool Fail;
        public AppSettings Load() => Current;
        public void Save(AppSettings settings)
        {
            if (Fail) throw new IOException("Simulated settings write failure");
            Saves++;
            Current = settings;
        }
    }
}
