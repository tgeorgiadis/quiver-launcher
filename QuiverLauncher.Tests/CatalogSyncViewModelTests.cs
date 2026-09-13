using FluentAssertions;
using QuiverLauncher;
using QuiverLauncher.Models;
using QuiverLauncher.Services;
using QuiverLauncher.ViewModels;

namespace QuiverLauncher.Tests;

public class CatalogSyncViewModelTests
{
    [Fact]
    public void Hidden_pending_count_respects_search_tags_and_partial_results()
    {
        var model = new CatalogSyncViewModel { ReviewFilter = CatalogReviewFilter.NeedsReview };
        model.Refresh(new() { CachedListVersion = "1" }, [], [
            new() { Name = "Alpha", Repository = "test/alpha", FolderName = "Alpha", Tags = ["alpha"] },
            new() { Name = "Beta", Repository = "test/beta", FolderName = "Beta", Tags = ["beta"] }]);
        model.SearchText = "Alpha";
        model.VisiblePendingReviewCount.Should().Be(1);
        model.FilteredOutPendingReviewCount.Should().Be(1);
        model.CycleTagChip("beta");
        model.VisiblePendingReviewCount.Should().Be(0);
        model.HiddenPendingReviewsText.Should().Be("2 reviews hidden by filters");
        model.ReviewFilter = CatalogReviewFilter.All;
        model.ShowHiddenPendingReviews.Should().BeFalse();
        model.RevealAllPendingReviews();
        model.ActiveTagChipCount.Should().Be(0);
        model.SearchText.Should().BeEmpty();
        model.GetFilteredRows().Should().HaveCount(2);
    }
    private static GameInfo CreateApp(string repository, string name = "Test App", string folderName = "TestFolder")
    {
        QuiverLauncher.Core.Services.CatalogPlatformIndex.Set("github", repository, null, null, new() { tag_name = "v1", assets = [new() { name = "app-Windows.zip" }] });
        return new()
        {
            Repository = repository,
            Name = name,
            FolderName = folderName,
        };
    }

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
    public void VersionBannerText_is_one_line_with_last_reviewed_in_tooltip()
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

