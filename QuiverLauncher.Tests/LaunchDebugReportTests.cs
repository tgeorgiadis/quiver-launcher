using System.Diagnostics;
using System.Text;
using FluentAssertions;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class LaunchDebugReportTests
{
    [Theory]
    [InlineData("APPDIR", true)]
    [InlineData("APPIMAGE", true)]
    [InlineData("APPIMAGE_EXTRACT_AND_RUN", true)]
    [InlineData("LD_LIBRARY_PATH", true)]
    [InlineData("LD_PRELOAD", true)]
    [InlineData("GST_PLUGIN_PATH", true)]
    [InlineData("SteamDeck", true)]
    [InlineData("STEAM_COMPAT_DATA_PATH", true)]
    [InlineData("GITHUB_TOKEN", false)]
    [InlineData("HOME", false)]
    [InlineData("SECRET", false)]
    [InlineData("Authorization", false)]
    public void IsAllowlisted_matches_plan_keys(string key, bool expected)
    {
        LaunchDebugReport.IsAllowlisted(key).Should().Be(expected);
    }

    [Fact]
    public void FilterAllowlisted_keeps_appdir_and_drops_secrets()
    {
        var filtered = LaunchDebugReport.FilterAllowlisted(new Dictionary<string, string>
        {
            ["APPDIR"] = "/tmp/.mount_QuiverLauncher",
            ["LD_LIBRARY_PATH"] = "/tmp/.mount_foo/usr/lib",
            ["GITHUB_TOKEN"] = "secret-token",
            ["HOME"] = "/home/tom",
        });

        filtered.Should().ContainKey("APPDIR");
        filtered.Should().ContainKey("LD_LIBRARY_PATH");
        filtered.Should().NotContainKey("GITHUB_TOKEN");
        filtered.Should().NotContainKey("HOME");
    }

    [Fact]
    public void Build_includes_allowlisted_vars_and_omits_secrets()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "/games/ygofm/Yu_Gi_Oh_Forbidden_Memories_Recompiled",
            WorkingDirectory = "/games/ygofm",
            UseShellExecute = false,
        };

        var report = LaunchDebugReport.Build(
            new DateTimeOffset(2026, 9, 6, 0, 0, 0, TimeSpan.Zero),
            "Yu-Gi-Oh",
            "/games/ygofm",
            "/games/ygofm/Yu_Gi_Oh_Forbidden_Memories_Recompiled",
            ["/games/ygofm/Yu_Gi_Oh_Forbidden_Memories_Recompiled"],
            "/games/ygofm/Yu_Gi_Oh_Forbidden_Memories_Recompiled",
            startInfo,
            new Dictionary<string, string>
            {
                ["APPDIR"] = "/tmp/.mount_QuiverLauncher",
                ["LD_LIBRARY_PATH"] = "/tmp/.mount_foo/usr/lib",
            },
            new Dictionary<string, string>
            {
                ["PATH"] = "/usr/bin",
            },
            pid: 4242,
            liveProcEnviron: new Dictionary<string, string>
            {
                ["APPDIR"] = "/tmp/.mount_QuiverLauncher",
            });

        report.Should().Contain("APPDIR=/tmp/.mount_QuiverLauncher");
        report.Should().Contain("LD_LIBRARY_PATH=/tmp/.mount_foo/usr/lib");
        report.Should().Contain("removedBySanitize=");
        report.Should().Contain("  APPDIR");
        report.Should().Contain("  LD_LIBRARY_PATH");
        report.Should().Contain("pid=4242");
        report.Should().NotContain("secret-token");
        report.Should().NotContain("GITHUB_TOKEN");
    }

    [Fact]
    public void KeysRemoved_lists_sanitized_host_vars()
    {
        var startInfo = new ProcessStartInfo { FileName = "game", UseShellExecute = false };
        startInfo.Environment["LD_LIBRARY_PATH"] = "/tmp/.mount_foo/usr/lib";
        startInfo.Environment["APPDIR"] = "/tmp/.mount_QuiverLauncher";
        startInfo.Environment["PATH"] = "/usr/bin";

        var before = LaunchDebugReport.SnapshotStartInfoEnvironment(startInfo);
        HostProcessEnvironment.Sanitize(startInfo);
        var after = LaunchDebugReport.SnapshotStartInfoEnvironment(startInfo);

        var removed = LaunchDebugReport.KeysRemoved(before, after);
        removed.Should().Contain("LD_LIBRARY_PATH");
        after.ContainsKey("LD_LIBRARY_PATH").Should().BeFalse();
        after["PATH"].Should().Be("/usr/bin");

        var report = LaunchDebugReport.Build(
            DateTimeOffset.UtcNow,
            "Game",
            "/games/app",
            "/games/app/game",
            ["/games/app/game"],
            null,
            startInfo,
            before,
            after,
            pid: 1,
            liveProcEnviron: after);

        report.Should().Contain("removedBySanitize=");
        report.Should().Contain("  LD_LIBRARY_PATH");
        report.Should().Contain("ProcessStartInfo env (after sanitize)");
        report.Should().Contain("=== process env (before) ===");
        report.Should().Contain("LD_LIBRARY_PATH=/tmp/.mount_foo/usr/lib");
    }

    [Fact]
    public void ParseProcEnviron_splits_null_delimited_allowlisted_entries()
    {
        var bytes = Encoding.UTF8.GetBytes(
            "APPDIR=/tmp/.mount_QuiverLauncher\0LD_LIBRARY_PATH=/tmp/lib\0HOME=/home/tom\0GITHUB_TOKEN=secret\0PATH=/usr/bin\0");

        var parsed = LaunchDebugReport.ParseProcEnviron(bytes);

        parsed.Should().ContainKey("APPDIR").WhoseValue.Should().Be("/tmp/.mount_QuiverLauncher");
        parsed.Should().ContainKey("LD_LIBRARY_PATH").WhoseValue.Should().Be("/tmp/lib");
        parsed.Should().ContainKey("PATH").WhoseValue.Should().Be("/usr/bin");
        parsed.Should().NotContainKey("HOME");
        parsed.Should().NotContainKey("GITHUB_TOKEN");
    }

    [Fact]
    public void FormatEnvBlock_lists_none_when_empty()
    {
        LaunchDebugReport.FormatEnvBlock("empty", new Dictionary<string, string>())
            .Should().Contain("(none)");
    }

    [Fact]
    public void SnapshotStartInfoEnvironment_is_empty_when_use_shell_execute()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "game.exe",
            UseShellExecute = true,
        };

        LaunchDebugReport.SnapshotStartInfoEnvironment(startInfo)
            .Should().BeEmpty();
    }

    [Fact]
    public void SnapshotStartInfoEnvironment_does_not_block_shell_execute_start()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c exit 0",
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };

        LaunchDebugReport.SnapshotStartInfoEnvironment(startInfo).Should().BeEmpty();

        using var process = Process.Start(startInfo);
        process.Should().NotBeNull();
        process!.WaitForExit(10_000).Should().BeTrue();
        process.ExitCode.Should().Be(0);
    }
}
