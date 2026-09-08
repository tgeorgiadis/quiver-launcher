using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using FluentAssertions;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class GamepadMenuFlyoutNavigationTests
{
    [Fact]
    public void MoveItemIndex_moves_and_wraps()
    {
        GamepadMenuFlyoutNavigation.MoveItemIndex(0, NavigationDirection.Down, 3).Should().Be(1);
        GamepadMenuFlyoutNavigation.MoveItemIndex(2, NavigationDirection.Down, 3).Should().Be(0);
        GamepadMenuFlyoutNavigation.MoveItemIndex(0, NavigationDirection.Up, 3).Should().Be(2);
    }

    [Fact]
    public void FindPreferredItemIndex_prefers_bold_item()
    {
        var items = new List<MenuItem>
        {
            new() { Header = "All platforms" },
            new() { Header = "Windows", FontWeight = FontWeight.SemiBold },
            new() { Header = "Linux" },
        };

        GamepadMenuFlyoutNavigation.FindPreferredItemIndex(items).Should().Be(1);
    }

    [AvaloniaFact]
    public void Toggle_opens_and_registers_flyout()
    {
        var flyout = CreatePlatformFlyout();
        var button = new Button { Content = "Platforms", Flyout = flyout };
        var window = new Window { Content = button, Width = 280, Height = 160 };

        try
        {
            GamepadMenuFlyoutNavigation.Instance.Close(flyout);
            window.Show();

            GamepadMenuFlyoutNavigation.Toggle(button);

            flyout.IsOpen.Should().BeTrue();
            GamepadMenuFlyoutNavigation.Instance.HasActiveMenuFlyout.Should().BeTrue();
        }
        finally
        {
            if (flyout.IsOpen)
                flyout.Hide();
            GamepadMenuFlyoutNavigation.Instance.Close(flyout);
            window.Close();
        }
    }

    [AvaloniaFact]
    public void TryHandleConfirm_activates_focused_item()
    {
        var clicked = false;
        var windows = new MenuItem { Header = "Windows" };
        windows.Click += (_, _) => clicked = true;
        var flyout = new MenuFlyout { Items = { windows, new MenuItem { Header = "Linux" } } };
        var button = new Button { Content = "Platforms", Flyout = flyout };
        var window = new Window { Content = button, Width = 280, Height = 160 };

        try
        {
            window.Show();
            GamepadMenuFlyoutNavigation.Toggle(button);
            GamepadMenuFlyoutNavigation.Instance.HasActiveMenuFlyout.Should().BeTrue();

            GamepadMenuFlyoutNavigation.Instance.TryHandleConfirm().Should().BeTrue();

            clicked.Should().BeTrue();
            flyout.IsOpen.Should().BeFalse();
            GamepadMenuFlyoutNavigation.Instance.HasActiveMenuFlyout.Should().BeFalse();
        }
        finally
        {
            if (flyout.IsOpen)
                flyout.Hide();
            GamepadMenuFlyoutNavigation.Instance.Close(flyout);
            window.Close();
        }
    }

    private static MenuFlyout CreatePlatformFlyout() =>
        new()
        {
            Items =
            {
                new MenuItem { Header = "All platforms" },
                new MenuItem { Header = "Windows" },
            },
        };
}