        viewModel.VersionBannerText.Should().Be("1.0.0 · 3/4 apps in library");
        viewModel.VersionBannerCompactText.Should().Be("1.0.0 · 3/4 apps in library");
        viewModel.VersionLastReviewedText.Should().Be("Last reviewed: not yet");
        viewModel.VersionBannerTooltip.Should().Contain("Last reviewed: not yet");
        viewModel.VersionBannerTooltip.Should().Contain("Using 3/4 apps from this list");
        viewModel.ShowVersionBannerEmphasis.Should().BeTrue();
        viewModel.ShowVersionBanner.Should().BeTrue();
    }

    [Fact]
    public void VersionBanner_hides_emphasis_when_list_is_acknowledged_and_synced()
    {
        var local = new List<GameInfo> { CreateApp("owner/a") };
        var external = new List<GameInfo> { CreateApp("owner/a") };
        var source = new AppCatalogSource
        {
            CachedListVersion = "1.0.6",
            AcknowledgedListVersion = "1.0.6",
        };

        var viewModel = new CatalogSyncViewModel();
        viewModel.Refresh(source, local, external);

        viewModel.ShowVersionBannerEmphasis.Should().BeFalse();
        viewModel.ShowBulkAddButton.Should().BeFalse();
        viewModel.ShowBulkReplaceButton.Should().BeFalse();
        viewModel.ShowSkipReviewButton.Should().BeFalse();
    }

    [Fact]
    public void ShowBulkButtons_only_when_filtered_count_is_positive()
    {
        var local = new List<GameInfo> { CreateApp("owner/changed", "Old", "Folder") };
        var external = new List<GameInfo>
        {
            CreateApp("owner/changed", "New", "Folder"),
            CreateApp("owner/new", "New App", "NewFolder"),
        };
        var source = new AppCatalogSource { CachedListVersion = "1.0.0" };
        var viewModel = new CatalogSyncViewModel { ReviewFilter = CatalogReviewFilter.All };
        viewModel.Refresh(source, local, external);

        viewModel.ShowBulkAddButton.Should().BeTrue();
        viewModel.ShowBulkReplaceButton.Should().BeTrue();

        viewModel.ReviewFilter = CatalogReviewFilter.UpToDate;
        viewModel.ShowBulkAddButton.Should().BeFalse();
        viewModel.ShowBulkReplaceButton.Should().BeFalse();
    }

    [Fact]
    public void ShowSkipReviewButton_hides_on_up_to_date_and_hidden_filters()
    {
        var local = new List<GameInfo> { CreateApp("owner/changed", "Old", "Folder") };
        var external = new List<GameInfo>
        {
            CreateApp("owner/changed", "New", "Folder"),
            CreateApp("owner/new", "New App", "NewFolder"),
        };
        var source = new AppCatalogSource { CachedListVersion = "1.0.0" };
        var viewModel = new CatalogSyncViewModel { ReviewFilter = CatalogReviewFilter.NeedsReview };
        viewModel.Refresh(source, local, external);

        viewModel.ShowSkipReviewButton.Should().BeTrue();

        viewModel.ReviewFilter = CatalogReviewFilter.All;
        viewModel.ShowSkipReviewButton.Should().BeTrue();

        viewModel.ReviewFilter = CatalogReviewFilter.UpToDate;
        viewModel.ShowSkipReviewButton.Should().BeFalse();

        viewModel.ReviewFilter = CatalogReviewFilter.Hidden;
        viewModel.ShowSkipReviewButton.Should().BeFalse();
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

    [Fact]
    public void FilteredBulkCounts_match_global_when_no_tag_filter()
    {
        var local = new List<GameInfo>
        {
            CreateApp("owner/changed", "Changed", "ChangedFolder"),
        };
        local[0].Tags = ["n64"];
        var external = new List<GameInfo>
        {
            CreateApp("owner/changed", "Changed Updated", "ChangedFolder"),
            CreateApp("owner/new-a", "New A", "NewA"),
            CreateApp("owner/new-b", "New B", "NewB"),
        };
        external[0].Tags = ["n64"];
        external[1].Tags = ["n64"];
        external[2].Tags = ["pc"];

        var source = new AppCatalogSource { CachedListVersion = "1.0.0" };
        var viewModel = new CatalogSyncViewModel { ReviewFilter = CatalogReviewFilter.All };
        viewModel.Refresh(source, local, external);

        viewModel.FilteredBulkAddCount.Should().Be(viewModel.ExternalOnlyCount).And.Be(2);
        viewModel.FilteredBulkReplaceCount.Should().Be(viewModel.ChangedCount).And.Be(1);
    }

    [Fact]
    public void FilteredBulkAddCount_excludes_folder_collision_rows()
    {
        var local = new List<GameInfo>
        {
            CreateApp("owner/existing", "Existing App", "SharedFolder"),
        };
        var external = new List<GameInfo>
        {
            CreateApp("owner/existing", "Existing App", "SharedFolder"),
            CreateApp("owner/new", "New App", "SharedFolder"),
            CreateApp("owner/other", "Other App", "OtherFolder"),
        };

        var source = new AppCatalogSource { CachedListVersion = "1.0.0" };
        var viewModel = new CatalogSyncViewModel { ReviewFilter = CatalogReviewFilter.All };
        viewModel.Refresh(source, local, external);

        viewModel.ExternalOnlyCount.Should().Be(2);
        viewModel.FilteredBulkAddCount.Should().Be(1);
        viewModel.GetFilteredBulkAddRows().Select(r => r.Repository).Should().Equal("owner/other");
        viewModel.GetFilteredBlockedAddRows().Select(r => r.Repository).Should().Equal("owner/new");
    }

    [Fact]
    public void FilteredBulkCounts_respect_tag_chip_include_filter()
    {
        var local = new List<GameInfo>
        {
            CreateApp("owner/changed-n64", "Changed N64", "ChangedN64"),
            CreateApp("owner/changed-pc", "Changed PC", "ChangedPc"),
        };
        local[0].Tags = ["n64"];
        local[1].Tags = ["pc"];

        var external = new List<GameInfo>
        {
            CreateApp("owner/changed-n64", "Changed N64 Updated", "ChangedN64"),
            CreateApp("owner/changed-pc", "Changed PC Updated", "ChangedPc"),
            CreateApp("owner/new-n64", "New N64", "NewN64"),
            CreateApp("owner/new-pc", "New PC", "NewPc"),
            CreateApp("owner/new-other", "New Other", "NewOther"),
        };
        external[0].Tags = ["n64"];
        external[1].Tags = ["pc"];
        external[2].Tags = ["n64"];
        external[3].Tags = ["pc"];
        external[4].Tags = ["other"];

        var source = new AppCatalogSource
        {
            CachedListVersion = "1.0.0",
            FeaturedTags = ["n64", "pc", "other"],
        };
        var viewModel = new CatalogSyncViewModel { ReviewFilter = CatalogReviewFilter.All };
        viewModel.Refresh(source, local, external);

        viewModel.ExternalOnlyCount.Should().Be(3);
        viewModel.ChangedCount.Should().Be(2);

        viewModel.CycleTagChip("n64"); // Neutral -> Include

        viewModel.FilteredBulkAddCount.Should().Be(1);
        viewModel.GetFilteredBulkAddRows().Select(r => r.Repository).Should().Equal("owner/new-n64");
        viewModel.FilteredBulkReplaceCount.Should().Be(1);
        viewModel.GetFilteredBulkReplaceRows().Select(r => r.Repository).Should().Equal("owner/changed-n64");
    }

    [Fact]
    public void RefreshTagChips_prefers_preferredTagFilters_over_featured_and_frequency()
    {
        var external = new List<GameInfo>
        {
            CreateApp("owner/a", "A", "A"),
            CreateApp("owner/b", "B", "B"),
            CreateApp("owner/c", "C", "C"),
        };
        external[0].Tags = ["n64", "recomp"];
        external[1].Tags = ["n64", "recomp"];
        external[2].Tags = ["n64", "translation"];

        var source = new AppCatalogSource
        {
            CachedListVersion = "1.0.0",
            FeaturedTags = ["recomp"],
            PreferredTagFilters = ["translation", "recomp"],
        };
        var viewModel = new CatalogSyncViewModel();
        viewModel.Refresh(source, [], external);

        viewModel.TagChips.Select(c => c.Tag).Should().StartWith(["recomp", "translation"]);
    }

    [Fact]
    public void RefreshTagChips_falls_back_to_featuredTags_when_preferred_empty()
    {
        var external = new List<GameInfo>
        {
            CreateApp("owner/a", "A", "A"),
            CreateApp("owner/b", "B", "B"),
        };
        external[0].Tags = ["n64", "recomp"];
        external[1].Tags = ["n64"];

        var source = new AppCatalogSource
        {
            CachedListVersion = "1.0.0",
            FeaturedTags = ["recomp"],
        };
        var viewModel = new CatalogSyncViewModel();
        viewModel.Refresh(source, [], external);

        viewModel.TagChips.Select(c => c.Tag).Should().StartWith("recomp");
    }

    [Fact]
    public void RefreshTagChips_omits_hiddenTagFilters()
    {
        var external = new List<GameInfo>
        {
            CreateApp("owner/a", "A", "A"),
            CreateApp("owner/b", "B", "B"),
        };
        external[0].Tags = ["n64", "recomp"];
        external[1].Tags = ["n64", "recomp"];

        var source = new AppCatalogSource
        {
            CachedListVersion = "1.0.0",
            FeaturedTags = ["n64"],
            PreferredTagFilters = ["n64"],
            HiddenTagFilters = ["n64"],
        };
        var settings = new AppSettings { PinnedFilterTags = ["n64"] };
        var viewModel = new CatalogSyncViewModel();
        viewModel.Refresh(source, [], external, settings);

        viewModel.TagChips.Select(c => c.Tag).Should().NotContain("n64");
        viewModel.TagChips.Select(c => c.Tag).Should().Contain("recomp");
    }

    [Fact]
    public void SearchText_narrows_filtered_rows_and_bulk_counts_with_tag_chip()
    {
        var local = new List<GameInfo>
        {
            CreateApp("owner/changed-n64", "Changed N64", "ChangedN64"),
            CreateApp("owner/changed-pc", "Changed PC", "ChangedPc"),
        };
        local[0].Tags = ["n64"];
        local[1].Tags = ["pc"];

        var external = new List<GameInfo>
        {
            CreateApp("owner/changed-n64", "Changed N64 Updated", "ChangedN64"),
            CreateApp("owner/changed-pc", "Changed PC Updated", "ChangedPc"),
            CreateApp("owner/new-n64", "New N64", "NewN64"),
            CreateApp("owner/new-pc", "New PC", "NewPc"),
            CreateApp("owner/new-other", "New Other", "NewOther"),
        };
        external[0].Tags = ["n64"];
        external[1].Tags = ["pc"];
        external[2].Tags = ["n64"];
        external[3].Tags = ["pc"];
        external[4].Tags = ["other"];

        var source = new AppCatalogSource
        {
            CachedListVersion = "1.0.0",
            FeaturedTags = ["n64", "pc", "other"],
        };
        var viewModel = new CatalogSyncViewModel { ReviewFilter = CatalogReviewFilter.All };
        viewModel.Refresh(source, local, external);
        viewModel.CycleTagChip("n64");
        viewModel.SearchText = "new";

        viewModel.GetFilteredRows().Select(r => r.Repository).Should().Equal("owner/new-n64");
        viewModel.FilteredBulkAddCount.Should().Be(1);
        viewModel.GetFilteredBulkAddRows().Select(r => r.Repository).Should().Equal("owner/new-n64");
        viewModel.FilteredBulkReplaceCount.Should().Be(0);
    }

    [Fact]
    public void CatalogReviewLayout_shows_grid_or_list_from_setting()
    {
        QuiverLauncher.Views.CatalogReviewView.ShouldShowCatalogReviewGrid(true).Should().BeTrue();
        QuiverLauncher.Views.CatalogReviewView.ShouldShowCatalogReviewList(true).Should().BeFalse();
        QuiverLauncher.Views.CatalogReviewView.ShouldShowCatalogReviewGrid(false).Should().BeFalse();
        QuiverLauncher.Views.CatalogReviewView.ShouldShowCatalogReviewList(false).Should().BeTrue();
        QuiverLauncher.Views.CatalogReviewView.ShouldShowCatalogReviewHelpLines(true).Should().BeFalse();
        QuiverLauncher.Views.CatalogReviewView.ShouldShowCatalogReviewHelpLines(false).Should().BeFalse();
        QuiverLauncher.Views.CatalogDetailsView.ShouldShowCatalogReviewOpenRepo("owner/repo").Should().BeTrue();
        QuiverLauncher.Views.CatalogDetailsView.ShouldShowCatalogReviewOpenRepo("").Should().BeFalse();
        QuiverLauncher.Views.CatalogDetailsView.ShouldShowCatalogReviewOpenRepo(null).Should().BeFalse();
    }

    [Fact]
    public void CatalogReviewDetails_open_close_does_not_change_filter_or_sort()
    {
        var viewModel = new CatalogSyncViewModel
        {
            ReviewFilter = CatalogReviewFilter.Changed,
            SortBy = "Repository",
        };

        var filter = viewModel.ReviewFilter;
        var sort = viewModel.SortBy;
        var overlayOpen = true;
        string? overlayKey = "owner/app";

        overlayOpen = false;
        overlayKey = null;

        overlayOpen.Should().BeFalse();
        overlayKey.Should().BeNull();
        viewModel.ReviewFilter.Should().Be(filter);
        viewModel.SortBy.Should().Be(sort);
        viewModel.ReviewFilter.Should().Be(CatalogReviewFilter.Changed);
        viewModel.SortBy.Should().Be("Repository");
    }
}
