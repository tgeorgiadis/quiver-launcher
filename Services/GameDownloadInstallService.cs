using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Avalonia.Threading;
using QuiverLauncher.Core.Models;
using QuiverLauncher.Core.Services;
using QuiverLauncher.Models;

namespace QuiverLauncher.Services;

public enum ReleaseInstallMode { Automatic, ExplicitRelease }

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
        FlatpakService? flatpakService = null,
        WindowsInstallerService? windowsInstallerService = null,
        ReleaseInstallMode releaseMode = ReleaseInstallMode.Automatic,
        bool reinstall = false)
    {
        dialogs ??= AvaloniaGameDownloadDialogs.Instance;
        flatpakService ??= FlatpakService.Current;
        var previousVersion = game.InstalledVersion;
        var rejectedFormatSwitch = false;
        var isUpdate = triggerStatus == GameStatus.UpdateAvailable;
        string? installingVersion = null;
        // A release to offer instead, when the one picked has lost its files.
        GitHubRelease? fallback = null;
        // Usage data: which app, which release, and how it went.
        Dictionary<string, object?> Usage(params (string Key, object? Value)[] more)
        {
            var properties = Telemetry.AppRef(game);
            properties["version"] = installingVersion;
            properties["update"] = isUpdate;
            foreach (var (key, value) in more)
                properties[key] = value;
            return properties;
        }
        void Failed(string kind, Exception ex) =>
            Telemetry.Current.Track("app_install_failed", Usage(("error", kind), ("reason", Telemetry.ReasonOf(ex))));
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

            if (releaseMode == ReleaseInstallMode.Automatic)
            {
                // Revalidate endpoint payloads even inside a background cache scope.
                // A supplied/cached selection is not proof that it is still latest.
                using var revalidate = ReleaseRequestCoordinator.AllowCachedMetadata(TimeSpan.Zero);
                game.DownloadProgress = 5;
                // The release to install: the player's pin, else the one Quiver verified, else the newest. A catalog
                // app's comes from quiverlauncher.com; only an app outside the catalog asks its repository.
                latestRelease = await game.CatalogReleaseAsync(LauncherSession.OperationCancellation).ConfigureAwait(false)
                    ?? await CatalogReleaseSelection.FetchSelectedAsync(httpClient,
                        game.RepositorySource, game.Repository, game.ReleaseTarget, apiToken,
                        LauncherSession.OperationCancellation).ConfigureAwait(false);
                if (latestRelease != null)
                    GitHubApiCache.SetCache(game.RepositorySource, game.Repository, latestRelease.tag_name, "", latestRelease);
            }
            if (latestRelease == null)
            {
                ResetNotInstalled(game);
                await dialogs.ShowErrorAsync($"No valid releases found for {game.Name}.", "No Releases");
                return;
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
            if (GameInstallationService.IsWindowsInstallerAsset(asset.name) && !OperatingSystem.IsWindows())
                throw new PlatformNotSupportedException("MSI installation requires Windows desktop. Choose a native download for this platform.");
            var oldFlatpak = FlatpakService.HasReceipt(gamePath)
                ? await flatpakService.GetStateAsync(gamePath).ConfigureAwait(false) : null;
            if ((!flatpakDownload && oldFlatpak?.Installed == true) ||
                (flatpakDownload && oldFlatpak == null && (File.Exists(versionFile) ||
                    GameInstallationService.FindExecutableCandidates(gamePath, SearchOption.AllDirectories,
                        game.GetInstallationOptions(), out _).Count > 0)))
                throw FormatSwitchError();
            if (flatpakDownload)
                await flatpakService.CheckAvailableAsync().ConfigureAwait(false);
            // Already installed: nothing to download, unless the player asked to reinstall it.
            else if (!reinstall && !GameInstallationService.IsWindowsInstallerAsset(asset.name) && !WindowsInstallerService.HasReceipt(gamePath) && oldFlatpak == null && File.Exists(versionFile) &&
                (OperatingSystem.IsAndroid() || GameInstallationService.HasCompletePortableInstallation(
                    gamePath, game.GetInstallationOptions())) &&
                (await File.ReadAllTextAsync(versionFile).ConfigureAwait(false)).Trim() == latestRelease.tag_name)
            {
                game.Status = GameStatus.Installed;
                game.InstalledVersion = latestRelease.tag_name;
                game.LatestVersion = latestRelease.tag_name;
                game.DownloadProgress = 0;
                return;
            }

            // A catalog release Quiver hasn't verified installs only once the player confirms, never by itself.
            var check = await CheckReleaseAsync(game, latestRelease.tag_name).ConfigureAwait(false);
            // A file Quiver didn't check, in a release whose files it did, is not verified either.
            if (check is { Checksums.Count: > 0 } && check.ChecksumFor(asset.name) == null)
                check = check with
                {
                    State = check.State == ReleaseCheckState.Blocked ? ReleaseCheckState.Blocked : ReleaseCheckState.Unverified,
                    Reasons = [.. check.Reasons, $"Quiver checked this release's other files, not {asset.name}."],
                    Checksums = new Dictionary<string, string>(),
                };
            // VirusTotal's verdict on the file downloaded, not the release's other files.
            check = check?.ForFile(asset.name);
            installingVersion = latestRelease.tag_name;
            if (check is { State: not ReleaseCheckState.Verified } &&
                !await dialogs.ConfirmUnverifiedReleaseAsync(game.DisplayName, latestRelease.tag_name, check))
            {
                Telemetry.Current.Track("app_install_cancelled", Usage(("stage", "not_verified"), ("blocked", check.State == ReleaseCheckState.Blocked)));
                game.Status = triggerStatus;
                game.DownloadProgress = 0;
                return;
            }
            // Several antivirus engines flag the file: the player's own antivirus may block or remove it.
            if (check is { State: ReleaseCheckState.Verified, Flagged: true } &&
                !await dialogs.ConfirmFlaggedReleaseAsync(game.DisplayName, latestRelease.tag_name, check, isUpdate))
            {
                Telemetry.Current.Track("app_install_cancelled", Usage(("stage", "antivirus_flagged")));
                game.Status = triggerStatus;
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

            Telemetry.Current.Track("app_install_started", Usage(
                ("verified", check == null ? null : check.State == ReleaseCheckState.Verified),
                ("blocked", check?.State == ReleaseCheckState.Blocked),
                ("format", InstallFormat(asset.name))));

            string? downloadPath = null;
            string? stagingDir = null;

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, asset.browser_download_url);
                using var downloadResponse = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead)
                    .ConfigureAwait(false);
                // Deleted by its developer since Quiver listed it.
                if (downloadResponse.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
                    throw new DownloadMissingException(asset.name, latestRelease.tag_name);
                downloadResponse.EnsureSuccessStatusCode();

                var dispositionFileName =
                    downloadResponse.Content.Headers.ContentDisposition?.FileNameStar ??
                    downloadResponse.Content.Headers.ContentDisposition?.FileName;
                var effectiveAssetName = GameInstallationService.ResolveEffectiveAssetName(
                    asset.name,
                    dispositionFileName);
                var msiDownload = GameInstallationService.IsWindowsInstallerAsset(effectiveAssetName);
                if (msiDownload && !OperatingSystem.IsWindows())
                    throw new PlatformNotSupportedException("MSI installation requires Windows desktop. Choose a native download for this platform.");
                var hasMsiReceipt = WindowsInstallerService.HasReceipt(gamePath);
                if ((hasMsiReceipt && !msiDownload) || (msiDownload && !hasMsiReceipt &&
                    (FlatpakService.HasReceipt(gamePath) || GameInstallationService.HasCompletePortableInstallation(gamePath, game.GetInstallationOptions()))))
                    throw new InvalidOperationException("Remove this library entry before switching between Windows Installer and portable or Flatpak installations. Existing application files will not be removed.");
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

                var downloadRoot = OperatingSystem.IsAndroid() || HostProcessEnvironment.IsSandboxed()
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

                // The file must be the one Quiver checked (or, outside the catalog, the one GitHub published).
                var expected = check?.ChecksumFor(asset.name) ?? DigestOf(asset);
                if (expected != null && await Sha256Async(downloadPath).ConfigureAwait(false) is var actual &&
                    !string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase) &&
                    // A rolling release is rebuilt under the same tag: the build GitHub publishes now is fine too.
                    !(check?.Rolling == true && string.Equals(actual, await LiveDigestAsync(game, httpClient, apiToken,
                        latestRelease.tag_name, asset.name).ConfigureAwait(false), StringComparison.OrdinalIgnoreCase)))
                    throw new DownloadMismatchException(asset.name, latestRelease.tag_name, check?.ChecksumFor(asset.name) != null);

                game.DownloadProgress = 90;
                game.Status = GameStatus.Installing;

                if (msiDownload)
                {
                    game.IsInstallIndeterminate = true;
                    game.IsLoading = true;
                    try
                    {
                        await (windowsInstallerService ?? WindowsInstallerService.Current).InstallAsync(
                            game, downloadPath, latestRelease.tag_name, gamePath, dialogs).ConfigureAwait(false);
                    }
                    finally { game.IsLoading = false; }
                    if (WindowsInstallerService.HasReceipt(gamePath)) WindowsInstallerService.ApplyState(game, gamePath);
                    else ResetNotInstalled(game);
                    game.DownloadProgress = 0;
                    game.ClearDownloadSelection();
                    game.AvailableDownloads = null;
                    return;
                }

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
                Telemetry.Current.Track(isUpdate ? "app_updated" : "app_installed", Usage(
                    ("from", isUpdate ? previousVersion : null),
                    ("verified", check == null ? null : check.State == ReleaseCheckState.Verified),
                    ("format", InstallFormat(effectiveAssetName))));
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
        catch (DownloadMismatchException ex)
        {
            Failed("checksum_mismatch", ex);
            // Nothing was installed or removed: the app stays as it was.
            game.Status = triggerStatus;
            game.DownloadProgress = 0;
            await ReportProblemAsync(game, ex.Version, ex.FileName, "mismatch").ConfigureAwait(false);
            await dialogs.ShowErrorAsync(ex.Message, "Download Not Verified");
        }
        catch (DownloadMissingException ex)
        {
            Failed("file_missing", ex);
            game.Status = triggerStatus;
            game.DownloadProgress = 0;
            // The site reads the release back now, so the next player isn't sent to the same dead link.
            await ReportProblemAsync(game, ex.Version, ex.FileName, "missing").ConfigureAwait(false);
            var other = await VerifiedFallbackAsync(game, ex.Version).ConfigureAwait(false);
            if (other != null && await dialogs.OfferOtherReleaseAsync(game.DisplayName, ex.Message, other.tag_name))
                fallback = other;
            else
                await dialogs.ShowErrorAsync(ex.Message, "Download Removed");
        }
        catch (HttpRequestException ex)
        {
            Failed(GameDialogService.IsRateLimitError(ex) ? "rate_limited" : "network", ex);
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
            Failed("permission", ex);
            await dialogs.ShowErrorAsync(
                $"Permission error installing {game.Name}: {ex.Message}\n\nPlease check folder permissions.",
                "Permission Error");
            ResetNotInstalled(game);
        }
        catch (Exception ex)
        {
            Failed(ex is OperationCanceledException ? "cancelled" : "other", ex);
            if (ex is not OperationCanceledException)
                Telemetry.Current.CaptureException(ex, handled: true, "app_install");
            await dialogs.ShowErrorAsync(
                InstallationErrorMessages.FormatInstallationError(game.Name, ex.Message),
                "Installation Error");
            ResetNotInstalled(game);
        }
        finally
        {
            game.IsInstallIndeterminate = false;
            var path = game.GetInstallPath(gamesFolder);
            if (WindowsInstallerService.HasReceipt(path))
            {
                game.DownloadProgress = 0;
                await GameStatusService.CheckStatusAsync(game, httpClient, gamesFolder,
                    checkRemoteVersion: false, applyCachedRelease: false).ConfigureAwait(false);
            }
            else if (FlatpakService.HasReceipt(path))
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

        if (fallback != null)
        {
            game.ClearDownloadSelection();
            await DownloadAndInstallAsync(game, httpClient, gamesFolder, fallback, settings, triggerStatus, dialogs,
                flatpakService, windowsInstallerService, ReleaseInstallMode.ExplicitRelease, reinstall).ConfigureAwait(false);
        }
    }

    /// <summary>The kind of file a release was installed from, for usage data.</summary>
    static string InstallFormat(string assetName) =>
        GameInstallationService.IsFlatpakAsset(assetName) ? "flatpak"
        : GameInstallationService.IsWindowsInstallerAsset(assetName) ? "msi"
        : assetName.EndsWith(".apk", StringComparison.OrdinalIgnoreCase) ? "apk"
        : assetName.EndsWith(".appimage", StringComparison.OrdinalIgnoreCase) ? "appimage"
        : GameInstallationService.IsSingleFileExecutableAsset(assetName) ? "executable"
        : "archive";

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

    private static async Task<ReleaseCheck?> CheckReleaseAsync(GameInfo game, string version)
    {
        if (game.GameManager?.CatalogReleases is not { } catalog) return null;
        try { return await catalog.CheckAsync(game, version, LauncherSession.OperationCancellation).ConfigureAwait(false); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Debug.WriteLine($"Release check failed for {game.Name}: {ex.Message}");
            return null;
        }
    }

    /// <summary>Tells the site a catalog app's download failed; never throws.</summary>
    private static async Task ReportProblemAsync(GameInfo game, string version, string fileName, string problem)
    {
        if (game.GameManager?.CatalogReleases is not { } catalog) return;
        try { await catalog.ReportDownloadProblemAsync(game, version, fileName, problem, LauncherSession.OperationCancellation).ConfigureAwait(false); }
        catch (Exception ex) { Debug.WriteLine($"Couldn't report a broken download of {game.Name}: {ex.Message}"); }
    }

    private static async Task<GitHubRelease?> VerifiedFallbackAsync(GameInfo game, string version)
    {
        if (game.GameManager?.CatalogReleases is not { } catalog) return null;
        try { return await catalog.VerifiedFallbackAsync(game, version, LauncherSession.OperationCancellation).ConfigureAwait(false); }
        catch (Exception ex)
        {
            Debug.WriteLine($"Couldn't find another release of {game.Name}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// The SHA-256 GitHub publishes now for a file of a rolling release, or null when it can't say. A file the release
    /// no longer has was removed, not rebuilt.
    /// </summary>
    private static async Task<string?> LiveDigestAsync(GameInfo game, HttpClient httpClient, string? token, string tag, string fileName)
    {
        if (RepositorySourceHelper.IsGitLab(game.RepositorySource) || string.IsNullOrEmpty(game.Repository)) return null;
        GitHubRelease? live;
        try
        {
            live = await GitHubReleaseService.FetchReleaseByTagAsync(httpClient, game.Repository, tag, token,
                LauncherSession.OperationCancellation).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Debug.WriteLine($"Couldn't read {tag} of {game.Name} back from GitHub: {ex.Message}");
            return null;
        }
        var file = live?.assets?.FirstOrDefault(a => string.Equals(a.name, fileName, StringComparison.OrdinalIgnoreCase));
        if (file == null) throw new DownloadMissingException(fileName, tag);
        return DigestOf(file);
    }

    private static string? DigestOf(GitHubAsset asset) =>
        asset.digest?.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) == true ? asset.digest["sha256:".Length..].ToLowerInvariant() : null;

    private static async Task<string> Sha256Async(string path)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream).ConfigureAwait(false));
    }
}

/// <summary>A download that isn't the file it should be; nothing was installed from it.</summary>
public sealed class DownloadMismatchException(string fileName, string version, bool checkedByQuiver) : Exception(checkedByQuiver
    ? $"{fileName} isn't the file Quiver checked for this release, so it wasn't installed. It may have been changed since Quiver saw it."
    : $"{fileName} doesn't match the checksum its developer published, so it wasn't installed. Try downloading it again.")
{
    public string FileName { get; } = fileName;
    public string Version { get; } = version;
}

/// <summary>A release file its developer has deleted since Quiver listed it; nothing was downloaded.</summary>
public sealed class DownloadMissingException(string fileName, string version)
    : Exception($"{fileName} was removed from {version} by its developer, so it can't be downloaded.")
{
    public string FileName { get; } = fileName;
    public string Version { get; } = version;
}

