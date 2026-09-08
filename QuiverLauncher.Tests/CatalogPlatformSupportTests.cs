using FluentAssertions;
using QuiverLauncher;
using QuiverLauncher.Core.Models;
using QuiverLauncher.Core.Services;
using QuiverLauncher.Models;
using QuiverLauncher.Services;
using QuiverLauncher.ViewModels;

namespace QuiverLauncher.Tests;

public class CatalogPlatformSupportTests : IDisposable
{
    private readonly string _cacheDir;

    public CatalogPlatformSupportTests()
    {
        _cacheDir = Path.Combine(Path.GetTempPath(), "QuiverCatalogPlatform_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_cacheDir);
        GitHubApiCache.Initialize(_cacheDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_cacheDir))
                Directory.Delete(_cacheDir, recursive: true);
        }
        catch
        {
            // Best effort
        }
    }

    [Theory]
    [InlineData("app-win.zip")]
    [InlineData("setup.exe")]
    [InlineData("Game_Windows_x64.msi")]
    [InlineData("SotE-Recomp-0.9beta.zip")]
    [InlineData("ChameleonTwistJPRecompiled.zip")]
    [InlineData("payload.7z")]
    [InlineData("game.rar")]
    public void FromAssetNames_detects_windows(string assetName)
    {
        CatalogPlatformSupport.FromAssetNames([assetName])
            .Should().HaveFlag(CatalogPlatformFlags.Windows);
    }

    [Theory]
    [InlineData("app-linux-x64.zip")]
    [InlineData("game-macos.zip")]
    [InlineData("app-darwin.zip")]
    public void FromAssetNames_labeled_archives_are_not_windows(string assetName)
    {
        CatalogPlatformSupport.FromAssetNames([assetName])
            .Should().NotHaveFlag(CatalogPlatformFlags.Windows);
    }

    [Theory]
    [InlineData("CrashBandicoot_Linux")]
    [InlineData("app-linux-x64.zip")]
    [InlineData("app-linux-arm64.zip")]
    [InlineData("MyApp.AppImage")]
    public void FromAssetNames_linux_includes_any_arch(string assetName)
    {
        CatalogPlatformSupport.FromAssetNames([assetName])
            .Should().HaveFlag(CatalogPlatformFlags.Linux);
    }

    [Theory]
    [InlineData("app-macos.dmg")]
    [InlineData("Game_darwin.zip")]
    [InlineData("MyApp.pkg")]
    public void FromAssetNames_detects_mac(string assetName)
    {
        CatalogPlatformSupport.FromAssetNames([assetName])
            .Should().HaveFlag(CatalogPlatformFlags.Mac);
    }

    [Theory]
    [InlineData("app-android.apk")]
    [InlineData("game-android-arm64-v8a.apk")]
    public void FromAssetNames_detects_android(string assetName)
    {
        CatalogPlatformSupport.FromAssetNames([assetName])
            .Should().HaveFlag(CatalogPlatformFlags.Android);
    }

    [Fact]
    public void FromAssetNames_multi_platform_release_sets_all_matching_flags()
    {
        var flags = CatalogPlatformSupport.FromAssetNames(
        [
            "game-win.exe",
            "game-linux.AppImage",
            "game-macos.dmg",
            "game-android.apk"
        ]);

        flags.Should().Be(
            CatalogPlatformFlags.Windows |
            CatalogPlatformFlags.Linux |
            CatalogPlatformFlags.Mac |
            CatalogPlatformFlags.Android);
    }

    [Fact]
    public void FilterAssetNames_respects_release_asset_filter()
    {
        var names = new[] { "EXIT1-win.zip", "EXIT2-linux.AppImage", "EXIT1-linux.tar.gz" };

        CatalogPlatformSupport.FilterAssetNames(names, "EXIT1")
            .Should().BeEquivalentTo("EXIT1-win.zip", "EXIT1-linux.tar.gz");
    }

    [Fact]
    public void Combo_or_keeps_windows_or_linux_and_drops_mac_only()
    {
        var selected = CatalogPlatformSupport.ParseFilters(["Windows", "Linux"]);

        CatalogPlatformSupport.Matches(
            CatalogPlatformSupport.FromAssetNames(["game-win.exe"]),
            selected).Should().BeTrue();
        CatalogPlatformSupport.Matches(
            CatalogPlatformSupport.FromAssetNames(["game-linux.AppImage"]),
            selected).Should().BeTrue();
        CatalogPlatformSupport.Matches(
            CatalogPlatformSupport.FromAssetNames(["game-macos.dmg"]),
            selected).Should().BeFalse();
    }

    [Fact]
    public void AppMatches_unknown_cache_stays_visible()
    {
        var repo = UniqueRepo("unknown");
        CatalogPlatformSupport.AppMatches("github", repo, null, ["Windows"])
            .Should().BeTrue();
    }

    [Fact]
    public void AppMatches_empty_asset_index_is_hidden_on_android()
    {
        var repo = UniqueRepo("empty-index");
        GitHubApiCache.SetCache(
            "github",
            repo,
            version: string.Empty,
            etag: "etag",
            new GitHubRelease { tag_name = string.Empty, assets = [] },
            replaceAssetNames: true);

        GitHubApiCache.TryGetAssetNames("github", repo, out var names).Should().BeTrue();
        names.Should().BeEmpty();
        CatalogPlatformSupport.AppMatches("github", repo, null, ["Android"])
            .Should().BeFalse();
        CatalogPlatformSupport.AppMatches("github", repo, null, ["Windows"])
            .Should().BeFalse();
    }

    [Fact]
    public void AppMatches_no_repository_stays_visible()
    {
        CatalogPlatformSupport.AppMatches("github", null, null, ["Android"])
            .Should().BeTrue();
    }

    [Fact]
    public void AppMatches_known_mismatch_is_hidden()
    {
        var repo = UniqueRepo("mac-only");
        GitHubApiCache.SetCache("github", repo, "v1.0.0", "etag", Release("v1.0.0", "game-macos.dmg"));

        CatalogPlatformSupport.AppMatches("github", repo, null, ["Windows"])
            .Should().BeFalse();
        CatalogPlatformSupport.AppMatches("github", repo, null, ["Mac"])
            .Should().BeTrue();
    }

    [Fact]
    public void AppMatches_release_asset_filter_limits_platforms()
    {
        var repo = UniqueRepo("shared");
        GitHubApiCache.SetCache(
            "github",
            repo,
            "v2.0.0",
            "etag",
            Release("v2.0.0", "EXIT1-win.zip", "EXIT2-linux.AppImage"));

        CatalogPlatformSupport.AppMatches("github", repo, "EXIT1", ["Windows"]).Should().BeTrue();
        CatalogPlatformSupport.AppMatches("github", repo, "EXIT1", ["Linux"]).Should().BeFalse();
        CatalogPlatformSupport.AppMatches("github", repo, "EXIT2", ["Linux"]).Should().BeTrue();
    }

    [Fact]
    public void SetCache_writes_asset_names()
    {
        var repo = UniqueRepo("named");
        GitHubApiCache.SetCache(
            "github",
            repo,
            "v3.0.0",
            "etag",
            Release("v3.0.0", "app-win.zip", "app.flatpak"));

        GitHubApiCache.TryGetAssetNames("github", repo, out var names).Should().BeTrue();
        names.Should().Equal("app-win.zip");
    }

    [Fact]
    public void TryGetAssetNames_backfills_from_cached_release()
    {
        var repo = UniqueRepo("backfill");
        var cacheKey = GitHubApiCache.GetCacheKey("github", repo);
        var jsonPath = Path.Combine(_cacheDir, "version_cache.json");
        File.WriteAllText(jsonPath, $$"""
            {
              "{{cacheKey}}": {
                "Version": "v1.2.3",
                "LastChecked": "{{DateTime.UtcNow:O}}",
                "ETag": "etag",
                "LastUpdateCheck": "{{DateTime.UtcNow:O}}",
                "CachedRelease": {
                  "tag_name": "v1.2.3",
                  "assets": [ { "name": "legacy-linux.AppImage", "browser_download_url": "https://example.com/a" } ]
                }
              }
            }
            """);

        GitHubApiCache.Initialize(_cacheDir);
        GitHubApiCache.TryGetAssetNames("github", repo, out var names).Should().BeTrue();
        names.Should().Equal("legacy-linux.AppImage");
    }

    [Fact]
    public void FormatLabel_and_toggle_support_combo_and_all()
    {
        CatalogPlatformSupport.FormatLabel([]).Should().Be("All platforms");
        CatalogPlatformSupport.FormatLabel(["Android"]).Should().Be("Android");
        CatalogPlatformSupport.FormatLabel(["Linux", "Windows"]).Should().Be("Windows + Linux");

        var toggled = CatalogPlatformSupport.Toggle([], "Windows");
        toggled.Should().Equal("Windows");
        CatalogPlatformSupport.Toggle(toggled, "Linux").Should().Equal("Windows", "Linux");
        CatalogPlatformSupport.Toggle(toggled, "All").Should().BeEmpty();
        CatalogPlatformSupport.IsAll([]).Should().BeTrue();
    }

    [Fact]
    public void DetectRuntimePlatform_returns_known_platform()
    {
        CatalogPlatformSupport.KnownPlatforms.Should().Contain(CatalogPlatformSupport.DetectRuntimePlatform());
    }

    [Fact]
    public void EnsureDefault_sets_runtime_platform_once()
    {
        var settings = new AppSettings();
        CatalogPlatformFilterSettings.EnsureDefault(settings, "Android").Should().BeTrue();
        settings.CatalogPlatformFilterChosen.Should().BeTrue();
        settings.CatalogPlatformFilters.Should().Equal("Android");

        CatalogPlatformFilterSettings.EnsureDefault(settings, "Windows").Should().BeFalse();
        settings.CatalogPlatformFilters.Should().Equal("Android");

        CatalogPlatformFilterSettings.SetAll(settings);
        settings.CatalogPlatformFilters.Should().BeEmpty();
        settings.CatalogPlatformFilterChosen.Should().BeTrue();
        CatalogPlatformFilterSettings.EnsureDefault(settings, "Linux").Should().BeFalse();
        settings.CatalogPlatformFilters.Should().BeEmpty();
    }

    [Fact]
    public void GetFilteredRows_applies_platform_filter_with_unknown_visible()
    {
        var windowsRepo = UniqueRepo("win-app");
        var macRepo = UniqueRepo("mac-app");
        var unknownRepo = UniqueRepo("pending");
        GitHubApiCache.SetCache("github", windowsRepo, "v1", "e", Release("v1", "game-win.exe"));
        GitHubApiCache.SetCache("github", macRepo, "v1", "e", Release("v1", "game-macos.dmg"));

        var viewModel = new CatalogSyncViewModel
        {
            ReviewFilter = CatalogReviewFilter.All,
            PlatformFilters = ["Windows"],
        };
        viewModel.Refresh(
            new AppCatalogSource { CachedListVersion = "1.0.0" },
            [],
            [
                new GameInfo { Repository = windowsRepo, Name = "Win Game", FolderName = "Win" },
                new GameInfo { Repository = macRepo, Name = "Mac Game", FolderName = "Mac" },
                new GameInfo { Repository = unknownRepo, Name = "Pending", FolderName = "Pending" },
                new GameInfo { Repository = "", Name = "Manual", FolderName = "Manual" },
            ]);

        viewModel.GetFilteredRows()
            .Select(row => row.DisplayName)
            .Should()
            .BeEquivalentTo("Win Game", "Pending", "Manual");
    }

    [Fact]
    public void CollectPending_skips_fresh_asset_index()
    {
        var cachedRepo = UniqueRepo("cached");
        var pendingRepo = UniqueRepo("needs-fetch");
        GitHubApiCache.SetCache("github", cachedRepo, "v1", "e", Release("v1", "game-win.exe"));

        var rows = new List<CatalogSyncRowItem>
        {
            new()
            {
                Repository = cachedRepo,
                External = new GameInfo { Repository = cachedRepo, Name = "Cached", FolderName = "Cached" },
            },
            new()
            {
                Repository = pendingRepo,
                External = new GameInfo { Repository = pendingRepo, Name = "Pending", FolderName = "Pending" },
            },
        };

        var pending = CatalogReleaseIndexWarmup.CollectPending(rows);
        pending.Select(target => target.Repository).Should().Equal(pendingRepo);
    }

    private static string UniqueRepo(string suffix) =>
        $"owner/{suffix}-{Guid.NewGuid():N}";

    private static GitHubRelease Release(string tag, params string[] assetNames) =>
        new()
        {
            tag_name = tag,
            assets = assetNames
                .Select(name => new GitHubAsset { name = name, browser_download_url = $"https://example.com/{name}" })
                .ToArray(),
        };
}
