using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using FluentAssertions;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class GamepadTextInputTests
{
    [AvaloniaFact]
    public void Highlight_focuses_when_chrome_active_outside_gaming_mode()
    {
        var box = new TextBox { Text = "hello", Focusable = true, IsEnabled = true };
        var window = new Window { Content = box, Width = 240, Height = 120 };

        try
        {
            GamepadTextInput.SkipNativeFocusOverride = () => false;
            GamepadFocusChrome.SetActive(true, window);
            window.Show();

            GamepadTextInput.Highlight(box);

            box.IsFocused.Should().BeTrue();
            GamepadTextInput.IsEditing.Should().BeFalse();
            GamepadTextInput.ShouldOpenSteamOskOnGotFocus.Should().BeFalse();
            GamepadTextInput.Active.Should().BeSameAs(box);
            box.IsReadOnly.Should().BeTrue();
            box.Classes.Contains("gamepad-focused").Should().BeTrue();
        }
        finally
        {
            GamepadTextInput.Reset();
            GamepadFocusChrome.SetActive(false);
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Highlight_does_not_focus_in_gaming_mode()
    {
        var other = new Button { Content = "other", Focusable = true };
        var box = new TextBox { Text = "hello", Focusable = true, IsEnabled = true };
        var window = new Window
        {
            Width = 240,
            Height = 160,
            Content = new StackPanel { Children = { other, box } },
        };

        try
        {
            GamepadTextInput.SkipNativeFocusOverride = () => true;
            GamepadFocusChrome.SetActive(true, window);
            window.Show();
            other.Focus();
            other.IsFocused.Should().BeTrue();

            GamepadTextInput.Highlight(box);

            box.IsFocused.Should().BeFalse();
            other.IsFocused.Should().BeTrue();
            GamepadTextInput.IsEditing.Should().BeFalse();
            GamepadTextInput.ShouldOpenSteamOskOnGotFocus.Should().BeFalse();
            GamepadTextInput.Active.Should().BeSameAs(box);
            box.IsReadOnly.Should().BeTrue();
            box.Classes.Contains("gamepad-focused").Should().BeTrue();
        }
        finally
        {
            GamepadTextInput.Reset();
            GamepadFocusChrome.SetActive(false);
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Highlight_focuses_without_entering_edit_when_chrome_inactive()
    {
        var box = new TextBox { Text = "hello", Focusable = true, IsEnabled = true };
        var window = new Window { Content = box, Width = 240, Height = 120 };

        try
        {
            GamepadFocusChrome.SetActive(false);
            window.Show();

            GamepadTextInput.Highlight(box);

            box.IsFocused.Should().BeTrue();
            GamepadTextInput.IsEditing.Should().BeFalse();
            GamepadTextInput.Active.Should().BeSameAs(box);
            box.IsReadOnly.Should().BeTrue();
        }
        finally
        {
            GamepadTextInput.Reset();
            window.Close();
        }
    }

    [AvaloniaFact]
    public void BeginEdit_enables_caret_and_marks_editing()
    {
        var box = new TextBox
        {
            Text = "hello",
            Focusable = true,
            IsEnabled = true,
            CaretIndex = 0,
        };
        var window = new Window { Content = box, Width = 240, Height = 120 };

        try
        {
            GamepadTextInput.SkipNativeFocusOverride = () => true;
            GamepadFocusChrome.SetActive(true, window);
            window.Show();
            GamepadTextInput.Highlight(box);
            GamepadTextInput.BeginEdit(box);

            GamepadTextInput.IsEditing.Should().BeTrue();
            GamepadTextInput.ShouldOpenSteamOskOnGotFocus.Should().BeTrue();
            box.IsFocused.Should().BeTrue();
            box.IsReadOnly.Should().BeFalse();
            box.CaretIndex.Should().Be(box.Text!.Length);
            box.CaretBrush.Should().NotBe(Brushes.Transparent);
        }
        finally
        {
            GamepadTextInput.Reset();
            GamepadFocusChrome.SetActive(false);
            window.Close();
        }
    }

    [AvaloniaFact]
    public void TryEndEdit_leaves_edit_and_keeps_highlight()
    {
        var box = new TextBox { Text = "hello", Focusable = true, IsEnabled = true };
        var window = new Window { Content = box, Width = 240, Height = 120 };

        try
        {
            GamepadTextInput.SkipNativeFocusOverride = () => false;
            GamepadFocusChrome.SetActive(true, window);
            window.Show();
            GamepadTextInput.BeginEdit(box);

            GamepadTextInput.TryEndEdit().Should().BeTrue();
            GamepadTextInput.TryEndEdit().Should().BeFalse();

            GamepadTextInput.IsEditing.Should().BeFalse();
            GamepadTextInput.ShouldOpenSteamOskOnGotFocus.Should().BeFalse();
            box.IsReadOnly.Should().BeTrue();
            box.Classes.Contains("gamepad-focused").Should().BeTrue();
        }
        finally
        {
            GamepadTextInput.Reset();
            GamepadFocusChrome.SetActive(false);
            window.Close();
        }
    }

    [Fact]
    public void ShouldSkipXyFocusOnHighlight_true_for_textbox_until_edit()
    {
        GamepadTextInput.ShouldSkipXyFocusOnHighlight(new TextBox()).Should().BeTrue();
        GamepadTextInput.ShouldSkipXyFocusOnHighlight(new Button()).Should().BeFalse();
        GamepadTextInput.ShouldSkipXyFocusOnHighlight(null).Should().BeFalse();
    }
}
