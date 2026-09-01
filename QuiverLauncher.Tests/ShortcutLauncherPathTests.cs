using FluentAssertions;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class ShortcutLauncherPathTests
{
    private const string MountPath = "/tmp/.mount_quiverkPngNh/usr/bin/QuiverLauncher";
    private const string AppImagePath = "/home/deck/Downloads/QuiverLauncher-linux-x64.AppImage";
    private const string UnpackagedLinuxPath = "/opt/QuiverLauncher/QuiverLauncher";
    private const string WindowsPath = @"C:\Projects\GithubLauncher\QuiverLauncher.Desktop.exe";

    [Fact]
    public void ResolveLauncherPath_prefers_APPIMAGE_over_mount_process_path()
    {
        ShortcutHelper.ResolveLauncherPath(
                velopackAppImagePath: null,
                appImageEnv: AppImagePath,
                processPath: MountPath)
            .Should().Be(AppImagePath);
    }

    [Fact]
    public void ResolveLauncherPath_prefers_Velopack_AppImagePath_over_process_path()
    {
        ShortcutHelper.ResolveLauncherPath(
                velopackAppImagePath: AppImagePath,
                appImageEnv: null,
                processPath: MountPath)
            .Should().Be(AppImagePath);
    }

    [Fact]
    public void ResolveLauncherPath_prefers_Velopack_over_APPIMAGE()
    {
        ShortcutHelper.ResolveLauncherPath(
                velopackAppImagePath: AppImagePath,
                appImageEnv: "/tmp/other.AppImage",
                processPath: MountPath)
            .Should().Be(AppImagePath);
    }

    [Fact]
    public void ResolveLauncherPath_falls_back_to_process_path_when_not_a_mount()
    {
        ShortcutHelper.ResolveLauncherPath(
                velopackAppImagePath: null,
                appImageEnv: null,
                processPath: UnpackagedLinuxPath)
            .Should().Be(UnpackagedLinuxPath);

        ShortcutHelper.ResolveLauncherPath(
                velopackAppImagePath: null,
                appImageEnv: null,
                processPath: WindowsPath)
            .Should().Be(WindowsPath);
    }

    [Fact]
    public void ResolveLauncherPath_returns_null_when_only_mount_path_is_available()
    {
        ShortcutHelper.ResolveLauncherPath(
                velopackAppImagePath: null,
                appImageEnv: null,
                processPath: MountPath)
            .Should().BeNull();
    }

    [Fact]
    public void ResolveLauncherPath_skips_mount_Velopack_path()
    {
        ShortcutHelper.ResolveLauncherPath(
                velopackAppImagePath: MountPath,
                appImageEnv: AppImagePath,
                processPath: MountPath)
            .Should().Be(AppImagePath);
    }

    [Theory]
    [InlineData("/tmp/.mount_quiverkPngNh/usr/bin/QuiverLauncher", true)]
    [InlineData("/tmp/.mount_abc/usr/bin/QuiverLauncher", true)]
    [InlineData(@"\tmp\.mount_quiver\usr\bin\QuiverLauncher", true)]
    [InlineData("/home/deck/Downloads/QuiverLauncher-linux-x64.AppImage", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void IsAppImageMountPath_detects_squashfs_mount(string? path, bool expected)
    {
        ShortcutHelper.IsAppImageMountPath(path).Should().Be(expected);
    }
}
