using QuiverLauncher.Core.Models;
using QuiverLauncher.Core.Services;
using QuiverLauncher.Models;

namespace QuiverLauncher.Services;

public static class GameDownloadService
{
    public static bool TrySelectPlatformDownload(GameInfo game, GitHubRelease? release, AppSettings settings)
    {
        if (release == null)
            return false;

        var assets = GitHubReleaseService.GetDownloadableAssets(release);
        if (assets.Count == 0)
            return false;

        game.AvailableDownloads = assets;

        if (assets.Count == 1)
        {
            game.SelectedDownload = assets[0];
            return true;
        }

        var platformIdentifier = GameInfo.GetPlatformIdentifier(settings);
        var matchingAsset = assets.FirstOrDefault(asset =>
            GameInfo.MatchesPlatform(asset.name, platformIdentifier));

        game.SelectedDownload = matchingAsset ?? assets[0];
        return true;
    }
}
