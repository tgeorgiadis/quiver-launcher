using FluentAssertions;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class CatalogReviewGridLayoutTests
{
    [Theory]
    [InlineData(360, false, 2)]
    [InlineData(400, false, 2)]
    [InlineData(320, false, 2)]
    [InlineData(300, false, 2)]
    [InlineData(280, false, 2)]
    [InlineData(270, false, 1)]
    [InlineData(200, false, 1)]
    public void Portrait_fills_two_columns_when_the_viewport_is_wide_enough(
        double width, bool landscape, int expected)
    {
        CatalogReviewGridLayout.GetColumns(width, landscape).Should().Be(expected);
    }

    [Theory]
    [InlineData(640, true, 3)]
    [InlineData(800, true, 4)]
    [InlineData(500, true, 2)]
    [InlineData(300, true, 1)]
    public void Landscape_uses_as_many_columns_as_fit(double width, bool landscape, int expected)
    {
        CatalogReviewGridLayout.GetColumns(width, landscape).Should().Be(expected);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    public void Invalid_viewport_falls_back_to_one_column(double width)
    {
        CatalogReviewGridLayout.GetColumns(width, landscape: false).Should().Be(1);
    }

    [Fact]
    public void Portrait_min_card_width_allows_two_columns_on_narrow_phones()
    {
        CatalogReviewGridLayout.PortraitMinCardWidth.Should().Be(128);
        CatalogReviewGridLayout.GetColumns(320, landscape: false).Should().Be(2);
    }

    [Fact]
    public void Portrait_never_exceeds_two_columns()
    {
        CatalogReviewGridLayout.GetColumns(1200, landscape: false).Should().Be(2);
    }

    [Fact]
    public void Portrait_card_width_fills_two_columns_without_overflow()
    {
        const double viewport = 360;
        var width = CatalogReviewGridLayout.GetCardWidth(viewport, landscape: false);
        width.Should().Be(172);
        var columns = CatalogReviewGridLayout.GetColumns(viewport, landscape: false);
        (columns * (width + CatalogReviewGridLayout.CardGutter)).Should().Be(viewport);
    }

    [Fact]
    public void Invalid_viewport_card_width_is_at_least_one()
    {
        CatalogReviewGridLayout.GetCardWidth(0, landscape: false).Should().Be(1);
    }
}
