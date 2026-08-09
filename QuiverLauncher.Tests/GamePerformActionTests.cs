using System.IO.Compression;
using System.Net;
using FluentAssertions;
using QuiverLauncher.Core.Models;
using QuiverLauncher.Core.Services;
using QuiverLauncher.Models;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class GamePerformActionTests
{
    public GamePerformActionTests()
    {
        var cacheDir = Path.Combine(Path.GetTempPath(), "QuiverTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(cacheDir);
        GitHubApiCache.Initialize(cacheDir);
    }

    [Fact]
    public async Task PerformActionAsync_returns_false_after_successful_single_asset_download()
    {
        var gamesFolder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(gamesFolder);

        try
        {
            const string assetName = "payload.zip";
            var release = new GitHubRelease
            {
                tag_name = "v1.2.3",
                assets =
                [
                    new GitHubAsset
                    {
                        name = assetName,
                        browser_download_url = "https://example.com/download/asset",
                    },
                ],
            };

            var game = new GameInfo
            {
                Name = "Test Game",
                Repository = "owner/test-game",
                FolderName = "TestGameFolder",
                Status = GameStatus.NotInstalled,
            };
            game.ApplyCachedRelease("v1.2.3", release);

            using var client = new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(CreateMinimalZipWithExe()),
            }));

            var launched = await game.PerformActionAsync(
                client, gamesFolder, new AppSettings(), HeadlessGameDownloadDialogs.Instance);

            launched.Should().BeFalse("downloads must not count as a launch for Close After Launch");
            game.Status.Should().Be(GameStatus.Installed);
        }
        finally
        {
            if (Directory.Exists(gamesFolder))
                Directory.Delete(gamesFolder, true);
        }
    }

    [Fact]
    public async Task PerformActionAsync_returns_false_on_network_error()
    {
        var gamesFolder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(gamesFolder);

        try
        {
            var release = new GitHubRelease
            {
                tag_name = "v1.2.3",
                assets =
                [
                    new GitHubAsset
                    {
                        name = "payload.zip",
                        browser_download_url = "https://example.com/download/asset",
                    },
                ],
            };

            var game = new GameInfo
            {
                Name = "Test Game",
                Repository = "owner/test-game",
                FolderName = "TestGameFolder",
                Status = GameStatus.NotInstalled,
            };
            game.ApplyCachedRelease("v1.2.3", release);

            using var client = new HttpClient(new StubHttpMessageHandler(_ =>
                throw new HttpRequestException("Network error")));

            var launched = await game.PerformActionAsync(
                client, gamesFolder, new AppSettings(), HeadlessGameDownloadDialogs.Instance);

            launched.Should().BeFalse("failed downloads must not count as a launch for Close After Launch");
            game.Status.Should().Be(GameStatus.NotInstalled);
            game.DownloadProgress.Should().Be(0);
        }
        finally
        {
            if (Directory.Exists(gamesFolder))
                Directory.Delete(gamesFolder, true);
        }
    }

    [Fact]
    public async Task PerformActionAsync_returns_false_when_install_directory_is_missing()
    {
        var gamesFolder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(gamesFolder);

        try
        {
            var game = new GameInfo
            {
                Name = "Missing Install",
                Repository = "owner/missing",
                FolderName = "MissingInstall",
                Status = GameStatus.Installed,
                InstalledVersion = "v1.0.0",
            };

            using var client = new HttpClient(new StubHttpMessageHandler(_ =>
                throw new InvalidOperationException("HTTP should not be called when launching")));

            var launched = await game.PerformActionAsync(
                client, gamesFolder, new AppSettings(), HeadlessGameDownloadDialogs.Instance);

            launched.Should().BeFalse("launch failures must not count as a launch for Close After Launch");
        }
        finally
        {
            if (Directory.Exists(gamesFolder))
                Directory.Delete(gamesFolder, true);
        }
    }

    private static byte[] CreateMinimalZipWithExe()
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("game.exe");
            using var writer = entry.Open();
            writer.Write(new byte[] { 0x4D, 0x5A, 0x90, 0x00 });
        }

        return ms.ToArray();
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(_handler(request));
    }
}
