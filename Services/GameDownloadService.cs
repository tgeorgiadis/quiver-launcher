using System.Text.Json;
using QuiverLauncher.Core.Models;
using QuiverLauncher.Core.Services;
using QuiverLauncher.Models;

namespace QuiverLauncher.Services;

public static class GameDownloadService
{
    public static string Context(GameInfo game, GitHubRelease release, AppSettings settings) =>
        JsonSerializer.Serialize(new { game.IdentityKey, release.tag_name, Platform = GameInfo.GetPlatformIdentifier(settings), game.ReleaseAssetFilter });

    public static DownloadAssetSelection Prepare(GameInfo game, GitHubRelease release, AppSettings settings)
    {
        var result = DownloadAssetPolicy.Select(release, GameInfo.GetPlatformIdentifier(settings), game.ReleaseAssetFilter);
        var context = Context(game, release, settings);
        var selected = result.Eligible.Concat(result.Uncertain).FirstOrDefault(a =>
            a.name == game.SelectedDownload?.name && a.browser_download_url == game.SelectedDownload?.browser_download_url);
        if (game.DownloadSelectionContext != context || selected == null) game.ClearDownloadSelection();
        else game.SelectedDownload = selected;
        game.DownloadSelectionContext = context;
        game.DownloadChoices = result;
        game.AvailableDownloads = result.Eligible.ToList();
        return result;
    }

    public static bool TrySelectPlatformDownload(GameInfo game, GitHubRelease? release, AppSettings settings)
    {
        game.ClearDownloadSelection();
        if (release == null) return false;
        var result = Prepare(game, release, settings);
        game.SelectedDownload = result.Automatic;
        return game.SelectedDownload != null;
    }

    public static void SelectExplicit(GameInfo game, GitHubRelease release, AppSettings settings, GitHubAsset asset)
    {
        var result = Prepare(game, release, settings);
        game.SelectedDownload = result.Eligible.Concat(result.Uncertain).FirstOrDefault(a =>
            a.name == asset.name && a.browser_download_url == asset.browser_download_url);
        if (game.SelectedDownload == null) throw new InvalidOperationException("This download is no longer available for the selected platform and release.");
    }
}
