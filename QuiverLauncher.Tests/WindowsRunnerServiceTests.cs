using System.Runtime.InteropServices;
using FluentAssertions;
using QuiverLauncher.Models;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class WindowsRunnerServiceTests
{
    [Fact]
    public void BuildWindowsRunnerCommand_appends_exe_placeholder_when_missing()
    {
        var command = WindowsRunnerService.BuildWindowsRunnerCommand(
            "wine",
            "/games/app/game.exe",
            "/games/app");

        command.FileName.Should().Be("wine");
        command.Arguments.Should().ContainSingle("/games/app/game.exe");
    }

    [Fact]
    public void BuildWindowsRunnerCommand_resolves_custom_placeholders()
    {
        var command = WindowsRunnerService.BuildWindowsRunnerCommand(
            "custom-runner --dir {exeDir} --root {gamePath} {exe}",
            "/games/app/game.exe",
            "/games/app");

        command.Arguments.Should().Contain("/games/app");
        command.Arguments.Should().Contain("/games/app/game.exe");
    }

    [Fact]
    public void SplitRunnerCommand_handles_quoted_arguments()
    {
        var tokens = WindowsRunnerService.SplitRunnerCommand("runner \"quoted path\" {exe}");

        tokens.Should().Equal("runner", "quoted path", "{exe}");
    }

    [Fact]
    public void IsWindowsRunnerAvailable_respects_platform_and_custom_command()
    {
        var settings = new AppSettings
        {
            LinuxWindowsLaunchCommand = "wine {exe}",
        };

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            WindowsRunnerService.IsWindowsRunnerAvailable(settings).Should().BeTrue();
        else
            WindowsRunnerService.IsWindowsRunnerAvailable(settings).Should().BeFalse();
    }
}
