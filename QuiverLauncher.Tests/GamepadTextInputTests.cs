using System.Collections;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Threading;
using FluentAssertions;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class GamepadTextInputTests
{
    [AvaloniaFact]
    public void Repeatedly_reattached_field_releases_input_ownership_after_unload()
    {
        // Inspect this service's references directly: Avalonia also keeps its own
        // focus history, which makes whole-control garbage collection nondeterministic.
        var states = (IDictionary)typeof(GamepadTextInput)
            .GetField("States", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var box = CreateEngagedBox();
        var window = new Window { Width = 240, Height = 120 };
        try
        {
            window.Show();
            for (var cycle = 0; cycle < 3; cycle++)
            {
                window.Content = box;
                Dispatcher.UIThread.RunJobs();
                GamepadTextInput.Highlight(box);
                states.Contains(box).Should().BeTrue();
                window.Content = null;
                Dispatcher.UIThread.RunJobs();
                states.Contains(box).Should().BeFalse();
                GamepadTextInput.Active.Should().BeNull();
            }
        }
        finally { window.Close(); GamepadTextInput.Reset(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reattached_field_supports_mouse_and_paste(bool clickBeforePaste)
    {
        var box = CreateEngagedBox();
        box.CaretBrush = Brushes.Lime;
        var window = new Window { Content = box, Width = 240, Height = 120 };
        try
        {
            GamepadTextInput.SkipNativeFocusOverride = () => false;
            GamepadFocusChrome.SetActive(true, window);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            for (var cycle = 0; cycle < 3; cycle++)
            {
                GamepadTextInput.Highlight(box);
                window.Content = null;
                Dispatcher.UIThread.RunJobs();
                box.IsReadOnly.Should().BeFalse();
                box.CaretBrush.Should().Be(Brushes.Lime);
                GamepadTextInput.Active.Should().BeNull();

                window.Content = box;
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                box.Text = "";
                GamepadTextInput.Highlight(box);
                if (clickBeforePaste)
                {
                    var point = box.TranslatePoint(new Point(12, 12), window)!.Value;
                    window.MouseDown(point, MouseButton.Left);
                    window.MouseUp(point, MouseButton.Left);
                    GamepadTextInput.IsEditing.Should().BeTrue();
                }
                await window.Clipboard!.SetTextAsync("dummy-token");
                window.KeyPress(Key.V, RawInputModifiers.Control, PhysicalKey.V, "v");
                Dispatcher.UIThread.RunJobs();
                box.Text.Should().Be("dummy-token");
                box.IsReadOnly.Should().BeFalse();
                box.CaretBrush.Should().Be(Brushes.Lime);
                GamepadTextInput.IsEditing.Should().BeTrue();
                window.KeyPress(Key.A, RawInputModifiers.Control, PhysicalKey.A, "a");
                box.SelectedText.Should().Be("dummy-token");
                window.KeyTextInput("replacement");
                box.Text.Should().Be("replacement");
                GamepadTextInput.TryEndEdit().Should().BeTrue();
            }
            window.Close();
            Dispatcher.UIThread.RunJobs();
            GamepadTextInput.Active.Should().BeNull();
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
            GamepadTextInput.Reset();
            GamepadFocusChrome.SetActive(false);
        }
    }

    [AvaloniaFact]
    public void Repeated_highlight_preserves_active_edit_and_selection_until_cancel()
    {
        var box = CreateEngagedBox();
        var window = new Window { Content = box };
        try
        {
            window.Show();
            GamepadTextInput.Highlight(box);
            GamepadControlActivation.ActivateTextBox(box);
            box.SelectionStart = 1;
            box.SelectionEnd = 4;
            GamepadTextInput.Highlight(box);
            GamepadTextInput.IsEditing.Should().BeTrue();
            box.IsReadOnly.Should().BeFalse();
            box.SelectedText.Should().Be("ell");
            GamepadTextInput.TryEndEdit().Should().BeTrue();
            box.IsReadOnly.Should().BeTrue();
        }
        finally { GamepadTextInput.Reset(); window.Close(); }
    }

    [AvaloniaFact]
    public void Unloading_another_field_preserves_edit_owner_and_original_readonly()
    {
        var readOnly = CreateEngagedBox();
        readOnly.IsReadOnly = true;
        readOnly.CaretBrush = Brushes.Lime;
        var editing = CreateEngagedBox();
        var panel = new StackPanel { Children = { readOnly, editing } };
        var window = new Window { Content = panel };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            GamepadTextInput.Highlight(readOnly);
            GamepadTextInput.BeginEdit(readOnly);
            readOnly.IsReadOnly.Should().BeTrue();
            GamepadTextInput.BeginEdit(editing);
            panel.Children.Remove(readOnly);
            Dispatcher.UIThread.RunJobs();
            readOnly.IsReadOnly.Should().BeTrue();
            readOnly.CaretBrush.Should().Be(Brushes.Lime);
            GamepadTextInput.Active.Should().BeSameAs(editing);
            GamepadTextInput.IsEditing.Should().BeTrue();
        }
        finally { GamepadTextInput.Reset(); window.Close(); }
    }

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
