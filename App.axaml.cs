using AsyncImageLoader;
using AsyncImageLoader.Loaders;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using QuiverLauncher.Services;

namespace QuiverLauncher;

public partial class App : Application, INotifyPropertyChanged
{
    private string _currentVersionString = string.Empty;

    public string currentVersionString
    {
        get => _currentVersionString;
        set
        {
            if (_currentVersionString != value)
            {
                _currentVersionString = value;
                OnPropertyChanged();
            }
        }
    }

    public new event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = "")
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private static bool _hasCheckedForAppUpdates = false;
    private static readonly object _updateLock = new object();
    private static readonly SemaphoreSlim _updateCheckSemaphore = new(1, 1);
    private readonly TaskCompletionSource _startupSelfUpdatePromptCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TrayIcon? _trayIcon;
    private NativeMenuItem? _trayOpenItem;
    private NativeMenuItem? _trayCheckUpdatesItem;
    private NativeMenuItem? _trayExitItem;

    /// <summary>
    /// Completes when the startup Quiver self-update check (and any prompt) finishes or is skipped.
    /// Catalog startup prompts await this so the two dialogs do not stack.
    /// </summary>
    public Task StartupSelfUpdatePromptCompleted => _startupSelfUpdatePromptCompleted.Task;

    private readonly VelopackUpdateService _velopackUpdates = new();

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
#if DEBUG
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            Program.LogCrashFromUiThread("Dispatcher.UIThread.UnhandledException", e.Exception);
            e.Handled = true;
        };
#endif

        ConfigureAsyncImageLoaderCache();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;

            var mainWindow = new MainWindow();
            mainWindow._app = this;
            desktop.MainWindow = mainWindow;

            InitializeTrayIcon();
            mainWindow.ApplyTraySettingsFromApp();
        }

        base.OnFrameworkInitializationCompleted();

        lock (_updateLock)
        {
            if (!_hasCheckedForAppUpdates)
            {
                _hasCheckedForAppUpdates = true;
                Task.Run(async () =>
                {
                    try
                    {
                        await CheckForUpdatesAndApplyAsync(isManualCheck: false);
                    }
                    finally
                    {
                        _startupSelfUpdatePromptCompleted.TrySetResult();
                    }
                });
            }
            else
            {
                _startupSelfUpdatePromptCompleted.TrySetResult();
            }
        }
    }

    private void InitializeTrayIcon()
    {
        if (_trayIcon != null)
            return;

        _trayOpenItem = new NativeMenuItem("Open Quiver Launcher");
        _trayOpenItem.Click += (_, _) => RestoreMainWindowFromTray();

        _trayCheckUpdatesItem = new NativeMenuItem("Check for updates");
        _trayCheckUpdatesItem.Click += (_, _) => _ = CheckUpdatesFromTrayAsync();

        _trayExitItem = new NativeMenuItem("Exit");
        _trayExitItem.Click += (_, _) => ExitFromTray();

        var menu = new NativeMenu();
        menu.Items.Add(_trayOpenItem);
        menu.Items.Add(_trayCheckUpdatesItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(_trayExitItem);

        _trayIcon = new TrayIcon
        {
            Icon = CreateTrayWindowIcon(),
            ToolTipText = "Quiver Launcher",
            IsVisible = false,
            Menu = menu,
        };
        _trayIcon.Clicked += (_, _) => RestoreMainWindowFromTray();

        var icons = new TrayIcons { _trayIcon };
        TrayIcon.SetIcons(this, icons);
    }

    private static WindowIcon CreateTrayWindowIcon()
    {
        var icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon.ico");
        if (File.Exists(icoPath))
            return new WindowIcon(icoPath);

        using var stream = AssetLoader.Open(new Uri("avares://QuiverLauncher/Assets/app.png"));
        return new WindowIcon(stream);
    }

    public void SetTrayVisible(bool visible)
    {
        if (_trayIcon == null)
            InitializeTrayIcon();

        if (_trayIcon != null)
            _trayIcon.IsVisible = visible;
    }

    public void UpdateTrayTooltip(int pendingUpdatesCount, bool isChecking)
    {
        if (_trayIcon == null)
            return;

        if (isChecking)
        {
            _trayIcon.ToolTipText = "Quiver Launcher · Checking for updates…";
            return;
        }

        if (pendingUpdatesCount <= 0)
        {
            _trayIcon.ToolTipText = "Quiver Launcher";
            return;
        }

        _trayIcon.ToolTipText = pendingUpdatesCount == 1
            ? "Quiver Launcher · 1 app needs review"
            : $"Quiver Launcher · {pendingUpdatesCount} apps need review";
    }

    private void RestoreMainWindowFromTray()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
            desktop.MainWindow is MainWindow mainWindow)
        {
            mainWindow.RestoreFromTray();
        }
    }

    private async Task CheckUpdatesFromTrayAsync()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop ||
            desktop.MainWindow is not MainWindow mainWindow)
        {
            return;
        }

        if (!mainWindow.IsVisible)
            mainWindow.RestoreFromTray();

        await mainWindow.RunUpdateCheckAsync(promptForReview: true, isManualCheck: true);
    }

    private void ExitFromTray()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
            desktop.MainWindow is MainWindow mainWindow)
        {
            mainWindow.RequestExit();
            return;
        }

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
            lifetime.Shutdown();
    }

    private static void ConfigureAsyncImageLoaderCache()
    {
        try
        {
            var imagesCache = Path.Combine(QuiverLauncherPaths.CacheDirectory, "Images");
            Directory.CreateDirectory(imagesCache);

            var previous = ImageLoader.AsyncImageLoader;
            ImageLoader.AsyncImageLoader = new DiskCachedWebImageLoader(imagesCache);
            if (!ReferenceEquals(previous, ImageLoader.AsyncImageLoader))
                previous?.Dispose();
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Failed to configure AsyncImageLoader disk cache: {ex.Message}");
        }
    }
}
