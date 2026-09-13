using FluentAssertions;
using QuiverLauncher.Models;
using QuiverLauncher.Core.Models;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public sealed class AndroidLibraryLifecycleTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "quiver-android-lifecycle", Guid.NewGuid().ToString("N"));
    private readonly GameInfo _game = new() { Name = "Fixture", FolderName = "game", Status = GameStatus.Installed, InstalledVersion = "v1.6", LatestVersion = "v1.6" };
    public AndroidLibraryLifecycleTests()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "version.txt"), "v1.6");
        File.WriteAllText(Path.Combine(_root, GameStatusService.AndroidPackageFileName), "test.fixture");
        File.WriteAllText(Path.Combine(_root, "save.dat"), "user data");
        AndroidInstalledRelease.Resolve(_root, new("test.fixture", 16, "0.10-spike", 100));
    }
    public void Dispose() => Directory.Delete(_root, true);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Uninstall_waits_for_android_and_clears_receipts_only_after_confirmed_removal(bool confirm)
    {
        var service = new FakeInstaller();
        var operation = AndroidLibraryUninstall.RunAsync(_game, _root, service);
        operation.IsCompleted.Should().BeFalse();
        _game.AndroidPackageName.Should().Be("test.fixture");
        service.TargetPath.Should().Be(_root);
        _game.IsLoading.Should().BeTrue();
        File.Exists(Path.Combine(_root, "android-release.json")).Should().BeTrue();
        service.Installed = !confirm;
        service.Result.SetResult(confirm);
        (await operation).Should().Be(confirm);
        _game.IsLoading.Should().BeFalse();
        _game.Status.Should().Be(confirm ? GameStatus.NotInstalled : GameStatus.Installed);
        _game.InstalledVersion.Should().Be(confirm ? "" : "v1.6");
        File.Exists(Path.Combine(_root, "version.txt")).Should().Be(!confirm);
        File.Exists(Path.Combine(_root, "android-release.json")).Should().Be(!confirm);
        File.ReadAllText(Path.Combine(_root, "save.dat")).Should().Be("user data");
        File.ReadAllText(Path.Combine(_root, GameStatusService.AndroidPackageFileName)).Should().Be("test.fixture");
    }

    [Fact]
    public async Task A_success_result_cannot_erase_receipts_while_the_app_is_still_installed()
    {
        var service = new FakeInstaller();
        service.Result.SetResult(true);
        var action = () => AndroidLibraryUninstall.RunAsync(_game, _root, service);
        await action.Should().ThrowAsync<InvalidOperationException>();
        _game.Status.Should().Be(GameStatus.Installed);
        _game.IsLoading.Should().BeFalse();
        File.Exists(Path.Combine(_root, "android-release.json")).Should().BeTrue();
    }

    [Fact]
    public async Task Failure_to_open_the_uninstaller_preserves_the_existing_installation()
    {
        var service = new FakeInstaller();
        service.Result.SetException(new InvalidOperationException("native prompt unavailable"));
        var action = () => AndroidLibraryUninstall.RunAsync(_game, _root, service);
        await action.Should().ThrowAsync<InvalidOperationException>();
        _game.Status.Should().Be(GameStatus.Installed);
        _game.IsLoading.Should().BeFalse();
        File.ReadAllText(Path.Combine(_root, "version.txt")).Should().Be("v1.6");
    }

    [Fact]
    public async Task Package_readiness_is_immediate_when_android_is_ready()
    {
        (await AndroidPackageReadiness.WaitAsync(() => true, () => throw new Exception("Unexpected delay"))).Should().BeTrue();
    }

    [Fact]
    public async Task Package_readiness_retries_transient_missing_state_without_waiting_indefinitely()
    {
        var queries = 0;
        var delays = 0;
        (await AndroidPackageReadiness.WaitAsync(() => ++queries == 3, () => { delays++; return Task.CompletedTask; })).Should().BeTrue();
        queries.Should().Be(3);
        delays.Should().Be(2);
        queries = 0;
        delays = 0;
        (await AndroidPackageReadiness.WaitAsync(() => { queries++; return false; }, () => { delays++; return Task.CompletedTask; })).Should().BeFalse();
        queries.Should().Be(9);
        delays.Should().Be(8);
    }

    private sealed class FakeInstaller : IAppInstallLaunchService
    {
        public bool Installed = true;
        public string? TargetPath;
        public TaskCompletionSource<bool> Result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<bool> UninstallAsync(GameInfo game, string gamePath) { TargetPath = gamePath; return Result.Task; }
        public bool IsInstalled(GameInfo game) => Installed;
        public string? GetInstalledVersion(GameInfo game, string gamePath) => Installed ? "v1.6" : null;
        public Task<bool> InstallAsync(GameInfo game, string path, string version, string gamePath) => throw new NotSupportedException();
        public Task<bool> LaunchAsync(GameInfo game, string folder) => throw new NotSupportedException();
    }
}
