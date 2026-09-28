using System.Diagnostics;
using FluentAssertions;
using QuiverLauncher.Core.Models;
using QuiverLauncher.Core.Services;
using QuiverLauncher.Models;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public sealed class MacWindowsRunnerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "quiver-mac-runner", Guid.NewGuid().ToString("N"));
    private string GamePath => Path.Combine(_root, "game");

    public MacWindowsRunnerTests() => Directory.CreateDirectory(GamePath);
    public void Dispose() => Directory.Delete(_root, true);

    [Fact]
    public void Mac_offers_the_windows_build_only_as_a_runner_fallback()
    {
        var windowsOnly = DownloadAssetPolicyTests.Release("game-windows.zip", "game-linux.AppImage");
        DownloadAssetPolicy.Select(windowsOnly, "macOS").Eligible.Should().BeEmpty();
        DownloadAssetPolicy.Select(windowsOnly, "macOS", windowsBuildFallback: true).Eligible
            .Select(a => a.name).Should().Equal("game-windows.zip");

        var both = DownloadAssetPolicyTests.Release("game-windows.zip", "game-macos.zip");
        DownloadAssetPolicy.Select(both, "macOS", windowsBuildFallback: true).Eligible
            .Select(a => a.name).Should().Equal(["game-macos.zip"], "a Mac build never needs a runner");
    }

    [Fact]
    public void Linux_keeps_offering_windows_builds_after_native_ones()
    {
        var both = DownloadAssetPolicyTests.Release("game-windows.zip", "game-linux.AppImage");
        DownloadAssetPolicy.Select(both, "Linux-X64").Eligible
            .Select(a => a.name).Should().Equal("game-linux.AppImage", "game-windows.zip");
    }

    [Fact]
    public async Task Windows_exe_launches_through_the_runner_on_macos()
    {
        if (!OperatingSystem.IsMacOS())
            Assert.Skip("Requires macOS.");

        var runner = Path.Combine(_root, "fake-wine");
        File.WriteAllText(runner, "#!/bin/sh\nprintf '%s\\n' \"$PWD\" \"$@\" > \"$(dirname \"$0\")/ran.txt\"\n");
        File.SetUnixFileMode(runner, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var exe = Path.Combine(GamePath, "Game.exe");
        File.WriteAllBytes(exe, [0x4d, 0x5a]);
        var game = new GameInfo
        {
            Name = "Windows Game", FolderName = "game", Status = GameStatus.Installed,
            LinuxRunner = "custom", LinuxCustomLaunchCommand = $"'{runner}' {{exe}}",
        };
        Process? launched = null;
        game.GameProcessStarted += process => launched = process;

        var result = await GameLaunchService.LaunchAsync(game, _root);

        result.Should().BeTrue();
        using (launched)
        {
            await launched!.WaitForExitAsync(TestContext.Current.CancellationToken);
            launched.ExitCode.Should().Be(0);
        }
        File.ReadAllLines(Path.Combine(_root, "ran.txt")).Should().Equal(GamePath, exe);

        var target = await GameShortcutLaunch.PrepareAsync(game, _root, new());
        target!.FileName.Should().Be(runner, "shortcuts start the game through the same runner");
        target.Arguments.Should().Equal(exe);
    }

    [Fact]
    public void CrossOver_bottles_are_listed_by_name()
    {
        var bottles = Path.Combine(_root, "Bottles");
        foreach (var name in new[] { "Steam", "Quiver Launcher", "games" })
        {
            Directory.CreateDirectory(Path.Combine(bottles, name));
            File.WriteAllText(Path.Combine(bottles, name, "cxbottle.conf"), "");
        }
        Directory.CreateDirectory(Path.Combine(bottles, "not-a-bottle"));

        WindowsRunnerService.ListCrossOverBottles(bottles).Should().Equal("games", "Quiver Launcher", "Steam");
        WindowsRunnerService.ListCrossOverBottles(Path.Combine(_root, "missing")).Should().BeEmpty();
    }

    [Fact]
    public void CrossOver_bottle_command_keeps_names_with_spaces_together()
    {
        const string wine = "/Applications/CrossOver.app/Contents/SharedSupport/CrossOver/bin/wine";
        var template = WindowsRunnerService.BuildCrossOverCommandTemplate(wine, "Quiver Launcher");

        var command = WindowsRunnerService.BuildWindowsRunnerCommand(template, "/Games/My Game/game.exe", "/Games/My Game");

        command.FileName.Should().Be(wine);
        command.Arguments.Should().Equal("--bottle", "Quiver Launcher", "/Games/My Game/game.exe");
    }

    [Fact]
    public async Task Only_quivers_own_bottle_is_created_and_only_once()
    {
        if (OperatingSystem.IsWindows())
            Assert.Skip("Uses a shell script as a stand-in for cxbottle.");

        var bin = Directory.CreateDirectory(Path.Combine(_root, "CrossOver.app/Contents/SharedSupport/CrossOver/bin")).FullName;
        var wine = Path.Combine(bin, "wine");
        var bottles = Path.Combine(_root, "Bottles");
        var log = Path.Combine(_root, "cxbottle.log");
        var cxbottle = Path.Combine(bin, "cxbottle");
        File.WriteAllText(cxbottle,
            $"#!/bin/sh\nprintf '%s|' \"$@\" >> '{log}'\necho >> '{log}'\nmkdir -p '{bottles}/'\"$2\" && touch '{bottles}/'\"$2\"/cxbottle.conf\n");
        File.SetUnixFileMode(cxbottle, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var ct = TestContext.Current.CancellationToken;

        await WindowsRunnerService.EnsureCrossOverBottleAsync(wine, ["--bottle", "Steam", "game.exe"], bottles, ct);
        await WindowsRunnerService.EnsureCrossOverBottleAsync("/usr/local/bin/wine", ["--bottle", "Quiver Launcher", "game.exe"], bottles, ct);
        File.Exists(log).Should().BeFalse("the user's bottles and other runners are left alone");

        await WindowsRunnerService.EnsureCrossOverBottleAsync(wine, ["--bottle", "Quiver Launcher", "game.exe"], bottles, ct);
        await WindowsRunnerService.EnsureCrossOverBottleAsync(wine, ["--bottle", "Quiver Launcher", "game.exe"], bottles, ct);

        File.ReadAllLines(log).Should().ContainSingle().Which.Should()
            .StartWith("--bottle|Quiver Launcher|--create|--template|win10_64|");
        WindowsRunnerService.ListCrossOverBottles(bottles).Should().Equal("Quiver Launcher");
    }

    [Fact]
    public async Task Failed_bottle_creation_is_reported()
    {
        if (OperatingSystem.IsWindows())
            Assert.Skip("Uses a shell script as a stand-in for cxbottle.");

        var bin = Directory.CreateDirectory(Path.Combine(_root, "CrossOver.app/Contents/SharedSupport/CrossOver/bin")).FullName;
        var cxbottle = Path.Combine(bin, "cxbottle");
        File.WriteAllText(cxbottle, "#!/bin/sh\necho 'trial expired' >&2\nexit 1\n");
        File.SetUnixFileMode(cxbottle, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var ensure = () => WindowsRunnerService.EnsureCrossOverBottleAsync(Path.Combine(bin, "wine"),
            ["--bottle", "Quiver Launcher", "game.exe"], Path.Combine(_root, "Bottles"), TestContext.Current.CancellationToken);

        await ensure.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Quiver Launcher*trial expired*");
    }

    [Fact]
    public void Auto_uses_crossover_in_quivers_bottle_when_wine_is_missing()
    {
        var crossOver = WindowsRunnerService.FindCrossOverWine();
        if (crossOver == null || WindowsRunnerService.IsWineAvailable())
            Assert.Skip("Needs macOS with CrossOver and without Wine.");

        var exe = Path.Combine(GamePath, "game.exe");
        var command = WindowsRunnerService.GetWindowsRunnerCommand(new AppSettings(), exe, GamePath, new GameInfo());

        command!.FileName.Should().Be(crossOver);
        command.Arguments.Should().Equal("--bottle", WindowsRunnerService.CrossOverBottleName, exe);
        WindowsRunnerService.IsWindowsRunnerAvailable(new AppSettings(), new GameInfo()).Should().BeTrue();
    }
}
