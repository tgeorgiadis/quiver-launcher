using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
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
            GamepadTextInput.ShouldSkipXyFocusOnHighlight(box).Should().BeFalse();
            GamepadTextInput.ShouldSkipXyFocusOnHighlight(new TextBox()).Should().BeTrue();
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

    [AvaloniaFact]
    public void ShouldSkipXyFocusOnHighlight_true_for_textbox_until_edit()
    {
        GamepadTextInput.Reset();
        try
        {
            GamepadTextInput.ShouldSkipXyFocusOnHighlight(new TextBox()).Should().BeTrue();
            GamepadTextInput.ShouldSkipXyFocusOnHighlight(new Button()).Should().BeFalse();
            GamepadTextInput.ShouldSkipXyFocusOnHighlight(null).Should().BeFalse();
        }
        finally
        {
            GamepadTextInput.Reset();
        }
    }

    [AvaloniaFact]
    public void Mouse_press_on_already_focused_highlight_enters_edit()
    {
        var box = CreateEngagedBox();
        var window = new Window { Content = box, Width = 240, Height = 120 };

        try
        {
            GamepadTextInput.SkipNativeFocusOverride = () => false;
            GamepadFocusChrome.SetActive(true, window);
            window.Show();
            window.UpdateLayout();

            GamepadTextInput.Highlight(box);
            box.IsFocused.Should().BeTrue();
            box.IsReadOnly.Should().BeTrue();
            GamepadTextInput.IsEditing.Should().BeFalse();

            var point = box.TranslatePoint(new Point(12, 12), window) ?? new Point(12, 12);
            window.MouseDown(point, MouseButton.Left);

            GamepadTextInput.IsEditing.Should().BeTrue();
            GamepadTextInput.Active.Should().BeSameAs(box);
            box.IsReadOnly.Should().BeFalse();
        }
        finally
        {
            GamepadTextInput.Reset();
            GamepadFocusChrome.SetActive(false);
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Reset_after_highlight_restores_writable()
    {
        var box = new TextBox { Text = "hello", Focusable = true, IsEnabled = true };
        var window = new Window { Content = box, Width = 240, Height = 120 };

        try
        {
            window.Show();
            GamepadTextInput.Highlight(box);
            box.IsReadOnly.Should().BeTrue();

            GamepadTextInput.Reset();

            box.IsReadOnly.Should().BeFalse();
            GamepadTextInput.Active.Should().BeNull();
            GamepadTextInput.IsEditing.Should().BeFalse();
        }
        finally
        {
            GamepadTextInput.Reset();
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Highlight_twice_does_not_stick_readonly_after_begin_edit()
    {
        var box = new TextBox { Text = "token", Focusable = true, IsEnabled = true };
        var window = new Window { Content = box, Width = 240, Height = 120 };

        try
        {
            window.Show();
            GamepadTextInput.Highlight(box);
            GamepadTextInput.Highlight(box);
            box.IsReadOnly.Should().BeTrue();

            GamepadTextInput.BeginEdit(box);

            box.IsReadOnly.Should().BeFalse();
            GamepadTextInput.IsEditing.Should().BeTrue();
        }
        finally
        {
            GamepadTextInput.Reset();
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Detach_after_highlight_restores_writable_and_next_edit_is_not_stuck()
    {
        var box = CreateEngagedBox();
        var window = new Window { Content = box, Width = 240, Height = 120 };

        try
        {
            window.Show();
            GamepadTextInput.Highlight(box);
            box.IsReadOnly.Should().BeTrue();

            GamepadTextInput.SetEngageOnConfirm(box, false);
            box.IsReadOnly.Should().BeFalse();

            GamepadTextInput.SetEngageOnConfirm(box, true);
            GamepadTextInput.Highlight(box);
            GamepadTextInput.BeginEdit(box);

            box.IsReadOnly.Should().BeFalse();
            GamepadTextInput.IsEditing.Should().BeTrue();
        }
        finally
        {
            GamepadTextInput.Reset();
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Ctrl_V_on_highlighted_field_enters_edit()
    {
        var box = CreateEngagedBox();
        var window = new Window { Content = box, Width = 240, Height = 120 };

        try
        {
            GamepadTextInput.SkipNativeFocusOverride = () => false;
            GamepadFocusChrome.SetActive(true, window);
            window.Show();
            window.UpdateLayout();

            GamepadTextInput.Highlight(box);
            box.IsFocused.Should().BeTrue();
            box.IsReadOnly.Should().BeTrue();

            window.KeyPress(Key.V, RawInputModifiers.Control, PhysicalKey.V, "v");

            GamepadTextInput.IsEditing.Should().BeTrue();
            box.IsReadOnly.Should().BeFalse();
        }
        finally
        {
            GamepadTextInput.Reset();
            GamepadFocusChrome.SetActive(false);
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Arrow_key_on_highlighted_field_does_not_enter_edit()
    {
        var box = CreateEngagedBox();
        var window = new Window { Content = box, Width = 240, Height = 120 };

        try
        {
            GamepadTextInput.SkipNativeFocusOverride = () => false;
            GamepadFocusChrome.SetActive(true, window);
            window.Show();
            window.UpdateLayout();

            GamepadTextInput.Highlight(box);
            box.IsReadOnly.Should().BeTrue();

            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);

            GamepadTextInput.IsEditing.Should().BeFalse();
            box.IsReadOnly.Should().BeTrue();
        }
        finally
        {
            GamepadTextInput.Reset();
            GamepadFocusChrome.SetActive(false);
            window.Close();
        }
    }

    private static TextBox CreateEngagedBox()
    {
        var box = new TextBox
        {
            Text = "hello",
            Focusable = true,
            IsEnabled = true,
            Width = 200,
            Height = 40,
        };
        GamepadTextInput.SetEngageOnConfirm(box, true);
        return box;
    }
}
