using FluentAssertions;
using QuiverLauncher.Core.Services;

namespace QuiverLauncher.Tests;

public class PlatformAssetMatcherTests
{
    [Theory]
    [InlineData("SoH-Ackbar-Delta-Mac.zip")]
    [InlineData("game_MAC_arm64.7z")]
    [InlineData("Mac.zip")]
    [InlineData("game Mac universal.zip")]
    public void Mac_label_is_excluded_from_other_platforms(string asset)
    {
        PlatformAssetMatcher.IsWindowsAsset(asset).Should().BeFalse();
        foreach (var platform in new[] { "Windows", "Linux-X64", "Linux-ARM64", "Android" })
            PlatformAssetMatcher.MatchesPlatform(asset, platform).Should().BeFalse();
        PlatformAssetMatcher.MatchesPlatform(asset, "macOS").Should().BeTrue();
        CatalogPlatformSupport.FromAssetNames([asset]).Should().Be(CatalogPlatformFlags.Mac);
    }

    [Fact]
    public void Ship_of_harkinian_release_recommends_only_win64_on_windows()
    {
        string[] assets = ["SoH-Ackbar-Delta-Mac.zip", "SoH-Ackbar-Delta-Win64.zip", "SoH-Ackbar-Delta-Linux.zip"];
        assets.Where(name => QuiverLauncher.Models.GameInfo.MatchesPlatform(name, "Windows"))
            .Should().Equal("SoH-Ackbar-Delta-Win64.zip");
    }

    [Theory]
    [InlineData("Machine-Win64.zip")]
    [InlineData("PacMac.zip")]
    public void Mac_inside_an_app_name_is_not_a_platform_label(string asset)
    {
        PlatformAssetMatcher.IsWindowsAsset(asset).Should().BeTrue();
        PlatformAssetMatcher.MatchesPlatform(asset, "macOS").Should().BeFalse();
    }

    [Theory]
    [InlineData("BM64Recompiled-AppImage-ARM64-Release.zip", "Linux-ARM64", "Linux-X64")]
    [InlineData("BM64Recompiled-AppImage-X64-Release.zip", "Linux-X64", "Linux-ARM64")]
    [InlineData("game_APPIMAGE_aarch64.7z", "Linux-ARM64", "Linux-X64")]
    public void Wrapped_appimages_are_linux_builds_with_matching_architecture(string asset, string linuxPlatform, string otherArchitecture)
    {
        PlatformAssetMatcher.MatchesPlatform(asset, "Windows").Should().BeFalse();
        PlatformAssetMatcher.IsWindowsAsset(asset).Should().BeFalse();
        PlatformAssetMatcher.MatchesPlatform(asset, "macOS").Should().BeFalse();
        PlatformAssetMatcher.MatchesPlatform(asset, "Android").Should().BeFalse();
        PlatformAssetMatcher.MatchesPlatform(asset, linuxPlatform).Should().BeTrue();
        PlatformAssetMatcher.MatchesPlatform(asset, otherArchitecture).Should().BeFalse();
        CatalogPlatformSupport.FromAssetNames([asset]).Should().Be(CatalogPlatformFlags.Linux);
        QuiverLauncher.Models.GameInfo.GetPlatformIcon(asset).Should().EndWith("platform_lin.png");
    }

    [Theory]
    [InlineData("BM64Recompiled-PDB-RelWithDebInfo.zip")]
    [InlineData("game-windows-x64-symbols.zip")]
    [InlineData("game-linux-x64-debug-symbols.tar.gz")]
    public void Symbol_packages_are_not_recommended_as_runnable_builds(string asset)
    {
        foreach (var platform in new[] { "Windows", "Linux-X64", "Linux-ARM64", "macOS", "Android" })
            PlatformAssetMatcher.MatchesPlatform(asset, platform).Should().BeFalse();
        PlatformAssetMatcher.IsWindowsAsset(asset).Should().BeFalse();
    }

    [Fact]
    public void Screenshot_release_recommends_only_the_windows_application_on_windows()
    {
        string[] assets = [
            "BM64Recompiled-AppImage-ARM64-Release.zip",
            "BM64Recompiled-AppImage-X64-Release.zip",
            "BM64Recompiled-PDB-RelWithDebInfo.zip",
            "BM64Recompiled-Windows-RelWithDebInfo.zip",
            "BM64Recompiled-Linux-ARM64-Release.zip",
            "BM64Recompiled-Linux-X64-Release.zip",
            "BM64Recompiled-macOS-Release.zip"
        ];
        assets.Where(name => QuiverLauncher.Models.GameInfo.MatchesPlatform(name, "Windows"))
            .Should().Equal("BM64Recompiled-Windows-RelWithDebInfo.zip");
    }

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
    public void MatchesPlatform_linux_arm64_accepts_unspecified_architecture()
    {
        PlatformAssetMatcher.MatchesPlatform("CrashBandicoot_Linux", "Linux-ARM64").Should().BeTrue();
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
