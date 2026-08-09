using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using FluentAssertions;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class TagEditOverlayLayoutTests
{
    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 1)]
    public void GetInitialFocusIndex_matches_gaming_mode(bool isGamingMode, int expected)
    {
        TagEditOverlayLayout.GetInitialFocusIndex(isGamingMode).Should().Be(expected);
    }

    [Fact]
    public void ApplyDialogPlacement_pins_top_in_gaming_mode()
    {
        var panel = new Border();

        TagEditOverlayLayout.ApplyDialogPlacement(panel, isGamingMode: true);

        panel.VerticalAlignment.Should().Be(VerticalAlignment.Top);
        panel.Margin.Should().Be(TagEditOverlayLayout.GamingModeMargin);
    }

    [Fact]
    public void ApplyDialogPlacement_centers_when_not_gaming_mode()
    {
        var panel = new Border
        {
            VerticalAlignment = VerticalAlignment.Top,
            Margin = TagEditOverlayLayout.GamingModeMargin,
        };

        TagEditOverlayLayout.ApplyDialogPlacement(panel, isGamingMode: false);

        panel.VerticalAlignment.Should().Be(VerticalAlignment.Center);
        panel.Margin.Should().Be(default(Thickness));
    }
}
