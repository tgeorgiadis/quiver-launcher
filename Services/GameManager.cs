using QuiverLauncher.Core.Models;
using QuiverLauncher.Core.Services;
using QuiverLauncher.Models;
using QuiverLauncher.Services.Mods;
using QuiverLauncher.ViewModels;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net.Http;

namespace QuiverLauncher.Services
{
    public class GameManager : INotifyPropertyChanged, IDisposable
    {
        private static readonly QuiverLauncherProfile Profile = QuiverLauncherProfile.Instance;
        private readonly ISettingsStore _settingsStore;
        internal AppSettings CurrentSettings => _settingsStore.Current;
        public AppSettings _settings = new();
        private readonly HttpClient _httpClient;
        private readonly AppCatalogService _catalogService;
        private readonly ModProviderRegistry _modProviderRegistry;
        private bool _disposed;
        private string _appsFolder;
        private readonly string _cacheFolder;
        private List<GameInfo> _catalogApps = [];
        private List<GameInfo> _allGames = [];
        private readonly object _catalogInitializationLock = new();
        private Task? _catalogInitialization;

        public Func<Action, Task>? UiThreadInvoker { get; set; }

        private async Task RunOnUiThreadAsync(Action action)
        {
            var invoker = UiThreadInvoker;
            if (invoker == null)
            {
                action();
                return;
            }

            await invoker(action).ConfigureAwait(false);
        }

        public ObservableCollection<GameInfo> Games { get; set; } = [];
        public IReadOnlyList<GameInfo> LibraryApps => _catalogApps.Count > 0 ? _catalogApps : Games.ToArray();
        /// <summary>Session-only library search. Applied after Show scope and tag display filters.</summary>
        public string LibrarySearchText { get; set; } = "";
        public bool HasLibrarySearch => !string.IsNullOrWhiteSpace(LibrarySearchText);
        public bool HasNoLibrarySearchMatches =>
            !IsLibraryEmpty && HasLibrarySearch && Games.Count == 0;
        public HttpClient HttpClient => _httpClient;
        public AppCatalogService CatalogService => _catalogService;
        public ModProviderRegistry ModProviderRegistry => _modProviderRegistry;
        public string AppsFolder => _appsFolder;
        public string GamesFolder => _appsFolder;
        public string CacheFolder => _cacheFolder;

        private string _currentVersionString = string.Empty;
        public string CurrentVersionString
        {
            get => _currentVersionString;
            set
            {
                if (_currentVersionString != value)
                {
                    _currentVersionString = value;
                    OnPropertyChanged(nameof(CurrentVersionString));
                }
            }
        }

        public GameManager(
            ISettingsStore? settingsStore = null,
            HttpClient? httpClient = null,
            AppCatalogService? catalogService = null)
        {
            _settingsStore = settingsStore ?? SettingsStoreProvider.Default;
            _httpClient = httpClient ?? CreateDefaultHttpClient();
            _catalogService = catalogService ?? new AppCatalogService(this);

            try
            {
                _settings = _settingsStore.Load();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load settings in GameManager: {ex.Message}");
                _settings = new AppSettings();
            }

            QuiverLauncherPaths.EnsureUserDataRootExists();

            _appsFolder = !string.IsNullOrEmpty(_settings?.AppsPath)
                ? _settings.AppsPath
                : QuiverLauncherPaths.DefaultAppsDirectory;

            _cacheFolder = QuiverLauncherPaths.CacheDirectory;

            try
            {
                Directory.CreateDirectory(_appsFolder);
                Directory.CreateDirectory(_cacheFolder);
                GitHubApiCache.Initialize(_cacheFolder);
                ReleaseRequestCoordinator.Configure(_httpClient, _cacheFolder);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to create directories: {ex.Message}");
            }

            _modProviderRegistry = new ModProviderRegistry(_httpClient, _cacheFolder);

            LoadVersionString();
        }

