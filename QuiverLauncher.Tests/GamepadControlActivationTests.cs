using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FluentAssertions;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class GamepadControlActivationTests
{
    [AvaloniaFact]
    public void ActivateButton_raises_click_once()
    {
        var clickCount = 0;
        var button = new Button
        {
            Content = "Check Updates",
            IsEnabled = true,
            IsVisible = true,
        };
        button.Click += (_, _) => clickCount++;

        GamepadControlActivation.ActivateButton(button);

        clickCount.Should().Be(1);
    }

    [AvaloniaFact]
    public void ActivateDialogButton_raises_click_multiple_times()
    {
        var clickCount = 0;
        var button = new Button
        {
            Content = "OK",
            IsEnabled = true,
            IsVisible = true,
        };
        button.Click += (_, _) => clickCount++;

        GamepadControlActivation.ActivateDialogButton(button);

        clickCount.Should().BeGreaterThanOrEqualTo(3);
    }

    [AvaloniaFact]
    public void ActivateMenuItem_raises_click_once()
    {
        var clickCount = 0;
        var item = new MenuItem
        {
            Header = "Locate Existing Install",
            IsEnabled = true,
            IsVisible = true,
        };
        item.Click += (_, _) => clickCount++;

        GamepadControlActivation.ActivateMenuItem(item);

        clickCount.Should().Be(1);
    }

    [AvaloniaFact]
    public void ActivateCheckBox_toggles_checked_state()
    {
        var checkBox = new CheckBox
        {
            Content = "Manually managed",
            IsEnabled = true,
            IsVisible = true,
            IsChecked = false,
        };

        GamepadControlActivation.ActivateCheckBox(checkBox);
        checkBox.IsChecked.Should().BeTrue();

        GamepadControlActivation.ActivateCheckBox(checkBox);
        checkBox.IsChecked.Should().BeFalse();
    }

    [Fact]
    public void IndexOfControlContainingFocus_matches_host_or_descendant()
    {
        var checkBox = new CheckBox { Content = "Enabled" };
        var textBox = new TextBox();
        var controls = new List<Control> { textBox, checkBox };

        GamepadControlActivation.IndexOfControlContainingFocus(controls, checkBox).Should().Be(1);
        GamepadControlActivation.IndexOfControlContainingFocus(controls, textBox).Should().Be(0);
        GamepadControlActivation.IndexOfControlContainingFocus(controls, new Button()).Should().Be(-1);
    }

    [Fact]
    public void ShouldKeyboardFocusOnGamepadHighlight_true_for_textbox()
    {
        GamepadControlActivation.ShouldKeyboardFocusOnGamepadHighlight(new TextBox())
            .Should().BeTrue();
    }

    [Fact]
    public void ShouldKeyboardFocusOnGamepadHighlight_true_for_button()
    {
        GamepadControlActivation.ShouldKeyboardFocusOnGamepadHighlight(new Button())
            .Should().BeTrue();
    }

    [AvaloniaFact]
    public void ApplyGamepadHighlightFocus_focuses_textbox()
    {
        var textBox = new TextBox { IsEnabled = true, IsVisible = true, Focusable = true };
        var window = new Window { Content = textBox, Width = 240, Height = 120 };

        try
        {
            window.Show();
            GamepadControlActivation.ApplyGamepadHighlightFocus(textBox);
            textBox.IsFocused.Should().BeTrue();
            GamepadTextInput.IsEditing.Should().BeFalse();
        }
        finally
        {
            GamepadTextInput.Reset();
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ApplyGamepadHighlightFocus_on_textbox_moves_focus_from_button()
    {
        var textBox = new TextBox { IsEnabled = true, IsVisible = true, Focusable = true };
        var button = new Button { Content = "Save", IsEnabled = true, IsVisible = true, Focusable = true };
        var window = new Window
        {
            Width = 240,
            Height = 160,
            Content = new StackPanel { Children = { textBox, button } },
        };

        try
        {
            window.Show();
            button.Focus();
            button.IsFocused.Should().BeTrue();

            GamepadControlActivation.ApplyGamepadHighlightFocus(textBox);

            button.IsFocused.Should().BeFalse();
            textBox.IsFocused.Should().BeTrue();
            GamepadTextInput.IsEditing.Should().BeFalse();
        }
        finally
        {
            GamepadTextInput.Reset();
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ApplyGamepadHighlightFocus_focuses_button()
    {
        var button = new Button { Content = "Save", IsEnabled = true, IsVisible = true, Focusable = true };
        var window = new Window { Content = button, Width = 240, Height = 120 };

        try
        {
            window.Show();
            GamepadControlActivation.ApplyGamepadHighlightFocus(button);
            button.IsFocused.Should().BeTrue();
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ActivateMenuItem_closes_root_context_menu_for_nested_item()
    {
        var editTags = new MenuItem
        {
            Header = "Edit Tags",
            IsEnabled = true,
            IsVisible = true,
        };
        var catalog = new MenuItem
        {
            Header = "Customize",
            Items = { editTags },
        };
        var button = new Button { Content = "Options" };
        var menu = new ContextMenu { Items = { catalog } };
        var window = new Window
        {
            Content = button,
            Width = 240,
            Height = 180,
        };

        try
        {
            window.Show();
            menu.Open(button);
            menu.IsOpen.Should().BeTrue();

            GamepadControlActivation.ActivateMenuItem(editTags);

            menu.IsOpen.Should().BeFalse();
        }
        finally
        {
            if (menu.IsOpen)
                menu.Close();
            window.Close();
        }
    }
}
