using FluentAssertions;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class CatalogReviewFilterGamepadLayoutTests
{
    [Theory]
    [InlineData(7, 4, 2)]
    [InlineData(7, 0, 2)]
    [InlineData(7, 4, 0)]
    [InlineData(7, 0, 0)]
    [InlineData(0, 0, 0)]
    public void Notice_is_a_separate_last_row_and_first_stop_from_apps(int status, int tags, int bulk)
    {
        var ranges = CatalogReviewFilterGamepadLayout.FromCounts(status, tags, bulk, 1);
        ranges.PreferredIndexFromList.Should().Be(ranges.NoticeStart);
        ranges.ResolveRow(ranges.NoticeStart).Should().Be(CatalogReviewFilterGamepadLayout.Row.Notice);
        ranges.LocalIndex(ranges.NoticeStart).Should().Be(0);
        ranges.RowCount(CatalogReviewFilterGamepadLayout.Row.Notice).Should().Be(1);
        ranges.AbsoluteIndex(CatalogReviewFilterGamepadLayout.Row.Notice, 0).Should().Be(status + tags + bulk);
        var withoutNotice = CatalogReviewFilterGamepadLayout.FromCounts(status, tags, bulk);
        withoutNotice.HasNotice.Should().BeFalse();
        withoutNotice.Total.Should().Be(ranges.Total - 1);
    }

    [Fact]
    public void Ranges_preferred_index_from_list_prefers_bulk()
    {
        var ranges = CatalogReviewFilterGamepadLayout.FromCounts(statusCount: 7, tagCount: 4, bulkCount: 2);

        ranges.PreferredIndexFromList.Should().Be(ranges.BulkStart);
        ranges.TagStart.Should().Be(7);
        ranges.BulkStart.Should().Be(11);
        ranges.Total.Should().Be(13);
    }

    [Fact]
    public void Ranges_preferred_index_from_list_falls_back_to_bulk_without_tags()
    {
        var ranges = CatalogReviewFilterGamepadLayout.FromCounts(statusCount: 7, tagCount: 0, bulkCount: 2);

        ranges.PreferredIndexFromList.Should().Be(ranges.BulkStart);
        ranges.BulkStart.Should().Be(7);
    }

    [Fact]
    public void Ranges_preferred_index_from_list_uses_status_when_tags_and_bulk_are_hidden()
    {
        var ranges = CatalogReviewFilterGamepadLayout.FromCounts(statusCount: 8, tagCount: 0, bulkCount: 0);

        ranges.PreferredIndexFromList.Should().Be(0);
        ranges.HasTags.Should().BeFalse();
        ranges.HasBulk.Should().BeFalse();
    }

    [Fact]
    public void ResolveRow_splits_status_tags_bulk()
    {
        var ranges = CatalogReviewFilterGamepadLayout.FromCounts(3, 2, 2);

        ranges.ResolveRow(0).Should().Be(CatalogReviewFilterGamepadLayout.Row.Status);
        ranges.ResolveRow(2).Should().Be(CatalogReviewFilterGamepadLayout.Row.Status);
        ranges.ResolveRow(3).Should().Be(CatalogReviewFilterGamepadLayout.Row.Tags);
        ranges.ResolveRow(4).Should().Be(CatalogReviewFilterGamepadLayout.Row.Tags);
        ranges.ResolveRow(5).Should().Be(CatalogReviewFilterGamepadLayout.Row.Bulk);
    }

    [Fact]
    public void MoveHorizontalClamped_stops_at_ends()
    {
        CatalogReviewFilterGamepadLayout.MoveHorizontalClamped(0, NavigationDirection.Left, 4)
            .Should().Be(0);
        CatalogReviewFilterGamepadLayout.MoveHorizontalClamped(3, NavigationDirection.Right, 4)
            .Should().Be(3);
        CatalogReviewFilterGamepadLayout.MoveHorizontalClamped(1, NavigationDirection.Right, 4)
            .Should().Be(2);
    }

    [Fact]
    public void ShouldLeaveToSidebarOnLeft_only_at_first_item()
    {
        CatalogReviewFilterGamepadLayout.ShouldLeaveToSidebarOnLeft(0, NavigationDirection.Left)
            .Should().BeTrue();
        CatalogReviewFilterGamepadLayout.ShouldLeaveToSidebarOnLeft(1, NavigationDirection.Left)
            .Should().BeFalse();
        CatalogReviewFilterGamepadLayout.ShouldLeaveToSidebarOnLeft(0, NavigationDirection.Right)
            .Should().BeFalse();
    }

    [Fact]
    public void Status_down_lands_on_tag_start()
    {
        var ranges = CatalogReviewFilterGamepadLayout.FromCounts(7, 3, 2);
        var statusIndex = 2;

        ranges.ResolveRow(statusIndex).Should().Be(CatalogReviewFilterGamepadLayout.Row.Status);
        // Down from status with tags → first tag
        ranges.HasTags.Should().BeTrue();
        ranges.TagStart.Should().Be(7);
    }

    [Fact]
    public void ShouldKeepFilterFocusAfterLayoutChange_only_on_filter_strip()
    {
        CatalogReviewFilterGamepadLayout.ShouldKeepFilterFocusAfterLayoutChange(
                GamepadNavigationZone.CatalogReviewFilters)
            .Should().BeTrue();
        CatalogReviewFilterGamepadLayout.ShouldKeepFilterFocusAfterLayoutChange(
                GamepadNavigationZone.CatalogReviewList)
            .Should().BeFalse();
    }
}
