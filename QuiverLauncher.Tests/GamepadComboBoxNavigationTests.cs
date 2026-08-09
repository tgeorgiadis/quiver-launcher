using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FluentAssertions;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class GamepadComboBoxNavigationTests
{
    [Fact]
    public void MoveItemIndex_moves_down_and_wraps()
    {
        GamepadComboBoxNavigation.MoveItemIndex(0, NavigationDirection.Down, 3).Should().Be(1);
        GamepadComboBoxNavigation.MoveItemIndex(2, NavigationDirection.Down, 3).Should().Be(0);
    }

    [Fact]
    public void MoveItemIndex_moves_up_and_wraps()
    {
        GamepadComboBoxNavigation.MoveItemIndex(0, NavigationDirection.Up, 3).Should().Be(2);
        GamepadComboBoxNavigation.MoveItemIndex(1, NavigationDirection.Up, 3).Should().Be(0);
    }

    [Fact]
    public void MoveItemIndex_treats_left_right_like_vertical_navigation()
    {
        GamepadComboBoxNavigation.MoveItemIndex(0, NavigationDirection.Right, 2).Should().Be(1);
        GamepadComboBoxNavigation.MoveItemIndex(1, NavigationDirection.Left, 2).Should().Be(0);
    }

    [Fact]
    public void MoveItemIndex_keeps_single_item_index()
    {
        GamepadComboBoxNavigation.MoveItemIndex(0, NavigationDirection.Down, 1).Should().Be(0);
        GamepadComboBoxNavigation.MoveItemIndex(0, NavigationDirection.Up, 1).Should().Be(0);
    }

    [AvaloniaFact]
    public void Open_registers_active_combo_box()
    {
        var comboBox = CreateSortComboBox();

        try
        {
            GamepadComboBoxNavigation.Instance.Close(comboBox);
            GamepadComboBoxNavigation.Instance.HasActiveComboBox.Should().BeFalse();

            GamepadComboBoxNavigation.Open(comboBox);

            GamepadComboBoxNavigation.Instance.HasActiveComboBox.Should().BeTrue();
            comboBox.IsDropDownOpen.Should().BeTrue();
        }
        finally
        {
            comboBox.IsDropDownOpen = false;
            GamepadComboBoxNavigation.Instance.Close(comboBox);
        }
    }

    [AvaloniaFact]
    public void Attach_registers_when_dropdown_opened_natively()
    {
        var comboBox = CreateSortComboBox();
        GamepadComboBoxNavigation.Attach(comboBox);
        var window = new Window { Content = comboBox, Width = 280, Height = 160 };

        try
        {
            GamepadComboBoxNavigation.Instance.Close(comboBox);
            window.Show();

            comboBox.IsDropDownOpen = true;

            GamepadComboBoxNavigation.Instance.HasActiveComboBox.Should().BeTrue();
        }
        finally
        {
            comboBox.IsDropDownOpen = false;
            GamepadComboBoxNavigation.Instance.Close(comboBox);
            if (window.IsVisible)
                window.Close();
        }
    }

    [AvaloniaFact]
    public void Open_expands_closed_dropdown()
    {
        var comboBox = CreateSortComboBox();
        comboBox.IsDropDownOpen = false;

        try
        {
            GamepadComboBoxNavigation.Open(comboBox);

            comboBox.IsDropDownOpen.Should().BeTrue();
            GamepadComboBoxNavigation.Instance.HasActiveComboBox.Should().BeTrue();
        }
        finally
        {
            comboBox.IsDropDownOpen = false;
            GamepadComboBoxNavigation.Instance.Close(comboBox);
        }
    }

    [AvaloniaFact]
    public void TryHandleNavigation_applies_gamepad_focused_hover_class()
    {
        var comboBox = CreateSortComboBox();
        comboBox.SelectedIndex = 0;
        var window = new Window { Content = comboBox, Width = 280, Height = 180 };

        try
        {
            window.Show();
            GamepadComboBoxNavigation.Open(comboBox);

            var first = (ComboBoxItem)comboBox.Items[0]!;
            var second = (ComboBoxItem)comboBox.Items[1]!;
            first.Classes.Contains("gamepad-focused").Should().BeTrue();

            GamepadComboBoxNavigation.Instance.TryHandleNavigation(NavigationDirection.Down)
                .Should().BeTrue();

            first.Classes.Contains("gamepad-focused").Should().BeFalse();
            second.Classes.Contains("gamepad-focused").Should().BeTrue();
            // Browsing must not commit selection — only Confirm does.
            comboBox.SelectedIndex.Should().Be(0);
        }
        finally
        {
            comboBox.IsDropDownOpen = false;
            GamepadComboBoxNavigation.Instance.Close(comboBox);
            if (window.IsVisible)
                window.Close();
        }
    }

    [AvaloniaFact]
    public void TryHandleNavigation_does_not_commit_selection_until_confirm()
    {
        var comboBox = CreateSortComboBox();
        comboBox.SelectedIndex = 0;
        var window = new Window { Content = comboBox, Width = 280, Height = 180 };

        try
        {
            window.Show();
            GamepadComboBoxNavigation.Open(comboBox);

            GamepadComboBoxNavigation.Instance.TryHandleNavigation(NavigationDirection.Down)
                .Should().BeTrue();
            comboBox.SelectedIndex.Should().Be(0);

            GamepadComboBoxNavigation.Instance.TryHandleNavigation(NavigationDirection.Down)
                .Should().BeTrue();
            comboBox.SelectedIndex.Should().Be(0);

            GamepadComboBoxNavigation.Instance.TryHandleConfirm().Should().BeTrue();
            comboBox.SelectedIndex.Should().Be(2);
        }
        finally
        {
            comboBox.IsDropDownOpen = false;
            GamepadComboBoxNavigation.Instance.Close(comboBox);
            if (window.IsVisible)
                window.Close();
        }
    }

    [AvaloniaFact]
    public void TryHandleConfirm_sets_selection_and_closes_dropdown()
    {
        var comboBox = CreateSortComboBox();
        comboBox.SelectedIndex = 0;
        comboBox.IsDropDownOpen = true;

        try
        {
            GamepadComboBoxNavigation.Open(comboBox);
            GamepadComboBoxNavigation.Instance.TryHandleNavigation(NavigationDirection.Down).Should().BeTrue();

            var confirmed = GamepadComboBoxNavigation.Instance.TryHandleConfirm();

            confirmed.Should().BeTrue();
            comboBox.IsDropDownOpen.Should().BeFalse();
            comboBox.SelectedIndex.Should().Be(1);
            GamepadComboBoxNavigation.Instance.HasActiveComboBox.Should().BeFalse();
        }
        finally
        {
            GamepadComboBoxNavigation.Instance.Close(comboBox);
        }
    }

    [AvaloniaFact]
    public void TryHandleCancel_restores_original_selection()
    {
        var comboBox = CreateSortComboBox();
        comboBox.SelectedIndex = 0;
        comboBox.IsDropDownOpen = true;

        try
        {
            GamepadComboBoxNavigation.Open(comboBox);
            GamepadComboBoxNavigation.Instance.TryHandleNavigation(NavigationDirection.Down).Should().BeTrue();

            var cancelled = GamepadComboBoxNavigation.Instance.TryHandleCancel();

            cancelled.Should().BeTrue();
            comboBox.IsDropDownOpen.Should().BeFalse();
            comboBox.SelectedIndex.Should().Be(0);
            GamepadComboBoxNavigation.Instance.HasActiveComboBox.Should().BeFalse();
        }
        finally
        {
            GamepadComboBoxNavigation.Instance.Close(comboBox);
        }
    }

    private static ComboBox CreateSortComboBox()
    {
        return new ComboBox
        {
            Items =
            {
                new ComboBoxItem { Content = "Name (A-Z)", Tag = "Name" },
                new ComboBoxItem { Content = "Name (Z-A)", Tag = "NameDesc" },
                new ComboBoxItem { Content = "Installed First", Tag = "Installed" },
            },
        };
    }
}
