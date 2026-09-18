using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using FluentAssertions;
using QuiverLauncher.Views;

namespace QuiverLauncher.Tests;

public class MobileEdgeSwipeTests
{
    [AvaloniaTheory]
    [InlineData(8, 80, 4, true, PointerType.Touch, true)]
    [InlineData(40, 80, 0, true, PointerType.Touch, false)]
    [InlineData(8, 20, 0, true, PointerType.Touch, false)]
    [InlineData(8, 80, 60, true, PointerType.Touch, false)]
    [InlineData(8, -30, 0, true, PointerType.Touch, false)]
    [InlineData(8, 80, 0, false, PointerType.Touch, false)]
    [InlineData(8, 80, 0, true, PointerType.Mouse, false)]
    public void Only_deliberate_edge_touch_swipes_open_drawer(double x, double dx, double dy,
        bool allowed, PointerType type, bool expected)
    {
        var child = new Border { Background = Brushes.Transparent };
        var root = new Border { Child = child };
        var window = new Window { Content = root, Width = 400, Height = 600 };
        var opened = 0;
        using var swipe = new MobileEdgeSwipe(root, () => allowed, () => opened++);
        swipe.Attach();
        try
        {
            window.Show();
            var pointer = new Pointer(1, type, true);
            var start = new Point(x, 100);
            var end = new Point(x + dx, 100 + dy);
            root.RaiseEvent(new PointerPressedEventArgs(root, pointer, window, start, 0,
                new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None));
            // Buttons and scrollable content may own capture before the swipe is recognised.
            pointer.Capture(child);
            root.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, root, pointer, window, end, 1,
                new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.Other), KeyModifiers.None));
            root.RaiseEvent(new PointerReleasedEventArgs(root, pointer, window, end, 2,
                new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased), KeyModifiers.None, MouseButton.Left));
            opened.Should().Be(expected ? 1 : 0);
            if (expected) pointer.Captured.Should().BeNull();
            pointer.Capture(null);
        }
        finally { window.Close(); }
    }
}

