using QuiverLauncher.Core.Models;
using QuiverLauncher.Models;
using QuiverLauncher.ViewModels;

namespace QuiverLauncher.Services;
/// <summary>Owns update policy, startup review ordering, and result presentation.</summary>
public sealed class LauncherUpdateWorkflow : IUpdateCheckWorkflow
{
    private readonly GameManager _gameManager;
    private readonly SettingsViewModel _settingsModel;
    private readonly LibraryViewModel Library;
    private readonly ShellViewModel Shell;
    private readonly LauncherSession _session;
    private readonly LauncherPromptService _prompts;
    private readonly IUpdatePresentation _presentation;
    private readonly Func<App?> _application;
    private readonly Func<Task> _refreshMods;
    private readonly Func<GameInfo, Task<bool>> _installAutomatically;
    private readonly VelopackUpdateService _velopackUpdateService;
    private App? _app => _application();
    private AppSettings _settings => _settingsModel.Current;
    private System.Collections.ObjectModel.ObservableCollection<GameInfo> Games => Library.Games;

    private readonly object _autoGate = new();
    private Task<int>? _automaticUpdates;
    private bool _showAutoFailures;
    public LauncherUpdateWorkflow(GameManager manager, SettingsViewModel settings, LibraryViewModel library, ShellViewModel shell, LauncherSession session, LauncherPromptService prompts, IUpdatePresentation presentation, Func<App?> application, VelopackUpdateService velopack, Func<Task> refreshMods, Func<GameInfo, Task<bool>> installAutomatically)
    {
        _gameManager = manager;
        _settingsModel = settings;
        Library = library;
        Shell = shell;
        _session = session;
        _prompts = prompts;
        _presentation = presentation;
        _application = application;
        _velopackUpdateService = velopack;
        _refreshMods = refreshMods;
        _installAutomatically = installAutomatically;
        if (AndroidLauncherUpdater.Current is {} android)
        {
            System.ComponentModel.PropertyChangedEventHandler changed = (_, _) => RefreshUpdateCheckStatus();
            android.PropertyChanged += changed;
            _session.OnShutdown(() => android.PropertyChanged -= changed);
        }
    }

    private Task ShowMessageBoxAsync(string message, string title) => _prompts.ShowMessageBoxAsync(message, title);
    private Task<bool> ShowMessageBoxAsync(string message, string title, bool question) => _prompts.ShowMessageBoxAsync(message, title, question);
    private Task? _secondaryRefresh;
    public Task<int> ApplyAutoUpdatesAsync(bool showFailureSummary)
    {
        if (_session.IsClosed) return Task.FromResult(0);
        lock (_autoGate)
        {
            if (_automaticUpdates is { IsCompleted: false })
            {
                _showAutoFailures |= showFailureSummary;
                return _automaticUpdates;
            }

            _showAutoFailures = showFailureSummary;
            return _automaticUpdates = ApplyAutoUpdatesCoreAsync();
        }
    }

    public void NotifyUpdateCheckUiProperties()
    {
        if (_session.IsClosed) return;
        Shell.NotifyUpdateCheckUiProperties();
        _presentation.UpdateStatusChanged();
    }

    public void RefreshUpdateCheckStatus(DateTime? manualCheckTime = null)
    {
        if (_session.IsClosed) return;
        if (manualCheckTime.HasValue)
            Shell.LastUpdateCheckTime = manualCheckTime.Value;
        var launcherPending = (AndroidLauncherUpdater.Current?.HasUpdate ?? false) || (_app?.IsLauncherUpdatePending() ?? false) || _velopackUpdateService.IsUpdatePendingRestart;
        var gamePending = AppUpdateSelection.CountManualPendingUpdates(_gameManager.LibraryApps);
        Shell.PendingUpdatesCount = LauncherUpdateService.ComputePendingUpdatesCount(launcherPending, gamePending);
    }

    /// <summary>Welcomes a new player once, after any launcher update prompt, then opens Browse.</summary>
    public async Task ShowFirstRunWelcomeIfNeededAsync()
    {
        if (_app != null)
            await _app.StartupSelfUpdatePromptCompleted.WaitAsync(_session.Token);
        _session.Token.ThrowIfCancellationRequested();
        // Named for the 3.x catalog migration; it now only marks that the welcome was shown.
        if (_settings.LocalFirstCatalogMigrationComplete)
        {
            await AskAboutUsageDataIfNeededAsync();
            return;
        }
        if (_settingsModel.KioskLocked)
        {
            _settings.LocalFirstCatalogMigrationComplete = true;
            _settingsModel.SaveCurrent();
            return;
        }
        await _prompts.ShowWelcomeMessageBoxAsync(FirstRunWelcomeMessage, FirstRunWelcomeTitle);
        if (_session.IsClosed)
            return;
        await AskAboutUsageDataIfNeededAsync();
        if (_session.IsClosed)
            return;
        _presentation.OpenBrowse();
        _settings.LocalFirstCatalogMigrationComplete = true;
        _settingsModel.SaveCurrent();
    }

