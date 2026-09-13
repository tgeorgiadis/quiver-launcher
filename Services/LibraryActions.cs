using Avalonia.Platform.Storage;
using QuiverLauncher.Core.Models;
using QuiverLauncher.Core.Services;
using QuiverLauncher.Models;
using QuiverLauncher.ViewModels;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace QuiverLauncher.Services;
/// <summary>Library management actions using the existing install and persistence services.</summary>
public sealed class LibraryActions
{
    private readonly GameManager _gameManager;
    private readonly LibraryPersistenceService _libraryPersistence;
    private readonly SettingsViewModel _settingsModel;
    private readonly LauncherSession _session;
    private readonly LibraryViewModel _library;
    private readonly Func<IStorageProvider> _storage;
    private readonly Func<string, string, bool, bool, Task<bool>> _prompt;
    private readonly Action<string> _openUrl;
    private readonly Func<Task> _catalogChanged;
    private AppSettings _settings => _settingsModel.Current;

    public LibraryActions(GameManager manager, LibraryPersistenceService persistence, SettingsViewModel settings, LauncherSession session, LibraryViewModel library, Func<IStorageProvider> storage, Func<string, string, bool, bool, Task<bool>> prompt, Action<string> openUrl, Func<Task> catalogChanged)
    {
        _gameManager = manager;
        _libraryPersistence = persistence;
        _settingsModel = settings;
        _session = session;
        _library = library;
        _storage = storage;
        _prompt = prompt;
        _openUrl = openUrl;
        _catalogChanged = catalogChanged;
    }

    private Task<bool> ShowMessageBoxAsync(string message, string title, bool isQuestion = false, bool preferCancelDefault = false) => _session.IsClosed ? Task.FromResult(false) : _prompt(message, title, isQuestion, preferCancelDefault);
    private void OpenUrl(string url)
    {
        if (!_session.IsClosed)
            _openUrl(url);
    }

    private void Changed()
    {
        if (!_session.IsClosed)
        {
            _library.ApplySorting();
            _library.RefreshContinue();
        }
    }

    public Task OpenRepositoryAsync(GameInfo? game) => _session.RunAsync(async () =>
    {
        if (string.IsNullOrEmpty(game?.Repository)) { await ShowMessageBoxAsync("Failed to open repository page", "Error"); return; }
        try { OpenUrl(RepositorySourceHelper.GetRepositoryPageUrl(game.RepositorySource, game.Repository)); }
        catch (Exception ex) { await ShowMessageBoxAsync($"Failed to open repository page: {ex.Message}", "Error"); }
    });

