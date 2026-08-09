using System.Runtime.InteropServices;
using FluentAssertions;
using QuiverLauncher.Models;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class WindowsRunnerPerAppTests
{
    [Theory]
    [InlineData(null, LinuxWindowsRunnerKind.Auto)]
    [InlineData("", LinuxWindowsRunnerKind.Auto)]
    [InlineData("auto", LinuxWindowsRunnerKind.Auto)]
    [InlineData("wine", LinuxWindowsRunnerKind.Wine)]
    [InlineData("Proton", LinuxWindowsRunnerKind.Proton)]
    [InlineData("custom", LinuxWindowsRunnerKind.Custom)]
    public void ParseRunnerKind_maps_known_values(string? value, LinuxWindowsRunnerKind expected)
    {
        WindowsRunnerService.ParseRunnerKind(value).Should().Be(expected);
    }

    [Fact]
    public void Default_prefix_paths_are_under_game_folder()
    {
        WindowsRunnerService.GetDefaultWinePrefixPath("/games/App")
            .Should().Be(Path.Combine("/games/App", ".wine-prefix"));
        WindowsRunnerService.GetDefaultProtonCompatDataPath("/games/App")
            .Should().Be(Path.Combine("/games/App", ".steam-compat-data"));
    }

    [Fact]
    public void GetWindowsRunnerCommand_uses_per_app_custom_command()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return;

        var settings = new AppSettings { LinuxWindowsLaunchCommand = "global-runner {exe}" };
        var game = new GameInfo
        {
            LinuxRunner = "custom",
            LinuxCustomLaunchCommand = "per-app-runner {exe}",
        };

        var command = WindowsRunnerService.GetWindowsRunnerCommand(
            settings,
            "/games/app/game.exe",
            "/games/app",
            game);

        command.Should().NotBeNull();
        command!.FileName.Should().Be("per-app-runner");
        command.Arguments.Should().ContainSingle("/games/app/game.exe");
    }

    [Fact]
    public void LinuxWindowsRunnerConfig_ApplyTo_round_trips_on_game()
    {
        var game = new GameInfo();
        var config = new LinuxWindowsRunnerConfig
        {
            Kind = LinuxWindowsRunnerKind.Proton,
            PrefixPath = "/games/app/.steam-compat-data",
            ProtonPath = "/steam/Proton/proton",
            CustomLaunchCommand = "should-be-cleared",
        };

        config.ApplyTo(game);

        game.LinuxRunner.Should().Be("proton");
        game.LinuxPrefixPath.Should().Be("/games/app/.steam-compat-data");
        game.LinuxProtonPath.Should().Be("/steam/Proton/proton");
        game.LinuxCustomLaunchCommand.Should().BeNull();

        var restored = LinuxWindowsRunnerConfig.FromGame(game);
        restored.Kind.Should().Be(LinuxWindowsRunnerKind.Proton);
        restored.PrefixPath.Should().Be(config.PrefixPath);
        restored.ProtonPath.Should().Be(config.ProtonPath);
    }

    [Fact]
    public void GetWindowsRunnerCommand_wine_sets_wineprefix_environment()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return;

        if (!WindowsRunnerService.IsWineAvailable())
            return;

        var gamePath = Path.Combine(Path.GetTempPath(), "QuiverWinePrefixTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(gamePath);
        try
        {
            var prefix = Path.Combine(gamePath, "my-prefix");
            var game = new GameInfo
            {
                LinuxRunner = "wine",
                LinuxPrefixPath = prefix,
            };

            var command = WindowsRunnerService.GetWindowsRunnerCommand(
                new AppSettings(),
                Path.Combine(gamePath, "game.exe"),
                gamePath,
                game);

            command.Should().NotBeNull();
            command!.FileName.Should().BeOneOf("wine", "wine64");
            command.EnvironmentVariables.Should().ContainKey("WINEPREFIX");
            command.EnvironmentVariables["WINEPREFIX"].Should().Be(prefix);
            Directory.Exists(prefix).Should().BeTrue();
        }
        finally
        {
            try { Directory.Delete(gamePath, true); } catch { /* ignore */ }
        }
    }
}
