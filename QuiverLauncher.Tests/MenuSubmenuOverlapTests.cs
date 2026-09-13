using FluentAssertions;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class MenuSubmenuOverlapTests
{
    [Theory]
    [InlineData(1)] [InlineData(1.5)] [InlineData(2)]
    public void Actual_gap_correction_is_scaled_and_stable_on_both_sides(double scale)
    {
        var right = MenuSubmenuOverlap.CorrectHorizontalOffset(0, 100 * scale, 200 * scale, 210 * scale, 310 * scale, scale);
        right.Should().Be(-15);
        MenuSubmenuOverlap.CorrectHorizontalOffset(right, 100 * scale, 200 * scale, 195 * scale, 295 * scale, scale).Should().Be(right);
        var left = MenuSubmenuOverlap.CorrectHorizontalOffset(0, 100 * scale, 200 * scale, -10 * scale, 90 * scale, scale);
        left.Should().Be(15);
        MenuSubmenuOverlap.CorrectHorizontalOffset(left, 100 * scale, 200 * scale, 5 * scale, 105 * scale, scale).Should().Be(left);
    }

    [Fact]
    public void ChooseHorizontalOffset_pulls_left_submenu_toward_parent()
    {
        MenuSubmenuOverlap.ChooseHorizontalOffset(
            parentLeft: 100,
            parentRight: 200,
            popupLeft: 0,
            popupRight: 90,
            overlapPixels: 8).Should().Be(8);
    }

    [Fact]
    public void ChooseHorizontalOffset_pulls_right_submenu_toward_parent()
    {
        MenuSubmenuOverlap.ChooseHorizontalOffset(
            parentLeft: 100,
            parentRight: 200,
            popupLeft: 210,
            popupRight: 300,
            overlapPixels: 8).Should().Be(-8);
    }

    [Fact]
    public void ChooseHorizontalOffset_uses_centers_when_already_overlapping_on_the_left()
    {
        MenuSubmenuOverlap.ChooseHorizontalOffset(
            parentLeft: 100,
            parentRight: 200,
            popupLeft: 20,
            popupRight: 110,
            overlapPixels: 8).Should().Be(8);
    }

    [Fact]
    public void ChooseHorizontalOffset_uses_centers_when_already_overlapping_on_the_right()
    {
        MenuSubmenuOverlap.ChooseHorizontalOffset(
            parentLeft: 100,
            parentRight: 200,
            popupLeft: 190,
            popupRight: 280,
            overlapPixels: 8).Should().Be(-8);
    }

    [Fact]
    public void ShouldOpenToTheLeft_when_submenu_would_pass_the_window_edge()
    {
        MenuSubmenuOverlap.ShouldOpenToTheLeft(
            itemRight: 900,
            popupWidth: 180,
            windowRight: 1024).Should().BeTrue();
    }

    [Fact]
    public void ShouldOpenToTheLeft_when_submenu_fits_on_the_right()
    {
        MenuSubmenuOverlap.ShouldOpenToTheLeft(
            itemRight: 400,
            popupWidth: 180,
            windowRight: 1024).Should().BeFalse();
    }

    [Fact]
    public void ShouldOpenToTheLeft_when_submenu_exactly_fits()
    {
        MenuSubmenuOverlap.ShouldOpenToTheLeft(
            itemRight: 844,
            popupWidth: 180,
            windowRight: 1024).Should().BeFalse();
    }
}
