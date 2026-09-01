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
        IGameDownloadDialogs? dialogs = null)
    {
        dialogs ??= AvaloniaGameDownloadDialogs.Instance;

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

                    if (releaseResult.Releases.Count == 0)
                    {
                        await dialogs.ShowErrorAsync($"No releases found for {game.Name}.", "No Releases");
                        ResetNotInstalled(game);
                        return;
                    }

                    latestRelease = GameInfo.SelectLatestRelease(
                        releaseResult.Releases,
                        game.PreferredVersion,
                        game.InstalledVersion);

                    if (latestRelease == null)
                    {
                        await dialogs.ShowErrorAsync($"No valid releases found for {game.Name}.", "No Releases");
                        ResetNotInstalled(game);
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

            if (File.Exists(versionFile))
            {
                var existingVersion = (await File.ReadAllTextAsync(versionFile).ConfigureAwait(false))?.Trim();
                if (existingVersion == latestRelease.tag_name)
                {
                    game.Status = GameStatus.Installed;
                    game.InstalledVersion = existingVersion;
                    game.LatestVersion = latestRelease.tag_name;
                    game.DownloadProgress = 0;
                    return;
                }
            }

            var allAssets = GitHubReleaseService.GetDownloadableAssets(latestRelease);
            var availableAssets = GitHubReleaseService.GetDownloadableAssets(latestRelease, game.ReleaseAssetFilter);

            if (allAssets.Count == 0)
            {
                await dialogs.ShowErrorAsync($"No download files found for {game.Name}.", "No Assets");
                ResetNotInstalled(game);
                return;
            }

            if (availableAssets.Count == 0)
            {
                var filter = RepositorySourceHelper.NormalizeReleaseAssetFilter(game.ReleaseAssetFilter);
                await dialogs.ShowErrorAsync(
                    $"No download files matched the release asset filter \"{filter}\" for {game.Name}.",
                    "No Matching Assets");
                ResetNotInstalled(game);
                return;
            }

            if (OperatingSystem.IsAndroid())
            {
                availableAssets = availableAssets
                    .Where(asset => PlatformAssetMatcher.MatchesPlatform(asset.name, "Android"))
                    .ToList();

                if (availableAssets.Count == 0)
                {
                    await dialogs.ShowErrorAsync(
                        $"{game.Name} has no Android build in this release.",
                        "No Android Build");
                    ResetNotInstalled(game);
                    return;
                }
            }

            game.AvailableDownloads = availableAssets;

            if (availableAssets.Count > 1 && game.SelectedDownload == null)
            {
                game.NotifyMultipleDownloadsChanged();
                game.Status = GameStatus.NotInstalled;
                game.DownloadProgress = 0;
                return;
            }

            var asset = game.SelectedDownload ?? availableAssets[0];

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

                var downloadDir = OperatingSystem.IsAndroid()
                    ? Path.Combine(QuiverLauncherPaths.CacheDirectory, "Downloads")
                    : Path.GetTempPath();
                Directory.CreateDirectory(downloadDir);
                downloadPath = Path.Combine(downloadDir, effectiveAssetName);

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
                }

                game.DownloadProgress = 90;
                game.Status = GameStatus.Installing;
                game.DownloadProgress = 95;

                if (OperatingSystem.IsAndroid() && GameInstallationService.IsAndroidPackageAsset(effectiveAssetName))
                {
                    var installed = await AppInstallLaunch.Current.InstallAsync(
                        game,
                        downloadPath,
                        latestRelease.tag_name).ConfigureAwait(false);
                    if (!installed)
                    {
                        await dialogs.ShowErrorAsync(
                            $"Could not start the Android installer for {game.Name}.",
                            "Install Failed");
                        ResetNotInstalled(game);
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
                    await GameInstallationService.InstallOrUpdateGameAsync(
                        downloadPath,
                        gamePath,
                        effectiveAssetName,
                        latestRelease.tag_name,
                        game.GetInstallationOptions()).ConfigureAwait(false);
                }

                AppFilesToAddService.Sync(gamePath, previous: null, game.FilesToAdd);

                game.DownloadProgress = 100;
                await Task.Delay(500).ConfigureAwait(false);

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
                // Single-file assets are moved into the game folder; archives stay in temp and must be deleted.
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

            ResetNotInstalled(game);
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
    }

    static void ResetNotInstalled(GameInfo game)
    {
        game.Status = GameStatus.NotInstalled;
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
