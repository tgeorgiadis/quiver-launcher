using FluentAssertions;
using QuiverLauncher.Core.Services;

namespace QuiverLauncher.Tests;

public class PlatformAssetMatcherTests
{
    [Theory]
    [InlineData("CrashBandicoot_Linux")]
    [InlineData("app-linux-x64.zip")]
    [InlineData("game_linux_amd64.tar.gz")]
    [InlineData("MyApp.AppImage")]
    public void MatchesPlatform_linux_x64_accepts_arch_unspecified_and_explicit_x64(string assetName)
    {
        PlatformAssetMatcher.MatchesPlatform(assetName, "Linux-X64").Should().BeTrue();
    }

    [Theory]
    [InlineData("app-linux-arm64.zip")]
    [InlineData("game_aarch64_linux.tar.gz")]
    [InlineData("CrashBandicoot_win.exe")]
    [InlineData("app-i686-linux.zip")]
    public void MatchesPlatform_linux_x64_rejects_arm_windows_and_32bit(string assetName)
    {
        PlatformAssetMatcher.MatchesPlatform(assetName, "Linux-X64").Should().BeFalse();
    }

    [Fact]
    public void MatchesPlatform_linux_arm64_still_requires_arm_marker()
    {
        PlatformAssetMatcher.MatchesPlatform("CrashBandicoot_Linux", "Linux-ARM64").Should().BeFalse();
        PlatformAssetMatcher.MatchesPlatform("app-linux-arm64.zip", "Linux-ARM64").Should().BeTrue();
    }
}
