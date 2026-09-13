using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FluentAssertions;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class MobileInsetsControllerTests
{
    [AvaloniaFact]
    public void Rotation_reapplies_side_insets_and_final_disposal_blocks_updates()
    {
        var root = new UserControl();
        root.Measure(new Size(400, 800));
        root.Arrange(new Rect(0, 0, 400, 800));
        var layouts = 0;
        var controller = new MobileInsetsController(root, () => layouts++);
        controller.ApplySafeAreaPadding(new Thickness(30, 24, 40, 20));
        root.Padding.Should().Be(new Thickness(0, 24, 0, 20));
        controller.Detach();
        controller.Attach();
        root.Measure(new Size(800, 400));
        root.Arrange(new Rect(0, 0, 800, 400));
        controller.Refresh();
        root.Padding.Should().Be(new Thickness(30, 24, 40, 20));
        controller.Dispose();
        controller.Dispose();
        controller.ApplySafeAreaPadding(new Thickness(200));
        root.Padding.Should().Be(new Thickness(30, 24, 40, 20));
        layouts.Should().Be(2);
    }
}
