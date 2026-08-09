using FluentAssertions;
using QuiverLauncher.Core.Models;
using QuiverLauncher.Models;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class GameDownloadServiceTests
{
    [Fact]
    public void TrySelectPlatformDownload_selects_matching_platform_asset()
    {
        var game = new GameInfo { Name = "Test", Repository = "owner/app", FolderName = "TestFolder" };
        var settings = new AppSettings { Platform = TargetOS.Windows };
        var release = new GitHubRelease
        {
            tag_name = "v1.0.0",
            assets =
            [
                new GitHubAsset { name = "app-linux-x64.zip", browser_download_url = "https://example.com/linux.zip" },
                new GitHubAsset { name = "app-win-x64.zip", browser_download_url = "https://example.com/win.zip" },
            ],
        };

        GameDownloadService.TrySelectPlatformDownload(game, release, settings).Should().BeTrue();
        game.SelectedDownload!.name.Should().Contain("win");
    }

    [Fact]
    public void TrySelectPlatformDownload_returns_false_without_release()
    {
        var game = new GameInfo();
        GameDownloadService.TrySelectPlatformDownload(game, null, new AppSettings()).Should().BeFalse();
    }
}