        private static HttpClient CreateDefaultHttpClient()
        {
            var client = new HttpClient(new ReleaseApiTransport());
            client.DefaultRequestHeaders.Add("User-Agent", Profile.UserAgent);
            client.Timeout = TimeSpan.FromMinutes(30);
            return client;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
                _httpClient.Dispose();

            _disposed = true;
        }

        public async Task CheckAllUpdatesAsync()
        {
            await CheckInstalledUpdatesAsync(true, null, CancellationToken.None);
        }

        public async Task<LibraryCheckResult> CheckInstalledUpdatesAsync(bool manual,
            IProgress<AppCheckProgress>? progress, CancellationToken token)
        {
            var apps = LibraryApps.ToArray();
            foreach (var app in apps)
            {
                token.ThrowIfCancellationRequested();
                if (app.Status is GameStatus.Downloading or GameStatus.Installing or GameStatus.Updating) continue;
                await GameStatusService.CheckStatusAsync(app, _httpClient, _appsFolder,
                    checkRemoteVersion: false, applyCachedRelease: false);
            }
            return await new LibraryUpdateChecker(_httpClient, _settingsStore.Current).CheckAsync(
                apps.Where(a => a.IsInstalled), manual, TimeSpan.FromHours(6), progress, token);
        }

        public Task<LibraryCheckResult> RefreshUninstalledUpdatesAsync(CancellationToken token) =>
            new LibraryUpdateChecker(_httpClient, _settingsStore.Current).CheckAsync(
                LibraryApps.Where(a => a.Status == GameStatus.NotInstalled).ToArray(), false,
                TimeSpan.FromHours(24), null, token);

        private void LoadVersionString()
        {
            CurrentVersionString = LauncherVersionService.ReadInstalledVersion();
        }

        public GameInfo? GetLatestPlayedInstalledGame()
        {
            if (_catalogApps.Count == 0 || string.IsNullOrEmpty(_appsFolder))
                return null;

            var settings = _settingsStore.Current;
            settings.EnsureInitialized();

            DateTime latestTime = DateTime.MinValue;
            GameInfo? latestGame = null;
            foreach (var game in _catalogApps)
            {
                if (game == null || string.IsNullOrEmpty(game.FolderName))
                    continue;

                if (IsGameManuallyHidden(settings, game))
                    continue;

                var gamePath = game.GetInstallPath(_appsFolder);
                var lastPlayedPath = Path.Combine(gamePath, "LastPlayed.txt");
                if (File.Exists(lastPlayedPath))
                {
                    var timeString = File.ReadAllText(lastPlayedPath).Trim();
                    if (DateTime.TryParseExact(timeString, "yyyy-MM-dd HH:mm:ss", null, System.Globalization.DateTimeStyles.None, out DateTime lastPlayed) && lastPlayed > latestTime)
                    {
                        latestTime = lastPlayed;
                        latestGame = game;
                    }
                }
            }
            return latestGame;
        }

        public async Task LoadCustomAndCachedIconsAsync(bool allowDownload = true, CancellationToken cancellationToken = default)
        {
            if (Games == null || string.IsNullOrEmpty(_cacheFolder))
                return;

            foreach (var game in Games)
            {
                game?.LoadCustomIcon(_cacheFolder);
            }

            var tasks = Games
                .Where(g => g != null)
                .Select(g => g.LoadAndCacheDefaultIconAsync(_cacheFolder, _settingsStore.Current.GitHubApiToken, allowDownload, cancellationToken));

            await Task.WhenAll(tasks);
        }

