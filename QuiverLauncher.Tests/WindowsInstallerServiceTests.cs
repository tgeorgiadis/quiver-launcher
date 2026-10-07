using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using QuiverLauncher.Core.Models;
using QuiverLauncher.Core.Services;
using QuiverLauncher.Models;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public sealed class WindowsInstallerServiceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "QuiverMsiTests", Guid.NewGuid().ToString("N"));
    private string Metadata => Path.Combine(root, "metadata");
    private string Executable => Path.Combine(root, "external program", "app.exe");
    private GameInfo Game() => new() { Name = "MSI app", FolderName = "metadata", Repository = "owner/msi-test" };

    public WindowsInstallerServiceTests()
    {
        Directory.CreateDirectory(Metadata);
        Directory.CreateDirectory(Path.GetDirectoryName(Executable)!);
        File.WriteAllText(Executable, "test executable; never executed");
        GitHubApiCache.Initialize(Path.Combine(root, "cache"));
    }

    [Theory]
    [InlineData("app.msi")]
    [InlineData("APP.MSI")]
    public void Recognizes_msi_without_treating_it_as_a_launchable_binary(string name)
    {
        GameInstallationService.HasRecognizedInstallExtension(name).Should().BeTrue();
        GameInstallationService.IsWindowsInstallerAsset(name).Should().BeTrue();
        GameInstallationService.IsSingleFileExecutableAsset(name).Should().BeFalse();
        GameInstallationService.ResolveEffectiveAssetName("download", name).Should().Be(name);
    }

    [Fact]
    public void Installer_arguments_preserve_paths_and_request_interactive_setup_without_restart()
    {
        var package = Path.Combine(root, "package & spaces.msi");
        var log = Path.Combine(root, "log with spaces.txt");
        var info = WindowsInstallerService.InstallerStartInfo(package, log);
        info.ArgumentList.Should().Equal("/i", package, "/qf", "/norestart", "/L*V", log);
        info.UseShellExecute.Should().BeTrue();
        Path.GetFileName(info.FileName).Should().Be("msiexec.exe");
        info.Verb.Should().BeEmpty("Windows Installer handles elevation");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3010)]
    public async Task Successful_install_links_external_executable_and_preserves_external_files(int code)
    {
        if (!OperatingSystem.IsWindows()) return;
        var runner = new Runner(code);
        var dialogs = new Dialogs { Selected = Executable };
        var game = Game();
        (await new WindowsInstallerService(runner).InstallAsync(game, Path.Combine(root, "setup.msi"), "v2", Metadata, dialogs)).Should().BeTrue();
        var receipt = WindowsInstallerService.ReadReceipt(Metadata)!;
        receipt.Should().Be(new WindowsInstallerReceipt("Linked", "v2", Executable));
        game.Status.Should().Be(GameStatus.Installed);
        game.InstallPath.Should().BeNull();
        game.HasExecutableChoice.Should().BeTrue();
        game.InstalledAppRemovalLabel.Should().Be("Uninstall in Windows…");
        dialogs.Notices.Count.Should().Be(code == 3010 ? 1 : 0);
        Directory.GetFiles(Path.GetDirectoryName(Executable)!).Should().Equal(Executable);
        File.ReadAllText(Executable).Should().Be("test executable; never executed");
        File.Exists(Path.Combine(Metadata, "version.txt")).Should().BeFalse();
    }

    [Fact]
    public async Task Cancelled_picker_survives_restart_and_resumes_linking_without_running_installer()
    {
        if (!OperatingSystem.IsWindows()) return;
        var runner = new Runner(0);
        await new WindowsInstallerService(runner).InstallAsync(Game(), Path.Combine(root, "setup.msi"), "v1", Metadata, new Dialogs());
        var restarted = Game();
        restarted.LatestVersion = "v2";
        using var client = new HttpClient(new ResponseHandler(() => throw new Exception("No network expected")));
        await GameStatusService.CheckStatusAsync(restarted, client, root, checkRemoteVersion: false, applyCachedRelease: false);
        restarted.Status.Should().Be(GameStatus.NeedsExecutable);
        restarted.ButtonText.Should().Be("Select executable");
        restarted.RefreshInstalledStatus();
        restarted.Status.Should().Be(GameStatus.NeedsExecutable);
        await restarted.PerformActionAsync(client, root, new AppSettings(), new Dialogs { Selected = Executable });
        restarted.Status.Should().Be(GameStatus.UpdateAvailable);
        runner.Calls.Should().Be(1);
        restarted.LoadSelectedExecutable(root).Should().Be(Executable);
    }

    [Fact]
    public async Task Cancelled_change_preserves_link_and_missing_executable_requires_selection()
    {
        WindowsInstallerService.WriteReceipt(Metadata, new("Linked", "v1", Executable));
        var game = Game();
        await WindowsInstallerService.SelectExecutableAsync(game, Metadata, new Dialogs());
        game.SelectedExecutable.Should().Be(Executable);
        File.Delete(Executable);
        WindowsInstallerService.ApplyState(game, Metadata);
        game.Status.Should().Be(GameStatus.NeedsExecutable);
        game.RefreshInstalledStatus();
        game.Status.Should().Be(GameStatus.NeedsExecutable);
        WindowsInstallerService.ReadReceipt(Metadata)!.ExecutablePath.Should().Be(Executable);
    }

    [Theory]
    [InlineData(1602)]
    [InlineData(1603)]
    [InlineData(1618)]
    public async Task Unsuccessful_update_keeps_previous_receipt(int code)
    {
        if (!OperatingSystem.IsWindows()) return;
        var previous = new WindowsInstallerReceipt("Linked", "v1", Executable);
        WindowsInstallerService.WriteReceipt(Metadata, previous);
        var action = () => new WindowsInstallerService(new Runner(code)).InstallAsync(Game(), Path.Combine(root, "setup.msi"), "v2", Metadata, new Dialogs());
        if (code == 1602) (await action()).Should().BeFalse();
        else await action.Should().ThrowAsync<InvalidOperationException>().WithMessage($"*{code}*Installation log:*");
        WindowsInstallerService.ReadReceipt(Metadata).Should().Be(previous);
        var restarted = Game();
        WindowsInstallerService.ApplyState(restarted, Metadata);
        restarted.Status.Should().Be(GameStatus.Installed);
        restarted.InstalledVersion.Should().Be("v1");
    }

    [Fact]
    public async Task Update_reuses_existing_link_without_picker()
    {
        if (!OperatingSystem.IsWindows()) return;
        WindowsInstallerService.WriteReceipt(Metadata, new("Linked", "v1", Executable));
        var dialogs = new Dialogs();
        await new WindowsInstallerService(new Runner(0)).InstallAsync(Game(), Path.Combine(root, "setup.msi"), "v2", Metadata, dialogs);
        dialogs.Picks.Should().Be(0);
        WindowsInstallerService.ReadReceipt(Metadata)!.ReleaseTag.Should().Be("v2");
    }

    [Fact]
    public async Task Declined_installer_does_not_start_process_or_write_receipt()
    {
        if (!OperatingSystem.IsWindows()) return;
        var runner = new Runner(0);
        (await new WindowsInstallerService(runner).InstallAsync(Game(), Path.Combine(root, "setup.msi"), "v1", Metadata, new Dialogs { Confirm = false })).Should().BeFalse();
        runner.Calls.Should().Be(0);
        WindowsInstallerService.HasReceipt(Metadata).Should().BeFalse();
    }

    [Fact]
    public void Interrupted_first_install_is_recoverable_without_claiming_success()
    {
        WindowsInstallerService.WriteReceipt(Metadata, new("Installing", null, null));
        var game = Game();
        WindowsInstallerService.ApplyState(game, Metadata);
        game.Status.Should().Be(GameStatus.NeedsExecutable);
        game.InstalledVersion.Should().BeEmpty();
        game.CanRemoveInstalledApp.Should().BeTrue();
    }

    [Fact]
    public async Task Shortcut_uses_external_executable_without_writing_beside_it()
    {
        if (!OperatingSystem.IsWindows()) return;
        WindowsInstallerService.WriteReceipt(Metadata, new("Linked", "v1", Executable));
        var target = await GameShortcutLaunch.PrepareAsync(Game(), root, new AppSettings());
        target!.FileName.Should().Be(Executable);
        target.WorkingDirectory.Should().Be(Path.GetDirectoryName(Executable));
        Directory.GetFiles(Path.GetDirectoryName(Executable)!).Should().Equal(Executable);
        File.Delete(Executable);
        var missing = () => GameShortcutLaunch.PrepareAsync(Game(), root, new AppSettings());
        await missing.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public void Installer_apps_skip_file_injection_and_reject_folder_retargeting()
    {
        WindowsInstallerService.WriteReceipt(Metadata, new("Linked", "v1", Executable));
        var game = Game();
        WindowsInstallerService.ApplyState(game, Metadata);
        AppFilesToAddService.Sync(Metadata, null, ["injected.txt"]);
        File.Exists(Path.Combine(Metadata, "injected.txt")).Should().BeFalse();
        var retarget = () => GameInstallLocationService.ApplyLocatedPath(game, Path.GetDirectoryName(Executable)!);
        retarget.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task Download_header_routes_msi_without_extraction_or_file_injection()
    {
        if (!OperatingSystem.IsWindows()) return;
        var release = new GitHubRelease { tag_name = "v1", assets = [new GitHubAsset { name = "windows-download", browser_download_url = "https://example.com/package" }] };
        using var client = new HttpClient(new ResponseHandler(() =>
        {
            var content = new ByteArrayContent([1, 2, 3]);
            content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileName = "setup.msi" };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        }));
        var game = Game();
        var dialogs = new Dialogs { Selected = Executable };
        var runner = new Runner(0);
        await GameDownloadInstallService.DownloadAndInstallAsync(game, client, root, release, new AppSettings(), GameStatus.NotInstalled, dialogs,
            windowsInstallerService: new(runner), releaseMode: ReleaseInstallMode.ExplicitRelease);
        dialogs.Notices.Should().BeEmpty();
        runner.Calls.Should().Be(1);
        game.Status.Should().Be(GameStatus.Installed);
        File.Exists(Path.Combine(Metadata, "setup.msi")).Should().BeFalse();
        File.Exists(Path.Combine(Metadata, "version.txt")).Should().BeFalse();
        File.Exists(runner.Info!.ArgumentList[1]).Should().BeFalse("staging is cleaned after the installer exits");
    }

    [Fact]
    public async Task Headless_flow_rejects_installer_before_process_start()
    {
        if (!OperatingSystem.IsWindows()) return;
        var runner = new Runner(0);
        var action = () => new WindowsInstallerService(runner).InstallAsync(Game(), Path.Combine(root, "setup.msi"), "v1", Metadata, HeadlessGameDownloadDialogs.Instance);
        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("*desktop interface*");
        runner.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Format_switch_does_not_modify_existing_installation(bool fromMsi)
    {
        if (!OperatingSystem.IsWindows()) return;
        if (fromMsi) WindowsInstallerService.WriteReceipt(Metadata, new("Linked", "v1", Executable));
        else File.WriteAllText(Path.Combine(Metadata, "portable.exe"), "portable executable");
        var before = Directory.GetFiles(Metadata).ToDictionary(path => path, File.ReadAllText);
        var name = fromMsi ? "windows.zip" : "windows.msi";
        var release = new GitHubRelease { tag_name = "v2", assets = [new GitHubAsset { name = name, browser_download_url = "https://example.com/file" }] };
        using var client = new HttpClient(new ResponseHandler(() => new(HttpStatusCode.OK) { Content = new ByteArrayContent([1]) }));
        var dialogs = new Dialogs();
        var runner = new Runner(0);
        await GameDownloadInstallService.DownloadAndInstallAsync(Game(), client, root, release, new AppSettings(), GameStatus.NotInstalled,
            dialogs, windowsInstallerService: new(runner), releaseMode: ReleaseInstallMode.ExplicitRelease);
        runner.Calls.Should().Be(0);
        dialogs.Notices.Should().ContainSingle(message => message.Contains("Remove this library entry"));
        Directory.GetFiles(Metadata).ToDictionary(path => path, File.ReadAllText).Should().BeEquivalentTo(before);
    }

    [Theory]
    [InlineData(1602)]
    [InlineData(1603)]
    public async Task Download_flow_restores_launchable_previous_version_on_update_failure(int code)
    {
        if (!OperatingSystem.IsWindows()) return;
        WindowsInstallerService.WriteReceipt(Metadata, new("Linked", "v1", Executable));
        var release = new GitHubRelease { tag_name = "v2", assets = [new GitHubAsset { name = "windows.msi", browser_download_url = "https://example.com/file" }] };
        using var client = new HttpClient(new ResponseHandler(() => new(HttpStatusCode.OK) { Content = new ByteArrayContent([1]) }));
        var game = Game();
        WindowsInstallerService.ApplyState(game, Metadata);
        await GameDownloadInstallService.DownloadAndInstallAsync(game, client, root, release, new AppSettings(), GameStatus.UpdateAvailable,
            new Dialogs(), windowsInstallerService: new(new Runner(code)), releaseMode: ReleaseInstallMode.ExplicitRelease);
        game.IsInstalled.Should().BeTrue();
        game.InstalledVersion.Should().Be("v1");
        game.SelectedExecutable.Should().Be(Executable);
        game.DownloadProgress.Should().Be(0);
        game.IsLoading.Should().BeFalse();
    }

    [Fact]
    public async Task Process_start_failure_restores_previous_receipt()
    {
        if (!OperatingSystem.IsWindows()) return;
        var previous = new WindowsInstallerReceipt("Linked", "v1", Executable);
        WindowsInstallerService.WriteReceipt(Metadata, previous);
        var action = () => new WindowsInstallerService(new ThrowingRunner()).InstallAsync(Game(), Path.Combine(root, "setup.msi"), "v2", Metadata, new Dialogs());
        await action.Should().ThrowAsync<IOException>();
        WindowsInstallerService.ReadReceipt(Metadata).Should().Be(previous);
    }

    [Fact]
    public void Msi_updates_require_interaction_even_when_auto_update_was_previously_enabled()
    {
        var game = Game();
        game.IsWindowsInstaller = true;
        game.AutoUpdate = true;
        game.Status = GameStatus.UpdateAvailable;
        AppUpdateSelection.GetAutoPendingUpdates([game]).Should().BeEmpty();
        AppUpdateSelection.GetManualPendingUpdates([game]).Should().ContainSingle();
        game.CanToggleAutoUpdate.Should().BeFalse();
    }

    [Fact]
    public async Task Non_windows_install_does_not_start_installer()
    {
        if (OperatingSystem.IsWindows()) return;
        var runner = new Runner(0);
        var action = () => new WindowsInstallerService(runner).InstallAsync(Game(), Path.Combine(root, "setup.msi"), "v1", Metadata, new Dialogs());
        await action.Should().ThrowAsync<PlatformNotSupportedException>();
        runner.Calls.Should().Be(0);
    }

    public void Dispose() => Directory.Delete(root, recursive: true);

    private sealed class Runner(int code) : IWindowsInstallerProcessRunner
    {
        public int Calls { get; private set; }
        public ProcessStartInfo? Info { get; private set; }
        public Task<int> RunAsync(ProcessStartInfo info) { Calls++; Info = info; return Task.FromResult(code); }
    }

    private sealed class ThrowingRunner : IWindowsInstallerProcessRunner
    {
        public Task<int> RunAsync(ProcessStartInfo info) => throw new IOException("Cannot start Windows Installer");
    }

    private sealed class Dialogs : IGameDownloadDialogs
    {
        public bool Confirm { get; init; } = true;
        public string? Selected { get; init; }
        public int Picks { get; private set; }
        public List<string> Notices { get; } = [];
        public Task<bool> ConfirmWindowsInstallerAsync(string name) => Task.FromResult(Confirm);
        public Task<string?> PickWindowsExecutableAsync(string name, string? previous) { Picks++; return Task.FromResult(Selected); }
        public Task<bool> ConfirmDownloadWithoutRunnerAsync() => Task.FromResult(false);
        public Task<LinuxWindowsRunnerConfig?> ConfigureWindowsRunnerAsync(string path, LinuxWindowsRunnerConfig? existing = null, bool isInstall = true) => Task.FromResult<LinuxWindowsRunnerConfig?>(null);
        public Task ShowRateLimitExceededAsync() => Task.CompletedTask;
        public Task ShowGitLabRateLimitExceededAsync() => Task.CompletedTask;
        public Task ShowErrorAsync(string message, string title) { Notices.Add(message); return Task.CompletedTask; }
        public Task<bool> ConfirmUnverifiedReleaseAsync(string appName, string version, ReleaseCheck check) => Task.FromResult(true);
    }

    private sealed class ResponseHandler(Func<HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(response());
    }
}
