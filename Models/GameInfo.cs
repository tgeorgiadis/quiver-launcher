using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using QuiverLauncher.Core.Models;
using QuiverLauncher.Core.Services;
using QuiverLauncher.Services;
using QuiverLauncher.Services.Mods;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Linq;

namespace QuiverLauncher.Models
{

    public class GameInfo : INotifyPropertyChanged, IDisposable
    {
        private const string DefaultInstalledVersion = "v0.0.0";
        public event Action<Process?>? GameProcessStarted;
        private string? _latestVersion;
        private string? _installedVersion;
        private string? _preferredVersion;
        private string? _skippedUpdateVersion;
        private GameStatus _status = GameStatus.NotInstalled;
        private bool _isLoading;
        private bool _isGamepadFocused;
        private GitHubRelease? _cachedRelease;
        public GameManager? GameManager { get; set; }

        public string? Name { get; set; }
        public string? Repository { get; set; }

        /// <summary>
        /// Release host: <c>github</c> (default) or <c>gitlab</c>. Null/empty means GitHub.
        /// </summary>
        public string? RepositorySource { get; set; }

        /// <summary>Normalized repository source after unknown-value fallback to GitHub.</summary>
        public string EffectiveRepositorySource =>
            RepositorySourceHelper.Normalize(RepositorySource);

        /// <summary>Composite identity key used for catalog dedupe and version cache.</summary>
        public string IdentityKey =>
            RepositorySourceHelper.GetIdentityKey(RepositorySource, Repository);

        public string? FolderName { get; set; }
        public string? InstallPath { get; set; }
        public string? GameIconUrl { get; set; }
        public bool IsExperimental { get; set; }
        public bool IsCustom { get; set; }
        public string? CatalogSourceId { get; set; }
        public List<string> Tags { get; set; } = [];
        public List<string> FilesToAdd { get; set; } = [];
        public string? ModsPath { get; set; }
        public List<GameModSource> ModsSources { get; set; } = [];

        /// <summary>
        /// Mod archive install layout: <c>flat</c> (default) or <c>folderPerMod</c>.
        /// </summary>
        public string? ModsLayout { get; set; }

        /// <summary>Per-app Linux Windows runner: auto, wine, proton, or custom. Null/empty = auto.</summary>
        public string? LinuxRunner { get; set; }

        /// <summary>Wine prefix or Proton compatdata directory for this app.</summary>
        public string? LinuxPrefixPath { get; set; }

        /// <summary>Optional path to a specific Proton executable.</summary>
        public string? LinuxProtonPath { get; set; }

        /// <summary>Per-app custom launch command (placeholders: {exe}, {gamePath}, {exeDir}).</summary>
        public string? LinuxCustomLaunchCommand { get; set; }

