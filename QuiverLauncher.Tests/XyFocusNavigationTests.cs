using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using FluentAssertions;
using QuiverLauncher.Services;
using NavigationDirection = QuiverLauncher.Services.NavigationDirection;

namespace QuiverLauncher.Tests;

public class XyFocusNavigationTests
{
    [Fact]
    public void ToAvalonia_maps_cardinal_directions()
    {
        XyFocusNavigation.ToAvalonia(NavigationDirection.Up).Should().Be(Avalonia.Input.NavigationDirection.Up);
        XyFocusNavigation.ToAvalonia(NavigationDirection.Down).Should().Be(Avalonia.Input.NavigationDirection.Down);
        XyFocusNavigation.ToAvalonia(NavigationDirection.Left).Should().Be(Avalonia.Input.NavigationDirection.Left);
        XyFocusNavigation.ToAvalonia(NavigationDirection.Right).Should().Be(Avalonia.Input.NavigationDirection.Right);
    }

    [AvaloniaFact]
    public void TryMove_moves_focus_between_buttons()
    {
        var left = new Button { Content = "Left", Width = 80, Height = 32 };
        var right = new Button { Content = "Right", Width = 80, Height = 32 };
        var window = new Window
        {
            Width = 320,
            Height = 120,
            Content = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Spacing = 16,
                Children = { left, right },
            },
        };

        try
        {
            XyFocusNavigation.EnableOn(window);
            XYFocus.SetRight(left, right);
            window.Show();
            left.Focus();
            left.IsFocused.Should().BeTrue();

            XyFocusNavigation.TryMove(window, NavigationDirection.Right, window).Should().BeTrue();
            right.IsFocused.Should().BeTrue();
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void TryMove_from_sidebar_button_follows_explicit_Right()
    {
        var sidebar = new Button { Name = "LibraryNav", Content = "Library", Width = 80, Height = 32 };
        var content = new Button { Name = "CheckUpdates", Content = "Check updates", Width = 120, Height = 32 };
        var window = new Window
        {
            Width = 420,
            Height = 120,
            Content = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Spacing = 24,
                Children = { sidebar, content },
            },
        };

        try
        {
            XyFocusNavigation.EnableOn(window);
            XYFocus.SetRight(sidebar, content);
            window.Show();
            sidebar.Focus();
            sidebar.IsFocused.Should().BeTrue();

            XyFocusNavigation.TryMove(window, NavigationDirection.Right, window).Should().BeTrue();
            content.IsFocused.Should().BeTrue();
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void FindNext_honors_explicit_XYFocus_Right()
    {
        var first = new Button { Name = "First", Content = "First", Width = 80, Height = 32 };
        var middle = new Button { Content = "Middle", Width = 80, Height = 32 };
        var last = new Button { Name = "Last", Content = "Last", Width = 80, Height = 32 };
        var window = new Window
        {
            Width = 420,
            Height = 120,
            Content = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Spacing = 16,
                Children = { first, middle, last },
            },
        };

        try
        {
            XyFocusNavigation.EnableOn(window);
            window.Show();
            XYFocus.SetRight(first, last);

            var next = XyFocusNavigation.FindNext(window, NavigationDirection.Right, window, first);
            next.Should().BeSameAs(last);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void EnableOn_does_not_enable_keyboard_xyfocus()
    {
        var window = new Window { Width = 200, Height = 80 };
        try
        {
            XyFocusNavigation.EnableOn(window);
            XYFocus.GetNavigationModes(window).Should().Be(
                XYFocusNavigationModes.Gamepad | XYFocusNavigationModes.Remote);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void EnableOnForDialog_enables_keyboard_xyfocus()
    {
        var window = new Window { Width = 200, Height = 80 };
        try
        {
            XyFocusNavigation.EnableOnForDialog(window);
            XYFocus.GetNavigationModes(window).Should().Be(XYFocusNavigationModes.Enabled);
        }
        finally
        {
            window.Close();
        }
    }
}
