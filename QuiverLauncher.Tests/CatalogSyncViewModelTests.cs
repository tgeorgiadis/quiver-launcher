using FluentAssertions;
using QuiverLauncher.Models;
using QuiverLauncher.Services;
using QuiverLauncher.ViewModels;

namespace QuiverLauncher.Tests;

public class CatalogSyncViewModelTests
{
    private static GameInfo CreateApp(string repository, string name = "Test App", string folderName = "TestFolder") =>
        new()
        {
            Repository = repository,
            Name = name,
            FolderName = folderName,
        };

    [Fact]
    public void ShowNeedsReviewCompleteState_true_when_needs_review_filter_and_no_actionable_rows()
    {
        var local = new List<GameInfo> { CreateApp("owner/same", "Same", "Folder") };
        var external = new List<GameInfo> { CreateApp("owner/same", "Same", "Folder") };
        var source = new AppCatalogSource { CachedListVersion = "1.0.0" };

        var viewModel = new CatalogSyncViewModel
        {
            ReviewFilter = CatalogReviewFilter.NeedsReview,
        };
        viewModel.Refresh(source, local, external);

        viewModel.NeedsReviewCount.Should().Be(0);
        viewModel.ShowNeedsReviewCompleteState.Should().BeTrue();
    }

    [Fact]
    public void ShowNeedsReviewCompleteState_false_when_actionable_rows_remain()
    {
        var local = new List<GameInfo>();
        var external = new List<GameInfo> { CreateApp("owner/new", "New App", "NewFolder") };
        var source = new AppCatalogSource { CachedListVersion = "1.0.0" };

        var viewModel = new CatalogSyncViewModel
        {
            ReviewFilter = CatalogReviewFilter.NeedsReview,
        };
        viewModel.Refresh(source, local, external);

        viewModel.NeedsReviewCount.Should().Be(1);
        viewModel.ShowNeedsReviewCompleteState.Should().BeFalse();
    }

    [Fact]
    public void ShowNeedsReviewCompleteState_false_when_filter_is_not_needs_review()
    {
        var local = new List<GameInfo> { CreateApp("owner/same", "Same", "Folder") };
        var external = new List<GameInfo> { CreateApp("owner/same", "Same", "Folder") };
        var source = new AppCatalogSource { CachedListVersion = "1.0.0" };

        var viewModel = new CatalogSyncViewModel
        {
            ReviewFilter = CatalogReviewFilter.All,
        };
        viewModel.Refresh(source, local, external);

        viewModel.ShowNeedsReviewCompleteState.Should().BeFalse();
    }

    [Fact]
    public void VersionBannerText_includes_usage_stats_below_version_summary()
    {
        var local = new List<GameInfo>
        {
            CreateApp("owner/a"),
            CreateApp("owner/b"),
            CreateApp("owner/c"),
        };
        var external = new List<GameInfo>
        {
            CreateApp("owner/a"),
            CreateApp("owner/b"),
            CreateApp("owner/c"),
            CreateApp("owner/d"),
        };
        var source = new AppCatalogSource
        {
            CachedListVersion = "1.0.0",
            AcknowledgedListVersion = null,
        };

        var viewModel = new CatalogSyncViewModel();
        viewModel.Refresh(source, local, external);

        viewModel.VersionBannerText.Should().Be(
            "List version: 1.0.0\nLast reviewed: not yet\nUsing 3/4 apps from this list");
    }

    [Fact]
    public void GetFilteredRows_applies_sort_mode()
    {
        var local = new List<GameInfo>
        {
            CreateApp("owner/zebra", "Zebra App"),
            CreateApp("owner/alpha", "Alpha App"),
        };
        var external = new List<GameInfo>
        {
            CreateApp("owner/zebra", "Zebra App"),
            CreateApp("owner/alpha", "Alpha App"),
        };
        var source = new AppCatalogSource { CachedListVersion = "1.0.0" };

        var viewModel = new CatalogSyncViewModel
        {
            ReviewFilter = CatalogReviewFilter.All,
            SortBy = "NameDesc",
        };
        viewModel.Refresh(source, local, external);

        viewModel.GetFilteredRows()
            .Select(r => r.DisplayName)
            .Should()
            .Equal("Zebra App", "Alpha App");
    }
}
