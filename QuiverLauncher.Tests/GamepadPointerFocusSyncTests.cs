using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using FluentAssertions;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class GamepadPointerFocusSyncTests
{
    private sealed class CardItem
    {
        public string Name { get; init; } = "";
    }

    [AvaloniaFact]
    public void FindDataContextInAncestors_resolves_nested_button_to_host_item()
    {
        var item = new CardItem { Name = "Zelda" };
        var play = new Button { Content = "Play" };
        var host = new Border { DataContext = item, Child = play };
        var window = new Window { Content = host, Width = 240, Height = 120 };

        try
        {
            window.Show();
            window.UpdateLayout();

            GamepadPointerFocusSync.FindDataContextInAncestors<CardItem>(play)
                .Should().BeSameAs(item);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void IndexOfDataContext_maps_nested_click_to_list_index()
    {
        var first = new CardItem { Name = "A" };
        var second = new CardItem { Name = "B" };
        var play = new Button { Content = "Play" };
        var host = new Border { DataContext = second, Child = play };
        var window = new Window { Content = host, Width = 240, Height = 120 };

        try
        {
            window.Show();
            window.UpdateLayout();

            GamepadPointerFocusSync.IndexOfDataContext(
                    new[] { first, second },
                    play)
                .Should().Be(1);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void FindDataContextInAncestors_null_when_missing()
    {
        GamepadPointerFocusSync.FindDataContextInAncestors<CardItem>(null)
            .Should().BeNull();
        GamepadPointerFocusSync.IndexOfDataContext(new[] { new CardItem() }, null)
            .Should().Be(-1);
    }

    [AvaloniaFact]
    public void ApplyCardSelectionFocus_stealFocus_false_does_not_park()
    {
        var play = new Button { Content = "Play", Width = 80, Height = 32, Focusable = true };
        var sink = new Control { Width = 1, Height = 1 };
        var window = new Window
        {
            Width = 320,
            Height = 120,
            Content = new StackPanel { Children = { play, sink } },
        };

        try
        {
            window.Show();
            play.Focus();
            play.IsFocused.Should().BeTrue();

            GamepadPointerFocusSync.ApplyCardSelectionFocus(sink, stealFocus: false);

            play.IsFocused.Should().BeTrue();
            sink.IsFocused.Should().BeFalse();
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ApplyCardSelectionFocus_stealFocus_true_parks_on_sink()
    {
        var play = new Button { Content = "Play", Width = 80, Height = 32, Focusable = true };
        var sink = new Control { Width = 1, Height = 1 };
        var window = new Window
        {
            Width = 320,
            Height = 120,
            Content = new StackPanel { Children = { play, sink } },
        };

        try
        {
            window.Show();
            play.Focus();
            play.IsFocused.Should().BeTrue();

            GamepadPointerFocusSync.ApplyCardSelectionFocus(sink, stealFocus: true);

            sink.IsFocused.Should().BeTrue();
            play.IsFocused.Should().BeFalse();
            KeyboardNavigation.GetIsTabStop(sink).Should().BeFalse();
        }
        finally
        {
            GamepadTextInput.Reset();
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Moving_gamepad_focused_class_leaves_only_the_clicked_control_ringed()
    {
        var first = new Button { Content = "Search" };
        var second = new Button { Content = "Settings" };
        first.Classes.Set("gamepad-focused", true);

        first.Classes.Contains("gamepad-focused").Should().BeTrue();

        first.Classes.Set("gamepad-focused", false);
        second.Classes.Set("gamepad-focused", true);

        first.Classes.Contains("gamepad-focused").Should().BeFalse();
        second.Classes.Contains("gamepad-focused").Should().BeTrue();
    }
}
