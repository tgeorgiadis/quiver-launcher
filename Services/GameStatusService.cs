using QuiverLauncher.Core.Models;
using QuiverLauncher.Core.Services;
using QuiverLauncher.Models;

namespace QuiverLauncher.Services;

public static class GameStatusService
{
    private const string DefaultInstalledVersion = "0.0.0";

    public const string AndroidPackageFileName = "android-package.txt";

    public static async Task CheckStatusAsync(
        GameInfo game,
        HttpClient httpClient,
        string gamesFolder,
        bool forceUpdateCheck = false,
        bool checkRemoteVersion = true,
        bool applyCachedRelease = true)
    {
        if (string.IsNullOrEmpty(game.FolderName))
        {
            System.Diagnostics.Debug.WriteLine($"Warning: FolderName is null or empty for game {game.Name}");
            game.Status = GameStatus.NotInstalled;
            return;
        }

        game.IsLoading = true;

        try
        {
            var gamePath = game.GetInstallPath(gamesFolder);
            var versionFile = Path.Combine(gamePath, "version.txt");

            var directoryExists = Directory.Exists(gamePath);
            var versionFileExists = File.Exists(versionFile);

            if (OperatingSystem.IsAndroid())
                TryRestoreAndroidPackageName(game, gamePath);

            var androidPackageInstalled = OperatingSystem.IsAndroid() && AppInstallLaunch.Current.IsInstalled(game);

            if (androidPackageInstalled)
            {
                game.InstalledVersion = AppInstallLaunch.Current.GetInstalledVersion(game, gamePath) ?? game.InstalledVersion;
                game.Status = GameStatus.Installed;
            }

            if (game.IsManuallyManaged)
            {
                game.LatestVersion = null;
                if (directoryExists && ManualAppFolderService.HasLaunchableFiles(game, gamesFolder))
                {
                    game.InstalledVersion = "";
                    game.Status = GameStatus.Installed;
                }
                else
                {
                    game.Status = GameStatus.NotInstalled;
                    game.InstalledVersion = "";
                }

                return;
            }

            var isInstalled = androidPackageInstalled;
            if (!isInstalled && OperatingSystem.IsAndroid())
            {
                game.Status = GameStatus.NotInstalled;
                game.InstalledVersion = "";
            }
            else if (!isInstalled && directoryExists)
            {
                if (versionFileExists)
                {
                    try
                    {
                        game.InstalledVersion = (await File.ReadAllTextAsync(versionFile).ConfigureAwait(false))?.Trim();

                        if (string.IsNullOrWhiteSpace(game.InstalledVersion))
                            game.InstalledVersion = await EnsureInstalledVersionFileAsync(versionFile).ConfigureAwait(false);
                    }
                    catch
                    {
                        game.InstalledVersion = null;
                    }

                    game.Status = GameStatus.Installed;
                    isInstalled = true;
                }
                else
                {
                    game.Status = GameStatus.Installed;
                    game.InstalledVersion = await EnsureInstalledVersionFileAsync(versionFile).ConfigureAwait(false);
                    isInstalled = true;
                }
            }
            else if (!isInstalled)
            {
                game.Status = GameStatus.NotInstalled;
                game.InstalledVersion = "";
            }

            if (!checkRemoteVersion)
            {
                if (applyCachedRelease && GitHubApiCache.TryGetCachedVersion(game.RepositorySource, game.Repository, out var cache) && cache != null)
                    game.ApplyCachedRelease(cache.Version, cache.CachedRelease);
            }
            else if (forceUpdateCheck)
                await game.CheckLatestVersionAsync(httpClient, forceCheck: true).ConfigureAwait(false);
            else if (isInstalled)
            {
                if (GitHubApiCache.NeedsUpdateCheck(game.RepositorySource, game.Repository ?? string.Empty, isInstalledGame: true))
                    await game.CheckLatestVersionAsync(httpClient).ConfigureAwait(false);
                else if (GitHubApiCache.TryGetCachedVersion(game.RepositorySource, game.Repository, out var cache) && cache != null)
                    game.ApplyCachedRelease(cache.Version, cache.CachedRelease);
            }
            else
            {
                if (GitHubApiCache.NeedsUpdateCheck(game.RepositorySource, game.Repository ?? string.Empty, isInstalledGame: false))
                    await game.CheckLatestVersionAsync(httpClient).ConfigureAwait(false);
                else if (GitHubApiCache.TryGetCachedVersion(game.RepositorySource, game.Repository, out var cache) && cache != null)
                    game.ApplyCachedRelease(cache.Version, cache.CachedRelease);
            }

            if (isInstalled && string.IsNullOrWhiteSpace(game.InstalledVersion))
            {
                game.InstalledVersion = string.IsNullOrWhiteSpace(game.LatestVersion)
                    ? "Unknown"
                    : DefaultInstalledVersion;
            }

            if (isInstalled)
                game.RefreshInstalledStatus();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error checking status for {game.Name}: {ex.Message}");
            game.Status = GameStatus.NotInstalled;
        }
        finally
        {
            game.IsLoading = false;
        }
    }

    public static void TryRestoreAndroidPackageName(GameInfo game, string gamePath)
    {
        if (!string.IsNullOrWhiteSpace(game.AndroidPackageName) || string.IsNullOrWhiteSpace(gamePath))
            return;

        var packageFile = Path.Combine(gamePath, AndroidPackageFileName);
        if (!File.Exists(packageFile))
            return;

        try
        {
            var packageName = File.ReadAllText(packageFile).Trim();
            if (!string.IsNullOrWhiteSpace(packageName))
                game.AndroidPackageName = packageName;
        }
        catch
        {
            // Keep whatever package name is already on the game.
        }
    }

    internal static async Task<string?> EnsureInstalledVersionFileAsync(string versionFile)
    {
        try
        {
            var versionDirectory = Path.GetDirectoryName(versionFile);
            if (!string.IsNullOrEmpty(versionDirectory))
                Directory.CreateDirectory(versionDirectory);

            await File.WriteAllTextAsync(versionFile, DefaultInstalledVersion).ConfigureAwait(false);
            return DefaultInstalledVersion;
        }
        catch
        {
            return null;
        }
    }
}
