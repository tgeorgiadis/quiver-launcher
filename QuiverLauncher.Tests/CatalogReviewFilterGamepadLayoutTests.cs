using FluentAssertions;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class CatalogReviewFilterGamepadLayoutTests
{
    [Fact]
    public void Ranges_preferred_index_from_list_prefers_tags()
    {
        var ranges = CatalogReviewFilterGamepadLayout.FromCounts(statusCount: 7, tagCount: 4, bulkCount: 2);

        ranges.PreferredIndexFromList.Should().Be(ranges.TagStart);
        ranges.TagStart.Should().Be(7);
        ranges.BulkStart.Should().Be(11);
        ranges.Total.Should().Be(13);
    }

    [Fact]
    public void Ranges_preferred_index_from_list_falls_back_to_status_without_tags()
    {
        var ranges = CatalogReviewFilterGamepadLayout.FromCounts(statusCount: 7, tagCount: 0, bulkCount: 2);

        ranges.PreferredIndexFromList.Should().Be(0);
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
}
