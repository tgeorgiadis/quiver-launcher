using FluentAssertions;
using QuiverLauncher.Core.Models;
using QuiverLauncher.Core.Services;
using QuiverLauncher.Models;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class DownloadAssetPolicyTests
{
    [Fact]
    public void Golden_balloon_json_sidecar_does_not_prevent_automatic_windows_selection()
    {
        var release = Release("Golden-Balloon-1.5.2-windows-x64.zip",
            "Golden-Balloon-1.5.2-windows-x64.zip.provenance.json");
        var choices = DownloadAssetPolicy.Select(release, "Windows");
        choices.Automatic!.name.Should().Be("Golden-Balloon-1.5.2-windows-x64.zip");
        choices.Uncertain.Should().BeEmpty();
        GitHubReleaseService.GetDownloadableAssets(release).Should().ContainSingle();
    }

    [Fact]
    public void Gen1recomp_selects_only_windows_instead_of_handheld_and_xbox_builds()
    {
        var release = Release("gen1recomp-0.2.59-rg34xxsp-stockos64-mod.zip",
            "gen1recomp-0.2.59-sbc-portmaster.zip", "gen1recomp-0.2.59-windows.zip",
            "gen1recomp-0.2.59-xbox-uwp.zip");
        var choices = GameDownloadService.Prepare(new GameInfo(), release, new() { Platform = TargetOS.Windows });
        choices.Automatic!.name.Should().Be("gen1recomp-0.2.59-windows.zip");
        choices.Eligible.Should().ContainSingle();
        choices.Uncertain.Should().BeEmpty();
        choices.NeedsChoice.Should().BeFalse();
    }

    [Theory]
    [InlineData("release.json")]
    [InlineData("game-Windows.zip.provenance.JSON")]
    [InlineData("game-linux.json")]
    [InlineData("game-android.apk.json")]
    [InlineData("game-macos.json")]
    public void Json_files_are_excluded_even_with_an_explicit_asset_filter(string name)
    {
        var release = Release(name);
        DownloadAssetPolicy.IsAuxiliary(name).Should().BeTrue();
        GitHubReleaseService.GetDownloadableAssets(release, "json").Should().BeEmpty();
        CatalogPlatformSupport.FromAssetNames([name]).Should().Be(CatalogPlatformFlags.None);
        foreach (var platform in new[] { "Windows", "Linux-X64", "Linux-ARM64", "macOS", "Android" })
        {
            var choices = DownloadAssetPolicy.Select(release, platform);
            choices.Eligible.Should().BeEmpty();
            choices.Uncertain.Should().BeEmpty();
        }
    }

    [Theory]
    [InlineData("game-rg34xxsp.zip")]
    [InlineData("game-stockos64-mod.zip")]
    [InlineData("game-sbc-PortMaster.7z")]
    [InlineData("game-XBOX-UWP.zip")]
    [InlineData("game-windows-xbox-uwp.zip")]
    [InlineData("game-linux-portmaster.tar.gz")]
    public void Dedicated_device_builds_are_not_desktop_or_android_downloads(string name)
    {
        PlatformAssetMatcher.IsWindowsAsset(name).Should().BeFalse();
        CatalogPlatformSupport.FromAssetNames([name]).Should().Be(CatalogPlatformFlags.None);
        foreach (var platform in new[] { "Windows", "Linux-X64", "Linux-ARM64", "macOS", "Android" })
        {
            var choices = DownloadAssetPolicy.Select(Release(name), platform);
            choices.Eligible.Should().BeEmpty();
            choices.Uncertain.Should().BeEmpty();
        }
    }

    [Theory]
    [InlineData("MyPortmasterGame-windows.zip")]
    [InlineData("Stockosaurus.zip")]
    [InlineData("game-windows-uwp.zip")]
    [InlineData("JsonQuest-windows.zip")]
    public void Similar_title_substrings_and_non_xbox_uwp_remain_usable(string name)
    {
        DownloadAssetPolicy.Select(Release(name), "Windows").Automatic!.name.Should().Be(name);
    }

    // Published assets at Unchiga/YuGiOhForbiddenMemoriesRecomp v0.5.9.
    internal static GitHubRelease ForbiddenMemoriesRelease() => new()
    {
        tag_name = "v0.5.9",
        assets = Release("ygofm-0.5.9-linux-x64.zip", "ygofm-0.5.9-macos-arm64.zip",
            "ygofm-0.5.9-macos-x64.zip", "ygofm-0.5.9-win-x64.zip").assets,
    };

    [Fact]
    public void Forbidden_memories_offers_linux_then_windows_on_linux_x64()
    {
        var game = new GameInfo { Repository = "Unchiga/YuGiOhForbiddenMemoriesRecomp" };
        var choices = GameDownloadService.Prepare(game, ForbiddenMemoriesRelease(),
            new AppSettings { Platform = TargetOS.LinuxX64 });
        choices.Eligible.Select(a => a.name).Should().Equal(
            "ygofm-0.5.9-linux-x64.zip", "ygofm-0.5.9-win-x64.zip");
        choices.Uncertain.Should().BeEmpty();
        choices.Automatic.Should().BeNull();
        choices.NeedsChoice.Should().BeTrue();
        game.AvailableDownloads.Should().Equal(choices.Eligible);
    }

    [Fact]
    public void Auto_platform_on_linux_uses_the_runtime_architecture()
    {
        if (!OperatingSystem.IsLinux() || OperatingSystem.IsAndroid()) return;
        var settings = new AppSettings { Platform = TargetOS.Auto };
        var platform = GameInfo.GetPlatformIdentifier(settings);
        platform.Should().StartWith("Linux-");
        if (System.Runtime.InteropServices.RuntimeInformation.OSArchitecture != System.Runtime.InteropServices.Architecture.X64) return;
        GameDownloadService.Prepare(new GameInfo(), ForbiddenMemoriesRelease(), settings)
            .Eligible.Select(a => a.name).Should().Equal(
                "ygofm-0.5.9-linux-x64.zip", "ygofm-0.5.9-win-x64.zip");
    }

    [Fact]
    public void Nautilus_ios_zip_does_not_prevent_automatic_windows_selection()
    {
        var release = Release("Nautilus-Alfa-iOS.zip", "Nautilus-Alfa-Win64.zip");
        foreach (var platform in new[] { "Windows", "Linux-X64", "Linux-ARM64" })
        {
            var choices = DownloadAssetPolicy.Select(release, platform);
            choices.Automatic!.name.Should().Be("Nautilus-Alfa-Win64.zip");
            choices.Uncertain.Should().BeEmpty();
        }
    }

    [Theory]
    [InlineData("Nautilus-Alfa-iOS.zip")]
    [InlineData("game_IOS_arm64.7z")]
    [InlineData("game-iPadOS.zip")]
    [InlineData("game-iPhone.zip")]
    [InlineData("game-iPad.zip")]
    [InlineData("game-iphoneos.tar.gz")]
    [InlineData("game.IPA")]
    [InlineData("game.ipa.zip")]
    [InlineData("game-apple-ios.zip")]
    public void Ios_builds_are_incompatible_not_uncertain_or_catalog_platform_evidence(string name)
    {
        PlatformAssetMatcher.IsWindowsAsset(name).Should().BeFalse();
        CatalogPlatformSupport.FromAssetNames([name]).Should().Be(CatalogPlatformFlags.None);
        foreach (var platform in new[] { "Windows", "Linux-X64", "Linux-ARM64", "macOS", "Android" })
        {
            var choices = DownloadAssetPolicy.Select(Release(name), platform);
            choices.Eligible.Should().BeEmpty();
            choices.Uncertain.Should().BeEmpty();
            choices.NeedsChoice.Should().BeFalse();
        }
    }

    [Theory]
    [InlineData("BIOS.zip")]
    [InlineData("Helios-Windows.zip")]
    [InlineData("GameStudios.zip")]
    public void Ios_detection_does_not_match_substrings_in_titles(string name)
    {
        PlatformAssetMatcher.IsIosAsset(name).Should().BeFalse();
        DownloadAssetPolicy.Select(Release(name), "Windows").Automatic.Should().NotBeNull();
    }

    internal static GitHubRelease Release(params string[] names) => new()
    {
        tag_name = "v1", assets = names.Select(n => new GitHubAsset { name = n, browser_download_url = "https://example.test/" + n }).ToArray(),
    };

    [Fact]
    public void Windows_automatically_selects_build_without_sidecars_or_other_platforms()
    {
        var result = DownloadAssetPolicy.Select(Release("GameWindows.zip", "GameWindows.zip.sha256", "GameWindows.sha256",
            "SHA256SUMS", "GameWindows.zip.sig", "GameLinux.AppImage", "GameMac.dmg", "GameAndroid.apk"), "Windows");
        result.Automatic!.name.Should().Be("GameWindows.zip");
        result.Uncertain.Should().BeEmpty();
    }

    [Theory]
    [InlineData("GameWindows.zip.sha256")]
    [InlineData("game-linux.tar.gz.asc")]
    [InlineData("SHA256SUMS.txt")]
    [InlineData("MD5SUMS")]
    [InlineData("checksums.txt")]
    [InlineData("game-windows-symbols.zip")]
    [InlineData("game-windows.pdb")]
    [InlineData("game-linux.debug")]
    [InlineData("game-macos.dSYM.zip")]
    public void Auxiliary_files_never_establish_platform_support(string name)
    {
        DownloadAssetPolicy.IsAuxiliary(name).Should().BeTrue();
        CatalogPlatformSupport.FromAssetNames([name]).Should().Be(CatalogPlatformFlags.None);
        DownloadAssetPolicy.Select(Release(name), "Windows").Uncertain.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Linux-X64", "game-linux-x64", "game-linux-arm64")]
    [InlineData("Linux-ARM64", "game-linux-arm64", "game-linux-x64")]
    public void Linux_keeps_native_appimage_and_windows_but_not_other_architectures(string platform, string native, string incompatible)
    {
        var result = DownloadAssetPolicy.Select(Release("game-windows.zip", native, "game.AppImage", incompatible,
            "game-macos.zip", "game-android.apk", "game-linux.zip.sha256"), platform);
        result.Eligible.Select(a => a.name).Should().Equal(native, "game.AppImage", "game-windows.zip");
        result.Automatic.Should().BeNull();
        result.Uncertain.Should().BeEmpty();
    }

    [Fact]
    public void Unlabeled_archives_keep_windows_fallback_and_unknown_binaries_require_choice()
    {
        foreach (var platform in new[] { "Windows", "Linux-X64" })
        {
            DownloadAssetPolicy.Select(Release("game.zip"), platform).Automatic!.name.Should().Be("game.zip");
            var unknown = DownloadAssetPolicy.Select(Release("game"), platform);
            unknown.Automatic.Should().BeNull();
            unknown.NeedsChoice.Should().BeTrue();
            unknown.EmptyReason.Should().NotBeNull();
            unknown.Uncertain.Should().ContainSingle();
        }
    }

    [Fact]
    public void Asset_filter_applies_to_regular_and_uncertain_downloads()
    {
        var result = DownloadAssetPolicy.Select(Release("First-windows.zip", "First", "Second", "Second-windows.zip"), "Windows", "First");
        result.Eligible.Should().ContainSingle(a => a.name == "First-windows.zip");
        result.Uncertain.Should().ContainSingle(a => a.name == "First");
        DownloadAssetPolicy.Select(Release("First-windows.zip"), "Windows", "Missing").EmptyReason.Should().Contain("filter");
    }

    [Fact]
    public void Checksums_only_latest_is_skipped_but_explicit_pin_is_preserved()
    {
        var empty = Release("game-windows.sha256"); empty.tag_name = "v2";
        var usable = Release("game-windows.zip");
        ReleaseSelection.SelectLatestRelease([empty, usable], githubLatestTag: "v2").Should().BeSameAs(usable);
        ReleaseSelection.SelectLatestRelease([empty, usable], preferredVersion: "v2").Should().BeSameAs(empty);
        DownloadAssetPolicy.Select(empty, "Windows").EmptyReason.Should().Contain("no installable");
    }

    [Fact]
    public void Explicit_selection_is_cleared_when_context_or_asset_changes()
    {
        var game = new GameInfo { Repository = "owner/app", FolderName = "app" };
        var settings = new AppSettings { Platform = TargetOS.Windows };
        var release = Release("game-windows.zip", "game-windows-portable.zip", "unknown");
        GameDownloadService.SelectExplicit(game, release, settings, release.assets[2]);
        GameDownloadService.Prepare(game, release, settings);
        game.SelectedDownload!.name.Should().Be("unknown");
        settings.Platform = TargetOS.LinuxX64;
        GameDownloadService.Prepare(game, release, settings);
        game.SelectedDownload.Should().BeNull();
        GameDownloadService.SelectExplicit(game, release, settings, release.assets[0]);
        release.tag_name = "v2";
        GameDownloadService.Prepare(game, release, settings);
        game.SelectedDownload.Should().BeNull();
        GameDownloadService.SelectExplicit(game, release, settings, release.assets[0]);
        game.ReleaseAssetFilter = "portable";
        GameDownloadService.Prepare(game, release, settings);
        game.SelectedDownload.Should().BeNull();
        game.ReleaseAssetFilter = null;
        GameDownloadService.SelectExplicit(game, release, settings, release.assets[0]);
        release.assets[0] = new() { name = "game-windows.zip", browser_download_url = "https://example.test/replaced" };
        GameDownloadService.Prepare(game, release, settings);
        game.SelectedDownload.Should().BeNull();
    }

    [Fact]
    public void Automatic_selection_does_not_choose_first_of_multiple_builds()
    {
        var game = new GameInfo { Repository = "owner/app" };
        GameDownloadService.TrySelectPlatformDownload(game, Release("game-linux", "game-windows.zip"),
            new() { Platform = TargetOS.LinuxX64 }).Should().BeFalse();
        game.SelectedDownload.Should().BeNull();
        game.DownloadChoices!.NeedsChoice.Should().BeTrue();
    }

    [Theory]
    [InlineData("macOS", "game-macos.dmg")]
    [InlineData("Android", "game-android.apk")]
    public void Other_platforms_only_offer_their_build(string platform, string expected)
    {
        var choices = DownloadAssetPolicy.Select(Release("game-windows.zip", "game-linux.AppImage", "game-macos.dmg", "game-android.apk"), platform);
        choices.Automatic!.name.Should().Be(expected);
        choices.Uncertain.Should().BeEmpty();
    }

    [Fact]
    public void Auxiliary_detection_does_not_reject_titles_containing_similar_words()
    {
        DownloadAssetPolicy.Select(Release("ChecksumQuest-Windows.zip"), "Windows").Automatic.Should().NotBeNull();
    }
}
