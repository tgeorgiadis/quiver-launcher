using System.IO.Compression;
using System.Net;
using FluentAssertions;
using QuiverLauncher.Core.Models;
using QuiverLauncher.Core.Services;
using QuiverLauncher.Models;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class GameDownloadInstallServiceTests
{
    public GameDownloadInstallServiceTests()
    {
        var cacheDir = Path.Combine(Path.GetTempPath(), "QuiverTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(cacheDir);
        GitHubApiCache.Initialize(cacheDir);
    }

    [Fact]
    public async Task DownloadAndInstallAsync_installs_single_zip_asset()
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
            };

            var dialogs = new RecordingDialogs();
            using var client = new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(CreateMinimalZipWithExe()),
            }));

            await GameDownloadInstallService.DownloadAndInstallAsync(
                game,
                client,
                gamesFolder,
                release,
                new AppSettings(),
                GameStatus.NotInstalled,
                dialogs);

            dialogs.LastError.Should().BeNull("install failed with: {0}", dialogs.LastError);
            game.Status.Should().Be(GameStatus.Installed);
            game.InstalledVersion.Should().Be("v1.2.3");

            var gamePath = game.GetInstallPath(gamesFolder);
            File.Exists(Path.Combine(gamePath, "version.txt")).Should().BeTrue();
            File.ReadAllText(Path.Combine(gamePath, "version.txt")).Trim().Should().Be("v1.2.3");
        }
        finally
        {
            if (Directory.Exists(gamesFolder))
                Directory.Delete(gamesFolder, true);
        }
    }

    [Fact]
    public async Task DownloadAndInstallAsync_installs_7z_asset_from_unique_staging_path()
    {
        var gamesFolder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(gamesFolder);
        var archivePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.7z");

        try
        {
            GameInstallationServiceSevenZipTests.WriteSolidSevenZip(archivePath, new Dictionary<string, string>
            {
                ["game.exe"] = "staged-binary",
                ["readme.txt"] = "staged-readme",
            });

            const string assetName = "CutTheRopeDX-v2.29.0.3-Windows-x64.7z";
            var release = new GitHubRelease
            {
                tag_name = "v2.29.0.3",
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
                Name = "Cut the Rope DX",
                Repository = "owner/cuttherope-dx",
                FolderName = "CutTheRopeDX",
            };

            var dialogs = new RecordingDialogs();
            using var client = new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(File.ReadAllBytes(archivePath)),
            }));

            await GameDownloadInstallService.DownloadAndInstallAsync(
                game,
                client,
                gamesFolder,
                release,
                new AppSettings(),
                GameStatus.NotInstalled,
                dialogs);

            dialogs.LastError.Should().BeNull("install failed with: {0}", dialogs.LastError);
            game.Status.Should().Be(GameStatus.Installed);

            var gamePath = game.GetInstallPath(gamesFolder);
            File.ReadAllText(Path.Combine(gamePath, "game.exe")).Should().Be("staged-binary");
            File.ReadAllText(Path.Combine(gamePath, "version.txt")).Trim().Should().Be("v2.29.0.3");
            game.DownloadProgress.Should().Be(0);
        }
        finally
        {
            if (File.Exists(archivePath))
                File.Delete(archivePath);
            if (Directory.Exists(gamesFolder))
                Directory.Delete(gamesFolder, true);
        }
    }

    [Fact]
    public async Task DownloadAndInstallAsync_resets_status_when_release_has_no_assets()
    {
        var gamesFolder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(gamesFolder);

        try
        {
            var game = new GameInfo
            {
                Name = "Empty Assets",
                Repository = "owner/empty",
                FolderName = "EmptyAssets",
            };

            var release = new GitHubRelease
            {
                tag_name = "v1.0.0",
                assets = [],
            };

            using var client = new HttpClient(new StubHttpMessageHandler(_ =>
                throw new InvalidOperationException("HTTP should not be called when there are no assets")));

            await GameDownloadInstallService.DownloadAndInstallAsync(
                game,
                client,
                gamesFolder,
                release,
                new AppSettings(),
                GameStatus.NotInstalled,
                HeadlessGameDownloadDialogs.Instance);

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
    public async Task DownloadAndInstallAsync_waits_for_asset_selection_when_multiple_downloads()
    {
        var gamesFolder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(gamesFolder);

        try
        {
            var game = new GameInfo
            {
                Name = "Multi Asset",
                Repository = "owner/multi",
                FolderName = "MultiAsset",
            };

            var release = new GitHubRelease
            {
                tag_name = "v2.0.0",
                assets =
                [
                    new GitHubAsset { name = "linux.zip", browser_download_url = "https://example.com/linux.zip" },
                    new GitHubAsset { name = "win.zip", browser_download_url = "https://example.com/win.zip" },
                ],
            };

            using var client = new HttpClient(new StubHttpMessageHandler(_ =>
                throw new InvalidOperationException("HTTP should not be called before asset selection")));

            await GameDownloadInstallService.DownloadAndInstallAsync(
                game,
                client,
                gamesFolder,
                release,
                new AppSettings { Platform = QuiverLauncher.Core.Models.TargetOS.LinuxX64 },
                GameStatus.NotInstalled,
                HeadlessGameDownloadDialogs.Instance);

            game.Status.Should().Be(GameStatus.NotInstalled);
            game.AvailableDownloads.Should().HaveCount(2);
            game.SelectedDownload.Should().BeNull();
        }
        finally
        {
            if (Directory.Exists(gamesFolder))
                Directory.Delete(gamesFolder, true);
        }
    }

    [Fact]
    public async Task DownloadAndInstallAsync_clears_selected_download_after_failed_install()
    {
        var gamesFolder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(gamesFolder);

        try
        {
            var badAsset = new GitHubAsset
            {
                name = "app-android.apk",
                browser_download_url = "https://example.com/app-android.apk",
            };
            var game = new GameInfo
            {
                Name = "Bad Asset",
                Repository = "owner/bad-asset",
                FolderName = "BadAsset",
                SelectedDownload = badAsset,
                AvailableDownloads =
                [
                    badAsset,
                    new GitHubAsset { name = "app-win.zip", browser_download_url = "https://example.com/app-win.zip" },
                ],
            };

            var release = new GitHubRelease
            {
                tag_name = "v1.0.0",
                assets =
                [
                    badAsset,
                    new GitHubAsset { name = "app-win.zip", browser_download_url = "https://example.com/app-win.zip" },
                ],
            };

            var dialogs = new RecordingDialogs();
            using var client = new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([0x50, 0x4B, 0x03, 0x04]),
            }));

            await GameDownloadInstallService.DownloadAndInstallAsync(
                game,
                client,
                gamesFolder,
                release,
                new AppSettings(),
                GameStatus.NotInstalled,
                dialogs);

            game.Status.Should().Be(GameStatus.NotInstalled);
            game.SelectedDownload.Should().BeNull();
            game.DownloadProgress.Should().Be(0);
            dialogs.LastError.Should().NotBeNullOrEmpty();
        }
        finally
        {
            if (Directory.Exists(gamesFolder))
                Directory.Delete(gamesFolder, true);
        }
    }

    [Theory]
    [InlineData(403, true)]
    [InlineData(429, true)]
    [InlineData(403, false)]
    [InlineData(401, false)]
    [InlineData(404, false)]
    [InlineData(500, false)]
    public async Task Failed_release_request_is_not_reported_as_no_releases(int status, bool rateLimited)
    {
        var game = new GameInfo { Name = "Yu-Gi-Oh! Forbidden Memories", FolderName = "ygofm", Repository = "owner/" + Guid.NewGuid().ToString("N") };
        var dialogs = new RecordingDialogs { OnDialog = () =>
        {
            game.Status.Should().Be(GameStatus.NotInstalled);
            game.DownloadProgress.Should().Be(0);
        }};
        var requests = 0;
        using var client = new HttpClient(new StubHttpMessageHandler(_ =>
        {
            requests++;
            var response = new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent("{}") };
            if (rateLimited) response.Headers.Add("X-RateLimit-Remaining", "0");
            return response;
        }));

        await GameDownloadInstallService.DownloadAndInstallAsync(game, client, Path.GetTempPath(), null,
            new AppSettings { Platform = TargetOS.LinuxX64 }, GameStatus.NotInstalled, dialogs);

        requests.Should().Be(1);
        dialogs.RateLimitShown.Should().Be(rateLimited);
        if (rateLimited) dialogs.LastError.Should().BeNull();
        else dialogs.LastError.Should().Contain($"HTTP {status}").And.NotContain("No Releases");
        game.DownloadChoices.Should().BeNull();
        GitHubApiCache.TryGetCachedVersion(game.RepositorySource, game.Repository, out _).Should().BeFalse();
    }

    [Fact]
    public async Task Successful_empty_release_list_still_reports_no_releases()
    {
        var game = new GameInfo { Name = "Empty", FolderName = "empty", Repository = "owner/" + Guid.NewGuid().ToString("N") };
        using var client = new HttpClient(new StubHttpMessageHandler(request =>
            request.RequestUri!.AbsolutePath.EndsWith("/latest")
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") }));
        var dialogs = new RecordingDialogs();
        await GameDownloadInstallService.DownloadAndInstallAsync(game, client, Path.GetTempPath(), null,
            new AppSettings { Platform = TargetOS.LinuxX64 }, GameStatus.NotInstalled, dialogs);
        dialogs.LastError.Should().StartWith("No Releases:");
        dialogs.RateLimitShown.Should().BeFalse();
    }

    [Fact]
    public async Task Forbidden_memories_fetch_populates_linux_and_windows_choices_without_downloading()
    {
        var release = DownloadAssetPolicyTests.ForbiddenMemoriesRelease();
        var game = new GameInfo { Name = "Yu-Gi-Oh! Forbidden Memories", FolderName = "ygofm", Repository = "owner/" + Guid.NewGuid().ToString("N") };
        var requests = 0;
        using var client = new HttpClient(new StubHttpMessageHandler(request =>
        {
            request.RequestUri!.Host.Should().Be("api.github.com");
            requests++;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(
                request.RequestUri.AbsolutePath.EndsWith("/latest")
                    ? System.Text.Json.JsonSerializer.Serialize(release)
                    : System.Text.Json.JsonSerializer.Serialize(new[] { release })) };
        }));
        var dialogs = new RecordingDialogs();
        await GameDownloadInstallService.DownloadAndInstallAsync(game, client, Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), null,
            new AppSettings { Platform = TargetOS.LinuxX64 }, GameStatus.NotInstalled, dialogs);
        requests.Should().Be(2);
        dialogs.LastError.Should().BeNull();
        game.AvailableDownloads.Select(a => a.name).Should().Equal(
            "ygofm-0.5.9-linux-x64.zip", "ygofm-0.5.9-win-x64.zip");
        game.DownloadChoices!.NeedsChoice.Should().BeTrue();
        game.SelectedDownload.Should().BeNull();
        game.Status.Should().Be(GameStatus.NotInstalled);
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

    private sealed class RecordingDialogs : IGameDownloadDialogs
    {
        public string? LastError { get; private set; }
        public bool RateLimitShown { get; private set; }
        public Action? OnDialog { get; init; }

        public Task<bool> ConfirmDownloadWithoutRunnerAsync() => Task.FromResult(true);

        public Task<LinuxWindowsRunnerConfig?> ConfigureWindowsRunnerAsync(
            string gamePath,
            LinuxWindowsRunnerConfig? existing = null,
            bool isInstall = true) =>
            HeadlessGameDownloadDialogs.Instance.ConfigureWindowsRunnerAsync(gamePath, existing, isInstall);

        public Task ShowRateLimitExceededAsync()
        {
            OnDialog?.Invoke();
            RateLimitShown = true;
            return Task.CompletedTask;
        }

        public Task ShowGitLabRateLimitExceededAsync() => Task.CompletedTask;

        public Task ShowErrorAsync(string message, string title)
        {
            OnDialog?.Invoke();
            LastError = $"{title}: {message}";
            return Task.CompletedTask;
        }
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
