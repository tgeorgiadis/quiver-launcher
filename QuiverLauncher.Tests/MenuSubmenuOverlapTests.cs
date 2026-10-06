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
}
