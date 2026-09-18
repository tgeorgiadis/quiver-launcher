using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using FluentAssertions;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class MouseWheelScrollTests
{
    [AvaloniaTheory]
    [InlineData(1, -1, 50)]
    [InlineData(2, -1, 100)]
    [InlineData(3, -1, 150)]
    [InlineData(5, -1, 250)]
    [InlineData(3, -0.25, 12.5)]
    public void Wheel_speed_scales_ticks_but_preserves_smooth_input(int speed, double delta, double expected)
    {
        var scroll = new ScrollViewer
        {
            Content = new Border { Height = 2000, Background = Brushes.Transparent },
        };
        MouseWheelScroll.SetMultiplier(scroll, speed);
        var window = new Window { Content = scroll, Width = 400, Height = 300 };
        try
        {
            window.Show();
            window.UpdateLayout();
            window.MouseWheel(new Point(50, 50), new Vector(0, delta));
            Dispatcher.UIThread.RunJobs();
            scroll.Offset.Y.Should().BeApproximately(expected, 0.01);
            // Changing the setting takes effect on the next tick without accumulating handlers.
            MouseWheelScroll.SetMultiplier(scroll, 1);
            window.MouseWheel(new Point(50, 50), new Vector(0, -1));
            Dispatcher.UIThread.RunJobs();
            scroll.Offset.Y.Should().BeApproximately(expected + 50, 0.01);
        }
        finally { window.Close(); }
    }
}