        public async Task ClearIconCacheAsync()
        {
            try
            {
                var iconsDir = Path.Combine(_cacheFolder, "Icons");
                if (Directory.Exists(iconsDir))
                {
                    Directory.Delete(iconsDir, true);
                    await LoadCustomAndCachedIconsAsync();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to clear icon cache: {ex.Message}");
            }
        }

        public GameInfo? FindGameByName(string name)
        {
            return Games.FirstOrDefault(g => string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        public GameInfo? FindGameByFolderName(string folderName)
        {
            return Games.FirstOrDefault(g => string.Equals(g.FolderName, folderName, StringComparison.OrdinalIgnoreCase));
        }

        public Task LoadGamesAsync(bool forceUpdateCheck = false) =>
            LoadGamesCoreAsync(forceUpdateCheck, refreshRemoteCatalogs: true);

        internal async Task RefreshLoadedLibraryMetadataAsync(CancellationToken cancellationToken)
        {
            // Keep the same app instances and collection while online results arrive.
            var apps = _catalogApps.ToArray();
            await Task.WhenAll(apps.Select(async app =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                await app.CheckLatestVersionAsync(_httpClient, cancellationToken: cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                await app.LoadAndCacheDefaultIconAsync(_cacheFolder, app.GetReleaseApiToken(_settingsStore.Current));
            }));
        }

        /// <summary>
        /// Reloads the library from local apps.json without fetching catalog sources.
        /// Status is preserved for existing apps; only <paramref name="statusCheckIdentityKeys"/>
        /// (or every app when null) are re-checked on disk.
        /// Set <paramref name="allowNetwork"/> to false for catalog mutations: use
        /// cached releases and icons without waiting for remote metadata.
        /// </summary>
        public Task ReloadLibraryFromDiskAsync(IEnumerable<string>? statusCheckIdentityKeys = null, bool allowNetwork = true) =>
            LoadGamesCoreAsync(forceUpdateCheck: false, refreshRemoteCatalogs: false, statusCheckIdentityKeys, allowNetwork);

        private async Task LoadGamesCoreAsync(
            bool forceUpdateCheck,
            bool refreshRemoteCatalogs,
            IEnumerable<string>? statusCheckIdentityKeys = null,
            bool allowNetwork = true)
        {
            // Normalization writes apps.json. Finish it before reading the library,
            // and keep it within the caller's awaited load/session lifetime.
            Task initialization;
            lock (_catalogInitializationLock)
            {
                if (_catalogInitialization is { IsFaulted: true } or { IsCanceled: true }) _catalogInitialization = null;
                initialization = _catalogInitialization ??= _catalogService.ValidateAndFixLocalAppsJsonAsync();
            }
            await initialization.ConfigureAwait(false);

            _settings = _settingsStore.Load();
            _settings.EnsureInitialized();

            if (refreshRemoteCatalogs && AppCatalogService.MigrateLegacyCatalogSources(_settings))
                _settingsStore.Save(_settings);

            if (refreshRemoteCatalogs)
            {
                await _catalogService.RefreshAllSourcesAsync(_httpClient, _settings).ConfigureAwait(false);
                _settingsStore.Save(_settings);
            }

            Games ??= [];
            var previousByKey = CatalogCompareService.IndexByInstanceKey(_catalogApps);
            var allApps = await _catalogService.LoadLocalCatalogAsync(_settings).ConfigureAwait(false);
            _catalogApps = allApps.Where(app => app != null).Cast<GameInfo>().ToList();

            HashSet<string>? checkKeys = null;
            if (!refreshRemoteCatalogs && statusCheckIdentityKeys != null)
            {
                checkKeys = new HashSet<string>(
                    statusCheckIdentityKeys.Where(key => !string.IsNullOrWhiteSpace(key)),
                    StringComparer.OrdinalIgnoreCase);
            }

            foreach (var app in _catalogApps)
            {
                if (app == null)
                    continue;

                app.IsInLocalAppsJson = true;
                if (checkKeys == null)
                    continue;
                if (checkKeys.Contains(app.InstanceKey))
                    continue;
                if (previousByKey.TryGetValue(app.InstanceKey, out var previous))
                    CopyRuntimeLibraryState(app, previous);
            }

            if (!string.IsNullOrEmpty(_appsFolder))
            {
                var appsToCheck = checkKeys == null
                    ? _catalogApps
                    : _catalogApps.Where(app => app != null && checkKeys.Contains(app.InstanceKey)).ToList();

                await Task.WhenAll(appsToCheck.Where(app => app != null).Select(async app =>
                {
                    try
                    {
                        await app.CheckStatusAsync(_httpClient, _appsFolder, forceUpdateCheck, checkRemoteVersion: allowNetwork);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error checking status for {app.Name}: {ex.Message}");
                    }
                }));
            }

            if (refreshRemoteCatalogs)
            {
                await _catalogService.ApplyPendingCatalogChangeFlagsAsync(_catalogApps, _settings)
                    .ConfigureAwait(false);
            }

            await RebuildVisibleGamesAsync(_settings);

            await LoadCustomAndCachedIconsAsync(allowDownload: allowNetwork);
        }

        public async Task InsertCatalogAppAsync(GameInfo app, AppSettings settings)
        {
            app.GameManager = this;
            app.IsInLocalAppsJson = true;
            AppCatalogService.ApplyUserAppTags(app, settings);
            AppCatalogService.ApplyUserAppDisplayNames(app, settings);
            app.ShowLibraryUpdateBadges = settings.ShowLibraryAppUpdateBadges;
            app.LibraryCardTagMaxLines = settings.LibraryCardTagMaxLines;
            app.TruncateLibraryCardTitles = settings.TruncateLibraryCardTitles;
            AppCatalogService.RefreshLibraryCardTags([app], settings);
            await Task.Run(() => GameGridViewModel.GetLastPlayedTime(app, _appsFolder));
            await RunOnUiThreadAsync(() =>
            {
                if (_catalogApps.Any(g => g.InstanceKey.Equals(app.InstanceKey, StringComparison.OrdinalIgnoreCase))) return;
                _settings = settings;
                _catalogApps.Add(app);
                app.IsManuallyHidden = IsGameManuallyHidden(settings, app);
                if (FilterCatalogByListScope([app], settings).Count > 0)
                {
                    _allGames.Add(app);
                    if (GetVisibleGames(settings).Contains(app))
                    {
                        var low = 0;
                        var high = Games.Count;
                        while (low < high)
                        {
                            var middle = (low + high) / 2;
                            if (GameGridViewModel.CompareForInsertion(app, Games[middle], settings.SortBy ?? "Name", settings.IgnoreArticlesWhenSorting) >= 0)
                                low = middle + 1;
                            else high = middle;
                        }
                        Games.Insert(low, app);
                    }
                }
                OnPropertyChanged(nameof(IsLibraryEmpty));
                OnPropertyChanged(nameof(HasNoLibrarySearchMatches));
            });
        }

        private static void CopyRuntimeLibraryState(GameInfo target, GameInfo source)
        {
            target.Status = source.Status;
            target.InstalledVersion = source.InstalledVersion;
            target.LatestVersion = source.LatestVersion;
            target.HasPendingCatalogChanges = source.HasPendingCatalogChanges;
        }

        public async Task ExportGamesAsync()
        {
            try
            {
                var apps = await _catalogService.LoadLocalAppsAsync().ConfigureAwait(false);
                await _catalogService.SaveLocalAppsAsync(apps).ConfigureAwait(false);
                System.Diagnostics.Debug.WriteLine($"Apps exported successfully to {_catalogService.AppsConfigPath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error exporting apps: {ex.Message}");
            }
        }

        public async Task UpdateGamesFolderAsync(string newPath)
        {
            try
            {
                string targetPath;

                if (!string.IsNullOrWhiteSpace(newPath))
                {
                    if (!Directory.Exists(newPath))
                        Directory.CreateDirectory(newPath);

                    targetPath = newPath;
                }
                else
                {
                    targetPath = QuiverLauncherPaths.DefaultAppsDirectory;
                    Directory.CreateDirectory(targetPath);
                }

                _appsFolder = targetPath;
                Games.Clear();

                await LoadGamesAsync();

                OnPropertyChanged(nameof(Games));
                OnPropertyChanged(nameof(AppsFolder));
                OnPropertyChanged(nameof(GamesFolder));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating apps folder: {ex.Message}");
                _appsFolder = QuiverLauncherPaths.DefaultAppsDirectory;
                Directory.CreateDirectory(_appsFolder);
                throw;
            }
        }

        private static string GetHiddenGameKey(GameInfo game)
        {
            if (!string.IsNullOrWhiteSpace(game.FolderName))
                return $"folder:{game.FolderName}";

            if (!string.IsNullOrWhiteSpace(game.Repository))
                return $"repo:{game.Repository}";

            return $"name:{game.Name ?? string.Empty}";
        }

        public void ToggleUserHide(GameInfo game)
        {
            if (game == null)
                return;

            var settings = _settingsStore.Current;
            if (IsGameManuallyHidden(settings, game))
            {
                RemoveManuallyHiddenGame(settings, game);
            }
            else
            {
                AddManuallyHiddenGame(settings, game);
            }
            _settingsStore.Save(settings);
            _settings = settings;
            FilterGames(settings);
        }

        public bool IsManuallyHidden(GameInfo game)
        {
            return IsGameManuallyHidden(_settingsStore.Current, game);
        }

        public void HideGame(GameInfo game)
        {
            if (game == null)
                return;

            var settings = _settingsStore.Current;
            settings.EnsureInitialized();
            if (!IsGameManuallyHidden(settings, game))
            {
                AddManuallyHiddenGame(settings, game);
                _settingsStore.Save(settings);
                _settings = settings;
                FilterGames(settings);
            }
        }

        public void UnhideAllGames()
        {
            SetListScope(AppListScope.AllApps);
        }

        public void HideAllNonInstalledGames()
        {
            SetListScope(AppListScope.InstalledOnly);
        }

        public void SetListScope(AppListScope scope, AppSettings? settings = null)
        {
            settings ??= _settingsStore.Load();
            settings.EnsureInitialized();
            settings.ListScope = scope;
            settings.HiddenApps.Clear();
            _settingsStore.Save(settings);
            _settings = settings;
            FilterGames(settings);
        }

        private void FilterGames(AppSettings settings)
        {
            RebuildVisibleGames(settings);
        }

        public void ApplyTagDisplayFilter(AppSettings? settings = null)
        {
            settings ??= _settingsStore.Load();
            settings.EnsureInitialized();

            ApplyGamesList(GetVisibleGames(settings));
        }

        private async Task ApplyTagDisplayFilterAsync(AppSettings settings)
        {
            var gamesToShow = GetVisibleGames(settings);
            await RunOnUiThreadAsync(() => ApplyGamesList(gamesToShow));
        }

        private List<GameInfo> GetVisibleGames(AppSettings settings)
        {
            settings.EnsureInitialized();

            IEnumerable<GameInfo> visibleGames = _allGames;
            if (!string.IsNullOrWhiteSpace(settings.ActiveTagDisplayFilterId))
            {
                var filter = settings.TagDisplayFilters.FirstOrDefault(f =>
                    string.Equals(f.Id, settings.ActiveTagDisplayFilterId, StringComparison.OrdinalIgnoreCase));

                if (filter != null)
                {
                    visibleGames = _allGames.Where(game =>
                        TagHelper.MatchesDisplayFilter(
                            game.Tags,
                            filter.Tags,
                            filter.MatchMode,
                            filter.ExcludeTags,
                            filter.ExcludeMatchMode));
                }
            }

            if (!string.IsNullOrWhiteSpace(LibrarySearchText))
                visibleGames = visibleGames.Where(game => CatalogReviewSearch.Matches(game, LibrarySearchText));

            return visibleGames.ToList();
        }

        private void ApplyGamesList(List<GameInfo> gamesToShow)
        {
            var sorted = new GameGridViewModel().SortGames(
                gamesToShow,
                _settings.SortBy ?? "Name",
                _appsFolder ?? string.Empty,
                _settings.IgnoreArticlesWhenSorting);

            Games.Clear();
            foreach (var app in sorted)
                Games.Add(app);

            OnPropertyChanged(nameof(Games));
            OnPropertyChanged(nameof(HasLibrarySearch));
            OnPropertyChanged(nameof(HasNoLibrarySearchMatches));
        }

        private async Task RebuildVisibleGamesAsync(AppSettings settings)
        {
            settings.EnsureInitialized();
            SyncManuallyHiddenFlags(settings);

            _allGames = FilterCatalogByListScope(_catalogApps, settings);

            await ApplyTagDisplayFilterAsync(settings);

            await RunOnUiThreadAsync(() =>
            {
                OnPropertyChanged(nameof(IsLibraryEmpty));
                OnPropertyChanged(nameof(HasNoLibrarySearchMatches));
            });
        }

        private void RebuildVisibleGames(AppSettings settings)
        {
            settings.EnsureInitialized();
            SyncManuallyHiddenFlags(settings);

            _allGames = FilterCatalogByListScope(_catalogApps, settings);

            ApplyTagDisplayFilter(settings);
            OnPropertyChanged(nameof(IsLibraryEmpty));
            OnPropertyChanged(nameof(HasNoLibrarySearchMatches));
        }

        private static List<GameInfo> FilterCatalogByListScope(IEnumerable<GameInfo> catalogApps, AppSettings settings)
        {
            var showHiddenOnly = settings.ListScope == AppListScope.HiddenOnly;

            return catalogApps
                .Where(app => app != null)
                .Where(app =>
                {
                    var isHidden = IsGameManuallyHidden(settings, app);
                    return showHiddenOnly ? isHidden : !isHidden;
                })
                .Where(app => settings.ListScope != AppListScope.InstalledOnly
                    || app.Status != GameStatus.NotInstalled)
                .ToList();
        }

        private void SyncManuallyHiddenFlags(AppSettings settings)
        {
            foreach (var app in _catalogApps)
            {
                if (app != null)
                    app.IsManuallyHidden = IsGameManuallyHidden(settings, app);
            }
        }

        public bool IsLibraryEmpty => _catalogApps.Count == 0;

        internal void SetCatalogAppsAndFilter(List<GameInfo> catalogApps, AppSettings settings)
        {
            _catalogApps = catalogApps;
            _settings = settings;
            RebuildVisibleGames(settings);
        }

        private static bool IsGameManuallyHidden(AppSettings settings, GameInfo game)
        {
            if (settings?.ManuallyHiddenApps == null)
                return false;

            var key = GetHiddenGameKey(game);
            return settings.ManuallyHiddenApps.Contains(key) ||
                   (!string.IsNullOrWhiteSpace(game.Name) && settings.ManuallyHiddenApps.Contains(game.Name));
        }

        private static void AddManuallyHiddenGame(AppSettings settings, GameInfo game)
        {
            if (settings?.ManuallyHiddenApps == null)
                return;

            var key = GetHiddenGameKey(game);
            if (!settings.ManuallyHiddenApps.Contains(key))
                settings.ManuallyHiddenApps.Add(key);
        }

        private static void RemoveManuallyHiddenGame(AppSettings settings, GameInfo game)
        {
            if (settings?.ManuallyHiddenApps == null)
                return;

            var key = GetHiddenGameKey(game);
            settings.ManuallyHiddenApps.Remove(key);
            if (!string.IsNullOrWhiteSpace(game.Name))
                settings.ManuallyHiddenApps.Remove(game.Name);
        }

        public void RefreshGamesWithFilter(AppSettings settings)
        {
            _ = LoadGamesAsync();
        }

        public void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
