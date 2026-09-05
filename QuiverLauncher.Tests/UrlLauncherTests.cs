using System.Diagnostics;
using FluentAssertions;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class UrlLauncherTests
{
    private const string SpacedPath = "/home/deck/.local/share/QuiverLauncher/Apps/My Game";

    [Fact]
    public void CreateLinuxStartInfo_puts_full_path_in_argument_list()
    {
        var startInfo = UrlLauncher.CreateLinuxStartInfo("xdg-open", [SpacedPath]);

        startInfo.FileName.Should().Be("xdg-open");
        startInfo.UseShellExecute.Should().BeFalse();
        startInfo.ArgumentList.Should().Equal(SpacedPath);
        startInfo.Arguments.Should().BeEmpty();
    }

    [Fact]
    public void SanitizeHostEnvironment_removes_appimage_and_steam_vars()
    {
        var startInfo = new ProcessStartInfo { FileName = "xdg-open", UseShellExecute = false };
        startInfo.Environment["LD_LIBRARY_PATH"] = "/tmp/.mount_foo/usr/lib";
        startInfo.Environment["LD_PRELOAD"] = "gameoverlayrenderer.so";
        startInfo.Environment["QT_PLUGIN_PATH"] = "/tmp/.mount_foo/plugins";
        startInfo.Environment["QTDIR"] = "/tmp/.mount_foo";
        startInfo.Environment["QT_QPA_PLATFORM_PLUGIN_PATH"] = "/tmp/.mount_foo/plugins/platforms";
        startInfo.Environment["PATH"] = "/usr/bin";

        UrlLauncher.SanitizeHostEnvironment(startInfo);

        foreach (var name in UrlLauncher.HostBreakingEnvironmentVariables)
            startInfo.Environment.ContainsKey(name).Should().BeFalse($"'{name}' should be stripped");

        startInfo.Environment["PATH"].Should().Be("/usr/bin");
    }

    [Fact]
    public void CreateLinuxStartInfo_strips_host_breaking_environment()
    {
        var startInfo = UrlLauncher.CreateLinuxStartInfo("xdg-open", ["/tmp/game"]);

        foreach (var name in UrlLauncher.HostBreakingEnvironmentVariables)
            startInfo.Environment.ContainsKey(name).Should().BeFalse();
    }

    [Fact]
    public void OpenOnLinux_uses_argument_list_with_full_path()
    {
        ProcessStartInfo? seen = null;

        UrlLauncher.OpenOnLinux(
            SpacedPath,
            startInfo =>
            {
                seen = startInfo;
                return 0;
            },
            _ => false);

        seen.Should().NotBeNull();
        seen!.FileName.Should().Be("xdg-open");
        seen.UseShellExecute.Should().BeFalse();
        seen.ArgumentList.Should().Equal(SpacedPath);
        foreach (var name in UrlLauncher.HostBreakingEnvironmentVariables)
            seen.Environment.ContainsKey(name).Should().BeFalse();
    }

    [Fact]
    public void OpenOnLinux_prefers_usr_bin_xdg_open_when_present()
    {
        ProcessStartInfo? seen = null;

        UrlLauncher.OpenOnLinux(
            "/tmp/game",
            startInfo =>
            {
                seen = startInfo;
                return 0;
            },
            path => path == "/usr/bin/xdg-open");

        seen.Should().NotBeNull();
        seen!.FileName.Should().Be("/usr/bin/xdg-open");
        seen.ArgumentList.Should().Equal("/tmp/game");
    }

    [Fact]
    public void OpenOnLinux_falls_back_to_gio_when_xdg_open_fails()
    {
        var files = new List<string>();

        UrlLauncher.OpenOnLinux(
            SpacedPath,
            startInfo =>
            {
                files.Add(startInfo.FileName);
                if (startInfo.FileName == "xdg-open")
                    return 1;

                startInfo.ArgumentList.Should().Equal("open", SpacedPath);
                return 0;
            },
            _ => false);

        files.Should().Equal("xdg-open", "gio");
    }

    [Fact]
    public void OpenOnLinux_falls_back_to_kde_open_then_dolphin()
    {
        var files = new List<string>();

        UrlLauncher.OpenOnLinux(
            "/tmp/game",
            startInfo =>
            {
                files.Add(startInfo.FileName);
                return startInfo.FileName == "dolphin" ? 0 : 1;
            },
            _ => false);

        files.Should().Equal("xdg-open", "gio", "kde-open", "dolphin");
    }

    [Fact]
    public void OpenOnLinux_falls_back_when_xdg_open_throws()
    {
        var files = new List<string>();

        UrlLauncher.OpenOnLinux(
            "/tmp/game",
            startInfo =>
            {
                if (startInfo.FileName == "xdg-open")
                    throw new InvalidOperationException("missing xdg-open");

                files.Add(startInfo.FileName);
                return 0;
            },
            _ => false);

        files.Should().Equal("gio");
    }

    [Fact]
    public void OpenOnLinux_treats_still_running_as_success()
    {
        var files = new List<string>();

        UrlLauncher.OpenOnLinux(
            "/tmp/game",
            startInfo =>
            {
                files.Add(startInfo.FileName);
                return null;
            },
            _ => false);

        files.Should().Equal("xdg-open");
    }

    [Fact]
    public void OpenOnLinux_throws_when_all_fallbacks_fail()
    {
        var act = () => UrlLauncher.OpenOnLinux(
            "/tmp/game",
            _ => 1,
            _ => false);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*xdg-open*gio*kde-open*dolphin*");
    }

    [Theory]
    [InlineData("https://example.com", true)]
    [InlineData("http://example.com", true)]
    [InlineData("mailto:user@example.com", true)]
    [InlineData(SpacedPath, false)]
    [InlineData("/home/deck/Apps/Game", false)]
    public void IsWebOrMailUri_classifies_targets(string urlOrPath, bool expected)
    {
        UrlLauncher.IsWebOrMailUri(urlOrPath, out _).Should().Be(expected);
    }
}
