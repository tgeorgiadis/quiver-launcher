using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using FluentAssertions;
using QuiverLauncher.Services;
using NavigationDirection = QuiverLauncher.Services.NavigationDirection;

namespace QuiverLauncher.Tests;

public class GamepadCardFocusSinkTests
{
    [AvaloniaFact]
    public void Park_moves_focus_off_continue_onto_non_tab_stop_sink()
    {
        var continueButton = new Button { Name = "Continue", Content = "Continue", Width = 160, Height = 40 };
        var sink = new Control { Width = 1, Height = 1 };
        var window = new Window
        {
            Width = 480,
            Height = 200,
            Content = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Children = { continueButton, sink },
            },
        };

        try
        {
            XyFocusNavigation.EnableOn(window);
            GamepadCardFocusSink.Configure(sink);
            window.Show();

            continueButton.Focus();
            continueButton.IsFocused.Should().BeTrue();

            GamepadCardFocusSink.Park(sink);

            KeyboardNavigation.GetIsTabStop(sink).Should().BeFalse();
            sink.IsFocused.Should().BeTrue();
            continueButton.IsFocused.Should().BeFalse();
        }
        finally
        {
            window.Close();
        }
    }

    // Same Park used after catalog review / app-updates / mods row selection.
    [AvaloniaFact]
    public void Park_then_arrow_does_not_move_focus_to_first_tab_stop()
    {
        var continueButton = new Button { Name = "Continue", Content = "Continue", Width = 160, Height = 40 };
        var sink = new Control { Width = 1, Height = 1 };
        var window = new Window
        {
            Width = 480,
            Height = 200,
            Content = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Children = { continueButton, sink },
            },
        };

        try
        {
            XyFocusNavigation.EnableOn(window);
            window.Show();
            GamepadCardFocusSink.Park(sink);
            sink.IsFocused.Should().BeTrue();

            XyFocusNavigation.TryMove(window, NavigationDirection.Down, window).Should().BeFalse();
            XyFocusNavigation.TryMove(window, NavigationDirection.Right, window).Should().BeFalse();

            var keyDown = new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Down,
                Source = sink,
            };
            window.RaiseEvent(keyDown);

            var focused = TopLevel.GetTopLevel(window)?.FocusManager?.GetFocusedElement();
            focused.Should().BeSameAs(sink);
            continueButton.IsFocused.Should().BeFalse();
        }
        finally
        {
            window.Close();
        }
    }
}
