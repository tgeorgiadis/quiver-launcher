using System.Diagnostics;
using FluentAssertions;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class HostProcessEnvironmentTests
{
    [Fact]
    public void Sanitize_removes_appimage_and_steam_vars_and_keeps_path()
    {
        var startInfo = CreatePollutedStartInfo();

        HostProcessEnvironment.Sanitize(startInfo);

        foreach (var name in HostProcessEnvironment.HostBreakingEnvironmentVariables)
            startInfo.Environment.ContainsKey(name).Should().BeFalse($"'{name}' should be stripped");

        startInfo.Environment["PATH"].Should().Be("/usr/bin");
    }

    [Fact]
    public void Sanitize_removes_appimage_identity_vars_and_rewrites_pwd()
    {
        var startInfo = CreatePollutedStartInfo();
        startInfo.WorkingDirectory = "/games/ygofm";
        startInfo.Environment["APPDIR"] = "/tmp/.mount_QuivergkBcJi";
        startInfo.Environment["APPIMAGE"] = "/home/tom/QuiverLauncher.AppImage";
        startInfo.Environment["APPIMAGE_EXTRACT_AND_RUN"] = "1";
        startInfo.Environment["ARGV0"] = "/home/tom/QuiverLauncher.AppImage";
        startInfo.Environment["OWD"] = "/home/tom/Downloads";
        startInfo.Environment["PWD"] = "/home/tom/Downloads";
        startInfo.Environment["PATH"] = "/tmp/.mount_QuivergkBcJi/usr/bin/:/usr/local/bin:/usr/bin";

        HostProcessEnvironment.Sanitize(startInfo);

        startInfo.Environment.ContainsKey("APPDIR").Should().BeFalse();
        startInfo.Environment.ContainsKey("APPIMAGE").Should().BeFalse();
        startInfo.Environment.ContainsKey("APPIMAGE_EXTRACT_AND_RUN").Should().BeFalse();
        startInfo.Environment.ContainsKey("ARGV0").Should().BeFalse();
        startInfo.Environment.ContainsKey("OWD").Should().BeFalse();
        startInfo.Environment["PWD"].Should().Be("/games/ygofm");
        startInfo.Environment["PATH"].Should().Be("/usr/local/bin:/usr/bin");
    }

    [Fact]
    public void SanitizeThenApply_keeps_proton_and_wine_runner_vars()
    {
        var startInfo = CreatePollutedStartInfo();

        HostProcessEnvironment.SanitizeThenApply(startInfo, new Dictionary<string, string>
        {
            ["STEAM_COMPAT_CLIENT_INSTALL_PATH"] = "/home/deck/.steam/root",
            ["STEAM_COMPAT_DATA_PATH"] = "/games/App/.steam-compat-data",
            ["STEAM_COMPAT_APP_ID"] = "12345",
            ["WINEPREFIX"] = "/games/App/.wine-prefix",
        });

        foreach (var name in HostProcessEnvironment.HostBreakingEnvironmentVariables)
            startInfo.Environment.ContainsKey(name).Should().BeFalse($"'{name}' should be stripped");

        startInfo.Environment["PATH"].Should().Be("/usr/bin");
        startInfo.Environment["STEAM_COMPAT_CLIENT_INSTALL_PATH"].Should().Be("/home/deck/.steam/root");
        startInfo.Environment["STEAM_COMPAT_DATA_PATH"].Should().Be("/games/App/.steam-compat-data");
        startInfo.Environment["STEAM_COMPAT_APP_ID"].Should().Be("12345");
        startInfo.Environment["WINEPREFIX"].Should().Be("/games/App/.wine-prefix");
    }

    [Fact]
    public void SanitizeThenApply_does_not_reintroduce_stripped_host_vars()
    {
        var startInfo = CreatePollutedStartInfo();

        HostProcessEnvironment.SanitizeThenApply(startInfo, new Dictionary<string, string>
        {
            ["WINEPREFIX"] = "/games/App/.wine-prefix",
        });

        startInfo.Environment.ContainsKey("LD_LIBRARY_PATH").Should().BeFalse();
        startInfo.Environment["WINEPREFIX"].Should().Be("/games/App/.wine-prefix");
    }

    static ProcessStartInfo CreatePollutedStartInfo()
    {
        var startInfo = new ProcessStartInfo { FileName = "game", UseShellExecute = false };
        startInfo.Environment["LD_LIBRARY_PATH"] = "/tmp/.mount_foo/usr/lib";
        startInfo.Environment["LD_PRELOAD"] = "gameoverlayrenderer.so";
        startInfo.Environment["QT_PLUGIN_PATH"] = "/tmp/.mount_foo/plugins";
        startInfo.Environment["QTDIR"] = "/tmp/.mount_foo";
        startInfo.Environment["QT_QPA_PLATFORM_PLUGIN_PATH"] = "/tmp/.mount_foo/plugins/platforms";
        startInfo.Environment["PATH"] = "/usr/bin";
        return startInfo;
    }
}
