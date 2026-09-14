using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Threading;
using QuiverLauncher.Core.Models;
using QuiverLauncher.Core.Services;
using QuiverLauncher.Models;

namespace QuiverLauncher.Services;

public static class GameDownloadInstallService
{
    public static async Task DownloadAndInstallAsync(
        GameInfo game,
        HttpClient httpClient,
        string gamesFolder,
        GitHubRelease? latestRelease,
        AppSettings settings,
        GameStatus triggerStatus,
        IGameDownloadDialogs? dialogs = null,
        FlatpakService? flatpakService = null)
    {
        dialogs ??= AvaloniaGameDownloadDialogs.Instance;
        flatpakService ??= FlatpakService.Current;
        var previousVersion = game.InstalledVersion;
        var rejectedFormatSwitch = false;
        InvalidOperationException FormatSwitchError()
        {
            rejectedFormatSwitch = true;
            return new("Uninstall the current app before switching between portable and Flatpak formats.");
        }

        try { await game.CatalogPreparation; }
        catch (Exception ex)
        {
            await dialogs.ShowErrorAsync($"This app was saved, but its files could not be prepared: {ex.Message}", "Preparation Error");
            return;
        }

        if (string.IsNullOrEmpty(game.FolderName))
        {
            await dialogs.ShowErrorAsync("App configuration is invalid (missing folder name).", "Configuration Error");
            return;
        }

        if (string.IsNullOrEmpty(game.Repository))
        {
            await dialogs.ShowErrorAsync("App configuration is invalid (missing repository).", "Configuration Error");
            return;
        }

        var apiToken = game.GetReleaseApiToken(settings);

        try
        {
            game.Status = triggerStatus == GameStatus.UpdateAvailable ? GameStatus.Updating : GameStatus.Downloading;
            game.DownloadProgress = 0;

            var gamePath = game.GetInstallPath(gamesFolder);
            var versionFile = Path.Combine(gamePath, "version.txt");

            if (latestRelease == null)
            {
                if (GitHubApiCache.TryGetCachedVersion(game.RepositorySource, game.Repository, out var cache) &&
                    cache?.CachedRelease != null)
                {
                    latestRelease = cache.CachedRelease;
                }
                else
                {
                    game.DownloadProgress = 5;
                    var releaseResult = await ReleaseSourceRegistry.Default.FetchReleasesAsync(
                        httpClient,
                        game.RepositorySource,
                        game.Repository,
                        apiToken).ConfigureAwait(false);

                    // An unsuccessful request has no releases too; it is not evidence
                    // that the repository has no downloads (for any platform).
                    releaseResult.EnsureSuccess();
                    if (releaseResult.Releases.Count == 0)
                    {
                        ResetNotInstalled(game);
                        await dialogs.ShowErrorAsync($"No releases found for {game.Name}.", "No Releases");
                        return;
                    }

                    latestRelease = GameInfo.SelectLatestRelease(
                        releaseResult.Releases,
                        game.PreferredVersion,
                        game.InstalledVersion,
                        releaseResult.LatestTag);

                    if (latestRelease == null)
                    {
                        ResetNotInstalled(game);
                        await dialogs.ShowErrorAsync($"No valid releases found for {game.Name}.", "No Releases");
                        return;
                    }

                    GitHubApiCache.SetCache(
                        game.RepositorySource,
                        game.Repository,
                        latestRelease.tag_name,
                        releaseResult.ETag ?? string.Empty,
                        latestRelease);
                }
            }

            game.DownloadProgress = 10;

            game.ApplyCachedRelease(latestRelease.tag_name, latestRelease);
            var choices = GameDownloadService.Prepare(game, latestRelease, settings);
            var asset = game.SelectedDownload ?? choices.Automatic;
            if (asset == null)
            {
                if (!choices.NeedsChoice)
                    await dialogs.ShowErrorAsync(choices.EmptyReason ?? "No eligible downloads.", "No Matching Assets");
                game.Status = triggerStatus == GameStatus.UpdateAvailable ? GameStatus.UpdateAvailable : GameStatus.NotInstalled;
                game.DownloadProgress = 0;
                game.NotifyMultipleDownloadsChanged();
                return;
            }

            var flatpakDownload = GameInstallationService.IsFlatpakAsset(asset.name);
            var oldFlatpak = FlatpakService.HasReceipt(gamePath)
                ? await flatpakService.GetStateAsync(gamePath).ConfigureAwait(false) : null;
            if ((!flatpakDownload && oldFlatpak?.Installed == true) ||
                (flatpakDownload && oldFlatpak == null && (File.Exists(versionFile) ||
                    GameInstallationService.FindExecutableCandidates(gamePath, SearchOption.AllDirectories,
                        game.GetInstallationOptions(), out _).Count > 0)))
                throw FormatSwitchError();
            if (flatpakDownload)
                await flatpakService.CheckAvailableAsync().ConfigureAwait(false);
            else if (oldFlatpak == null && File.Exists(versionFile) &&
                (await File.ReadAllTextAsync(versionFile).ConfigureAwait(false)).Trim() == latestRelease.tag_name)
            {
                game.Status = GameStatus.Installed;
                game.InstalledVersion = latestRelease.tag_name;
                game.LatestVersion = latestRelease.tag_name;
                game.DownloadProgress = 0;
                return;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) &&
                !OperatingSystem.IsAndroid() &&
                PlatformAssetMatcher.IsWindowsAsset(asset.name))
            {
                var gamePathForRunner = game.GetInstallPath(gamesFolder);

                if (!WindowsRunnerService.IsWindowsRunnerAvailable(settings, game))
                {
                    if (!await dialogs.ConfirmDownloadWithoutRunnerAsync())
                    {
                        ResetNotInstalled(game);
                        return;
                    }
                }
                else
                {
                    var runnerConfig = await dialogs.ConfigureWindowsRunnerAsync(
                        gamePathForRunner,
                        LinuxWindowsRunnerConfig.FromGame(game),
                        isInstall: true).ConfigureAwait(false);

                    if (runnerConfig == null)
                    {
                        ResetNotInstalled(game);
                        return;
                    }

                    runnerConfig.ApplyTo(game);
                    await PersistLinuxRunnerSettingsAsync(game).ConfigureAwait(false);
                }
            }

            string? downloadPath = null;
            string? stagingDir = null;

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, asset.browser_download_url);
                using var downloadResponse = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead)
                    .ConfigureAwait(false);
                downloadResponse.EnsureSuccessStatusCode();

                var dispositionFileName =
                    downloadResponse.Content.Headers.ContentDisposition?.FileNameStar ??
                    downloadResponse.Content.Headers.ContentDisposition?.FileName;
                var effectiveAssetName = GameInstallationService.ResolveEffectiveAssetName(
                    asset.name,
                    dispositionFileName);
                // A Content-Disposition filename can reveal the package type after selection.
                if (GameInstallationService.IsFlatpakAsset(effectiveAssetName) != flatpakDownload)
                {
                    flatpakDownload = GameInstallationService.IsFlatpakAsset(effectiveAssetName);
                    if (oldFlatpak?.Installed == true || flatpakDownload && oldFlatpak == null &&
                        (File.Exists(versionFile) || GameInstallationService.FindExecutableCandidates(gamePath,
                            SearchOption.AllDirectories, game.GetInstallationOptions(), out _).Count > 0))
                        throw FormatSwitchError();
                    if (flatpakDownload) await flatpakService.CheckAvailableAsync().ConfigureAwait(false);
                }

                var downloadRoot = OperatingSystem.IsAndroid()
                    ? Path.Combine(QuiverLauncherPaths.CacheDirectory, "Downloads")
                    : DownloadStaging.GetDesktopRoot();
                Directory.CreateDirectory(downloadRoot);
                (stagingDir, downloadPath) = DownloadStaging.CreateStagedDownload(downloadRoot, effectiveAssetName);

                var totalBytes = downloadResponse.Content.Headers.ContentLength ?? 0;
                var canReportProgress = totalBytes > 0;

                using (var contentStream = await downloadResponse.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var fs = new FileStream(downloadPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                {
                    var buffer = new byte[8192];
                    long totalRead = 0;
                    int bytesRead;

                    while ((bytesRead = await contentStream.ReadAsync(buffer).ConfigureAwait(false)) > 0)
                    {
                        await fs.WriteAsync(buffer.AsMemory(0, bytesRead)).ConfigureAwait(false);
                        totalRead += bytesRead;

                        if (canReportProgress)
                        {
                            var downloadPercent = (double)totalRead / totalBytes;
                            game.DownloadProgress = 10 + (downloadPercent * 80);
                        }
                    }

                    await fs.FlushAsync().ConfigureAwait(false);
                    fs.Flush(true);
                }

                game.DownloadProgress = 90;
                game.Status = GameStatus.Installing;

                if (GameInstallationService.IsFlatpakAsset(effectiveAssetName))
                {
                    game.IsInstallIndeterminate = true;
                    await flatpakService.InstallAsync(downloadPath, latestRelease.tag_name, gamePath,
                        game.GameManager?.Games.Where(other => other != game && !string.IsNullOrWhiteSpace(other.FolderName))
                            .Select(other => other.GetInstallPath(gamesFolder))).ConfigureAwait(false);
                    game.IsFlatpak = true;
                }
                else if (OperatingSystem.IsAndroid())
                {
                    var packagePath = await AndroidPackagePreparation.PrepareAsync(
                        downloadPath, effectiveAssetName, stagingDir!).ConfigureAwait(false);
                    var installed = await AppInstallLaunch.Current.InstallAsync(
                        game,
                        packagePath,
                        latestRelease.tag_name,
                        gamePath).ConfigureAwait(false);
                    if (!installed)
                    {
                        await dialogs.ShowErrorAsync(
                            $"Android did not complete the installation of {game.Name}. The installation may have been cancelled or rejected.",
                            "Installation Not Completed");
                        await GameStatusService.CheckStatusAsync(game, httpClient, gamesFolder, checkRemoteVersion: false).ConfigureAwait(false);
                        game.DownloadProgress = 0;
                        game.ClearDownloadSelection();
                        return;
                    }

                    Directory.CreateDirectory(gamePath);
                    await File.WriteAllTextAsync(versionFile, latestRelease.tag_name).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(game.AndroidPackageName))
                    {
                        await File.WriteAllTextAsync(
                            Path.Combine(gamePath, GameStatusService.AndroidPackageFileName),
                            game.AndroidPackageName).ConfigureAwait(false);
                    }
                }
                else
                {
                    // Keep failed first installs (including partially extracted apps)
                    // uninstalled across restarts. A working previous release remains
                    // usable if an update fails before replacing its files.
                    if (!File.Exists(versionFile) ||
                        GameInstallationService.FindExecutableCandidates(gamePath, SearchOption.AllDirectories,
                            game.GetInstallationOptions(), out _).Count == 0)
                    {
                        Directory.CreateDirectory(gamePath);
                        await File.WriteAllTextAsync(Path.Combine(gamePath, GameInstallationService.IncompleteInstallFileName),
                            latestRelease.tag_name).ConfigureAwait(false);
                    }

                    var baseOptions = game.GetInstallationOptions();
                    var installOptions = new GameInstallationOptions
                    {
                        Log = baseOptions.Log,
                        AdditionalMetadataFileNames = baseOptions.AdditionalMetadataFileNames,
                        ExtractProgress = new Progress<double>(p =>
                        {
                            game.DownloadProgress = 90 + (Math.Clamp(p, 0, 1) * 9);
                        }),
                    };

                    await GameInstallationService.InstallOrUpdateGameAsync(
                        downloadPath,
                        gamePath,
                        effectiveAssetName,
                        latestRelease.tag_name,
                        installOptions).ConfigureAwait(false);
                    File.Delete(Path.Combine(gamePath, FlatpakService.ReceiptFileName));
                    File.Delete(Path.Combine(gamePath, FlatpakService.PendingFileName));
                    game.IsFlatpak = false;
                }

                AppFilesToAddService.Sync(gamePath, previous: null, game.FilesToAdd);

                game.DownloadProgress = 100;
                await Task.Delay(500).ConfigureAwait(false);

                if (!game.IsFlatpak && !OperatingSystem.IsAndroid())
                {
                    if (GameInstallationService.FindExecutableCandidates(gamePath, SearchOption.AllDirectories,
                        game.GetInstallationOptions(), out _).Count == 0)
                    {
                        await File.WriteAllTextAsync(Path.Combine(gamePath, GameInstallationService.IncompleteInstallFileName),
                            latestRelease.tag_name).ConfigureAwait(false);
                        throw new InvalidOperationException("The installation did not leave a launchable app. " +
                            "The download may be incomplete, or security software may have removed its files.");
                    }
                    File.Delete(Path.Combine(gamePath, GameInstallationService.IncompleteInstallFileName));
                }

                game.InstalledVersion = latestRelease.tag_name;
                if (string.IsNullOrWhiteSpace(game.LatestVersion) ||
                    LauncherVersionService.IsNewerVersion(latestRelease.tag_name, game.LatestVersion))
                {
                    game.LatestVersion = latestRelease.tag_name;
                }

                game.Status = GameStatus.Installed;
                game.DownloadProgress = 0;
                game.ClearDownloadSelection();
                game.AvailableDownloads = null;
            }
            finally
            {
                game.IsInstallIndeterminate = false;
                // Single-file assets are moved into the game folder; archives stay in the staging dir and must be deleted.
                // If a single-file move failed, the temp file may still exist — clean it up either way for archives,
                // and for leftover single-file temps after a failed install.
                if (!string.IsNullOrEmpty(downloadPath) && File.Exists(downloadPath))
                {
                    var installedName = Path.GetFileName(downloadPath);
                    var wasSingleExecutable = GameInstallationService.IsSingleFileExecutableAsset(installedName);

                    if (!wasSingleExecutable || !File.Exists(Path.Combine(gamePath, installedName)))
                    {
                        try
                        {
                            File.Delete(downloadPath);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Failed to delete temp file {downloadPath}: {ex.Message}");
                        }
                    }
                }

                DownloadStaging.TryDeleteDirectory(stagingDir);
            }

            if (game.GameManager != null)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    game.GameManager.OnPropertyChanged(nameof(GameManager.Games));
                });
            }
        }
        catch (HttpRequestException ex)
        {
            // Stop the card's downloading indicator while the error dialog is open.
            ResetNotInstalled(game);
            if (GameDialogService.IsRateLimitError(ex))
            {
                if (RepositorySourceHelper.IsGitLab(game.RepositorySource))
                    await dialogs.ShowGitLabRateLimitExceededAsync();
                else
                    await dialogs.ShowRateLimitExceededAsync();
            }
            else
            {
                await dialogs.ShowErrorAsync(
                    $"Network error installing {game.Name}: {ex.Message}\n\nPlease check your internet connection.",
                    "Network Error");
            }

        }
        catch (UnauthorizedAccessException ex)
        {
            await dialogs.ShowErrorAsync(
                $"Permission error installing {game.Name}: {ex.Message}\n\nPlease check folder permissions.",
                "Permission Error");
            ResetNotInstalled(game);
        }
        catch (Exception ex)
        {
            await dialogs.ShowErrorAsync(
                InstallationErrorMessages.FormatInstallationError(game.Name, ex.Message),
                "Installation Error");
            ResetNotInstalled(game);
        }
        finally
        {
            game.IsInstallIndeterminate = false;
            var path = game.GetInstallPath(gamesFolder);
            if (FlatpakService.HasReceipt(path))
            {
                if (game.Status == GameStatus.NotInstalled && !string.IsNullOrWhiteSpace(previousVersion))
                {
                    game.InstalledVersion = previousVersion;
                    game.Status = triggerStatus is GameStatus.Installed or GameStatus.UpdateAvailable
                        ? triggerStatus : GameStatus.Installed;
                }
                await GameStatusService.CheckStatusAsync(game, httpClient, gamesFolder,
                    checkRemoteVersion: false, applyCachedRelease: false, flatpakService: flatpakService).ConfigureAwait(false);
            }
            else if (rejectedFormatSwitch || game.Status == GameStatus.NotInstalled && triggerStatus == GameStatus.UpdateAvailable)
                await GameStatusService.CheckStatusAsync(game, httpClient, gamesFolder,
                    checkRemoteVersion: false, applyCachedRelease: false).ConfigureAwait(false);
        }
    }

    static void ResetNotInstalled(GameInfo game)
    {
        game.Status = GameStatus.NotInstalled;
        game.InstalledVersion = "";
        game.DownloadProgress = 0;
        game.ClearDownloadSelection();
    }

    static async Task PersistLinuxRunnerSettingsAsync(GameInfo game)
    {
        var catalog = game.GameManager?.CatalogService;
        if (catalog == null || string.IsNullOrWhiteSpace(game.Repository))
            return;

        try
        {
            var allGames = await catalog.LoadLocalAppsAsync().ConfigureAwait(false);
            var matchingGame = allGames.FirstOrDefault(g =>
                !string.IsNullOrWhiteSpace(g.Repository) &&
                g.Repository.Equals(game.Repository, StringComparison.OrdinalIgnoreCase));

            if (matchingGame == null)
                return;

            matchingGame.LinuxRunner = game.LinuxRunner;
            matchingGame.LinuxPrefixPath = game.LinuxPrefixPath;
            matchingGame.LinuxProtonPath = game.LinuxProtonPath;
            matchingGame.LinuxCustomLaunchCommand = game.LinuxCustomLaunchCommand;
            await catalog.SaveLocalAppsAsync(allGames).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to persist Linux runner settings: {ex.Message}");
        }
    }
}
