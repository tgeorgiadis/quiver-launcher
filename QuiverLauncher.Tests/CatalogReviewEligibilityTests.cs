using FluentAssertions;
using QuiverLauncher.Core.Models;
using QuiverLauncher.Core.Services;
using QuiverLauncher.Models;
using QuiverLauncher.Services;
using QuiverLauncher.ViewModels;

namespace QuiverLauncher.Tests;

public class CatalogReviewEligibilityTests
{
    private static GameInfo App() => new() { Repository = $"review-{Guid.NewGuid():N}/app", Name = "App", FolderName = "App" };
    private static void Assets(GameInfo app, params string[] names) => CatalogPlatformIndex.Set("github",
        app.Repository, app.PreferredVersion, null, new GitHubRelease
        { tag_name = "1", assets = names.Select(n => new GitHubAsset { name = n }).ToArray() });

    [Theory]
    [InlineData("app.dmg")]
    [InlineData("app.apk")]
    [InlineData("app-Linux.AppImage")]
    [InlineData("app-Windows.zip.sha256")]
    public void Incompatible_assets_do_not_count_or_acknowledge_and_all_platforms_reveals_them(string asset)
    {
        var app = App(); Assets(app, asset);
        var source = new AppCatalogSource { CachedListVersion = "1", Enabled = true };
        var model = new CatalogSyncViewModel { PlatformFilters = ["Windows"], ReviewFilter = CatalogReviewFilter.NeedsReview };
        model.Refresh(source, [], [app]);
        model.NeedsReviewCount.Should().Be(0);
        model.NotInLibraryCount.Should().Be(0);
        model.ShowHiddenPendingReviews.Should().BeFalse();
        model.GetFilteredRows().Should().BeEmpty();
        model.GetFilteredBulkAddRows().Should().BeEmpty();
        CatalogReviewEligibility.Reconcile(source, model.AllRows, devicePlatform: "Windows");
        source.PendingReviewCount.Should().Be(0);
        source.UpdateAvailable.Should().BeFalse();
        source.AcknowledgedListVersion.Should().BeNull();
        source.IgnoredChangesAtVersion.Should().BeEmpty();
        var operatingSystem = CatalogPlatformSupport.DetectRuntimePlatform() is "Mac" ? "macOS" : CatalogPlatformSupport.DetectRuntimePlatform();
        CatalogSourceListItem.FromSource(source).VersionLineText.Should().Contain($"No pending reviews for {operatingSystem}");
        CatalogSourceListItem.FromSource(source).AllReviewedVisible.Should().BeTrue();
        CatalogSourceListItem.FromSource(source).ReviewedToolTip.Should().Contain($"available for {operatingSystem}");
        model.PlatformFilters = [];
        model.NeedsReviewCount.Should().Be(1);
        model.GetFilteredRows().Should().ContainSingle();
        // Same catalog version, newly compatible release: no acknowledgement to undo.
        Assets(app, "app-Windows.zip");
        CatalogReviewEligibility.Reconcile(source, model.AllRows, devicePlatform: "Windows");
        source.PendingReviewCount.Should().Be(1);
        source.UpdateAvailable.Should().BeTrue();
        CatalogSourceListItem.FromSource(source).AllReviewedVisible.Should().BeFalse();
    }

    [Fact]
    public void Unknown_checks_stay_unverified_and_pending_until_successful()
    {
        var app = App();
        var source = new AppCatalogSource { CachedListVersion = "1" };
        var model = new CatalogSyncViewModel { PlatformFilters = ["Windows"], ReviewFilter = CatalogReviewFilter.NeedsReview };
        model.Refresh(source, [], [app]);
        model.UnverifiedPlatformCount.Should().Be(1);
        model.NeedsReviewCount.Should().Be(1);
        model.ShowNeedsReviewCompleteState.Should().BeFalse();
        CatalogReviewEligibility.Reconcile(source, model.AllRows, devicePlatform: "Windows");
        source.PendingReviewCount.Should().Be(1);
        CatalogSourceListItem.FromSource(source).AllReviewedVisible.Should().BeFalse();
        source.AcknowledgedListVersion.Should().BeNull();
        Assets(app); // A successful empty release is different from missing metadata.
        model.UnverifiedPlatformCount.Should().Be(0);
        model.NeedsReviewCount.Should().Be(0);
    }

    [Fact]
    public void Private_metadata_does_not_exclude_apps_for_another_credential()
    {
        var app = App();
        var row = CatalogCompareService.BuildCompareRows([], [app]).Single();
        CatalogPlatformIndex.Set("github", app.Repository, null, "fixture-context-a",
            new GitHubRelease { tag_name = "1", assets = [new() { name = "app.dmg" }] });
        CatalogReviewEligibility.IsRelevant(row, ["Windows"], "fixture-context-a").Should().BeFalse();
        CatalogReviewEligibility.IsRelevant(row, ["Windows"], "fixture-context-b").Should().BeTrue();
        CatalogReviewEligibility.IsRelevant(row, ["Windows"]).Should().BeTrue();
    }

    [Fact]
    public void Context_and_asset_filter_are_respected_without_linux_windows_compatibility()
    {
        var app = App(); Assets(app, "app-Windows.zip");
        var row = CatalogCompareService.BuildCompareRows([], [app]).Single();
        CatalogReviewEligibility.IsRelevant(row, ["Linux"]).Should().BeFalse();
        app.PreferredVersion = "pinned";
        CatalogReviewEligibility.IsRelevant(row, ["Linux"]).Should().BeTrue("the pinned target is still unknown");
        Assets(app, "app-Windows.zip", "app-Linux.AppImage");
        app.ReleaseAssetFilter = "Windows";
        CatalogReviewEligibility.IsRelevant(row, ["Linux"]).Should().BeFalse();
        CatalogReviewEligibility.IsRelevant(row, ["Windows"]).Should().BeTrue();
    }
}
