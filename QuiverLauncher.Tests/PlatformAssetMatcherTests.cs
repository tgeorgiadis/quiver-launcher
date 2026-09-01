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

    [Theory]
    [InlineData("app-android.apk")]
    [InlineData("game-android-arm64-v8a.apk")]
    [InlineData("MyGame_Android.apk")]
    public void MatchesPlatform_android_accepts_apk_assets(string assetName)
    {
        PlatformAssetMatcher.MatchesPlatform(assetName, "Android").Should().BeTrue();
    }

    [Theory]
    [InlineData("app-win.zip")]
    [InlineData("game_linux_amd64.tar.gz")]
    [InlineData("MyApp.AppImage")]
    [InlineData("setup.exe")]
    public void MatchesPlatform_android_rejects_desktop_assets(string assetName)
    {
        PlatformAssetMatcher.MatchesPlatform(assetName, "Android").Should().BeFalse();
    }

    [Fact]
    public void GetPlatformIdentifier_android_enum_returns_android()
    {
        PlatformAssetMatcher.GetPlatformIdentifier(QuiverLauncher.Core.Models.TargetOS.Android)
            .Should().Be("Android");
    }
}
