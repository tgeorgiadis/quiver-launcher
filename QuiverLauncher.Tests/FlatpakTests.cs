using FluentAssertions;
using QuiverLauncher.Core.Models;
using QuiverLauncher.Core.Services;
using QuiverLauncher.Models;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public sealed class FlatpakTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "quiver-flatpak-" + Guid.NewGuid());
    private readonly FakeRunner _runner = new();
    private readonly FakeBundles _bundles = new();
    private string GamePath => Path.Combine(_root, "game");
    private FlatpakService Service(Action<string, FlatpakReceipt>? writer = null) =>
        new(_runner, _bundles, Path.Combine(_root, "owners"), writer);
    public FlatpakTests() { Directory.CreateDirectory(_root); }
    public void Dispose() => TestFixtures.CleanupDirectory(_root);

    [Theory]
    [InlineData("TriAevum-v0.6.0-alpha.2c-Linux-x86_64.flatpak", "Linux-X64", true)]
    [InlineData("TriAevum-v0.6.0-alpha.2c-Linux-x86_64.flatpak", "Linux-ARM64", false)]
    [InlineData("game-aarch64.flatpak", "Linux-X64", false)]
    [InlineData("game-aarch64.flatpak", "Linux-ARM64", true)]
    [InlineData("game.flatpak", "Linux-X64", true)]
    [InlineData("game.FLATPAK", "Linux-ARM64", true)]
    [InlineData("game.flatpak", "Windows", false)]
    [InlineData("game.flatpak", "Android", false)]
    [InlineData("game.flatpak", "macOS", false)]
    public void Bundle_platform_detection(string asset, string platform, bool matches)
    {
        PlatformAssetMatcher.MatchesPlatform(asset, platform).Should().Be(matches);
        PlatformAssetMatcher.IsWindowsAsset(asset).Should().BeFalse();
        GameInfo.GetPlatformIcon(asset).Should().EndWith("platform_lin.png");
        CatalogPlatformSupport.FromAssetNames([asset]).Should().Be(CatalogPlatformFlags.Linux);
    }

    [Fact]
    public void Bundle_support_does_not_expose_references_or_wrapped_bundles()
    {
        var release = new GitHubRelease { assets = [new() { name = "app.flatpak" }, new() { name = "app.flatpakref" },
            new() { name = "app.flatpakrepo" }, new() { name = "app-flatpak.zip" }, new() { name = "app.flatpak.sha256" }] };
        GitHubReleaseService.GetDownloadableAssets(release).Select(a => a.name).Should().Equal("app.flatpak");
        DownloadAssetPolicy.Select(release, "Windows").Uncertain.Should().BeEmpty();
    }

    [Fact]
    public void Multiple_linux_formats_still_require_a_choice()
    {
        var release = new GitHubRelease { assets = [new() { name = "app.flatpak" }, new() { name = "app.AppImage" }] };
        var selection = DownloadAssetPolicy.Select(release, "Linux-X64");
        selection.NeedsChoice.Should().BeTrue();
        selection.Automatic.Should().BeNull();
    }

    [Fact]
    public void Content_disposition_can_identify_a_bundle()
    {
        GameInstallationService.ResolveEffectiveAssetName("download", "game.flatpak").Should().Be("game.flatpak");
        GameInstallationService.IsSingleFileExecutableAsset("game.flatpak").Should().BeFalse();
    }

    [Fact]
    public async Task Install_records_verified_bundle_and_uses_user_scope()
    {
        var receipt = await Service().InstallAsync(Path.Combine(_root, "game with spaces.flatpak"), "v1", GamePath);
        receipt.ApplicationId.Should().Be("io.github.example.Game");
        FlatpakService.ReadReceipt(GamePath).Should().Be(receipt);
        File.ReadAllText(Path.Combine(GamePath, "version.txt")).Should().Be("v1");
        File.Exists(Path.Combine(GamePath, FlatpakService.PendingFileName)).Should().BeFalse();
        _runner.Commands.Single(c => c[0] == "install").Should().Equal("install", "--user", "--bundle",
            "--or-update", "--noninteractive", "--assumeyes", Path.Combine(_root, "game with spaces.flatpak"));
        (await Service().GetStateAsync(GamePath))!.Installed.Should().BeTrue();
    }

    [Fact]
    public async Task Update_replaces_receipt_only_after_package_verification()
    {
        await Service().InstallAsync("game.flatpak", "v1", GamePath);
        _bundles.Receipt = _bundles.Receipt with { Commit = new string('b', 64) };
        _runner.NextCommit = _bundles.Receipt.Commit;
        await Service().InstallAsync("game.flatpak", "v2", GamePath);
        (await Service().GetStateAsync(GamePath))!.Version.Should().Be("v2");
    }

    [Fact]
    public async Task Failed_update_preserves_previous_working_release()
    {
        await Service().InstallAsync("game.flatpak", "v1", GamePath);
        var previous = FlatpakService.ReadReceipt(GamePath);
        _bundles.Receipt = _bundles.Receipt with { Commit = new string('b', 64) };
        _runner.InstallError = "Runtime not found";
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service().InstallAsync("game.flatpak", "v2", GamePath));
        FlatpakService.ReadReceipt(GamePath).Should().Be(previous);
        var state = await Service().GetStateAsync(GamePath);
        state!.Installed.Should().BeTrue();
        state.Version.Should().Be("v1");
    }

    [Fact]
    public async Task Failed_first_install_never_counts_metadata_as_installed()
    {
        _runner.InstallError = "Runtime not found";
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Service().InstallAsync("game.flatpak", "v1", GamePath));
        ex.Message.Should().Contain("Runtime not found");
        (await Service().GetStateAsync(GamePath))!.Installed.Should().BeFalse();
        File.Exists(Path.Combine(GamePath, "version.txt")).Should().BeFalse();
    }

    [Fact]
    public async Task Successful_install_recovers_when_final_receipt_write_fails()
    {
        var service = Service((_, _) => throw new IOException("Disk full"));
        await Assert.ThrowsAsync<IOException>(() => service.InstallAsync("game.flatpak", "v1", GamePath));
        FlatpakService.ReadReceipt(GamePath).Should().BeNull();
        FlatpakService.ReadReceipt(GamePath, pending: true).Should().NotBeNull();
        var state = await Service().GetStateAsync(GamePath);
        state!.Installed.Should().BeTrue();
        state.Version.Should().Be("v1");
        await Service().UninstallAsync(GamePath);
        (await Service().GetStateAsync(GamePath))!.Installed.Should().BeFalse();
    }

    [Fact]
    public async Task Unexpected_installed_commit_does_not_record_success()
    {
        _runner.NextCommit = new string('c', 64);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service().InstallAsync("game.flatpak", "v1", GamePath));
        FlatpakService.ReadReceipt(GamePath).Should().BeNull();
        (await Service().GetStateAsync(GamePath))!.Version.Should().Be("Unknown");
    }

    [Fact]
    public async Task External_removal_and_update_are_reflected_in_status()
    {
        await Service().InstallAsync("game.flatpak", "v1", GamePath);
        _runner.Commit = new string('c', 64);
        (await Service().GetStateAsync(GamePath))!.Version.Should().Be("Unknown");
        _runner.Commit = null;
        var state = await Service().GetStateAsync(GamePath);
        state!.Installed.Should().BeFalse();
        state.Version.Should().BeEmpty();
    }

    [Fact]
    public async Task Package_manager_errors_are_not_treated_as_uninstalled()
    {
        await Service().InstallAsync("game.flatpak", "v1", GamePath);
        _runner.ListError = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service().GetStateAsync(GamePath));
        FlatpakService.ReadReceipt(GamePath)!.ReleaseTag.Should().Be("v1");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Uninstall_preserves_data_and_does_not_remove_shared_dependencies(bool fail)
    {
        await Service().InstallAsync("game.flatpak", "v1", GamePath);
        var save = Path.Combine(GamePath, "save.dat");
        File.WriteAllText(save, "keep");
        _runner.UninstallError = fail;
        if (fail)
            await Assert.ThrowsAsync<InvalidOperationException>(() => Service().UninstallAsync(GamePath));
        else
            await Service().UninstallAsync(GamePath);
        File.ReadAllText(save).Should().Be("keep");
        _runner.Commands.Single(c => c[0] == "uninstall").Should().Equal("uninstall", "--user",
            "--noninteractive", "--assumeyes", "--no-related", _bundles.Receipt.Reference);
        (await Service().GetStateAsync(GamePath))!.Installed.Should().Be(fail);
        File.Exists(Path.Combine(GamePath, "version.txt")).Should().Be(fail);
    }

    [Fact]
    public async Task Duplicate_entries_are_blocked_even_from_another_service_instance()
    {
        await Service().InstallAsync("game.flatpak", "v1", GamePath);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service().InstallAsync("game.flatpak", "v1", Path.Combine(_root, "other")));
        _runner.Commands.Count(c => c[0] == "install").Should().Be(1);
        await Service().UninstallAsync(GamePath);
        await Service().InstallAsync("game.flatpak", "v1", Path.Combine(_root, "other"));
    }

    [Fact]
    public async Task Different_identity_and_rollback_are_rejected_before_installation()
    {
        await Service().InstallAsync("game.flatpak", "v2", GamePath);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service().InstallAsync("game.flatpak", "v1", GamePath));
        _bundles.Receipt = _bundles.Receipt with { Branch = "beta" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service().InstallAsync("game.flatpak", "v3", GamePath));
        _runner.Commands.Count(c => c[0] == "install").Should().Be(1);
    }

    [Fact]
    public async Task Missing_flatpak_and_invalid_bundles_do_not_start_installation()
    {
        _bundles.Error = new InvalidOperationException(FlatpakService.SetupGuidance);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service().CheckAvailableAsync());
        _runner.Commands.Should().BeEmpty();
        _bundles.Error = null;
        _bundles.Receipt = _bundles.Receipt with { ApplicationId = "../../escape" };
        await Assert.ThrowsAsync<InvalidDataException>(() => Service().InstallAsync("game.flatpak", "v1", GamePath));
        _runner.Commands.Should().NotContain(c => c[0] == "install");
    }

    [Fact]
    public void Launch_arguments_and_shortcuts_select_the_exact_user_installation()
    {
        var args = FlatpakService.LaunchArguments(_bundles.Receipt);
        args.Should().Equal("run", "--user", "--arch=x86_64", "--branch=stable", "io.github.example.Game");
        if (!OperatingSystem.IsLinux())
        {
            var info = FlatpakService.StartInfo(args, capture: false);
            info.FileName.Should().Be("flatpak");
            info.UseShellExecute.Should().BeFalse();
            info.ArgumentList.Should().Equal(args);
        }
        var desktop = ShortcutHelper.BuildLinuxDesktopFile("Game", new("flatpak", args, _root), null);
        desktop.Should().Contain("--user").And.Contain("--branch=stable").And.Contain("io.github.example.Game");
    }

    [Fact]
    public void Flatpak_entries_hide_portable_actions_and_unknown_versions_remain_launchable()
    {
        var game = new GameInfo { Name = "Game", Repository = "owner/game", IsFlatpak = true,
            Status = GameStatus.Installed, InstalledVersion = "Unknown", LatestVersion = "v2" };
        game.RefreshInstalledStatus();
        game.CanLaunch.Should().BeTrue();
        game.CanChangeVersion.Should().BeFalse();
        game.CanManageMods.Should().BeFalse();
        game.CanLaunchOptions.Should().BeFalse();
        game.InstalledAppRemovalLabel.Should().Be("Uninstall");
    }

    [Fact]
    public async Task Download_workflow_uses_flatpak_and_preserves_card_state_on_failed_update()
    {
        var game = new GameInfo { Name = "Game", FolderName = "game", Repository = "flatpak/" + Guid.NewGuid() };
        var handler = new BundleDownload();
        using var client = new HttpClient(handler);
        var dialogs = new RecordingDialogs();
        var release = Release("v1", "Game.flatpak");
        await GameDownloadInstallService.DownloadAndInstallAsync(game, client, _root, release,
            new AppSettings { Platform = TargetOS.LinuxX64 }, GameStatus.NotInstalled, dialogs, Service());
        dialogs.Error.Should().BeNull();
        game.IsFlatpak.Should().BeTrue();
        game.IsInstallIndeterminate.Should().BeFalse();
        game.Status.Should().Be(GameStatus.Installed);
        game.InstalledVersion.Should().Be("v1");

        _runner.InstallError = "Runtime unavailable";
        _bundles.Receipt = _bundles.Receipt with { Commit = new string('b', 64) };
        await GameDownloadInstallService.DownloadAndInstallAsync(game, client, _root, Release("v2", "Game.flatpak"),
            new AppSettings { Platform = TargetOS.LinuxX64 }, GameStatus.UpdateAvailable, dialogs, Service());
        dialogs.Error.Should().Contain("Runtime unavailable");
        game.InstalledVersion.Should().Be("v1");
        game.Status.Should().Be(GameStatus.UpdateAvailable);
        game.IsInstallIndeterminate.Should().BeFalse();
    }

    [Fact]
    public async Task Availability_is_checked_before_downloading_bundle_bytes()
    {
        _bundles.Error = new InvalidOperationException(FlatpakService.SetupGuidance);
        var handler = new BundleDownload();
        using var client = new HttpClient(handler);
        var dialogs = new RecordingDialogs();
        var game = new GameInfo { Name = "Game", FolderName = "game", Repository = "flatpak/" + Guid.NewGuid() };
        await GameDownloadInstallService.DownloadAndInstallAsync(game, client, _root, Release("v1", "Game.flatpak"),
            new AppSettings { Platform = TargetOS.LinuxX64 }, GameStatus.NotInstalled, dialogs, Service());
        handler.Requests.Should().Be(0);
        dialogs.Error.Should().Contain("Install Flatpak");
        game.Status.Should().Be(GameStatus.NotInstalled);
    }

    [Fact]
    public async Task Package_formats_cannot_switch_while_installed_even_at_the_same_release()
    {
        var game = new GameInfo { Name = "Game", FolderName = "game", Repository = "flatpak/" + Guid.NewGuid() };
        var handler = new BundleDownload();
        using var client = new HttpClient(handler);
        var dialogs = new RecordingDialogs();
        Directory.CreateDirectory(GamePath);
        File.WriteAllText(Path.Combine(GamePath, "version.txt"), "v1");
        var portableExecutable = Path.Combine(GamePath, OperatingSystem.IsMacOS() ? "game" : "game.exe");
        File.WriteAllText(portableExecutable, "#!/bin/sh\nexit 0\n");
        await GameDownloadInstallService.DownloadAndInstallAsync(game, client, _root, Release("v1", "Game.flatpak"),
            new AppSettings { Platform = TargetOS.LinuxX64 }, GameStatus.Installed, dialogs, Service());
        dialogs.Error.Should().Contain("Uninstall");
        handler.Requests.Should().Be(0);
        game.IsInstalled.Should().BeTrue();

        File.Delete(Path.Combine(GamePath, "version.txt"));
        File.Delete(portableExecutable);
        await Service().InstallAsync("Game.flatpak", "v1", GamePath);
        await GameDownloadInstallService.DownloadAndInstallAsync(game, client, _root, Release("v2", "Game.AppImage"),
            new AppSettings { Platform = TargetOS.LinuxX64 }, GameStatus.UpdateAvailable, dialogs, Service());
        handler.Requests.Should().Be(0);
        game.IsInstalled.Should().BeTrue();
    }

    [Fact]
    public async Task Status_checks_use_package_manager_and_never_the_metadata_folder()
    {
        await Service().InstallAsync("game.flatpak", "v1", GamePath);
        var game = new GameInfo { Name = "Game", FolderName = "game", Repository = "flatpak/" + Guid.NewGuid() };
        using var client = new HttpClient(new BundleDownload());
        async Task Check() => await GameStatusService.CheckStatusAsync(game, client, _root,
            checkRemoteVersion: false, applyCachedRelease: false, flatpakService: Service());
        await Check();
        game.IsInstalled.Should().BeTrue();
        _runner.ListError = true;
        await Check();
        game.IsInstalled.Should().BeTrue("a query failure cannot prove removal");
        _runner.ListError = false;
        _runner.Commit = new string('b', 64);
        await Check();
        game.CanLaunch.Should().BeTrue();
        game.InstalledVersion.Should().Be("Unknown");
        await Service().UninstallAsync(GamePath);
        await Check();
        game.Status.Should().Be(GameStatus.NotInstalled);
        Directory.Exists(GamePath).Should().BeTrue();
        File.Exists(Path.Combine(GamePath, "version.txt")).Should().BeFalse();
    }

    private static GitHubRelease Release(string tag, string asset) => new()
    {
        tag_name = tag,
        assets = [new() { name = asset, browser_download_url = "https://example.test/" + asset }]
    };

    private sealed class BundleDownload : HttpMessageHandler
    {
        public int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) });
        }
    }

    private sealed class RecordingDialogs : IGameDownloadDialogs
    {
        public string? Error;
        public Task<bool> ConfirmDownloadWithoutRunnerAsync() => Task.FromResult(false);
        public Task<LinuxWindowsRunnerConfig?> ConfigureWindowsRunnerAsync(string path, LinuxWindowsRunnerConfig? existing = null, bool isInstall = true) => Task.FromResult(existing);
        public Task ShowRateLimitExceededAsync() => Task.CompletedTask;
        public Task ShowGitLabRateLimitExceededAsync() => Task.CompletedTask;
        public Task ShowErrorAsync(string message, string title) { Error = message; return Task.CompletedTask; }
    }

    private sealed class FakeBundles : IFlatpakBundleReader
    {
        public FlatpakReceipt Receipt = new("io.github.example.Game", "x86_64", "stable", new string('a', 64), "v1");
        public Exception? Error;
        public void CheckAvailable() { if (Error != null) throw Error; }
        public FlatpakReceipt Read(string path, string releaseTag) => Receipt with { ReleaseTag = releaseTag };
    }

    private sealed class FakeRunner : IFlatpakProcessRunner
    {
        public List<string[]> Commands = [];
        public string? Commit;
        public string NextCommit = new('a', 64);
        public string? InstallError;
        public bool UninstallError, ListError;
        public Task<FlatpakProcessResult> RunAsync(IReadOnlyList<string> args, CancellationToken token = default)
        {
            Commands.Add(args.ToArray());
            var result = new FlatpakProcessResult(0, "", "");
            switch (args[0])
            {
                case "list": result = ListError ? new(1, "", "database unreadable") : new(0,
                    Commit == null ? "" : "io.github.example.Game/x86_64/stable\n", ""); break;
                case "info": result = new(0, Commit ?? "", ""); break;
                case "install":
                    if (InstallError != null) result = new(1, "", InstallError);
                    else Commit = NextCommit;
                    break;
                case "uninstall":
                    if (UninstallError) result = new(1, "", "uninstall failed");
                    else Commit = null;
                    break;
            }
            return Task.FromResult(result);
        }
    }
}