    /// <summary>
    /// Asks once whether to send anonymous usage data: after the welcome for a new player, or on the
    /// first start after updating to a version that has it. Not while kiosk mode is locked.
    /// </summary>
    public async Task AskAboutUsageDataIfNeededAsync()
    {
        if (!Telemetry.Current.Available || _settings.UsageDataAsked || _settingsModel.KioskLocked || _session.IsClosed)
            return;
        var yes = await _prompts.ShowMessageBoxAsync(UsageDataQuestion, UsageDataTitle, isQuestion: true);
        // Closed before answering: ask again next time.
        if (_session.IsClosed)
            return;
        _settingsModel.SetUsageData(yes, "prompt");
    }

    public const string UsageDataTitle = "Help improve Quiver Launcher";
    public const string UsageDataQuestion =
        """
        Send anonymous usage data to help make Quiver Launcher better?

        This shares which features get used, which apps are installed and launched, and any errors, so problems can be found and fixed. It never includes your name, files or folders.

        You can change this at any time in Settings → General → Usage data.
        """;

    public const string FirstRunWelcomeTitle = "Welcome to Quiver Launcher";
    public const string FirstRunWelcomeMessage =
        """
        Discover community apps and manage downloads and updates in one place.

        Browse the Quiver catalog, choose what you'd like to add, then download it from your library.

        You'll need an internet connection to browse the catalog and download apps.
        """;

    public List<GameInfo> GetPendingAppUpdates() => _gameManager.LibraryApps.Where(g => g.Status == GameStatus.UpdateAvailable).OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList();
    public List<GameInfo> GetAppUpdateReviewRows() => _gameManager.LibraryApps.Where(g => g.Status is GameStatus.UpdateAvailable or GameStatus.Updating or GameStatus.Installing).OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList();
    private async Task<bool> TryPromptAppUpdatesReviewAsync()
    {
        if (!UpdatePromptPolicy.ShouldPromptAppUpdateReviews(_settings, _settingsModel.KioskLocked))
            return false;
        // After auto-updates run, anything still pending needs review (including auto apps
        // that could not resolve a platform asset).
        var pendingGames = GetPendingAppUpdates();
        if (pendingGames.Count == 0)
            return false;
        var openReview = await ShowMessageBoxAsync(AppUpdateReviewMessages.FormatPendingAppUpdatesMessage(pendingGames, includeOpenPrompt: true), "App Updates", true);
        if (openReview && !_session.IsClosed)
            _presentation.OpenAppUpdatesReview();
        return true;
    }

    /// <summary>
    /// Silently installs updates for apps with AutoUpdate enabled.
    /// Returns how many installs completed. Unresolved multi-asset apps are left pending.
    /// </summary>
    private async Task<int> ApplyAutoUpdatesCoreAsync()
    {
        var autoGames = AppUpdateSelection.GetAutoPendingUpdates(_gameManager.LibraryApps);
        if (autoGames.Count == 0)
            return 0;
        var updated = 0;
        var failures = new List<string>();
        foreach (var game in autoGames)
        {
            _session.Token.ThrowIfCancellationRequested();
            if (game.Status != GameStatus.UpdateAvailable)
                continue;
            try
            {
                var installed = await _installAutomatically(game);
                if (installed)
                    updated++;
            }
            catch (Exception ex)
            {
                failures.Add($"{game.Name}: {ex.Message}");
            }
        }

        if (_session.IsClosed) return updated;
        if (updated > 0)
        {
            Library.ApplySorting();
            Library.RefreshContinue();
        }

        RefreshUpdateCheckStatus();
        NotifyUpdateCheckUiProperties();
        if (_showAutoFailures && failures.Count > 0 && _presentation.CanShowFailureSummary && !_settingsModel.KioskLocked)
        {
            await ShowMessageBoxAsync("Some automatic updates could not be completed:\n\n" + string.Join('\n', failures), "Auto Update");
        }

        return updated;
    }

    bool IUpdateCheckWorkflow.CanPresentResults => _presentation.CanPresentResults && !_session.IsClosed;

    Task<LibraryCheckResult> IUpdateCheckWorkflow.CheckAppsAsync(bool manual, IProgress<AppCheckProgress> progress, CancellationToken token,
        IReadOnlySet<string>? appKeys) =>
        _gameManager.CheckInstalledUpdatesAsync(manual, progress, token, appKeys);
    void IUpdateCheckWorkflow.ApplyCheckResult(UpdateCheckResult result, DateTime checkedAt)
    {
        RefreshUpdateCheckStatus(checkedAt);
        Shell.LastLauncherCheckNote = result.Note;
        Shell.UpdateCheckDetails = result.Details;
        Shell.UpdateCheckStatus = result.Note ?? "Installed apps checked";
        NotifyUpdateCheckUiProperties();
    }

