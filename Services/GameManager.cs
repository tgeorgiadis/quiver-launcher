using QuiverLauncher.Core.Models;
using QuiverLauncher.Core.Services;
using QuiverLauncher.Models;
using QuiverLauncher.Services.Mods;
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
        public AppSettings _settings = new();
        private readonly HttpClient _httpClient;
        private readonly AppCatalogService _catalogService;
        private readonly ModProviderRegistry _modProviderRegistry;
        private bool _disposed;
        private string _appsFolder;
        private readonly string _cacheFolder;
        private List<GameInfo> _catalogApps = [];
        private List<GameInfo> _allGames = [];

        public static Func<Action, Task>? UiThreadInvoker { get; set; }

        private static async Task RunOnUiThreadAsync(Action action)
        {
            if (UiThreadInvoker != null)
                await UiThreadInvoker(action);
            else
                action();
        }

        public ObservableCollection<GameInfo> Games { get; set; } = [];
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
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to create directories: {ex.Message}");
            }

            _modProviderRegistry = new ModProviderRegistry(_httpClient, _cacheFolder);

            LoadVersionString();
            _ = _catalogService.ValidateAndFixLocalAppsJsonAsync();
        }

        private static HttpClient CreateDefaultHttpClient()
        {
            var client = new HttpClient();
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
            await LoadGamesAsync(forceUpdateCheck: true);
        }

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

        private async Task LoadCustomAndCachedIconsAsync()
        {
            if (Games == null || string.IsNullOrEmpty(_cacheFolder))
                return;

            foreach (var game in Games)
            {
                game?.LoadCustomIcon(_cacheFolder);
            }

            var tasks = Games
                .Where(g => g != null)
                .Select(g => g.LoadAndCacheDefaultIconAsync(_cacheFolder));

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

        public async Task LoadGamesAsync(bool forceUpdateCheck = false)
        {
            _settings = _settingsStore.Load();
            _settings.EnsureInitialized();

            if (AppCatalogService.MigrateLegacyCatalogSources(_settings))
                _settingsStore.Save(_settings);

            await _catalogService.RefreshAllSourcesAsync(_httpClient, _settings).ConfigureAwait(false);
            _settingsStore.Save(_settings);

            Games ??= [];
            var allApps = await _catalogService.LoadLocalCatalogAsync(_settings).ConfigureAwait(false);
            _catalogApps = allApps.Where(app => app != null).Cast<GameInfo>().ToList();

            foreach (var app in _catalogApps)
            {
                if (app == null)
                    continue;

                app.IsInLocalAppsJson = true;
            }

            if (!string.IsNullOrEmpty(_appsFolder))
            {
                await Task.WhenAll(_catalogApps.Where(app => app != null).Select(async app =>
                {
                    try
                    {
                        await app.CheckStatusAsync(_httpClient, _appsFolder, forceUpdateCheck);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error checking status for {app.Name}: {ex.Message}");
                    }
                }));
            }

            await _catalogService.ApplyPendingCatalogChangeFlagsAsync(_catalogApps, _settings)
                .ConfigureAwait(false);

            await RebuildVisibleGamesAsync(_settings);

            await LoadCustomAndCachedIconsAsync();
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

            return visibleGames.ToList();
        }

        private void ApplyGamesList(List<GameInfo> gamesToShow)
        {
            Games.Clear();
            foreach (var app in gamesToShow)
                Games.Add(app);

            OnPropertyChanged(nameof(Games));
        }

        private async Task RebuildVisibleGamesAsync(AppSettings settings)
        {
            settings.EnsureInitialized();
            SyncManuallyHiddenFlags(settings);

            _allGames = FilterCatalogByListScope(_catalogApps, settings);

            await ApplyTagDisplayFilterAsync(settings);

            await RunOnUiThreadAsync(() => OnPropertyChanged(nameof(IsLibraryEmpty)));
        }

        private void RebuildVisibleGames(AppSettings settings)
        {
            settings.EnsureInitialized();
            SyncManuallyHiddenFlags(settings);

            _allGames = FilterCatalogByListScope(_catalogApps, settings);

            ApplyTagDisplayFilter(settings);
            OnPropertyChanged(nameof(IsLibraryEmpty));
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