        private bool _autoUpdate;
        /// <summary>When true, available updates install automatically without the review prompt.</summary>
        public bool AutoUpdate
        {
            get => _autoUpdate;
            set
            {
                if (_autoUpdate != value)
                {
                    _autoUpdate = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _hasModUpdates;
        public bool HasModUpdates
        {
            get => _hasModUpdates;
            set
            {
                if (_hasModUpdates != value)
                {
                    _hasModUpdates = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool CanOpenMods =>
            GameModsConfig.HasUsableConfig(ModsPath, ModsSources);
        private bool _isInLocalAppsJson;
        public bool IsInLocalAppsJson
        {
            get => _isInLocalAppsJson;
            set
            {
                if (_isInLocalAppsJson != value)
                {
                    _isInLocalAppsJson = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _isManuallyHidden;
        /// <summary>Runtime flag synced from settings; not persisted on the game entry.</summary>
        public bool IsManuallyHidden
        {
            get => _isManuallyHidden;
            set
            {
                if (_isManuallyHidden != value)
                {
                    _isManuallyHidden = value;
                    OnPropertyChanged();
                }
            }
        }
        private string? _customIconPath { get; set; }
        public string? CustomIconPath
        {
            get => _customIconPath;
            set
            {
                if (_customIconPath != value)
                {
                    _customIconPath = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IconUrl));
                    OnPropertyChanged(nameof(HasCustomIcon));
                }
            }
        }
        private string? _cachedDefaultIconPath;
        public bool HasCustomIcon => !string.IsNullOrEmpty(CustomIconPath) && File.Exists(CustomIconPath);

        public string IconUrl
        {
            get
            {
                // custom cover image
                if (!string.IsNullOrEmpty(CustomIconPath) && File.Exists(CustomIconPath))
                {
                    return CustomIconPath;
                }

                // Cached default icon
                if (!string.IsNullOrEmpty(_cachedDefaultIconPath) && File.Exists(_cachedDefaultIconPath))
                {
                    return _cachedDefaultIconPath;
                }

                // Direct URL (will download)
                return DefaultIconUrl;
            }
        }

        public bool HasStoredExecutable
        {
            get
            {
                if (string.IsNullOrEmpty(FolderName) || GameManager == null)
                    return false;

                try
                {
                    var gamePath = GetInstallPath(GameManager.GamesFolder);
                    if (string.IsNullOrWhiteSpace(gamePath))
                        return false;

                    var selectedExePath = Path.Combine(gamePath, "selected_executable.txt");
                    return File.Exists(selectedExePath);
                }
                catch
                {
                    return false;
                }
            }
        }

        public string DefaultIconUrl
        {
            get
            {
                if (!string.IsNullOrEmpty(GameIconUrl))
                    return GameIconUrl;

                return "/Assets/DefaultGame.png";
            }
        }

        private List<string>? _availableExecutables;
        public List<string>? AvailableExecutables
        {
            get => _availableExecutables;
            set
            {
                if (_availableExecutables != value)
                {
                    _availableExecutables = value;
                    DispatchPropertyChanged();
                    DispatchPropertyChanged(nameof(HasMultipleExecutables));
                    DispatchPropertyChanged(nameof(HasExecutableChoice));
                    DispatchPropertyChanged(nameof(CanLaunchOptions));
                    DispatchPropertyChanged(nameof(ShowWindowsRunnerOptions));
                }
            }
        }

        private string? _selectedExecutable;
        public string? SelectedExecutable
        {
            get => _selectedExecutable;
            set
            {
                if (_selectedExecutable != value)
                {
                    _selectedExecutable = value;
                    DispatchPropertyChanged();
                }
            }
        }

        public bool HasMultipleExecutables => AvailableExecutables?.Count > 1;
        public bool HasExecutableChoice
        {
            get
            {
                if (HasMultipleExecutables)
                    return true;

                return TryFindExecutableCandidates(out var executables, out _, expandWhenSingleCandidate: true)
                       && executables.Count > 1;
            }
        }

        private List<GitHubAsset>? _availableDownloads;
        public List<GitHubAsset>? AvailableDownloads
        {
            get => _availableDownloads;
            set
            {
                if (_availableDownloads != value)
                {
                    _availableDownloads = value;
                    DispatchPropertyChanged();
                }
            }
        }

        private GitHubAsset? _selectedDownload;
        public GitHubAsset? SelectedDownload
        {
            get => _selectedDownload;
            set
            {
                if (_selectedDownload != value)
                {
                    _selectedDownload = value;
                    DispatchPropertyChanged();
                }
            }
        }

        public bool HasMultipleDownloads => AvailableDownloads?.Count > 1;

        /// <summary>
        /// Clears a pending release-asset pick so the next NotInstalled install can re-prompt.
        /// Leaves <see cref="AvailableDownloads"/> intact for the picker menu.
        /// </summary>
        public void ClearDownloadSelection()
        {
            SelectedDownload = null;
        }

        public bool IsInstalled
        {
            get
            {
                return Status == GameStatus.Installed ||
                       Status == GameStatus.UpdateAvailable;
            }
        }

        public bool CanLaunch => Status == GameStatus.Installed;
        public bool CanDownload => Status == GameStatus.NotInstalled;
        public bool CanLocateInstall => Status == GameStatus.NotInstalled;
        public bool CanUpdate => Status == GameStatus.UpdateAvailable;
        public bool CanSkipUpdate => Status == GameStatus.UpdateAvailable;
        public bool CanChangeVersion => IsInstalled && !string.IsNullOrWhiteSpace(Repository);
        public bool CanVersionOptions => CanSkipUpdate || CanChangeVersion || IsInstalled;
        public bool CanLaunchOptions => HasExecutableChoice || IsInstalled;

        /// <summary>Linux-only: configure Wine/Proton runner and prefix for Windows-only installs.</summary>
        public bool ShowWindowsRunnerOptions =>
            RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
            && TryFindExecutableCandidates(out _, out var needsWine)
            && needsWine;
        public bool CanInfoOptions => !string.IsNullOrWhiteSpace(Repository);
        public bool HasPreferredVersion => !string.IsNullOrWhiteSpace(PreferredVersion);

        public string? LatestVersion
        {
            get => _latestVersion;
            set
            {
                if (_latestVersion != value)
                {
                    _latestVersion = value;

                    if (AreVersionsEquivalent(_preferredVersion, _latestVersion))
                    {
                        _preferredVersion = null;
                        DispatchPropertyChanged(nameof(PreferredVersion));
                        DispatchPropertyChanged(nameof(HasPreferredVersion));
                    }

                    DispatchPropertyChanged();
                    DispatchPropertyChanged(nameof(StatusText));
                }
            }
        }

        public string? InstalledVersion
        {
            get => _installedVersion;
            set
            {
                if (_installedVersion != value)
                {
                    _installedVersion = value;
                    DispatchPropertyChanged();
                    DispatchPropertyChanged(nameof(StatusText));
                }
            }
        }

        public string? PreferredVersion
        {
            get => _preferredVersion;
            set
            {
                if (AreVersionsEquivalent(value, LatestVersion))
                {
                    value = null;
                }

                if (_preferredVersion != value)
                {
                    _preferredVersion = value;
                    DispatchPropertyChanged();
                    DispatchPropertyChanged(nameof(HasPreferredVersion));
                }
            }
        }

        public string? SkippedUpdateVersion
        {
            get => _skippedUpdateVersion;
            set
            {
                if (_skippedUpdateVersion != value)
                {
                    _skippedUpdateVersion = value;
                    DispatchPropertyChanged();
                    DispatchPropertyChanged(nameof(StatusText));
                }
            }
        }

        public GameStatus Status
        {
            get => _status;
            set
            {
                if (_status != value)
                {
                    _status = value;
                    DispatchPropertyChanged();
                    DispatchPropertyChanged(nameof(ButtonText));
                    DispatchPropertyChanged(nameof(ButtonImage));
                    DispatchPropertyChanged(nameof(ButtonColor));
                    DispatchPropertyChanged(nameof(StatusText));
                    DispatchPropertyChanged(nameof(IsInstalled));
                    DispatchPropertyChanged(nameof(CanLaunch));
                    DispatchPropertyChanged(nameof(CanDownload));
                    DispatchPropertyChanged(nameof(CanLocateInstall));
                    DispatchPropertyChanged(nameof(CanUpdate));
                    DispatchPropertyChanged(nameof(CanSkipUpdate));
                    DispatchPropertyChanged(nameof(CanChangeVersion));
                    DispatchPropertyChanged(nameof(CanVersionOptions));
                    DispatchPropertyChanged(nameof(HasExecutableChoice));
                    DispatchPropertyChanged(nameof(CanLaunchOptions));
                    DispatchPropertyChanged(nameof(ShowWindowsRunnerOptions));
                    DispatchPropertyChanged(nameof(IsDownloading));
                    DispatchPropertyChanged(nameof(ProgressBarColor));
                }
            }
        }

        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                if (_isLoading != value)
                {
                    _isLoading = value;
                    DispatchPropertyChanged();
                }
            }
        }

        public bool IsGamepadFocused
        {
            get => _isGamepadFocused;
            set
            {
                if (_isGamepadFocused != value)
                {
                    _isGamepadFocused = value;
                    DispatchPropertyChanged();
                }
            }
        }

        public string ButtonText
        {
            get
            {
                return Status switch
                {
                    GameStatus.NotInstalled => "Download",
                    GameStatus.Installed => "Launch",
                    GameStatus.UpdateAvailable => "Update",
                    GameStatus.Downloading => "Downloading...",
                    GameStatus.Installing => "Installing...",
                    _ => "Download"
                };
            }
        }

        private Avalonia.Media.Imaging.Bitmap? _buttonImageCache;

        public Avalonia.Media.Imaging.Bitmap ButtonImage
        {
            get
            {
                var imagePath = Status switch
                {
                    GameStatus.NotInstalled => "avares://QuiverLauncher/Assets/Icons/button_download.png",
                    GameStatus.Installed => "avares://QuiverLauncher/Assets/Icons/button_launch.png",
                    GameStatus.UpdateAvailable => "avares://QuiverLauncher/Assets/Icons/button_update.png",
                    GameStatus.Downloading => "avares://QuiverLauncher/Assets/Icons/button_loading.png",
                    GameStatus.Installing => "avares://QuiverLauncher/Assets/Icons/button_loading.png",
                    _ => "avares://QuiverLauncher/Assets/Icons/button_loading.png"
                };

                // Only create new bitmap if image path changed
                if (_buttonImageCache == null || _lastImagePath != imagePath)
                {
                    _buttonImageCache?.Dispose();
                    _buttonImageCache = new Avalonia.Media.Imaging.Bitmap(
                        Avalonia.Platform.AssetLoader.Open(new Uri(imagePath)));
                    _lastImagePath = imagePath;
                }

                return _buttonImageCache;
            }
        }
        public void Dispose()
        {
            _buttonImageCache?.Dispose();
            _buttonImageCache = null;
        }

        private string? _lastImagePath;

        public IBrush ButtonColor
        {
            get
            {
                return Status switch
                {
                    GameStatus.NotInstalled => new SolidColorBrush(Color.FromRgb(0, 122, 255)),
                    GameStatus.Installed => new SolidColorBrush(Color.FromRgb(52, 199, 89)),
                    GameStatus.UpdateAvailable => new SolidColorBrush(Color.FromRgb(255, 149, 0)),
                    GameStatus.Downloading or GameStatus.Installing => new SolidColorBrush(Color.FromRgb(142, 142, 147)),
                    _ => new SolidColorBrush(Color.FromRgb(0, 122, 255))
                };
            }
        }

        public string StatusText
        {
            get
            {
                if (Status == GameStatus.Installed && !string.IsNullOrEmpty(InstalledVersion))
                    return $"Installed: {InstalledVersion}";

                if (Status == GameStatus.UpdateAvailable && !string.IsNullOrEmpty(LatestVersion))
                    return $"Update available!: {InstalledVersion} -> {LatestVersion}";
                return Status switch
                {
                    GameStatus.NotInstalled => "Not installed",
                    GameStatus.Downloading => "Downloading...",
                    GameStatus.Installing => "Installing...",
                    GameStatus.Updating => "Updating...",
                    _ => ""
                };
            }
        }

        private double _downloadProgress;
        public double DownloadProgress
        {
            get => _downloadProgress;
            set
            {
                if (_downloadProgress != value)
                {
                    _downloadProgress = value;
                    DispatchPropertyChanged();
                    DispatchPropertyChanged(nameof(IsDownloading));
                    DispatchPropertyChanged(nameof(ProgressBarColor));
                }
            }
        }

        public bool IsDownloading => Status == GameStatus.Downloading || Status == GameStatus.Installing || Status == GameStatus.Updating;

        public IBrush ProgressBarColor
        {
            get
            {
                if (Status == GameStatus.Updating)
                {
                    // Yellow to Green gradient based on progress
                    var progress = DownloadProgress / 100.0;
                    byte r = (byte)(255 - (255 - 52) * progress);
                    byte g = (byte)(149 + (199 - 149) * progress);
                    byte b = (byte)(0 + (89 - 0) * progress);
                    return new SolidColorBrush(Color.FromRgb(r, g, b));
                }
                else
                {
                    // Blue to Green gradient based on progress
                    var progress = DownloadProgress / 100.0;
                    byte r = (byte)(0 + (52 - 0) * progress);
                    byte g = (byte)(122 + (199 - 122) * progress);
                    byte b = (byte)(255 - (255 - 89) * progress);
                    return new SolidColorBrush(Color.FromRgb(r, g, b));
                }
            }
        }

        public void SetGameManager(GameManager gameManager)
        {
            GameManager = gameManager;
            DispatchPropertyChanged(nameof(HasExecutableChoice));
            DispatchPropertyChanged(nameof(CanLaunchOptions));
            DispatchPropertyChanged(nameof(ShowWindowsRunnerOptions));
        }

        /// <summary>
        /// Scans the install folder for launch candidates (top-level first, then recursive if needed).
        /// </summary>
        /// <param name="expandWhenSingleCandidate">
        /// When true, recurse if the top-level scan found at most one candidate (executable-choice UI).
        /// When false, recurse only if the top-level scan found none (matches launch).
        /// </param>
        private bool TryFindExecutableCandidates(
            out List<string> executables,
            out bool needsWine,
            bool expandWhenSingleCandidate = false)
        {
            executables = [];
            needsWine = false;

            if (!IsInstalled || string.IsNullOrWhiteSpace(FolderName) || GameManager == null)
                return false;

            try
            {
                var gamePath = GetInstallPath(GameManager.GamesFolder);
                if (!Directory.Exists(gamePath))
                    return false;

                executables = GameInstallationService.FindExecutableCandidates(
                    gamePath,
                    SearchOption.TopDirectoryOnly,
                    GetInstallationOptions(),
                    out needsWine);

                var shouldExpand = expandWhenSingleCandidate
                    ? executables.Count <= 1
                    : executables.Count == 0;

                if (shouldExpand)
                {
                    executables = GameInstallationService.FindExecutableCandidates(
                        gamePath,
                        SearchOption.AllDirectories,
                        GetInstallationOptions(),
                        out needsWine);
                }

                return true;
            }
            catch
            {
                executables = [];
                needsWine = false;
                return false;
            }
        }

        private void DispatchPropertyChanged([CallerMemberName] string propertyName = "")
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                OnPropertyChanged(propertyName);
            }
            else
            {
                Dispatcher.UIThread.InvokeAsync(() => OnPropertyChanged(propertyName));
            }
        }

        static Task ShowMessageBoxAsync(string message, string title) =>
            GameDialogService.ShowMessageBoxAsync(message, title);

        public Task CheckStatusAsync(HttpClient httpClient, string gamesFolder, bool forceUpdateCheck = false) =>
            GameStatusService.CheckStatusAsync(this, httpClient, gamesFolder, forceUpdateCheck);

        public string GetInstallPath(string gamesFolder)
        {
            if (!string.IsNullOrWhiteSpace(InstallPath))
                return InstallPath;

            return string.IsNullOrWhiteSpace(FolderName)
                ? string.Empty
                : Path.Combine(gamesFolder, FolderName);
        }

        private static string NormalizeVersionString(string? version) =>
            LauncherVersionService.NormalizeVersionString(version);

        private static bool IsNewerVersion(string candidateVersion, string baselineVersion) =>
            LauncherVersionService.IsNewerVersion(candidateVersion, baselineVersion);

        private static bool AreVersionsEquivalent(string? firstVersion, string? secondVersion) =>
            LauncherVersionService.AreVersionsEquivalent(firstVersion, secondVersion);

        private bool ShouldSuggestUpdate()
        {
            if (string.IsNullOrWhiteSpace(LatestVersion) ||
                string.IsNullOrWhiteSpace(InstalledVersion) ||
                InstalledVersion == "Unknown")
            {
                return false;
            }

            if (!IsNewerVersion(LatestVersion, InstalledVersion))
                return false;

            if (!string.IsNullOrWhiteSpace(SkippedUpdateVersion) &&
                !IsNewerVersion(LatestVersion, SkippedUpdateVersion))
            {
                return false;
            }

            return true;
        }

        public void SetVersionPreferences(string? preferredVersion, string? skippedUpdateVersion)
        {
            PreferredVersion = preferredVersion;
            SkippedUpdateVersion = skippedUpdateVersion;
            RefreshInstalledStatus();
        }

        public void SkipLatestUpdate()
        {
            if (string.IsNullOrWhiteSpace(LatestVersion))
                return;

            var effectiveInstalledVersion = InstalledVersion;
            if (string.IsNullOrWhiteSpace(effectiveInstalledVersion) ||
                effectiveInstalledVersion == "Unknown" ||
                AreVersionsEquivalent(effectiveInstalledVersion, DefaultInstalledVersion))
            {
                effectiveInstalledVersion = LatestVersion;
            }

            if (!string.IsNullOrWhiteSpace(effectiveInstalledVersion))
            {
                InstalledVersion = effectiveInstalledVersion;
                PreferredVersion = effectiveInstalledVersion;
            }

            SkippedUpdateVersion = LatestVersion;
            RefreshInstalledStatus();
        }

        public async Task ForceUpdateAsync(HttpClient httpClient, string gamesFolder)
        {
            if (string.IsNullOrWhiteSpace(FolderName))
                throw new InvalidOperationException("App configuration is invalid (missing folder name).");

            if (string.IsNullOrWhiteSpace(Repository))
                throw new InvalidOperationException("App configuration is invalid (missing repository).");

            var gamePath = GetInstallPath(gamesFolder);
            if (!Directory.Exists(gamePath))
                throw new DirectoryNotFoundException($"App folder not found: {gamePath}");

            var versionFile = Path.Combine(gamePath, "version.txt");

            IsLoading = true;
            try
            {
                _cachedRelease = null;
                LatestVersion = string.Empty;
                GitHubApiCache.RemoveCache(RepositorySource, Repository);

                if (File.Exists(versionFile))
                {
                    File.Delete(versionFile);
                }

                await CheckStatusAsync(httpClient, gamesFolder, forceUpdateCheck: true).ConfigureAwait(false);
            }
            finally
            {
                IsLoading = false;
            }
        }

        public void SetCustomIcon(string sourcePath, string cacheDirectory)
        {
            if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
                throw new ArgumentException("Source file does not exist or path is invalid.");

            if (string.IsNullOrEmpty(FolderName))
                throw new InvalidOperationException("FolderName is required for custom icon operations.");

            var customIconsDir = Path.Combine(cacheDirectory, "CustomIcons");
            Directory.CreateDirectory(customIconsDir);

            var extension = Path.GetExtension(sourcePath);
            var fileName = $"{FolderName}_custom{extension}";
            var destinationPath = Path.Combine(customIconsDir, fileName);

            try
            {
                if (!string.IsNullOrEmpty(CustomIconPath) && File.Exists(CustomIconPath))
                {
                    ClearImageFromMemory();

                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();

                    TryDeleteFileWithRetry(CustomIconPath, maxRetries: 3, delayMs: 100);
                }

                using (var sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var destStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    sourceStream.CopyTo(destStream);
                }

                if (File.Exists(destinationPath))
                {
                    var attributes = File.GetAttributes(destinationPath);
                    if ((attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                    {
                        File.SetAttributes(destinationPath, attributes & ~FileAttributes.ReadOnly);
                    }
                }

                CustomIconPath = destinationPath;

                OnPropertyChanged(nameof(CustomIconPath));
                OnPropertyChanged(nameof(IconUrl));
                OnPropertyChanged(nameof(HasCustomIcon));
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to set custom icon: {ex.Message}", ex);
            }
        }

        public void RemoveCustomIcon()
        {
            if (string.IsNullOrEmpty(CustomIconPath))
                return;

            var pathToDelete = CustomIconPath;

            try
            {
                CustomIconPath = "";

                OnPropertyChanged(nameof(CustomIconPath));
                OnPropertyChanged(nameof(IconUrl));
                OnPropertyChanged(nameof(HasCustomIcon));

                ClearImageFromMemory();

                Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    await Task.Delay(100);

                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();

                    try
                    {
                        if (File.Exists(pathToDelete))
                        {
                            TryDeleteFileWithRetry(pathToDelete, maxRetries: 5, delayMs: 200);
                        }
                    }
                    catch (Exception deleteEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"Warning: Failed to delete custom icon file {pathToDelete}: {deleteEx.Message}");
                    }
                }, DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to remove custom icon: {ex.Message}", ex);
            }
        }

        public void LoadCustomIcon(string cacheDirectory)
        {
            if (string.IsNullOrEmpty(FolderName))
                return;

            var customIconsDir = Path.Combine(cacheDirectory, "CustomIcons");
            if (!Directory.Exists(customIconsDir))
                return;

            var possibleExtensions = new[] { ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif", ".ico" };
            foreach (var ext in possibleExtensions)
            {
                var fileName = $"{FolderName}_custom{ext}";
                var iconPath = Path.Combine(customIconsDir, fileName);
                if (File.Exists(iconPath))
                {
                    try
                    {
                        var attributes = File.GetAttributes(iconPath);
                        if ((attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                        {
                            File.SetAttributes(iconPath, attributes & ~FileAttributes.ReadOnly);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to check/modify file attributes for {iconPath}: {ex.Message}");
                    }

                    CustomIconPath = iconPath;
                    break;
                }
            }
        }

        public void SaveSelectedExecutable(string executablePath, string gamesFolder)
        {
            if (string.IsNullOrEmpty(FolderName) || string.IsNullOrEmpty(executablePath))
                return;

            try
            {
                var gamePath = GetInstallPath(gamesFolder);
                var selectedExePath = Path.Combine(gamePath, "selected_executable.txt");
                File.WriteAllText(selectedExePath, executablePath);
                System.Diagnostics.Debug.WriteLine($"Saved selected executable for {Name}: {executablePath}");
                OnPropertyChanged(nameof(HasStoredExecutable));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save selected executable for {Name}: {ex.Message}");
            }
        }

        public string? LoadSelectedExecutable(string gamesFolder)
        {
            if (string.IsNullOrEmpty(FolderName))
                return null;

            try
            {
                var gamePath = GetInstallPath(gamesFolder);
                var selectedExePath = Path.Combine(gamePath, "selected_executable.txt");

                if (File.Exists(selectedExePath))
                {
                    var savedPath = File.ReadAllText(selectedExePath).Trim();
                    if (File.Exists(savedPath))
                    {
                        System.Diagnostics.Debug.WriteLine($"Loaded selected executable for {Name}: {savedPath}");
                        return savedPath;
                    }
                    else
                    {
                        // File no longer exists, delete the preference
                        File.Delete(selectedExePath);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load selected executable for {Name}: {ex.Message}");
            }

            return null;
        }

        public void ClearSelectedExecutable(string gamesFolder)
        {
            if (string.IsNullOrEmpty(FolderName))
                return;

            try
            {
                var gamePath = GetInstallPath(gamesFolder);
                var selectedExePath = Path.Combine(gamePath, "selected_executable.txt");

                if (File.Exists(selectedExePath))
                {
                    File.Delete(selectedExePath);
                    System.Diagnostics.Debug.WriteLine($"Cleared selected executable for {Name}");
                }

                SelectedExecutable = null;
                OnPropertyChanged(nameof(HasStoredExecutable));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to clear selected executable for {Name}: {ex.Message}");
            }
        }

        public async Task LoadAndCacheDefaultIconAsync(string cacheDirectory)
        {
            if (string.IsNullOrEmpty(FolderName))
                return;

            try
            {
                var iconsDir = Path.Combine(cacheDirectory, "Icons");
                Directory.CreateDirectory(iconsDir);

                var defaultUrl = DefaultIconUrl;

                // Only cache if it's an actual URL (not a local asset path)
                if (!defaultUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                    !defaultUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                // Create a safe filename from the URL
                var urlHash = GetUrlHash(defaultUrl);
                var extension = Path.GetExtension(defaultUrl);

                // If no extension in URL or it's too long, default to .png
                if (string.IsNullOrEmpty(extension) || extension.Length > 5 || extension.Contains('?'))
                    extension = ".png";

                var cachedIconPath = Path.Combine(iconsDir, $"{FolderName}_{urlHash}{extension}");

                // If cached icon exists and is valid, use it
                if (File.Exists(cachedIconPath))
                {
                    try
                    {
                        // Verify the file is valid by checking its size
                        var fileInfo = new FileInfo(cachedIconPath);
                        if (fileInfo.Length > 0)
                        {
                            _cachedDefaultIconPath = cachedIconPath;
                            OnPropertyChanged(nameof(IconUrl));
                            System.Diagnostics.Debug.WriteLine($"Using cached icon for {Name}: {cachedIconPath}");
                            return;
                        }
                    }
                    catch
                    {
                        // If file is corrupted, delete it and re-download
                        try { File.Delete(cachedIconPath); } catch { }
                    }
                }

                // Download icon if not cached
                System.Diagnostics.Debug.WriteLine($"Downloading icon for {Name} from {defaultUrl}");

                using var httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(10);
                httpClient.DefaultRequestHeaders.Add("User-Agent", "Github-Launcher/1.0");

                var iconData = await httpClient.GetByteArrayAsync(defaultUrl);

                // Save to cache
                await File.WriteAllBytesAsync(cachedIconPath, iconData);
                _cachedDefaultIconPath = cachedIconPath;
                OnPropertyChanged(nameof(IconUrl));
                System.Diagnostics.Debug.WriteLine($"Icon cached for {Name}: {cachedIconPath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to cache icon for {Name}: {ex.Message}");
                // Fallback to direct URL
            }
        }

        private static string GetUrlHash(string url)
        {
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(url));
            return Convert.ToHexString(hashBytes).Substring(0, 16).ToLowerInvariant();
        }

        private void ClearImageFromMemory()
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                OnPropertyChanged(nameof(IconUrl));
                OnPropertyChanged(nameof(HasCustomIcon));
            }, DispatcherPriority.Render);
        }

        private static void TryDeleteFileWithRetry(string filePath, int maxRetries = 5, int delayMs = 200)
        {
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    if (File.Exists(filePath))
                    {
                        var attributes = File.GetAttributes(filePath);
                        if ((attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                        {
                            File.SetAttributes(filePath, FileAttributes.Normal);
                        }

                        GC.Collect();
                        GC.WaitForPendingFinalizers();

                        File.Delete(filePath);
                        System.Diagnostics.Debug.WriteLine($"Successfully deleted file: {filePath}");
                        return;
                    }
                    else
                    {
                        return;
                    }
                }
                catch (IOException ex) when (i < maxRetries - 1)
                {
                    System.Diagnostics.Debug.WriteLine($"Attempt {i + 1}/{maxRetries} failed to delete {filePath}: {ex.Message}");
                    System.Threading.Thread.Sleep(delayMs * (i + 1));

                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                }
                catch (UnauthorizedAccessException ex) when (i < maxRetries - 1)
                {
                    System.Diagnostics.Debug.WriteLine($"Attempt {i + 1}/{maxRetries} - Access denied for {filePath}: {ex.Message}");

                    try
                    {
                        File.SetAttributes(filePath, FileAttributes.Normal);
                    }
                    catch { }

                    System.Threading.Thread.Sleep(delayMs * (i + 1));
                }
            }

            System.Diagnostics.Debug.WriteLine($"Unable to delete file after {maxRetries} attempts: {filePath}. File may be in use.");
        }

        private static string GetGitHubApiToken(AppSettings? settings = null)
        {
            if (settings != null)
                return settings.GitHubApiToken ?? string.Empty;

            try
            {
                return AppSettings.Load()?.GitHubApiToken ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string GetGitLabApiToken(AppSettings? settings = null)
        {
            if (settings != null)
                return settings.GitLabApiToken ?? string.Empty;

            try
            {
                return AppSettings.Load()?.GitLabApiToken ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        public string GetReleaseApiToken(AppSettings? settings = null) =>
            RepositorySourceHelper.IsGitLab(RepositorySource)
                ? GetGitLabApiToken(settings)
                : GetGitHubApiToken(settings);

        public async Task<bool> PerformActionAsync(
            HttpClient httpClient,
            string gamesFolder,
            AppSettings settings,
            IGameDownloadDialogs? dialogs = null)
        {
            if (string.IsNullOrEmpty(FolderName))
            {
                await ShowMessageBoxAsync("App configuration is invalid (missing folder name).", "Configuration Error");
                return false;
            }

            switch (Status)
            {
                case GameStatus.NotInstalled:
                case GameStatus.UpdateAvailable:
                    await GameDownloadInstallService.DownloadAndInstallAsync(
                        this, httpClient, gamesFolder, GetLatestRelease(), settings, _status, dialogs);
                    return false;

                case GameStatus.Installed:
                    return await GameLaunchService.LaunchAsync(this, gamesFolder);

                default:
                    return false;
            }
        }

        public GitHubRelease? GetLatestRelease() => _cachedRelease;

        public bool TrySelectPlatformDownload(AppSettings settings) =>
            GameDownloadService.TrySelectPlatformDownload(this, GetLatestRelease(), settings);

        internal GameInstallationOptions GetInstallationOptions() =>
            new()
            {
                Log = message => Debug.WriteLine(message),
                AdditionalMetadataFileNames = AppFilesToAddService.Normalize(FilesToAdd),
            };

        internal void ApplyCachedRelease(string version, GitHubRelease? release)
        {
            LatestVersion = version;
            _cachedRelease = release;
        }

        internal async Task CheckLatestVersionAsync(HttpClient httpClient, bool forceCheck = false)
        {
            if (string.IsNullOrEmpty(Repository))
            {
                System.Diagnostics.Debug.WriteLine($"Warning: Repository is null or empty for game {Name}");
                return;
            }

            try
            {
                if (!forceCheck && !GitHubApiCache.NeedsUpdateCheck(RepositorySource, Repository))
                {
                    if (GitHubApiCache.TryGetCachedVersion(RepositorySource, Repository, out var cachedData) && cachedData != null)
                    {
                        ApplyCachedRelease(cachedData.Version, cachedData.CachedRelease);
                        RefreshInstalledStatus();
                    }

                    return;
                }

                var result = await ReleaseSourceRegistry.Default.FetchReleasesAsync(
                    httpClient,
                    RepositorySource,
                    Repository,
                    GetReleaseApiToken(),
                    GitHubApiCache.GetETag(RepositorySource, Repository)).ConfigureAwait(false);

                if (result.IsNotModified)
                {
                    if (GitHubApiCache.TryGetCachedVersion(RepositorySource, Repository, out var existingCache) && existingCache != null)
                    {
                        ApplyCachedRelease(existingCache.Version, existingCache.CachedRelease);
                        GitHubApiCache.SetCache(
                            RepositorySource,
                            Repository,
                            existingCache.Version,
                            existingCache.ETag,
                            existingCache.CachedRelease);
                        RefreshInstalledStatus();
                    }

                    return;
                }

                var latestRelease = result.Releases.FirstOrDefault();
                if (latestRelease != null && !string.IsNullOrWhiteSpace(latestRelease.tag_name))
                {
                    ApplyCachedRelease(latestRelease.tag_name, latestRelease);
                    GitHubApiCache.SetCache(
                        RepositorySource,
                        Repository,
                        latestRelease.tag_name,
                        result.ETag ?? string.Empty,
                        latestRelease);
                    RefreshInstalledStatus();
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"No releases found for {IdentityKey}");
                }
            }
            catch (HttpRequestException ex)
            {
                System.Diagnostics.Debug.WriteLine($"Network error fetching latest version for {IdentityKey}: {ex.Message}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error fetching latest version for {IdentityKey}: {ex.Message}");
            }
        }

        internal void RefreshInstalledStatus()
        {
            if (ShouldSuggestUpdate())
                Status = GameStatus.UpdateAvailable;
            else if (Status != GameStatus.Downloading && Status != GameStatus.Installing && !string.IsNullOrWhiteSpace(InstalledVersion))
                Status = GameStatus.Installed;
        }

        internal void NotifyMultipleDownloadsChanged()
        {
            OnPropertyChanged(nameof(HasMultipleDownloads));
            OnPropertyChanged(nameof(AvailableDownloads));
        }

        internal void NotifyMultipleExecutablesChanged()
        {
            OnPropertyChanged(nameof(HasMultipleExecutables));
            OnPropertyChanged(nameof(AvailableExecutables));
        }

        internal void RaiseGameProcessStarted(Process? process) => GameProcessStarted?.Invoke(process);

        internal void UpdateLastPlayedTime(string gamePath)
        {
            if (string.IsNullOrEmpty(gamePath))
                return;

            try
            {
                if (!Directory.Exists(gamePath))
                {
                    System.Diagnostics.Debug.WriteLine($"Cannot update LastPlayed: directory does not exist: {gamePath}");
                    return;
                }

                var lastPlayedPath = Path.Combine(gamePath, "LastPlayed.txt");
                var currentTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                File.WriteAllText(lastPlayedPath, currentTime);
            }
            catch (UnauthorizedAccessException ex)
            {
                System.Diagnostics.Debug.WriteLine($"Permission denied updating LastPlayed.txt for {Name}: {ex.Message}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to update LastPlayed.txt for {Name}: {ex.Message}");
            }
        }

        public async Task<List<GitHubRelease>> FetchReleasesAsync(HttpClient httpClient)
        {
            if (string.IsNullOrWhiteSpace(Repository))
                return [];

            return await ReleaseSourceRegistry.Default.FetchReleasesWithAssetsAsync(
                httpClient,
                RepositorySource,
                Repository,
                GetReleaseApiToken()).ConfigureAwait(false);
        }
        public async Task InstallReleaseAsync(HttpClient httpClient, string gamesFolder, AppSettings settings, GitHubRelease release, GitHubAsset selectedAsset)
        {
            SelectedDownload = selectedAsset;
            await GameDownloadInstallService.DownloadAndInstallAsync(
                this, httpClient, gamesFolder, release, settings, Status);
        }

        public static string? GetPlatformIcon(string assetName)
        {
            var assetNameLower = assetName.ToLowerInvariant();

            // Check for Windows
            if (HasAnyOf(assetNameLower, "windows", "win64", "win32", "win-x64", "win-x86", "-win.", "_win.", ".exe", ".msi") ||
                System.Text.RegularExpressions.Regex.IsMatch(assetNameLower, @"[_-]win[_-]|[_-]win\d|^win[_-]"))
            {
                // Exclude false positives
                if (!HasAnyOf(assetNameLower, "linux", "macos", "darwin", ".deb", ".rpm", ".appimage", ".dmg"))
                {
                    return "avares://QuiverLauncher/Assets/Icons/platform_win.png";
                }
            }

            // Check for macOS
            if (HasAnyOf(assetNameLower, "macos", "osx", "darwin", ".dmg", ".pkg") ||
                (assetNameLower.Contains("mac") && !assetNameLower.Contains("machin")))
            {
                // Exclude false positives
                if (!HasAnyOf(assetNameLower, "linux", "windows", "win32", "win64", ".exe"))
                {
                    return "avares://QuiverLauncher/Assets/Icons/platform_mac.png";
                }
            }

            // Check for Linux
            if (HasAnyOf(assetNameLower, "linux", ".appimage", ".deb", ".rpm", "tar.gz", "tar.xz"))
            {
                // Exclude false positives
                if (!HasAnyOf(assetNameLower, "windows", "win32", "win64", "macos", "osx", "darwin", ".exe", ".dmg"))
                {
                    return "avares://QuiverLauncher/Assets/Icons/platform_lin.png";
                }
            }

            return null; // No platform detected
        }

        public static bool MatchesPlatform(string assetName, string platformIdentifier)
        {
            return PlatformAssetMatcher.MatchesPlatform(assetName, platformIdentifier);
        }

        private static bool HasAnyOf(string input, params string[] substrings)
        {
            foreach (var substring in substrings)
            {
                if (input.Contains(substring))
                {
                    return true;
                }
            }
            return false;
        }

        private static GameInstallationOptions CreateInstallationOptions() =>
            new() { Log = message => Debug.WriteLine(message) };

        internal static void EnsureExecutableAtRoot(string gamePath)
        {
            GameInstallationService.EnsureExecutableAtRoot(gamePath, CreateInstallationOptions());
        }

        internal static List<string> GetExecutableCandidates(string gamePath, SearchOption searchOption, out bool needsWine)
        {
            return GameInstallationService.FindExecutableCandidates(
                gamePath,
                searchOption,
                CreateInstallationOptions(),
                out needsWine);
        }

        static void SetAttributesNormal(DirectoryInfo dir)
        {
            try
            {
                foreach (var subDir in dir.GetDirectories())
                {
                    SetAttributesNormal(subDir);
                }

                foreach (var file in dir.GetFiles())
                {
                    file.Attributes = FileAttributes.Normal;
                }

                dir.Attributes = FileAttributes.Normal;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Warning setting file attributes: {ex.Message}");
            }
        }

        static void TryDeleteDirectoryIfEmpty(string dir, string stopAt)
        {
            try
            {
                if (!Directory.Exists(dir))
                    return;

                if (!Directory.EnumerateFileSystemEntries(dir).Any())
                {
                    Directory.Delete(dir, false);
                    var parent = Path.GetDirectoryName(dir);
                    if (!string.IsNullOrEmpty(parent) &&
                        !Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar)
                            .Equals(Path.GetFullPath(stopAt).TrimEnd(Path.DirectorySeparatorChar),
                                    StringComparison.OrdinalIgnoreCase))
                    {
                        TryDeleteDirectoryIfEmpty(parent, stopAt);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed while cleaning up directory '{dir}': {ex.Message}");
            }
        }

        public static string GetPlatformIdentifier(AppSettings settings)
        {
            return PlatformAssetMatcher.GetPlatformIdentifier(settings.Platform);
        }

        private static bool IsWindowsRunnerAvailable(AppSettings? settings = null) =>
            WindowsRunnerService.IsWindowsRunnerAvailable(settings);

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

