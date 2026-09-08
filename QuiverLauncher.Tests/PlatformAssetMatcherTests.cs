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

    [Theory]
    [InlineData("SotE-Recomp-0.9beta.zip")]
    [InlineData("ChameleonTwistJPRecompiled.zip")]
    [InlineData("payload.7z")]
    [InlineData("game.rar")]
    public void MatchesPlatform_windows_accepts_unlabeled_archives(string assetName)
    {
        PlatformAssetMatcher.MatchesPlatform(assetName, "Windows").Should().BeTrue();
        PlatformAssetMatcher.IsWindowsAsset(assetName).Should().BeTrue();
    }

    [Theory]
    [InlineData("app-linux-x64.zip")]
    [InlineData("game-macos.zip")]
    [InlineData("app-darwin.zip")]
    [InlineData("textures-android.zip")]
    public void MatchesPlatform_windows_rejects_labeled_non_windows_archives(string assetName)
    {
        PlatformAssetMatcher.MatchesPlatform(assetName, "Windows").Should().BeFalse();
        PlatformAssetMatcher.IsWindowsAsset(assetName).Should().BeFalse();
    }

    [Fact]
    public void MatchesPlatform_labeled_linux_and_mac_zips_keep_their_platforms()
    {
        PlatformAssetMatcher.MatchesPlatform("app-linux-x64.zip", "Linux-X64").Should().BeTrue();
        PlatformAssetMatcher.MatchesPlatform("game-macos.zip", "macOS").Should().BeTrue();
        PlatformAssetMatcher.MatchesPlatform("app-darwin.zip", "macOS").Should().BeTrue();
    }
}
