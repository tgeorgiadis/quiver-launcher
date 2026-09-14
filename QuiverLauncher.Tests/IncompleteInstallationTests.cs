using System.IO.Compression;
using System.Net;
using FluentAssertions;
using QuiverLauncher.Core.Models;
using QuiverLauncher.Core.Services;
using QuiverLauncher.Models;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public sealed class IncompleteInstallationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "quiver-incomplete-" + Guid.NewGuid().ToString("N"));
    private string GamePath => Path.Combine(_root, "game");
    private string VersionPath => Path.Combine(GamePath, "version.txt");
    private string IncompletePath => Path.Combine(GamePath, GameInstallationService.IncompleteInstallFileName);
    private static string ExecutableName => OperatingSystem.IsMacOS() ? "game" : "game.exe";
    private static GitHubRelease Release => new()
    {
        tag_name = "v2", assets = [new GitHubAsset { name = "payload.zip", browser_download_url = "https://example.test/payload.zip" }]
    };
    public IncompleteInstallationTests() => Directory.CreateDirectory(GamePath);
    public void Dispose() => Directory.Delete(_root, true);
    private GameInfo Game() => new() { Name = "Game", FolderName = "game", Repository = "fixture/" + Guid.NewGuid().ToString("N") };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Leftover_folder_is_not_installed_even_with_a_version_file(bool hasVersion)
    {
        if (hasVersion) File.WriteAllText(VersionPath, "v1");
        File.WriteAllText(Path.Combine(GamePath, "LICENSE"), new string('x', 4096));
        File.WriteAllText(Path.Combine(GamePath, "game.dll"), "library");
        File.WriteAllText(Path.Combine(GamePath, "save.dat"), "save");
        var game = Game();
        game.InstalledVersion = "v1";
        game.LatestVersion = "v2";
        game.Status = GameStatus.UpdateAvailable;
        await Refresh(game);
        game.Status.Should().Be(GameStatus.NotInstalled);
        game.InstalledVersion.Should().BeNullOrEmpty();
        game.CanUpdate.Should().BeFalse();
        File.Exists(VersionPath).Should().Be(hasVersion, "status checks must not invent an installation version");
        File.ReadAllText(Path.Combine(GamePath, "save.dat")).Should().Be("save");
    }

    [Fact]
    public async Task Removing_the_executable_clears_installed_and_update_status_on_restart()
    {
        var executable = Path.Combine(GamePath, ExecutableName);
        File.WriteAllText(executable, "#!/bin/sh\nexit 0\n");
        File.WriteAllText(VersionPath, "v1");
        var game = Game();
        game.LatestVersion = "v2";
        await Refresh(game);
        game.Status.Should().Be(GameStatus.UpdateAvailable);
        File.Delete(executable); // Model removal/quarantine without invoking antivirus.
        var restarted = Game();
        restarted.LatestVersion = "v2";
        await Refresh(restarted);
        restarted.Status.Should().Be(GameStatus.NotInstalled);
        restarted.CanUpdate.Should().BeFalse();
    }

    [Fact]
    public async Task Existing_launchable_install_without_version_keeps_legacy_detection()
    {
        var nested = Path.Combine(GamePath, "bin");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(nested, ExecutableName), "#!/bin/sh\nexit 0\n");
        var game = Game();
        await Refresh(game);
        game.Status.Should().Be(GameStatus.Installed);
        game.InstalledVersion.Should().Be("0.0.0");
    }

    [Fact]
    public async Task Partial_extraction_stays_uninstalled_after_restart_and_retry_can_complete()
    {
        // The first file extracts, then a conflicting path makes extraction fail.
        File.WriteAllText(Path.Combine(GamePath, "blocked"), "existing support file");
        var game = Game();
        var dialogs = new Dialogs();
        using (var client = Client(Zip((ExecutableName, "#!/bin/sh\nexit 0\n"), ("blocked/file.txt", "data"))))
            await Install(game, client, dialogs);
        dialogs.Error.Should().NotBeNull();
        File.Exists(Path.Combine(GamePath, ExecutableName)).Should().BeTrue("this is a partial extraction, not just an empty folder");
        File.Exists(IncompletePath).Should().BeTrue();
        game.Status.Should().Be(GameStatus.NotInstalled);
        var restarted = Game();
        restarted.LatestVersion = "v2";
        await Refresh(restarted);
        restarted.Status.Should().Be(GameStatus.NotInstalled);
        File.Exists(VersionPath).Should().BeFalse();

        dialogs = new Dialogs();
        using (var client = Client(Zip((ExecutableName, "#!/bin/sh\nexit 0\n"))))
            await Install(restarted, client, dialogs);
        dialogs.Error.Should().BeNull();
        restarted.Status.Should().Be(GameStatus.Installed);
        File.Exists(IncompletePath).Should().BeFalse();
        await Refresh(restarted);
        restarted.Status.Should().Be(GameStatus.Installed);
        File.ReadAllText(Path.Combine(GamePath, "blocked")).Should().Be("existing support file");
    }

    [Fact]
    public async Task Download_with_no_remaining_executable_does_not_report_success()
    {
        var game = Game();
        var dialogs = new Dialogs();
        using var client = Client(Zip(("README.txt", "files left after quarantine"), ("save.dat", "save")));
        await Install(game, client, dialogs);
        dialogs.Error.Should().Contain("did not leave a launchable app");
        game.Status.Should().Be(GameStatus.NotInstalled);
        game.InstalledVersion.Should().BeNullOrEmpty();
        var restarted = Game();
        await Refresh(restarted);
        restarted.Status.Should().Be(GameStatus.NotInstalled);
        File.ReadAllText(Path.Combine(GamePath, "save.dat")).Should().Be("save");
    }

    [Fact]
    public async Task Failed_update_keeps_a_previous_usable_installation()
    {
        File.WriteAllText(Path.Combine(GamePath, ExecutableName), "#!/bin/sh\nexit 0\n");
        File.WriteAllText(VersionPath, "v1");
        var game = Game();
        game.InstalledVersion = "v1";
        game.LatestVersion = "v2";
        game.Status = GameStatus.UpdateAvailable;
        using var client = Client([], HttpStatusCode.InternalServerError);
        await GameDownloadInstallService.DownloadAndInstallAsync(game, client, _root, Release, new(), GameStatus.UpdateAvailable, new Dialogs());
        game.Status.Should().Be(GameStatus.UpdateAvailable);
        game.InstalledVersion.Should().Be("v1");
        File.Exists(IncompletePath).Should().BeFalse();
    }

    private Task Install(GameInfo game, HttpClient client, Dialogs dialogs) =>
        GameDownloadInstallService.DownloadAndInstallAsync(game, client, _root, Release, new(), GameStatus.NotInstalled, dialogs);
    private async Task Refresh(GameInfo game)
    {
        using var client = Client([]);
        await GameStatusService.CheckStatusAsync(game, client, _root, checkRemoteVersion: false, applyCachedRelease: false);
    }
    private static HttpClient Client(byte[] bytes, HttpStatusCode status = HttpStatusCode.OK) => new(new Handler(bytes, status));
    private sealed class Handler(byte[] bytes, HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new ByteArrayContent(bytes) });
    }
    private static byte[] Zip(params (string Name, string Contents)[] files)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, contents) in files)
            {
                using var writer = new StreamWriter(archive.CreateEntry(name).Open());
                writer.Write(contents);
            }
        return stream.ToArray();
    }
    private sealed class Dialogs : IGameDownloadDialogs
    {
        public string? Error;
        public Task<bool> ConfirmDownloadWithoutRunnerAsync() => Task.FromResult(true);
        public Task<LinuxWindowsRunnerConfig?> ConfigureWindowsRunnerAsync(string gamePath, LinuxWindowsRunnerConfig? existing = null, bool isInstall = true) => Task.FromResult<LinuxWindowsRunnerConfig?>(null);
        public Task ShowRateLimitExceededAsync() => Task.CompletedTask;
        public Task ShowGitLabRateLimitExceededAsync() => Task.CompletedTask;
        public Task ShowErrorAsync(string message, string title) { Error = message; return Task.CompletedTask; }
    }
}
