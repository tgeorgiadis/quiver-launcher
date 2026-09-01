using FluentAssertions;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class MobileImeOverlapTests
{
    [Fact]
    public void BottomPadding_is_zero_when_keyboard_closed()
    {
        MobileImeOverlap.BottomPadding(800, 0, 0, 2.75).Should().Be(0);
    }

    [Fact]
    public void BottomPadding_uses_overlap_in_dips()
    {
        MobileImeOverlap.BottomPadding(800, 500, 300, 1).Should().Be(300);
    }

    [Fact]
    public void BottomPadding_converts_pixel_occlusion_to_dips()
    {
        MobileImeOverlap.BottomPadding(800, 1375, 825, 2.75).Should().BeApproximately(300, 0.5);
    }

    [Fact]
    public void BottomPadding_is_zero_when_client_already_resized_above_keyboard()
    {
        MobileImeOverlap.BottomPadding(500, 500, 300, 1).Should().Be(0);
    }
}