    void IUpdateCheckWorkflow.QueuePostCheckWork(UpdateCheckResult result, bool retry)
    {
        _ = _session.RunAsync(async () => { await Task.Yield(); await ApplyAutoUpdatesAsync(false); });
        if (retry) return;
        if (_secondaryRefresh is { IsCompleted: false }) return;
        _secondaryRefresh = _session.RunAsync(async () =>
        {
            await Task.Yield();
            async Task Stage(string name, Func<Task> action)
            {
                _session.Token.ThrowIfCancellationRequested();
                var watch = System.Diagnostics.Stopwatch.StartNew();
                try { await action(); }
                catch (OperationCanceledException) when (_session.IsClosed) { throw; }
                catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"Background update {name}: {ex.GetType().Name}"); }
                System.Diagnostics.Trace.WriteLine($"Background update {name}: elapsedMs={watch.ElapsedMilliseconds}");
            }
            await Stage("uninstalled apps", async () => { await _gameManager.RefreshUninstalledUpdatesAsync(_session.Token); });
            await Stage("mods", _refreshMods);
            await Stage("artwork", () => _gameManager.LoadCustomAndCachedIconsAsync(cancellationToken: _session.Token));
            if (!_session.IsClosed) RefreshUpdateCheckStatus();
        });
    }

    async Task IUpdateCheckWorkflow.ReportCheckFailureAsync(Exception error, bool present)
    {
        if (present)
            await ShowMessageBoxAsync($"Failed to check for updates: {error.Message}", "Error");
        if (!_session.IsClosed)
            RefreshUpdateCheckStatus();
    }

    async Task<ManualLauncherCheckResult> IUpdateCheckWorkflow.CheckLauncherAsync(bool isManualCheck, CancellationToken token)
    {
        if (AndroidLauncherUpdater.Current is {} android)
        {
            if (isManualCheck) await android.CheckAsync(true, token);
            return new ManualLauncherCheckResult
            {
                InstalledVersion = android.Installer.Installed.VersionName,
                CheckSucceeded = !android.HasError,
                ErrorMessage = android.Error,
                LauncherUpdatePending = android.HasUpdate,
                AvailableLauncherVersion = android.AvailableVersion
            };
        }
        ManualLauncherCheckResult launcherResult;
        if (isManualCheck && _app != null)
        {
            launcherResult = await _app.CheckForAppUpdatesManually().WaitAsync(token);
        }
        else
        {
            var pending = (_app?.IsLauncherUpdatePending() ?? false) || _velopackUpdateService.IsUpdatePendingRestart;
            launcherResult = new ManualLauncherCheckResult
            {
                InstalledVersion = _velopackUpdateService.CurrentVersion ?? LauncherVersionService.ReadInstalledVersion(AppDomain.CurrentDomain.BaseDirectory),
                CheckSucceeded = true,
                LauncherUpdatePending = pending,
                AvailableLauncherVersion = _velopackUpdateService.LastUpdateInfo?.TargetFullRelease.Version.ToString(),
            };
        }

        return launcherResult;
    }

    async Task IUpdateCheckWorkflow.PresentCheckResultAsync(ManualLauncherCheckResult launcherResult, int autoUpdated, bool isManualCheck)
    {
        var pendingApps = AppUpdateSelection.GetManualPendingUpdates(_gameManager.LibraryApps);
        var launcherApp = _app;
        var launcherPending = AndroidLauncherUpdater.Current == null && launcherResult.LauncherUpdatePending && launcherApp != null;
        if (_settingsModel.KioskLocked)
            return;
        var promptApps = isManualCheck || UpdatePromptPolicy.ShouldPromptAppUpdateReviews(_settings, kioskLocked: false);
        var reviewableApps = promptApps ? pendingApps : new List<GameInfo>();
        if (launcherPending && launcherApp != null && reviewableApps.Count > 0)
        {
            var choice = await _prompts.PromptCombinedUpdatesAsync(launcherResult.AvailableLauncherVersion, reviewableApps);
            if (!_session.IsClosed && choice == CombinedUpdateChoice.UpdateQuiver)
                await launcherApp.ApplyPendingLauncherUpdateAsync();
            else if (!_session.IsClosed && choice == CombinedUpdateChoice.UpdateApps)
                _presentation.OpenAppUpdatesReview();
        }
        else if (launcherPending && launcherApp != null)
        {
            await launcherApp.PromptForPendingLauncherUpdateAsync();
        }
        else if (reviewableApps.Count > 0)
        {
            var open = await ShowMessageBoxAsync(AppUpdateReviewMessages.FormatPendingAppUpdatesMessage(reviewableApps, includeOpenPrompt: true), "App Updates", true);
            if (open && !_session.IsClosed) _presentation.OpenAppUpdatesReview();
        }
        else if (autoUpdated > 0 && isManualCheck)
        {
            await ShowMessageBoxAsync(AppUpdateReviewMessages.FormatAutoUpdatedSummary(autoUpdated), "App Updates");
        }
    }
}
