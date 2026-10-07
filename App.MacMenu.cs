using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System;
using System.Threading.Tasks;
using QuiverLauncher.Services;

namespace QuiverLauncher;

public partial class App
{
    /// <summary>
    /// macOS app menu. Items on the Application's NativeMenu appear ahead of the
    /// Services/Hide/Quit items; without them macOS shows Avalonia's generic defaults
    /// ("About Avalonia"). Must run from <see cref="Initialize"/>: Avalonia reads the
    /// application menu once during platform setup, before the framework-initialized hook.
    /// </summary>
    private void InitializeMacAppMenu()
    {
        // Menu bar title and the "Hide"/"Quit" labels when running unbundled (dotnet run).
        Name = "Quiver Launcher";

        var about = new NativeMenuItem("About Quiver Launcher");
        about.Click += (_, _) => _ = ShowAboutWindowAsync();

        var settings = new NativeMenuItem("Settings…")
        {
            Gesture = new KeyGesture(Key.OemComma, KeyModifiers.Meta),
        };
        settings.Click += (_, _) => OpenSettingsFromMenu();

        var checkUpdates = new NativeMenuItem("Check for Updates…");
        checkUpdates.Click += (_, _) => _ = CheckUpdatesFromTrayAsync();

        NativeMenu.SetMenu(this, new NativeMenu
        {
            about,
            new NativeMenuItemSeparator(),
            settings,
            checkUpdates,
        });
    }

    /// <summary>macOS Window menu (⌘M), attached to the main window's menu bar.</summary>
    private static void InitializeMacWindowMenu(MainWindow mainWindow)
    {
        var minimize = new NativeMenuItem("Minimize")
        {
            Gesture = new KeyGesture(Key.M, KeyModifiers.Meta),
        };
        minimize.Click += (_, _) => mainWindow.WindowState = WindowState.Minimized;

        var zoom = new NativeMenuItem("Zoom");
        zoom.Click += (_, _) => mainWindow.WindowState =
            mainWindow.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        NativeMenu.SetMenu(mainWindow, new NativeMenu
        {
            new NativeMenuItem("Window") { Menu = new NativeMenu { minimize, zoom } },
        });
    }

    private void OpenSettingsFromMenu()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: MainWindow mainWindow })
            return;

        if (!mainWindow.IsVisible)
            mainWindow.RestoreFromTray();

        mainWindow.OpenSettings();
    }

    private async Task ShowAboutWindowAsync()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: { } owner })
            return;

        using var iconStream = AssetLoader.Open(new Uri("avares://QuiverLauncher/Assets/app.png"));
        var repositoryLink = new Button
        {
            Content = VelopackUpdateService.GitHubRepoUrl.Replace("https://", string.Empty),
            HorizontalAlignment = HorizontalAlignment.Center,
            Background = Brushes.Transparent,
            Foreground = (IBrush?)Resources["ThemeAccent"],
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        repositoryLink.Click += (_, _) => UrlLauncher.Open(VelopackUpdateService.GitHubRepoUrl);

        var window = new Window
        {
            Title = "About Quiver Launcher",
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = (IBrush?)Resources["ThemeBase"],
            Content = new StackPanel
            {
                Margin = new Thickness(32, 24),
                Spacing = 8,
                Width = 320,
                Children =
                {
                    new Image { Source = new Bitmap(iconStream), Width = 96, Height = 96, Margin = new Thickness(0, 0, 0, 8) },
                    new TextBlock
                    {
                        Text = "Quiver Launcher",
                        FontSize = 20,
                        FontWeight = FontWeight.SemiBold,
                        HorizontalAlignment = HorizontalAlignment.Center,
                    },
                    new TextBlock
                    {
                        Text = $"Version {LauncherVersionService.ReadInstalledVersion()}",
                        Foreground = (IBrush?)Resources["ThemeTextSecondary"],
                        HorizontalAlignment = HorizontalAlignment.Center,
                    },
                    new TextBlock
                    {
                        Text = "Download, install and run apps from GitHub and GitLab releases.",
                        TextWrapping = TextWrapping.Wrap,
                        TextAlignment = TextAlignment.Center,
                        Margin = new Thickness(0, 8, 0, 0),
                    },
                    repositoryLink,
                },
            },
        };

        DesktopInterfaceScaling.PrepareDialog(window, owner);
        await window.ShowDialog(owner);
    }
}
