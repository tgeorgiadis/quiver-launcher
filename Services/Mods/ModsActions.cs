using QuiverLauncher.ViewModels;
using QuiverLauncher.Services.Mods.Providers.GameBanana;
using QuiverLauncher.Services.Mods.Providers.Thunderstore;

namespace QuiverLauncher.Services.Mods;

public delegate Task<IReadOnlyList<ModDownloadFile>> ModFileChoice(string title, string prompt, string confirmLabel,
    IReadOnlyList<ModDownloadFile> files, IReadOnlyCollection<string>? preselectedFileIds);

/// <summary>Mod install and uninstall actions, independent of controls and focus.</summary>
public sealed class ModsActions(GameManager _gameManager, LauncherSession _session, ModsViewModel Model,
    ModsCatalogWorkspace Workspace, ModFileChoice ShowModDownloadFilePickerAsync,
    Func<string, string, Task> ShowMessageBoxAsync, Func<string, string, Task<MessagePromptResult>> ShowChoicePromptAsync)
{
    private ModInstallService ModInstaller => Workspace.ModInstaller;
    private System.Collections.ObjectModel.ObservableCollection<ModListItem> ModListRows => Model.Rows;
    private void SetModsStatus(string status) { if (!_session.IsClosed) Model.Status = status; }
    public Task InstallModAsync(ModListItem item, bool updateInstalledFilesOnly = false, bool promptForDependencies = true) =>
        _session.RunAsync(() => InstallCoreAsync(item, updateInstalledFilesOnly, promptForDependencies));
    public Task UninstallModAsync(ModListItem item) => _session.RunAsync(() => UninstallCoreAsync(item));
    public Task UpdateAllVisibleModsAsync() => _session.RunAsync(UpdateAllCoreAsync);
    private async Task InstallCoreAsync(
        ModListItem item,
        bool updateInstalledFilesOnly = false,
        bool promptForDependencies = true)
    {
        var game = Model.Game;
        var version = Model.OpenVersion;
        bool Current() => !_session.IsClosed && version == Model.OpenVersion && ReferenceEquals(Model.Game, game);
        if (game == null || !Current() || item.IsBusy)
            return;

        if (!game.IsInstalled)
        {
            await ShowMessageBoxAsync("Install the app before installing mods.", "Mods");
            return;
        }

        var installRoot = game.GetInstallPath(_gameManager.GamesFolder);
        var modsPath = GameModsConfig.NormalizePath(game.ModsPath);
        if (string.IsNullOrWhiteSpace(installRoot) || modsPath.Length == 0)
            return;

        if (!_gameManager.ModProviderRegistry.TryGet(item.ProviderId, out var provider))
        {
            await ShowMessageBoxAsync($"Unknown mod provider '{item.ProviderId}'.", "Mods");
            return;
        }

        item.IsBusy = true;
        var isUpdate = updateInstalledFilesOnly;
        var actionLabel = isUpdate ? "Updating" : "Installing";
        SetModsStatus($"{actionLabel} {item.DisplayName}…");
        try
        {
            var package = item.Package;
            IReadOnlyList<ModDownloadFile>? selectedFiles = null;

            if (provider is GameBananaModProvider gameBanana)
            {
                package = await gameBanana.EnrichWithFilesAsync(package, _session.Token).ConfigureAwait(true);
                if (!Current()) return;

            Workspace.ReplaceCatalogPackage(package);

                if (package.DownloadFiles.Count == 0)
                    throw new InvalidOperationException("This mod has no downloadable files.");

                var installedRecords = ModsCatalogWorkspace.FindInstalledRecords(ModInstaller.LoadInstalled(installRoot), package);

                if (updateInstalledFilesOnly)
                {
                    selectedFiles = ModDownloadFileSelection.ResolveFilesToUpdate(package, installedRecords);
                    if (selectedFiles.Count == 0)
                    {
                        SetModsStatus("Nothing to update");
                        return;
                    }
                }
                else
                {
                    var remaining = ModDownloadFileSelection.GetUninstalledFiles(
                        package.DownloadFiles,
                        installedRecords);
                    if (remaining.Count == 0)
                    {
                        SetModsStatus("All files already installed");
                        return;
                    }

                    if (package.DownloadFiles.Count == 1)
                    {
                        selectedFiles = remaining;
                    }
                    else
                    {
                        selectedFiles = await ShowModDownloadFilePickerAsync(
                            $"Choose files — {package.Name}",
                            "This mod has multiple download files. Select one or more to install:",
                            "Install",
                            remaining,
                            preselectedFileIds: null).ConfigureAwait(true);
                        if (selectedFiles.Count == 0)
                        {
                            SetModsStatus("Install cancelled");
                            return;
                        }
                    }
                }
            }
            else if (provider is ThunderstoreModProvider thunderstore)
            {
                package = await thunderstore.EnrichForInstallAsync(package, _session.Token).ConfigureAwait(true);
                if (!Current()) return;

            Workspace.ReplaceCatalogPackage(package);
                if (string.IsNullOrWhiteSpace(package.LatestVersion?.DownloadUrl))
                    throw new InvalidOperationException("Could not resolve a download URL for this mod.");
            }

            var installDependencies = true;
            if (promptForDependencies && provider is ThunderstoreModProvider)
            {
                var missingDeps = ModInstaller.ListMissingDirectDependencies(
                    ModInstaller.LoadInstalled(installRoot),
                    package,
                    Model.Catalog);
                if (missingDeps.Count > 0)
                {
                    var list = string.Join("\n", missingDeps);
                    var choice = await ShowChoicePromptAsync(
                        "This mod has some requirements you haven't installed yet:\n\n" +
                        list +
                        "\n\nDo you wish to install them as well?",
                        "Mod requirements").ConfigureAwait(true);
                    if (choice == MessagePromptResult.Cancel)
                    {
                        SetModsStatus("Install cancelled");
                        return;
                    }

                    installDependencies = choice == MessagePromptResult.Yes;
                }
            }

            if (!Current()) return;
            var progress = new Progress<double>(p => { if (Current()) item.DownloadProgress = p * 100.0; });

            await ModInstaller.InstallSelectedFilesAsync(
                installRoot,
                modsPath,
                package,
                Model.Catalog,
                provider,
                selectedFiles,
                progress,
                modsLayout: game.ModsLayout,
                installDependencies: installDependencies, cancellationToken: _session.Token).ConfigureAwait(true);

            if (!Current()) return;
            Workspace.ApplyInstalledStateToAllItems();
            Workspace.ApplyFilters();
            await Workspace.RefreshModUpdateFlagsForGameAsync(game).ConfigureAwait(true);
            if (!Current()) return;
            SetModsStatus(isUpdate ? $"Updated {item.DisplayName}" : $"Installed {item.DisplayName}");
        }
        catch (Exception) when (!Current()) { }
        catch (Exception ex)
        {
            SetModsStatus($"{actionLabel} failed: {ex.Message}");
            await ShowMessageBoxAsync($"Failed to install mod:\n{ex.Message}", "Mods");
        }
        finally
        {
            if (Current()) item.IsBusy = false;
        }
    }


    private async Task UninstallCoreAsync(ModListItem item)
    {
        var game = Model.Game;
        var version = Model.OpenVersion;
        bool Current() => !_session.IsClosed && version == Model.OpenVersion && ReferenceEquals(Model.Game, game);
        if (game == null || !Current() || !game.IsInstalled || item.IsBusy)
            return;

        var installRoot = game.GetInstallPath(_gameManager.GamesFolder);
        var modsPath = GameModsConfig.NormalizePath(game.ModsPath);
        if (string.IsNullOrWhiteSpace(installRoot) || modsPath.Length == 0)
            return;

        var package = item.Package;
        var installedRecords = ModsCatalogWorkspace.FindInstalledRecords(ModInstaller.LoadInstalled(installRoot), package);
        if (installedRecords.Count == 0)
            return;

        IReadOnlyList<InstalledModRecord> toRemove = installedRecords;
        if (package.DownloadFiles.Count == 0 &&
            _gameManager.ModProviderRegistry.TryGet(item.ProviderId, out var provider) &&
            provider is GameBananaModProvider gameBanana)
        {
            package = await gameBanana.EnrichWithFilesAsync(package, _session.Token).ConfigureAwait(true);
            if (!Current()) return;
            Workspace.ReplaceCatalogPackage(package);
            installedRecords = ModsCatalogWorkspace.FindInstalledRecords(ModInstaller.LoadInstalled(installRoot), package);
            toRemove = installedRecords;
        }

        if (package.DownloadFiles.Count > 1)
        {
            var pickerFiles = ModDownloadFileSelection.ResolveInstalledFilesForPicker(package, installedRecords);
            var selected = await ShowModDownloadFilePickerAsync(
                $"Uninstall files — {package.Name}",
                "This mod has multiple download files. Select one or more to uninstall:",
                "Uninstall",
                pickerFiles,
                preselectedFileIds: null).ConfigureAwait(true);
            if (selected.Count == 0)
            {
                SetModsStatus("Uninstall cancelled");
                return;
            }

            var selectedIds = selected.Select(f => f.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            toRemove = installedRecords.Where(r =>
                selectedIds.Contains(r.DownloadFileId ?? string.Empty) ||
                selected.Any(f =>
                    !string.IsNullOrWhiteSpace(r.DownloadFileName) &&
                    string.Equals(f.FileName, r.DownloadFileName, StringComparison.OrdinalIgnoreCase))).ToList();
            if (toRemove.Count == 0)
            {
                SetModsStatus("Uninstall cancelled");
                return;
            }
        }

        item.IsBusy = true;
        try
        {
            foreach (var record in toRemove)
                ModInstaller.UninstallMatchingFile(installRoot, modsPath, package, record.DownloadFileId);

            if (!Current()) return;
            Workspace.ApplyInstalledStateToAllItems();
            Workspace.ApplyFilters();
            _ = _session.RunAsync(() => Workspace.RefreshModUpdateFlagsForGameAsync(game));
            SetModsStatus($"Uninstalled {item.DisplayName}");
        }
        catch (Exception) when (!Current()) { }
        catch (Exception ex)
        {
            SetModsStatus($"Uninstall failed: {ex.Message}");
            _ = _session.RunAsync(() => ShowMessageBoxAsync($"Failed to uninstall mod:\n{ex.Message}", "Mods"));
        }
        finally
        {
            if (Current()) item.IsBusy = false;
        }
    }


    private async Task UpdateAllCoreAsync()
    {
        var game = Model.Game;
        var version = Model.OpenVersion;
        var toUpdate = ModListRows.Where(r => r.CanUpdate).ToList();
        foreach (var item in toUpdate)
        {
            if (_session.IsClosed || version != Model.OpenVersion || !ReferenceEquals(game, Model.Game)) return;
            await InstallCoreAsync(item, updateInstalledFilesOnly: true, promptForDependencies: false);
        }
    }

}
