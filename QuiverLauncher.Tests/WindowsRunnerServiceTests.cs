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

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            WindowsRunnerService.IsWindowsRunnerAvailable(settings).Should().BeTrue();
        else
            WindowsRunnerService.IsWindowsRunnerAvailable(settings).Should().BeFalse();
    }

    [Fact]
    public void Wine_on_the_path_is_used_by_name()
    {
        WindowsRunnerService.ResolveWineBinary(isMacOS: true, name => name == "wine", _ => true, "/Users/test")
            .Should().Be("wine");
        WindowsRunnerService.ResolveWineBinary(isMacOS: false, name => name is "wine" or "wine64", _ => true, "/home/test")
            .Should().Be("wine64");
    }

    [Fact]
    public void macOS_finds_wine_outside_the_gui_path()
    {
        var existing = new HashSet<string>
        {
            "/Users/test/Applications/Wine Staging.app/Contents/Resources/wine/bin/wine",
            "/usr/local/opt/game-porting-toolkit/bin/wine64",
        };

        WindowsRunnerService.ResolveWineBinary(isMacOS: true, _ => false, existing.Contains, "/Users/test")
            .Should().Be("/Users/test/Applications/Wine Staging.app/Contents/Resources/wine/bin/wine");

        existing.Add("/opt/homebrew/bin/wine");
        WindowsRunnerService.ResolveWineBinary(isMacOS: true, _ => false, existing.Contains, "/Users/test")
            .Should().Be("/opt/homebrew/bin/wine", "Homebrew comes before app bundles");

        WindowsRunnerService.ResolveWineBinary(isMacOS: false, _ => false, existing.Contains, "/Users/test")
            .Should().BeNull("Linux only uses PATH");
    }

    [Fact]
    public void Wine_command_uses_the_resolved_binary_and_a_per_app_prefix()
    {
        var gamePath = Path.Combine(Path.GetTempPath(), "QuiverWineCommand_" + Guid.NewGuid().ToString("N"));
        try
        {
            var command = WindowsRunnerService.BuildWineCommand(
                "/opt/homebrew/bin/wine", Path.Combine(gamePath, "game.exe"), gamePath, prefixPath: null);

            command.FileName.Should().Be("/opt/homebrew/bin/wine");
            command.Arguments.Should().Equal(Path.Combine(gamePath, "game.exe"));
            command.EnvironmentVariables["WINEPREFIX"].Should().Be(WindowsRunnerService.GetDefaultWinePrefixPath(gamePath));
            Directory.Exists(WindowsRunnerService.GetDefaultWinePrefixPath(gamePath)).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(gamePath)) Directory.Delete(gamePath, true);
        }
    }
}
