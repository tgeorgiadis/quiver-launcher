using QuiverLauncher.Models;
using QuiverLauncher.Core.Services;
using QuiverLauncher.Services.Mods;
using QuiverLauncher.ViewModels;

namespace QuiverLauncher.Services;

public interface IAppEntryService
{
    Task<bool> SaveAsync(AppEntryDraft draft, Action<EntryNotice> notice, Action<GameInfo> openFolder, CancellationToken token);
}

public sealed class AppEntryService(GameManager manager, SettingsViewModel settingsModel) : IAppEntryService
{
    private GameManager _gameManager => manager;
    private SettingsViewModel _settingsModel => settingsModel;
    private AppSettings _settings => settingsModel.Current;
    public async Task<bool> SaveAsync(AppEntryDraft draft, Action<EntryNotice> notice, Action<GameInfo> openFolder, CancellationToken token)
    {
        void Notify(string message, string title) => notice(new EntryNotice(message, title));
        token.ThrowIfCancellationRequested();
        var name = draft.Name;
        var manuallyManaged = draft.ManuallyManaged;
        var repository = manuallyManaged ? "" : draft.Repository;
        var normalizedSource = RepositorySourceHelper.Normalize(draft.RepositorySource, out var unsupportedSource);
        var repositorySource = manuallyManaged ? null : normalizedSource;
        if (!manuallyManaged && unsupportedSource)
            Notify("Unsupported repository source; defaulting to GitHub.", "Repository Source");
        var folderName = draft.FolderName;
        var iconUrl = draft.IconUrl;
        var project = string.IsNullOrWhiteSpace(draft.Project) ? null : draft.Project;
        var customDisplayName = string.IsNullOrWhiteSpace(draft.CustomDisplayName) ? null : draft.CustomDisplayName;
        var tags = TagHelper.ParseCommaSeparatedTags(draft.Tags);
        var filesToAdd = AppFilesToAddService.ParseCommaSeparated(draft.FilesToAdd);
        var releaseAssetFilter = manuallyManaged ? null : RepositorySourceHelper.NormalizeReleaseAssetFilter(draft.ReleaseAssetFilter);
        var modsPath = GameModsConfig.NormalizePath(draft.ModsPath);
        var modsSources = GameModsFormHelper.ParseSourcesFromEditor(draft.ModsSources);
        var modsLayout = draft.ModsFolderPerMod ? GameModsConfig.LayoutFolderPerMod : null;
        var identityKey = RepositorySourceHelper.GetInstanceKey(repositorySource, repository, folderName);
                var games = await _gameManager.CatalogService.LoadLocalAppsAsync();
                token.ThrowIfCancellationRequested();

                if (!string.IsNullOrEmpty(draft.EditingIdentityKey) || !string.IsNullOrEmpty(draft.EditingRepository))
                {
                    var appToUpdate = games.FirstOrDefault(g =>
                        (!string.IsNullOrEmpty(draft.EditingIdentityKey) &&
                         string.Equals(g.InstanceKey, draft.EditingIdentityKey, StringComparison.OrdinalIgnoreCase)) ||
                        (string.IsNullOrEmpty(draft.EditingIdentityKey) &&
                         g.Repository == draft.EditingRepository));
                    if (appToUpdate == null)
                    {
                        Notify("Could not find the app to update.", "Error");
                        return false;
                    }

                    // Name may be shared across ports (distinguished by project / repository).
                    var oldRepository = appToUpdate.Repository;
                    var oldRepositorySource = appToUpdate.RepositorySource;
                    var oldIdentityKey = appToUpdate.InstanceKey;
                    var oldFolderName = appToUpdate.FolderName;
                    if (!string.Equals(oldIdentityKey, identityKey, StringComparison.OrdinalIgnoreCase) &&
                        games.Any(g =>
                            !ReferenceEquals(g, appToUpdate) &&
                            string.Equals(g.InstanceKey, identityKey, StringComparison.OrdinalIgnoreCase)))
                    {
                        Notify(
                            "Another app already uses this folder name.",
                            "Duplicate Folder");
                        return false;
                    }

                    if (!string.Equals(appToUpdate.FolderName, folderName, StringComparison.OrdinalIgnoreCase) &&
                        games.Any(g =>
                            !ReferenceEquals(g, appToUpdate) &&
                            string.Equals(g.FolderName, folderName, StringComparison.OrdinalIgnoreCase)))
                    {
                        Notify(
                            "Another app already uses this folder name.",
                            "Duplicate Folder");
                        return false;
                    }

                    if (appToUpdate.FolderName != folderName && !string.IsNullOrEmpty(appToUpdate.FolderName))
                    {
                        var oldPath = Path.Combine(_settings.AppsPath, appToUpdate.FolderName);
                        var newPath = Path.Combine(_settings.AppsPath, folderName);

                        if (Directory.Exists(oldPath))
                        {
                            try
                            {
                                Directory.Move(oldPath, newPath);
                            }
                            catch (Exception ex)
                            {
                                Notify($"Failed to rename folder {appToUpdate.FolderName}", ex.Message);
                            }
                        }
                    }

                    var wasManual = appToUpdate.IsManuallyManaged;
                    var previousFilesToAdd = AppFilesToAddService.Normalize(appToUpdate.FilesToAdd);
                    appToUpdate.Name = name;
                    appToUpdate.Project = project;
                    appToUpdate.CustomDisplayName = customDisplayName;
                    appToUpdate.Repository = repository ?? "";
                    appToUpdate.RepositorySource = manuallyManaged || RepositorySourceHelper.IsGitHub(repositorySource)
                        ? null
                        : repositorySource;
                    appToUpdate.FolderName = folderName;
                    appToUpdate.GameIconUrl = iconUrl;
                    appToUpdate.Tags = tags;
                    appToUpdate.FilesToAdd = filesToAdd;
                    appToUpdate.ReleaseAssetFilter = releaseAssetFilter;
                    appToUpdate.ModsPath = modsPath.Length > 0 ? modsPath : null;
                    appToUpdate.ModsSources = modsSources;
                    appToUpdate.ModsLayout = modsLayout;

                    if (manuallyManaged)
                    {
                        appToUpdate.AutoUpdate = false;
                        appToUpdate.PreferredVersion = null;
                        appToUpdate.SkippedUpdateVersion = null;
                        appToUpdate.DeferUpdateTracking = false;
                        appToUpdate.LatestVersion = null;
                    }
                    else if (wasManual)
                    {
                        appToUpdate.AutoUpdate = false;
                        appToUpdate.PreferredVersion = null;
                        appToUpdate.SkippedUpdateVersion = null;
                        appToUpdate.DeferUpdateTracking = true;
                    }

                    if (!string.IsNullOrWhiteSpace(appToUpdate.Repository))
                        _settings.UserAppDisplayNames.Remove(appToUpdate.Repository);

                    if (AppIdentityMigration.MigrateIdentity(
                            _settings,
                            oldRepositorySource,
                            oldRepository,
                            appToUpdate.RepositorySource,
                            appToUpdate.Repository,
                            oldFolderName,
                            appToUpdate.FolderName))
                    {
                        _settingsModel.Save(_settings);
                    }
                    else
                    {
                        _settingsModel.Save(_settings);
                    }

                    await SaveGamesToJsonAsync(games);
                    if (appToUpdate.IsManuallyManaged)
                        ManualAppFolderService.EnsurePrepared(appToUpdate, _gameManager.GamesFolder);
                    else
                        AppFilesToAddService.SyncForGame(appToUpdate, _gameManager.GamesFolder, previousFilesToAdd);
                    Notify("App entry updated successfully", "App Updated");
                }
                else
                {
                    // Name may be shared across ports; uniqueness is install folder
                    // (hosted apps may share a repository when folders differ).
                    if (games.Any(g =>
                            string.Equals(g.InstanceKey, identityKey, StringComparison.OrdinalIgnoreCase)))
                    {
                        Notify(
                            "An app with this folder name already exists.",
                            "Duplicate Folder");
                        return false;
                    }

                    if (games.Any(g =>
                            string.Equals(g.FolderName, folderName, StringComparison.OrdinalIgnoreCase)))
                    {
                        Notify(
                            "An app with this folder name already exists.",
                            "Duplicate Folder");
                        return false;
                    }

                    var newApp = new GameInfo
                    {
                        Name = name,
                        Project = project,
                        CustomDisplayName = customDisplayName,
                        Repository = repository ?? "",
                        RepositorySource = manuallyManaged || RepositorySourceHelper.IsGitHub(repositorySource)
                            ? null
                            : repositorySource,
                        FolderName = folderName,
                        GameIconUrl = iconUrl,
                        Tags = tags,
                        FilesToAdd = filesToAdd,
                        ReleaseAssetFilter = releaseAssetFilter,
                        ModsPath = modsPath.Length > 0 ? modsPath : null,
                        ModsSources = modsSources,
                        ModsLayout = modsLayout,
                        AutoUpdate = !manuallyManaged && _settings.AutoUpdateNewlyAddedApps,
                        IsCustom = true,
                        IsExperimental = false
                    };
                    games.Add(newApp);

                    await SaveGamesToJsonAsync(games);
                    if (newApp.IsManuallyManaged)
                    {
                        ManualAppFolderService.EnsurePrepared(newApp, _gameManager.GamesFolder);
                        openFolder(newApp);
                        Notify(
                            "Manually managed app added. Place the app files in the opened folder.",
                            "App Added");
                    }
                    else
                    {
                        AppFilesToAddService.SyncForGame(newApp, _gameManager.GamesFolder);
                    }
                }


        return true;
    }
    private async Task SaveGamesToJsonAsync(List<GameInfo> games)
    {
        foreach(var game in games) game.GameManager ??= _gameManager;
        await _gameManager.CatalogService.SaveLocalAppsAsync(games);
    }
}