    public void OpenGameFolder(GameInfo game)
    {
        if (_session.IsClosed) return;
        if (string.IsNullOrEmpty(game.FolderName) && string.IsNullOrWhiteSpace(game.InstallPath))
        {
            _ = _session.RunAsync(() => ShowMessageBoxAsync("Unable to identify the game folder.", "Action Error"));
            return;
        }

        try
        {
            if (game.IsManuallyManaged)
                ManualAppFolderService.EnsurePrepared(game, _gameManager.GamesFolder);
            var folderPath = game.GetInstallPath(_gameManager.GamesFolder);
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                _ = _session.RunAsync(() => ShowMessageBoxAsync("Unable to identify the game folder.", "Action Error"));
                return;
            }

            if (!Directory.Exists(folderPath))
                Directory.CreateDirectory(folderPath);
            OpenUrl(folderPath);
        }
        catch (Exception ex)
        {
            _ = _session.RunAsync(() => ShowMessageBoxAsync($"Failed to open folder: {ex.Message}", "Action Error"));
        }
    }

    public async Task ForceUpdateAsync(GameInfo? game)
    {
        await _session.RunAsync(async () =>
        {
            if (game == null)
            {
                await ShowMessageBoxAsync("Unable to identify the selected app.", "Error");
                return;
            }

            try
            {
                await _libraryPersistence.SaveVersionPreferencesAsync(game, null, null);
                _session.Token.ThrowIfCancellationRequested();
                await game.ForceUpdateAsync(_gameManager.HttpClient, _gameManager.GamesFolder);
                Changed();
            }
            catch (Exception ex)
            {
                await ShowMessageBoxAsync($"Failed to force update {game.Name}: {ex.Message}", "Force Update Failed");
            }
        });
    }

    public async Task ConfigureRunnerAsync(GameInfo? game)
    {
        await _session.RunAsync(async () =>
        {
            if (game == null || string.IsNullOrWhiteSpace(game.FolderName))
            {
                await ShowMessageBoxAsync("Unable to identify the selected app.", "Error");
                return;
            }

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                await ShowMessageBoxAsync("Windows runner settings are only used on Linux.", "Windows Runner");
                return;
            }

            try
            {
                var gamePath = game.GetInstallPath(_gameManager.GamesFolder);
                var config = await GameDialogService.ShowLinuxWindowsRunnerDialogAsync(gamePath, LinuxWindowsRunnerConfig.FromGame(game), isInstall: false, cancellationToken: _session.Token);
                if (config == null || _session.IsClosed)
                    return;
                config.ApplyTo(game);
                var allGames = await LoadGamesFromJsonAsync();
                var matchingGame = FindMatchingSavedApp(allGames, game);
                if (matchingGame != null)
                {
                    matchingGame.LinuxRunner = game.LinuxRunner;
                    matchingGame.LinuxPrefixPath = game.LinuxPrefixPath;
                    matchingGame.LinuxProtonPath = game.LinuxProtonPath;
                    matchingGame.LinuxCustomLaunchCommand = game.LinuxCustomLaunchCommand;
                    await SaveGamesToJsonAsync(allGames);
                }
            }
            catch (Exception ex)
            {
                await ShowMessageBoxAsync($"Failed to save Windows runner settings: {ex.Message}", "Windows Runner");
            }
        });
    }

    public async Task LocateInstallAsync(GameInfo? game)
    {
        await _session.RunAsync(async () =>
        {
            if (game == null)
            {
                await ShowMessageBoxAsync("Unable to identify the selected app.", "Error");
                return;
            }

            var folders = await _storage().OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = $"Select existing install folder for {game.Name}", AllowMultiple = false }).WaitAsync(_session.Token);
            _session.Token.ThrowIfCancellationRequested();
            if (folders == null || folders.Count == 0)
                return;
            var selectedPath = folders[0].Path.LocalPath;
            if (string.IsNullOrWhiteSpace(selectedPath) || !Directory.Exists(selectedPath))
            {
                await ShowMessageBoxAsync("The selected install folder could not be found.", "Install Folder Not Found");
                return;
            }

            GameInstallLocationService.ApplyLocatedPath(game, selectedPath);
            await _libraryPersistence.SaveInstallLocationAsync(game);
            await game.CheckStatusAsync(_gameManager.HttpClient, _gameManager.GamesFolder, forceUpdateCheck: true);
            Changed();
        });
    }

    public async Task UninstallAsync(GameInfo? game)
    {
        await _session.RunAsync(async () =>
        {
            if (game == null)
                return;
            if (game.Status == GameStatus.NotInstalled)
            {
                _ = _session.RunAsync(() => ShowMessageBoxAsync($"{game.Name} is not installed.", "Nothing to Delete"));
                return;
            }

            if (PlatformCapabilities.IsMobile)
            {
                try
                {
                    await AndroidLibraryUninstall.RunAsync(game, game.GetInstallPath(_gameManager.GamesFolder), AppInstallLaunch.Current);
                }
                catch (Exception ex)
                {
                    await ShowMessageBoxAsync($"Could not uninstall {game.Name}: {ex.Message}", "Uninstall Failed");
                }
                finally { Changed(); }
                return;
            }

            var confirmMessage = PlatformCapabilities.IsMobile ? $"Are you sure you want to uninstall {game.Name} from this device?" : $"Are you sure you want to delete {game.Name}?\n\n" + "Quiver Launcher will attempt to move game files to your system's Recycle Bin / Trash so you can restore them if needed. " + "Portable installs may include save data in the same folder.";
            var result = await ShowMessageBoxAsync(confirmMessage, "Confirm Deletion", isQuestion: true, preferCancelDefault: true);
            if (result && !_session.IsClosed)
            {
                try
                {
                    if (string.IsNullOrEmpty(game.FolderName))
                    {
                        await ShowMessageBoxAsync($"Failed to delete {game.Name}: game folder is not configured.", "Deletion Failed");
                        return;
                    }

                    game.Status = GameStatus.Installing;
                    game.IsLoading = true;
                    var gamePath = game.GetInstallPath(_gameManager.GamesFolder);
                    if (Directory.Exists(gamePath))
                    {
                        await Task.Run(() => RecycleBinHelper.MoveToRecycleBin(gamePath));
                    }

                    game.IsLoading = false;
                    await game.CheckStatusAsync(_gameManager.HttpClient, _gameManager.GamesFolder);
                    Changed();
                }
                catch (Exception ex)
                {
                    game.IsLoading = false;
                    _ = _session.RunAsync(() => ShowMessageBoxAsync($"Failed to delete {game.Name}: {ex.Message}", "Deletion Failed"));
                }
            }
        });
    }

    private Task<List<GameInfo>> LoadGamesFromJsonAsync() => _gameManager.CatalogService.LoadLocalAppsAsync();
    private async Task SaveGamesToJsonAsync(List<GameInfo> appsToSave)
    {
        foreach (var app in appsToSave)
            app.GameManager ??= _gameManager;
        await _gameManager.CatalogService.SaveLocalAppsAsync(appsToSave);
    }

    private async Task SaveGamesToJsonAsync(Dictionary<string, JsonElement> gamesData)
    {
        var imported = _gameManager.CatalogService.ParseAppsFromDictionary(gamesData);
        foreach (var app in imported)
            app.GameManager = _gameManager;
        await _gameManager.CatalogService.SaveLocalAppsAsync(imported);
    }

    private static GameInfo? FindMatchingSavedApp(IEnumerable<GameInfo> apps, GameInfo game) => apps.FirstOrDefault(g => string.Equals(g.InstanceKey, game.InstanceKey, StringComparison.OrdinalIgnoreCase));
    public async Task RemoveEntryAsync(GameInfo? game)
    {
        await _session.RunAsync(async () =>
        {
            if (game == null || string.IsNullOrEmpty(game.Name))
            {
                _ = _session.RunAsync(() => ShowMessageBoxAsync("Unable to identify the selected app.", "Error"));
                return;
            }

            if (!game.IsInLocalAppsJson)
            {
                _ = _session.RunAsync(() => ShowMessageBoxAsync("This app is not in your local list.", "Error"));
                return;
            }

            try
            {
                var confirm = await ShowMessageBoxAsync($"Remove '{game.Name}' from your Library?\n\nYour files will not be deleted.", "Remove from Library", true);
                if (!confirm || _session.IsClosed)
                    return;
                if (!string.IsNullOrWhiteSpace(game.Repository))
                {
                    await _gameManager.CatalogService.IgnoreRepositoryInMatchingSourcesAsync(_settings, game.Repository);
                }

                var games = await LoadGamesFromJsonAsync();
                var gameToRemove = FindMatchingSavedApp(games, game);
                if (gameToRemove == null)
                {
                    await ShowMessageBoxAsync($"Could not find '{game.Name}' in the saved apps list.", "Error");
                    return;
                }

                games.Remove(gameToRemove);
                await SaveGamesToJsonAsync(games);
                await _gameManager.LoadGamesAsync();
                if (_session.IsClosed)
                    return;
                Changed();
                _settingsModel.SaveCurrent();
                await _catalogChanged();
                _ = _session.RunAsync(() => ShowMessageBoxAsync($"'{game.Name}' was removed successfully.", "Removed"));
            }
            catch (Exception ex)
            {
                _ = _session.RunAsync(() => ShowMessageBoxAsync($"Error removing app: {ex.Message}", "Error"));
            }
        });
    }

    public async Task CreateShortcutAsync(GameInfo? game)
    {
        await _session.RunAsync(async () =>
        {
            if (game == null)
            {
                await ShowMessageBoxAsync("Unable to identify the selected app.", "Error");
                return;
            }

            try
            {
                var target = await PrepareShortcutAsync(game);
                if (target == null || _session.IsClosed) return;
                await ShortcutHelper.CreateGameShortcutAsync(game, target, _gameManager.CacheFolder);
            }
            catch (Exception ex)
            {
                await ShowMessageBoxAsync($"Failed to create shortcut: {ex.Message}", "Error");
            }
        });
    }

    public async Task AddToSteamAsync(GameInfo? game)
    {
        await _session.RunAsync(async () =>
        {
            if (game == null)
            {
                await ShowMessageBoxAsync("Unable to identify the selected app.", "Error");
                return;
            }

            try
            {
                var target = await PrepareShortcutAsync(game);
                if (target == null || _session.IsClosed) return;
                string resultMessage = ShortcutHelper.IsSteamRunning()
                    ? ShortcutHelper.QueueGameAddToSteam(game, ShortcutHelper.ResolveLauncherPath() ?? "")
                    : await Task.Run(() => ShortcutHelper.AddGameToSteam(game, target, _gameManager.CacheFolder), _session.Token);
                await ShowMessageBoxAsync(resultMessage, "Steam Shortcut");
            }
            catch (Exception ex)
            {
                await ShowMessageBoxAsync($"Failed to add the game to Steam: {ex.Message}", "Error");
            }
        });
    }

    private Task<GameShortcutTarget?> PrepareShortcutAsync(GameInfo game) =>
        GameShortcutLaunch.PrepareAsync(game, _gameManager.GamesFolder, _settings, async candidates =>
        {
            var dialog = new QuiverLauncher.Views.ShortcutExecutableDialog(game.Name ?? "this app", candidates);
            await GameDialogService.ShowWindowAsync(dialog, _session.Token);
            return dialog.SelectedExecutable;
        }, _session.Token);
}
