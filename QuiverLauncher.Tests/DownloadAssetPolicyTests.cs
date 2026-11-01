using System.Runtime.InteropServices;
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
    public void Ios_builds_are_incompatible_not_uncertain(string name)
    {
        PlatformAssetMatcher.IsWindowsAsset(name).Should().BeFalse();
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

    [Theory]
    [InlineData("Game-1.2.0-linux-x64.zip", "v1.2.0", "v1.3.0", "Game-1.3.0-linux-x64.zip")]
    [InlineData("Game-v1.2.0-linux-x64.zip", "v1.2.0", "v1.3.0", "Game-v1.3.0-linux-x64.zip")]
    [InlineData("game_1.2.0-beta.2_linux-x64.zip", "1.2.0-beta.2", "1.2.0", "game_1.2.0_linux-x64.zip")]
    [InlineData("Game-r22-linux-x64.zip", "r22", "r23", "Game-r23-linux-x64.zip")]
    [InlineData("Game-nightly-2026-10-10-linux-x64.zip", "nightly-2026-10-10", "nightly-2026-10-11", "Game-nightly-2026-10-11-linux-x64.zip")]
    [InlineData("Game-linux-x64.zip", "v1.2.0", "v1.3.0", "Game-linux-x64.zip")]
    [InlineData("GAME-1.2.0-LINUX-X64.ZIP", "v1.2.0", "v1.3.0", "Game-1.3.0-linux-x64.zip")]
    [InlineData("Game-1.2.0-windows-x64.zip", "v1.2.0", "v1.3.0", "Game-1.3.0-windows-x64.zip")]
    // A nightly rebuilt under one tag names its files by the build's date or commit.
    [InlineData("Game-nightly-20261017-linux-x64.zip", "nightly", "nightly", "Game-nightly-20261018-linux-x64.zip")]
    [InlineData("Game_2026_10_17_linux-x64.zip", "latest", "latest", "Game_2026_10_18_linux-x64.zip")]
    [InlineData("Game-linux-x64-a1b2c3d.zip", "continuous", "continuous", "Game-linux-x64-e4f5a6b.zip")]
    public void Update_keeps_the_installed_build_with_its_version_changed(string installed, string installedTag, string tag, string expected)
    {
        // The release's other files: other architectures, formats and platforms.
        var choices = DownloadAssetPolicy.Select(TaggedRelease(tag, new[] { expected, expected.Replace("x64", "arm64"),
            expected.Replace(".zip", ".AppImage"), expected.Replace("linux", "windows") }.Distinct().ToArray()), "Linux-X64");
        DownloadAssetPolicy.MatchInstalled(choices, installed, installedTag, tag)!.name.Should().Be(expected);
    }

    [Fact]
    public void Linux_update_keeps_whichever_of_the_linux_and_windows_builds_is_installed()
    {
        var release = TaggedRelease("v1.3.0", "Game-1.3.0-linux-x64.zip", "Game-1.3.0-windows-x64.zip");
        var choices = DownloadAssetPolicy.Select(release, "Linux-X64");
        choices.Automatic.Should().BeNull();
        DownloadAssetPolicy.MatchInstalled(choices, "Game-1.2.0-linux-x64.zip", "v1.2.0", "v1.3.0")!.name.Should().Be("Game-1.3.0-linux-x64.zip");
        DownloadAssetPolicy.MatchInstalled(choices, "Game-1.2.0-windows-x64.zip", "v1.2.0", "v1.3.0")!.name.Should().Be("Game-1.3.0-windows-x64.zip");
    }

    [Theory]
    [InlineData("Game-1.2.0-linux-arm64.zip")] // Another architecture.
    [InlineData("Game-1.2.0-linux-x64.AppImage")] // Another format.
    [InlineData("Game-1.2.0-linux-x64-debug.zip")] // A build the release no longer has.
    [InlineData("")]
    public void Update_matches_nothing_when_the_installed_build_is_not_in_the_release(string installed)
    {
        var choices = DownloadAssetPolicy.Select(TaggedRelease("v1.3.0", "Game-1.3.0-linux-x64.zip", "Game-1.3.0-windows-x64.zip"), "Linux-X64");
        DownloadAssetPolicy.MatchInstalled(choices, installed, "v1.2.0", "v1.3.0").Should().BeNull();
    }

    [Fact]
    public void Update_matches_nothing_when_two_files_differ_only_by_version()
    {
        var choices = DownloadAssetPolicy.Select(TaggedRelease("v1.3.0", "Game-1.3.0-linux-x64.zip", "Game-1.3.1-linux-x64.zip"), "Linux-X64");
        DownloadAssetPolicy.MatchInstalled(choices, "Game-1.2.0-linux-x64.zip", "v1.2.0", "v1.3.0").Should().BeNull();
    }

    [Fact]
    public void Update_matches_nothing_when_two_files_differ_only_by_build_date()
    {
        var choices = DownloadAssetPolicy.Select(TaggedRelease("nightly", "Game-20261018-linux-x64.zip", "Game-20261019-linux-x64.zip"), "Linux-X64");
        DownloadAssetPolicy.MatchInstalled(choices, "Game-20261017-linux-x64.zip", "nightly", "nightly").Should().BeNull();
    }

    [Theory]
    [InlineData("Game-linux-x64-1234567.zip", "Game-linux-x64-7654321.zip")] // A build number, not a commit.
    [InlineData("Game-linux-x64-deadbeef.zip", "Game-linux-x64-cafebabe.zip")] // Words, not a commit.
    [InlineData("Game-linux-x64-2026-13-01.zip", "Game-linux-x64-2026-14-01.zip")] // Not a date.
    public void Update_matches_nothing_for_names_differing_by_more_than_a_build_stamp(string installed, string offered)
    {
        var choices = DownloadAssetPolicy.Select(TaggedRelease("nightly", offered, offered.Replace("linux", "windows")), "Linux-X64");
        DownloadAssetPolicy.MatchInstalled(choices, installed, "nightly", "nightly").Should().BeNull();
    }

    [Fact]
    public void Update_matches_an_installed_file_from_the_other_downloads()
    {
        var choices = DownloadAssetPolicy.Select(TaggedRelease("v1.3.0", "Game-1.3.0-linux-x64.zip", "Game-1.3.0-data.pak"), "Linux-X64");
        choices.Uncertain.Should().ContainSingle();
        DownloadAssetPolicy.MatchInstalled(choices, "Game-1.2.0-data.pak", "v1.2.0", "v1.3.0")!.name.Should().Be("Game-1.3.0-data.pak");
    }

    internal static GitHubRelease Release(params string[] names) => new()
    {
        tag_name = "v1", assets = names.Select(n => new GitHubAsset { name = n, browser_download_url = "https://example.test/" + n }).ToArray(),
    };

    private static GitHubRelease TaggedRelease(string tag, params string[] names)
    {
        var release = Release(names);
        release.tag_name = tag;
        return release;
    }

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

    [Theory]
    [InlineData("Windows", "Dusklight-v2.0.3-win32-arm64.zip", "Dusklight-v2.0.3-win32-x86_64.zip")]
    [InlineData("Windows", "SymphonyRecomp-v0.5.1b-windows-arm64.zip", "SymphonyRecomp-v0.5.1b-windows-x64.zip")]
    [InlineData("Windows", "Melee-Windows-x86_64.zip", "Melee-Windows-arm64.zip")]
    [InlineData("macOS", "Game-macos-arm64.zip", "Game-macos-x64.zip")]
    public void An_x64_computer_installs_the_x64_build_without_asking(string platform, string first, string second)
    {
        var choices = DownloadAssetPolicy.Select(Release(first, second), platform, machine: Architecture.X64);
        choices.NeedsChoice.Should().BeFalse();
        DownloadAssetPolicy.ProcessorOf(choices.Automatic!.name).Should().Be(Architecture.X64);
    }

    [Theory]
    [InlineData("Windows", "SymphonyRecomp-v0.5.1b-windows-arm64.zip", "SymphonyRecomp-v0.5.1b-windows-x64.zip")]
    [InlineData("macOS", "Game-macos-x64.zip", "Game-macos-arm64.zip")]
    public void An_arm64_computer_installs_the_arm64_build_without_asking(string platform, string first, string second)
    {
        var choices = DownloadAssetPolicy.Select(Release(first, second), platform, machine: Architecture.Arm64);
        DownloadAssetPolicy.ProcessorOf(choices.Automatic!.name).Should().Be(Architecture.Arm64);
    }

    [Fact]
    public void An_arm64_computer_keeps_x64_builds_when_there_is_no_arm64_one()
    {
        var choices = DownloadAssetPolicy.Select(Release("Game-windows-x64.zip"), "Windows", machine: Architecture.Arm64);
        choices.Automatic!.name.Should().Be("Game-windows-x64.zip");
    }

    [Fact]
    public void Builds_that_do_not_name_a_processor_stay_on_offer()
    {
        var choices = DownloadAssetPolicy.Select(Release("Game-windows.zip", "Game-windows-arm64.zip", "Game-windows-x64.zip"),
            "Windows", machine: Architecture.X64);
        choices.Eligible.Select(a => a.name).Should().BeEquivalentTo("Game-windows.zip", "Game-windows-x64.zip");
    }

    [Fact]
    public void A_release_with_only_an_arm64_build_still_offers_it()
    {
        var choices = DownloadAssetPolicy.Select(Release("Game-windows-arm64.zip"), "Windows", machine: Architecture.X64);
        choices.Automatic!.name.Should().Be("Game-windows-arm64.zip");
    }

    [Theory]
    [InlineData("Game-windows-arm64.zip", Architecture.Arm64)]
    [InlineData("Game-aarch64-macos.zip", Architecture.Arm64)]
    [InlineData("Game-win32-x86_64.zip", Architecture.X64)]
    [InlineData("Game-win64.zip", Architecture.X64)]
    [InlineData("Game-x86-setup.exe", Architecture.X64)]
    [InlineData("Game-windows.zip", null)]
    [InlineData("Harmony-windows.zip", null)]
    [InlineData("Game-macos-universal.zip", null)]
    [InlineData("Game-macos-arm64-x64.zip", null)]
    public void Processor_is_read_from_the_file_name(string name, Architecture? expected)
    {
        DownloadAssetPolicy.ProcessorOf(name).Should().Be(expected);
    }
}
