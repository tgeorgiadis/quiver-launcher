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
    public Action? CatalogsRefreshed { get; set; }
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

    public async Task<bool> TryPromptCatalogReviewAsync()
    {
        if (!UpdatePromptPolicy.ShouldPromptCatalogUpdates(_settings))
            return false;
        if (!_settings.AppCatalogSources.Any(s => s.Enabled && s.UpdateAvailable))
            return false;
        var alreadyInAppCatalog = Shell.Mode == MainViewMode.AppCatalog;
        var openCatalog = await ShowMessageBoxAsync(FormatPendingReviewSourcesMessage(_settings, includeOpenPrompt: true, alreadyInAppCatalog: alreadyInAppCatalog), "Catalog Updates", true);
        if (openCatalog && !_session.IsClosed)
            await _presentation.OpenCatalogReviewAsync();
        return true;
    }

    public async Task NotifyCatalogUpdatesIfNeededAsync()
    {
        if (_app != null)
            await _app.StartupSelfUpdatePromptCompleted.WaitAsync(_session.Token);
        _session.Token.ThrowIfCancellationRequested();
        if (!_settings.LocalFirstCatalogMigrationComplete)
        {
            await RunLocalFirstCatalogMigrationAsync();
            return;
        }

        if (!UpdatePromptPolicy.ShouldPromptCatalogUpdates(_settings))
            return;
        await TryPromptCatalogReviewAsync();
    }

    public List<GameInfo> GetPendingAppUpdates() => _gameManager.LibraryApps.Where(g => g.Status == GameStatus.UpdateAvailable).OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList();
    public List<GameInfo> GetAppUpdateReviewRows() => _gameManager.LibraryApps.Where(g => g.Status is GameStatus.UpdateAvailable or GameStatus.Updating or GameStatus.Installing).OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList();
    private async Task<bool> TryPromptAppUpdatesReviewAsync()
    {
        if (!UpdatePromptPolicy.ShouldPromptAppUpdateReviews(_settings))
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
        if (_showAutoFailures && failures.Count > 0 && _presentation.CanShowFailureSummary)
        {
            await ShowMessageBoxAsync("Some automatic updates could not be completed:\n\n" + string.Join('\n', failures), "Auto Update");
        }

        return updated;
    }

    private static string FormatPendingReviewSourcesMessage(AppSettings settings, bool includeOpenPrompt, bool alreadyInAppCatalog = false)
    {
        var pendingSources = settings.AppCatalogSources.Where(s => s.Enabled && s.PendingReviewCount > 0).OrderByDescending(s => s.PendingReviewCount).ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
        if (pendingSources.Count == 0)
        {
            if (!includeOpenPrompt)
                return "Catalog updates are available. Open App Catalog to review and sync apps.";
            return alreadyInAppCatalog ? "Catalog updates are available. Review changes now?" : "Catalog updates are available. Open App Catalog now to review changes?";
        }

        var lines = pendingSources.Select(s => $"• {s.Name} ({s.PendingReviewCount})");
        var body = "Catalog updates are available:\n\n" + string.Join("\n", lines);
        if (!includeOpenPrompt)
            return body + "\n\nOpen App Catalog to review and sync apps.";
        return alreadyInAppCatalog ? body + "\n\nReview these changes now?" : body + "\n\nOpen App Catalog to review these sources?";
    }

    private async Task RunLocalFirstCatalogMigrationAsync()
    {
        AppCatalogService.MigrateLegacyCatalogSources(_settings);
        _settingsModel.SaveCurrent();
        await ShowFirstRunWelcomeAsync();
        if (_session.IsClosed)
            return;
        _presentation.OpenCatalogSources();
        _settings.LocalFirstCatalogMigrationComplete = true;
        _settingsModel.SaveCurrent();
    }

    private Task ShowFirstRunWelcomeAsync() => _prompts.ShowWelcomeMessageBoxAsync(CommunityCatalogDefaults.FirstRunWelcomeMessage, CommunityCatalogDefaults.FirstRunWelcomeTitle);
    bool IUpdateCheckWorkflow.CanPresentResults => _presentation.CanPresentResults && !_session.IsClosed;

    Task<LibraryCheckResult> IUpdateCheckWorkflow.CheckAppsAsync(bool manual, IProgress<AppCheckProgress> progress, CancellationToken token) =>
        _gameManager.CheckInstalledUpdatesAsync(manual, progress, token);
    void IUpdateCheckWorkflow.ApplyCheckResult(UpdateCheckResult result, DateTime checkedAt)
    {
        RefreshUpdateCheckStatus(checkedAt);
        Shell.LastLauncherCheckNote = result.Note;
        Shell.UpdateCheckStatus = result.Note ?? "Installed apps checked";
        NotifyUpdateCheckUiProperties();
    }

    void IUpdateCheckWorkflow.QueuePostCheckWork(UpdateCheckResult result)
    {
        _ = _session.RunAsync(async () => { await Task.Yield(); await ApplyAutoUpdatesAsync(false); });
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
            await Stage("catalogs", async () =>
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_session.Token);
                deadline.CancelAfter(TimeSpan.FromSeconds(60));
                await _gameManager.CatalogService.RefreshAllSourcesAsync(_gameManager.HttpClient, _settings, deadline.Token);
                _session.Token.ThrowIfCancellationRequested();
                await _gameManager.CatalogService.ApplyPendingCatalogChangeFlagsAsync(_gameManager.LibraryApps, _settings);
                _settingsModel.SaveCurrent();
                CatalogsRefreshed?.Invoke();
            });
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
        var promptApps = isManualCheck || UpdatePromptPolicy.ShouldPromptAppUpdateReviews(_settings);
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
