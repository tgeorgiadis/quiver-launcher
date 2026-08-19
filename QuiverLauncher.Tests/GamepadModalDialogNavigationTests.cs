using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using FluentAssertions;
using QuiverLauncher.Services;
using NavigationDirection = QuiverLauncher.Services.NavigationDirection;

namespace QuiverLauncher.Tests;

public class GamepadModalDialogNavigationTests
{
    [Fact]
    public void MoveButtonIndex_moves_right_between_horizontal_buttons()
    {
        var positions = new List<(double X, double Y)>
        {
            (0, 0),
            (100, 0),
        };

        GamepadModalDialogNavigation.MoveButtonIndex(0, NavigationDirection.Right, positions).Should().Be(1);
        GamepadModalDialogNavigation.MoveButtonIndex(1, NavigationDirection.Left, positions).Should().Be(0);
    }

    [Fact]
    public void MoveButtonIndex_stays_on_bottom_row_when_pressing_down()
    {
        var positions = new List<(double X, double Y)>
        {
            (0, 0),
            (100, 0),
            (0, 100),
            (100, 100),
        };

        GamepadModalDialogNavigation.MoveButtonIndex(2, NavigationDirection.Down, positions).Should().Be(2);
        GamepadModalDialogNavigation.MoveButtonIndex(3, NavigationDirection.Down, positions).Should().Be(3);
    }

    [Fact]
    public void MoveButtonIndex_stays_on_top_row_when_pressing_up()
    {
        var positions = new List<(double X, double Y)>
        {
            (0, 0),
            (100, 0),
            (0, 100),
            (100, 100),
        };

        GamepadModalDialogNavigation.MoveButtonIndex(0, NavigationDirection.Up, positions).Should().Be(0);
        GamepadModalDialogNavigation.MoveButtonIndex(1, NavigationDirection.Up, positions).Should().Be(1);
    }

    [Fact]
    public void MoveButtonIndex_stays_put_at_horizontal_edges()
    {
        var positions = new List<(double X, double Y)>
        {
            (0, 0),
            (100, 0),
        };

        GamepadModalDialogNavigation.MoveButtonIndex(0, NavigationDirection.Left, positions).Should().Be(0);
        GamepadModalDialogNavigation.MoveButtonIndex(1, NavigationDirection.Right, positions).Should().Be(1);
    }

    [AvaloniaFact]
    public void TryHandleNavigation_highlights_textbox_without_keyboard_focus()
    {
        var locationBox = new TextBox { Watermark = "URL", Focusable = true };
        var addButton = new Button { Content = "Add", MinWidth = 80 };
        var cancelButton = new Button { Content = "Cancel", MinWidth = 80 };
        var dialog = new Window
        {
            Width = 420,
            Height = 220,
            Content = new StackPanel
            {
                Children = { locationBox, addButton, cancelButton },
            },
        };
        var nav = GamepadModalDialogNavigation.Instance;

        try
        {
            GamepadFocusChrome.SetActive(true, dialog);
            dialog.Show();
            GamepadModalDialogNavigation.Attach(dialog);
            nav.RefreshDialogButtons();

            // Default focus prefers Add; move toward the text field at the top.
            nav.TryHandleNavigation(NavigationDirection.Up).Should().BeTrue();

            locationBox.Classes.Contains("gamepad-focused").Should().BeTrue();
            dialog.Classes.Contains(GamepadFocusChrome.WindowClassName).Should().BeTrue();
            locationBox.IsFocused.Should().BeFalse();
        }
        finally
        {
            nav.UnregisterModalDialog(dialog);
            GamepadFocusChrome.SetActive(false);
            if (dialog.IsVisible)
                dialog.Close();
        }
    }

    [AvaloniaFact]
    public void TryHandleConfirm_on_textbox_activates_edit_without_closing_dialog()
    {
        var locationBox = new TextBox
        {
            Watermark = "URL",
            Focusable = true,
            Text = "https://example.com/apps.json",
            CaretIndex = 0,
        };
        var addButton = new Button { Content = "Add", MinWidth = 80 };
        var cancelButton = new Button { Content = "Cancel", MinWidth = 80 };
        var dialog = new Window
        {
            Width = 420,
            Height = 220,
            Content = new StackPanel
            {
                Children = { locationBox, addButton, cancelButton },
            },
        };
        var nav = GamepadModalDialogNavigation.Instance;
        var closed = false;
        dialog.Closed += (_, _) => closed = true;

        try
        {
            GamepadFocusChrome.SetActive(true, dialog);
            dialog.Show();
            GamepadModalDialogNavigation.Attach(dialog);
            nav.RefreshDialogButtons();
            nav.TryHandleNavigation(NavigationDirection.Up).Should().BeTrue();
            locationBox.Classes.Contains("gamepad-focused").Should().BeTrue();

            nav.TryHandleConfirm().Should().BeTrue();

            closed.Should().BeFalse();
            dialog.IsVisible.Should().BeTrue();
            locationBox.IsFocused.Should().BeTrue();
            locationBox.CaretIndex.Should().Be(locationBox.Text!.Length);
        }
        finally
        {
            nav.UnregisterModalDialog(dialog);
            GamepadFocusChrome.SetActive(false);
            if (dialog.IsVisible)
                dialog.Close();
        }
    }

    [AvaloniaFact]
    public void Escape_while_editing_textbox_exits_edit_without_closing_dialog()
    {
        var locationBox = new TextBox
        {
            Watermark = "URL",
            Focusable = true,
            Text = "https://example.com/apps.json",
        };
        var addButton = new Button { Content = "Add", MinWidth = 80 };
        var cancelButton = new Button { Content = "Cancel", MinWidth = 80 };
        var dialog = new Window
        {
            Width = 420,
            Height = 220,
            Content = new StackPanel { Children = { locationBox, addButton, cancelButton } },
        };
        var nav = GamepadModalDialogNavigation.Instance;
        var previousResolver = nav.ResolveKeyboardAction;

        try
        {
            nav.ResolveKeyboardAction = (key, modifiers) =>
                KeyboardBindingDefaults.FindAction(KeyboardBindingDefaults.Create(), key, modifiers);

            GamepadFocusChrome.SetActive(true, dialog);
            dialog.Show();
            GamepadModalDialogNavigation.Attach(dialog);
            nav.RefreshDialogButtons();
            nav.TryHandleNavigation(NavigationDirection.Up).Should().BeTrue();
            nav.TryHandleConfirm().Should().BeTrue();
            locationBox.IsFocused.Should().BeTrue();

            nav.TryHandleDialogKeyDown(Key.Escape, KeyModifiers.None).Should().BeTrue();

            dialog.IsVisible.Should().BeTrue();
            locationBox.IsFocused.Should().BeFalse();
            locationBox.Classes.Contains("gamepad-focused").Should().BeTrue();
        }
        finally
        {
            nav.ResolveKeyboardAction = previousResolver;
            nav.UnregisterModalDialog(dialog);
            GamepadFocusChrome.SetActive(false);
            if (dialog.IsVisible)
                dialog.Close();
        }
    }

    [AvaloniaFact]
    public void Enter_while_editing_textbox_exits_edit_without_closing_dialog()
    {
        var locationBox = new TextBox
        {
            Watermark = "Prefix",
            Focusable = true,
            Text = "/tmp/prefix",
        };
        var saveButton = new Button { Content = "Save", MinWidth = 80 };
        var cancelButton = new Button { Content = "Cancel", MinWidth = 80 };
        var dialog = new Window
        {
            Width = 420,
            Height = 220,
            Content = new StackPanel { Children = { locationBox, saveButton, cancelButton } },
        };
        var nav = GamepadModalDialogNavigation.Instance;

        try
        {
            GamepadFocusChrome.SetActive(true, dialog);
            dialog.Show();
            GamepadModalDialogNavigation.Attach(dialog);
            nav.RefreshDialogButtons();
            nav.TryHandleNavigation(NavigationDirection.Up).Should().BeTrue();
            nav.TryHandleConfirm().Should().BeTrue();
            locationBox.IsFocused.Should().BeTrue();

            nav.TryHandleDialogKeyDown(Key.Enter, KeyModifiers.None).Should().BeTrue();

            dialog.IsVisible.Should().BeTrue();
            locationBox.IsFocused.Should().BeFalse();
            locationBox.Classes.Contains("gamepad-focused").Should().BeTrue();
        }
        finally
        {
            nav.UnregisterModalDialog(dialog);
            GamepadFocusChrome.SetActive(false);
            if (dialog.IsVisible)
                dialog.Close();
        }
    }

    [AvaloniaFact]
    public void CollectDialogFocusableControls_includes_listbox()
    {
        var root = new StackPanel
        {
            Children =
            {
                new ListBox { Focusable = true, Items = { "One", "Two" } },
                new Button { Content = "Install" },
                new Button { Content = "Cancel" },
            },
        };

        var window = new Window { Content = root };
        try
        {
            window.Show();
            var controls = GamepadModalDialogNavigation.CollectDialogFocusableControls(window);
            controls.Should().Contain(c => c is ListBox);
            controls.OfType<Button>().Select(b => b.Content?.ToString())
                .Should().BeEquivalentTo("Install", "Cancel");
            GamepadModalDialogNavigation.GetDefaultFocusIndex(controls).Should().Be(
                controls.FindIndex(c => c is ListBox));
        }
        finally
        {
            if (window.IsVisible)
                window.Close();
        }
    }

    [AvaloniaFact]
    public void CollectDialogFocusableControls_includes_disabled_install_button()
    {
        var install = new Button { Content = "Install", IsEnabled = false, Focusable = true };
        var cancel = new Button { Content = "Cancel", Focusable = true };
        var window = new Window
        {
            Content = new StackPanel
            {
                Children =
                {
                    new ListBox { Focusable = true, Items = { "One", "Two" } },
                    install,
                    cancel,
                },
            },
        };

        try
        {
            window.Show();
            var controls = GamepadModalDialogNavigation.CollectDialogFocusableControls(window);
            controls.Should().Contain(install);
            controls.Should().Contain(cancel);
        }
        finally
        {
            if (window.IsVisible)
                window.Close();
        }
    }

    [AvaloniaFact]
    public void TryHandleNavigation_clears_listbox_selection_when_moving_to_buttons()
    {
        var listBox = new ListBox
        {
            Focusable = true,
            Items = { "One", "Two" },
            SelectedIndex = 1,
        };
        var install = new Button { Content = "Install", IsEnabled = false, Focusable = true, MinWidth = 100 };
        var cancel = new Button { Content = "Cancel", Focusable = true, MinWidth = 100 };
        var dialog = new Window
        {
            Width = 480,
            Height = 360,
            Content = new StackPanel
            {
                Spacing = 12,
                Children = { listBox, install, cancel },
            },
        };

        var nav = GamepadModalDialogNavigation.Instance;
        try
        {
            GamepadFocusChrome.SetActive(true, dialog);
            dialog.Show();
            GamepadModalDialogNavigation.Attach(dialog);
            nav.RefreshDialogButtons();

            // Establish list focus via nav (Refresh posts focus asynchronously).
            nav.TryHandleNavigation(NavigationDirection.Up).Should().BeTrue();
            listBox.Classes.Contains("gamepad-focused").Should().BeTrue();

            // Park on the last row, then leave the list for the button row.
            listBox.SelectedIndex = 1;
            nav.TryHandleNavigation(NavigationDirection.Down).Should().BeTrue();

            listBox.SelectedIndex.Should().Be(-1);
            listBox.Classes.Contains("gamepad-focused").Should().BeFalse();
            install.Classes.Contains("gamepad-focused").Should().BeTrue();

            // Disabled Install must not dismiss the dialog on Confirm.
            nav.TryHandleConfirm().Should().BeTrue();
            dialog.IsVisible.Should().BeTrue();
            install.Classes.Contains("gamepad-focused").Should().BeTrue();

            nav.TryHandleNavigation(NavigationDirection.Up).Should().BeTrue();

            listBox.Classes.Contains("gamepad-focused").Should().BeTrue();
            listBox.SelectedIndex.Should().Be(1);
            install.Classes.Contains("gamepad-focused").Should().BeFalse();
        }
        finally
        {
            nav.UnregisterModalDialog(dialog);
            GamepadFocusChrome.SetActive(false);
            if (dialog.IsVisible)
                dialog.Close();
        }
    }

    [AvaloniaFact]
    public void CollectDialogFocusableControls_includes_textbox_and_buttons()
    {
        var root = new StackPanel
        {
            Children =
            {
                new TextBox { Watermark = "URL" },
                new Button { Content = "Browse…" },
                new Button { Content = "Add" },
                new Button { Content = "Cancel" },
            },
        };

        // Attach to a window so visual tree / bounds resolve for ordering.
        var window = new Window { Content = root };
        try
        {
            window.Show();
            var controls = GamepadModalDialogNavigation.CollectDialogFocusableControls(window);
            controls.Should().Contain(c => c is TextBox);
            controls.OfType<Button>().Select(b => b.Content?.ToString())
                .Should().BeEquivalentTo("Browse…", "Add", "Cancel");
        }
        finally
        {
            if (window.IsVisible)
                window.Close();
        }
    }

    [AvaloniaFact]
    public void CollectDialogFocusableControls_includes_combobox_not_nested_toggle()
    {
        var comboBox = new ComboBox
        {
            Focusable = true,
            Items =
            {
                new ComboBoxItem { Content = "Auto" },
                new ComboBoxItem { Content = "Wine" },
            },
            SelectedIndex = 0,
        };
        var prefixBox = new TextBox { Watermark = "Prefix", Focusable = true };
        var saveButton = new Button { Content = "Save" };
        var cancelButton = new Button { Content = "Cancel" };
        var window = new Window
        {
            Width = 520,
            Height = 420,
            Content = new StackPanel
            {
                Children = { comboBox, prefixBox, saveButton, cancelButton },
            },
        };

        try
        {
            window.Show();
            var controls = GamepadModalDialogNavigation.CollectDialogFocusableControls(window);

            controls.Should().Contain(comboBox);
            controls.Should().Contain(prefixBox);
            controls.OfType<Button>().Select(b => b.Content?.ToString())
                .Should().BeEquivalentTo("Save", "Cancel");
            controls.Should().NotContain(c =>
                c is Button && GamepadModalDialogNavigation.IsNestedInsideNavigableHost(c));
        }
        finally
        {
            if (window.IsVisible)
                window.Close();
        }
    }

    [AvaloniaFact]
    public void CollectDialogFocusableControls_includes_checkbox()
    {
        var checkBox = new CheckBox { Content = "Manually managed", Focusable = true };
        var saveButton = new Button { Content = "Save" };
        var cancelButton = new Button { Content = "Cancel" };
        var window = new Window
        {
            Width = 420,
            Height = 280,
            Content = new StackPanel
            {
                Children = { checkBox, saveButton, cancelButton },
            },
        };

        try
        {
            window.Show();
            var controls = GamepadModalDialogNavigation.CollectDialogFocusableControls(window);

            controls.Should().Contain(checkBox);
            controls.OfType<Button>().Where(b => b is not CheckBox).Select(b => b.Content?.ToString())
                .Should().BeEquivalentTo("Save", "Cancel");
        }
        finally
        {
            if (window.IsVisible)
                window.Close();
        }
    }

    [AvaloniaFact]
    public void TryHandleConfirm_on_checkbox_toggles_without_closing()
    {
        var checkBox = new CheckBox
        {
            Content = "Manually managed",
            Focusable = true,
            IsChecked = false,
            MinHeight = 32,
        };
        var saveButton = new Button { Content = "Save", MinWidth = 80 };
        var cancelButton = new Button { Content = "Cancel", MinWidth = 80 };
        var dialog = new Window
        {
            Width = 420,
            Height = 280,
            Content = new StackPanel
            {
                Spacing = 10,
                Margin = new Avalonia.Thickness(20),
                Children = { checkBox, saveButton, cancelButton },
            },
        };
        var nav = GamepadModalDialogNavigation.Instance;
        var closed = false;
        dialog.Closed += (_, _) => closed = true;

        try
        {
            GamepadFocusChrome.SetActive(true, dialog);
            dialog.Show();
            GamepadModalDialogNavigation.Attach(dialog);
            nav.RefreshDialogButtons();

            nav.TryHandleNavigation(NavigationDirection.Up).Should().BeTrue();
            if (!checkBox.Classes.Contains("gamepad-focused"))
                nav.TryHandleNavigation(NavigationDirection.Up).Should().BeTrue();

            checkBox.Classes.Contains("gamepad-focused").Should().BeTrue();
            nav.TryHandleConfirm().Should().BeTrue();

            checkBox.IsChecked.Should().BeTrue();
            closed.Should().BeFalse();
            dialog.IsVisible.Should().BeTrue();
        }
        finally
        {
            nav.UnregisterModalDialog(dialog);
            GamepadFocusChrome.SetActive(false);
            if (dialog.IsVisible)
                dialog.Close();
        }
    }

    [AvaloniaFact]
    public void TryHandleNavigation_moves_up_from_buttons_to_combobox()
    {
        var comboBox = new ComboBox
        {
            Focusable = true,
            MinWidth = 200,
            Items =
            {
                new ComboBoxItem { Content = "Auto" },
                new ComboBoxItem { Content = "Wine" },
            },
            SelectedIndex = 0,
        };
        var prefixBox = new TextBox { Watermark = "Prefix", Focusable = true, MinWidth = 200 };
        var saveButton = new Button { Content = "Save", MinWidth = 80 };
        var cancelButton = new Button { Content = "Cancel", MinWidth = 80 };
        var dialog = new Window
        {
            Width = 520,
            Height = 420,
            Content = new StackPanel
            {
                Spacing = 10,
                Margin = new Avalonia.Thickness(20),
                Children =
                {
                    comboBox,
                    prefixBox,
                    new StackPanel
                    {
                        Orientation = Avalonia.Layout.Orientation.Horizontal,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                        Spacing = 10,
                        Children = { saveButton, cancelButton },
                    },
                },
            },
        };
        var nav = GamepadModalDialogNavigation.Instance;

        try
        {
            GamepadFocusChrome.SetActive(true, dialog);
            dialog.Show();
            GamepadModalDialogNavigation.Attach(dialog);
            nav.RefreshDialogButtons();

            // Default focus is Save; Cancel is to the right.
            nav.TryHandleNavigation(NavigationDirection.Right).Should().BeTrue();
            cancelButton.Classes.Contains("gamepad-focused").Should().BeTrue();

            nav.TryHandleNavigation(NavigationDirection.Up).Should().BeTrue();
            cancelButton.Classes.Contains("gamepad-focused").Should().BeFalse();
            saveButton.Classes.Contains("gamepad-focused").Should().BeFalse();

            // One or two Ups should reach the ComboBox (via prefix TextBox).
            if (!comboBox.Classes.Contains("gamepad-focused"))
                nav.TryHandleNavigation(NavigationDirection.Up).Should().BeTrue();

            comboBox.Classes.Contains("gamepad-focused").Should().BeTrue();
        }
        finally
        {
            nav.UnregisterModalDialog(dialog);
            GamepadFocusChrome.SetActive(false);
            if (dialog.IsVisible)
                dialog.Close();
        }
    }

    [AvaloniaFact]
    public void TryHandleConfirm_on_combobox_opens_dropdown_without_closing()
    {
        var comboBox = new ComboBox
        {
            Focusable = true,
            Items =
            {
                new ComboBoxItem { Content = "Auto" },
                new ComboBoxItem { Content = "Wine" },
            },
            SelectedIndex = 0,
        };
        GamepadComboBoxNavigation.Attach(comboBox);
        var saveButton = new Button { Content = "Save", MinWidth = 80 };
        var dialog = new Window
        {
            Width = 420,
            Height = 280,
            Content = new StackPanel
            {
                Children = { comboBox, saveButton },
            },
        };
        var nav = GamepadModalDialogNavigation.Instance;
        var closed = false;
        dialog.Closed += (_, _) => closed = true;

        try
        {
            GamepadFocusChrome.SetActive(true, dialog);
            dialog.Show();
            GamepadModalDialogNavigation.Attach(dialog);
            nav.RefreshDialogButtons();
            nav.TryHandleNavigation(NavigationDirection.Up).Should().BeTrue();
            comboBox.Classes.Contains("gamepad-focused").Should().BeTrue();

            nav.TryHandleConfirm().Should().BeTrue();

            closed.Should().BeFalse();
            dialog.IsVisible.Should().BeTrue();
            GamepadComboBoxNavigation.Instance.HasActiveComboBox.Should().BeTrue();
            comboBox.IsDropDownOpen.Should().BeTrue();
        }
        finally
        {
            GamepadComboBoxNavigation.Instance.Close(comboBox);
            nav.UnregisterModalDialog(dialog);
            GamepadFocusChrome.SetActive(false);
            if (dialog.IsVisible)
                dialog.Close();
        }
    }

    [AvaloniaFact]
    public void GetDefaultButtonIndex_prefers_ok_and_yes()
    {
        var buttons = new List<Button>
        {
            new() { Content = "No" },
            new() { Content = "Yes" },
        };

        GamepadModalDialogNavigation.GetDefaultButtonIndex(buttons).Should().Be(1);

        var okButtons = new List<Button>
        {
            new() { Content = "OK" },
        };

        GamepadModalDialogNavigation.GetDefaultButtonIndex(okButtons).Should().Be(0);
    }

    [AvaloniaFact]
    public void GetDefaultButtonIndex_prefers_IsDefault_over_yes_label()
    {
        var yes = new Button { Content = "Yes" };
        var no = new Button { Content = "No", IsDefault = true };
        var buttons = new List<Button> { yes, no };

        GamepadModalDialogNavigation.GetDefaultButtonIndex(buttons).Should().Be(1);
    }

    [AvaloniaFact]
    public void FindCancelButtonIndex_prefers_cancel_and_no()
    {
        var yesNo = new List<Button>
        {
            new() { Content = "Yes" },
            new() { Content = "No" },
        };

        GamepadModalDialogNavigation.FindCancelButtonIndex(yesNo).Should().Be(1);

        var okCancel = new List<Button>
        {
            new() { Content = "Cancel" },
            new() { Content = "OK" },
        };

        GamepadModalDialogNavigation.FindCancelButtonIndex(okCancel).Should().Be(0);
    }

    [Fact]
    public void MoveButtonIndex_keeps_single_button_index()
    {
        var positions = new List<(double X, double Y)> { (0, 0) };

        GamepadModalDialogNavigation.MoveButtonIndex(0, NavigationDirection.Right, positions).Should().Be(0);
        GamepadModalDialogNavigation.MoveButtonIndex(0, NavigationDirection.Down, positions).Should().Be(0);
    }

    [AvaloniaFact]
    public void Attach_sets_active_dialog_before_opened_event()
    {
        var dialog = new Window
        {
            Content = new Button { Content = "OK" },
        };

        try
        {
            GamepadModalDialogNavigation.Instance.UnregisterModalDialog(dialog);
            GamepadModalDialogNavigation.Instance.HasActiveDialog.Should().BeFalse();

            GamepadModalDialogNavigation.Attach(dialog);

            GamepadModalDialogNavigation.Instance.HasActiveDialog.Should().BeTrue();
        }
        finally
        {
            GamepadModalDialogNavigation.Instance.UnregisterModalDialog(dialog);
        }
    }

    [AvaloniaFact]
    public void TryHandleConfirm_closes_ok_dialog()
    {
        var dialog = new Window
        {
            Width = 420,
            Height = 200,
            Content = new StackPanel
            {
                Children = { new Button { Content = "OK" } },
            },
        };

        var closed = false;
        dialog.Closed += (_, _) => closed = true;

        try
        {
            dialog.Show();
            GamepadModalDialogNavigation.Attach(dialog);
            GamepadModalDialogNavigation.Instance.RefreshDialogButtons();

            GamepadModalDialogNavigation.Instance.TryHandleConfirm().Should().BeTrue();
            closed.Should().BeTrue();
            dialog.IsVisible.Should().BeFalse();
        }
        finally
        {
            GamepadModalDialogNavigation.Instance.UnregisterModalDialog(dialog);
            if (dialog.IsVisible)
                dialog.Close();
        }
    }

    [AvaloniaFact]
    public void Attach_stacks_dialogs_and_unregister_restores_previous()
    {
        var dialogA = new Window { Content = new Button { Content = "Yes" } };
        var dialogB = new Window { Content = new Button { Content = "Update Quiver Launcher" } };
        var nav = GamepadModalDialogNavigation.Instance;

        try
        {
            nav.UnregisterModalDialog(dialogA);
            nav.UnregisterModalDialog(dialogB);

            GamepadModalDialogNavigation.Attach(dialogA);
            nav.HasActiveDialog.Should().BeTrue();
            nav.ActiveDialog.Should().BeSameAs(dialogA);
            nav.DialogStackCount.Should().Be(1);

            GamepadModalDialogNavigation.Attach(dialogB);
            nav.ActiveDialog.Should().BeSameAs(dialogB);
            nav.DialogStackCount.Should().Be(2);

            nav.UnregisterModalDialog(dialogB);
            nav.HasActiveDialog.Should().BeTrue();
            nav.ActiveDialog.Should().BeSameAs(dialogA);
            nav.DialogStackCount.Should().Be(1);

            nav.UnregisterModalDialog(dialogA);
            nav.HasActiveDialog.Should().BeFalse();
            nav.ActiveDialog.Should().BeNull();
            nav.DialogStackCount.Should().Be(0);
        }
        finally
        {
            nav.UnregisterModalDialog(dialogB);
            nav.UnregisterModalDialog(dialogA);
        }
    }

    [AvaloniaFact]
    public void Unregister_top_dialog_keeps_has_active_and_restores_previous()
    {
        var dialogA = new Window { Content = new Button { Content = "Yes" } };
        var dialogB = new Window
        {
            Content = new StackPanel
            {
                Children =
                {
                    new Button { Content = "Update Quiver Launcher" },
                    new Button { Content = "Not now" },
                },
            },
        };
        var nav = GamepadModalDialogNavigation.Instance;

        try
        {
            GamepadModalDialogNavigation.Attach(dialogA);
            GamepadModalDialogNavigation.Attach(dialogB);
            nav.DialogStackCount.Should().Be(2);

            nav.UnregisterModalDialog(dialogB);

            nav.HasActiveDialog.Should().BeTrue();
            nav.ActiveDialog.Should().BeSameAs(dialogA);
            nav.DialogStackCount.Should().Be(1);

            var act = () => nav.RefreshDialogButtons();
            act.Should().NotThrow();
        }
        finally
        {
            nav.UnregisterModalDialog(dialogB);
            nav.UnregisterModalDialog(dialogA);
        }
    }

    [AvaloniaFact]
    public void TryHandleConfirm_on_yes_invokes_question_result_callback_true()
    {
        bool? callbackResult = null;
        var yesButton = new Button { Content = "Yes", MinWidth = 80 };
        var noButton = new Button { Content = "No", MinWidth = 80 };
        var dialog = new Window
        {
            Width = 420,
            Height = 200,
            Content = new StackPanel
            {
                Children = { yesButton, noButton },
            },
        };

        yesButton.Click += (_, _) => dialog.Close();
        noButton.Click += (_, _) => dialog.Close();

        var nav = GamepadModalDialogNavigation.Instance;

        try
        {
            dialog.Show();
            GamepadModalDialogNavigation.Attach(dialog, accepted => callbackResult = accepted);
            nav.RefreshDialogButtons();

            nav.TryHandleConfirm().Should().BeTrue();
            callbackResult.Should().Be(true);
            dialog.IsVisible.Should().BeFalse();
        }
        finally
        {
            nav.UnregisterModalDialog(dialog);
            if (dialog.IsVisible)
                dialog.Close();
        }
    }

    [AvaloniaFact]
    public void TryHandleCancel_invokes_question_result_callback_false()
    {
        bool? callbackResult = null;
        var yesButton = new Button { Content = "Yes", MinWidth = 80 };
        var noButton = new Button { Content = "No", MinWidth = 80 };
        var dialog = new Window
        {
            Width = 420,
            Height = 200,
            Content = new StackPanel
            {
                Children = { yesButton, noButton },
            },
        };

        yesButton.Click += (_, _) => dialog.Close();
        noButton.Click += (_, _) => dialog.Close();

        var nav = GamepadModalDialogNavigation.Instance;

        try
        {
            dialog.Show();
            GamepadModalDialogNavigation.Attach(dialog, accepted => callbackResult = accepted);
            nav.RefreshDialogButtons();

            nav.TryHandleCancel().Should().BeTrue();
            callbackResult.Should().Be(false);
            dialog.IsVisible.Should().BeFalse();
        }
        finally
        {
            nav.UnregisterModalDialog(dialog);
            if (dialog.IsVisible)
                dialog.Close();
        }
    }

    [AvaloniaFact]
    public void ApplyDialogResultHint_sets_tag_for_yes_and_no()
    {
        var dialog = new Window();
        GamepadModalDialogNavigation.ApplyDialogResultHint(dialog, new Button { Content = "Yes" });
        dialog.Tag.Should().Be(true);

        GamepadModalDialogNavigation.ApplyDialogResultHint(dialog, new Button { Content = "No" });
        dialog.Tag.Should().Be(false);

        GamepadModalDialogNavigation.ApplyDialogResultHint(dialog, new Button { Content = "Update Quiver Launcher" });
        dialog.Tag.Should().Be(true);

        GamepadModalDialogNavigation.ApplyDialogResultHint(dialog, new Button { Content = "Not now" });
        dialog.Tag.Should().Be(false);
    }

    [AvaloniaFact]
    public void TryHandleDialogKeyDown_routes_to_open_combobox_not_dialog_fields()
    {
        var comboBox = new ComboBox
        {
            Focusable = true,
            Items =
            {
                new ComboBoxItem { Content = "Auto" },
                new ComboBoxItem { Content = "Custom command" },
            },
            SelectedIndex = 0,
        };
        GamepadComboBoxNavigation.Attach(comboBox);
        var saveButton = new Button { Content = "Save", MinWidth = 80 };
        var cancelButton = new Button { Content = "Cancel", MinWidth = 80 };
        var dialog = new Window
        {
            Width = 520,
            Height = 360,
            Content = new StackPanel
            {
                Spacing = 10,
                Children = { comboBox, saveButton, cancelButton },
            },
        };
        var nav = GamepadModalDialogNavigation.Instance;
        var previousResolver = nav.ResolveKeyboardAction;
        var previousChromeCallback = nav.OnKeyboardNavigationActivated;

        try
        {
            nav.ResolveKeyboardAction = (key, modifiers) =>
                KeyboardBindingDefaults.FindAction(KeyboardBindingDefaults.Create(), key, modifiers);
            nav.OnKeyboardNavigationActivated = () =>
            {
                GamepadFocusChrome.SetKeyboardNavigationActive(true);
                GamepadFocusChrome.SetActive(true, dialog);
            };

            GamepadFocusChrome.SetActive(true, dialog);
            dialog.Show();
            GamepadModalDialogNavigation.Attach(dialog);
            nav.RefreshDialogButtons();

            GamepadComboBoxNavigation.Open(comboBox);
            GamepadComboBoxNavigation.Instance.HasActiveComboBox.Should().BeTrue();
            comboBox.SelectedIndex.Should().Be(0);

            nav.TryHandleDialogKeyDown(Key.Down, KeyModifiers.None).Should().BeTrue();

            // Must move the open list, not jump dialog focus to Save/Cancel.
            comboBox.SelectedIndex.Should().Be(0); // selection updates on confirm; focus item moved
            GamepadComboBoxNavigation.Instance.HasActiveComboBox.Should().BeTrue();
            saveButton.Classes.Contains("gamepad-focused").Should().BeFalse();
            cancelButton.Classes.Contains("gamepad-focused").Should().BeFalse();

            nav.TryHandleDialogKeyDown(Key.Enter, KeyModifiers.None).Should().BeTrue();
            comboBox.SelectedIndex.Should().Be(1);
            comboBox.IsDropDownOpen.Should().BeFalse();
        }
        finally
        {
            GamepadComboBoxNavigation.Instance.Close(comboBox);
            nav.ResolveKeyboardAction = previousResolver;
            nav.OnKeyboardNavigationActivated = previousChromeCallback;
            nav.UnregisterModalDialog(dialog);
            GamepadFocusChrome.SetKeyboardNavigationActive(false);
            GamepadFocusChrome.SetActive(false);
            if (dialog.IsVisible)
                dialog.Close();
        }
    }

    [AvaloniaFact]
    public void TryHandleDialogKeyDown_moves_left_right_between_yes_and_no()
    {
        var yesButton = new Button { Content = "Yes", IsDefault = true, MinWidth = 80 };
        var noButton = new Button { Content = "No", MinWidth = 80 };
        var dialog = new Window
        {
            Width = 420,
            Height = 200,
            Content = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                Children = { yesButton, noButton },
            },
        };
        var nav = GamepadModalDialogNavigation.Instance;
        var previousResolver = nav.ResolveKeyboardAction;
        var previousChromeCallback = nav.OnKeyboardNavigationActivated;

        try
        {
            nav.ResolveKeyboardAction = (key, modifiers) =>
                KeyboardBindingDefaults.FindAction(KeyboardBindingDefaults.Create(), key, modifiers);
            nav.OnKeyboardNavigationActivated = () =>
            {
                GamepadFocusChrome.SetKeyboardNavigationActive(true);
                GamepadFocusChrome.SetActive(true, dialog);
            };

            dialog.Show();
            GamepadModalDialogNavigation.Attach(dialog);
            nav.RefreshDialogButtons();

            nav.TryHandleDialogKeyDown(Key.Right, KeyModifiers.None).Should().BeTrue();
            noButton.Classes.Contains("gamepad-focused").Should().BeTrue();
            yesButton.Classes.Contains("gamepad-focused").Should().BeFalse();

            nav.TryHandleDialogKeyDown(Key.Left, KeyModifiers.None).Should().BeTrue();
            yesButton.Classes.Contains("gamepad-focused").Should().BeTrue();
            noButton.Classes.Contains("gamepad-focused").Should().BeFalse();
        }
        finally
        {
            nav.ResolveKeyboardAction = previousResolver;
            nav.OnKeyboardNavigationActivated = previousChromeCallback;
            nav.UnregisterModalDialog(dialog);
            GamepadFocusChrome.SetKeyboardNavigationActive(false);
            GamepadFocusChrome.SetActive(false);
            if (dialog.IsVisible)
                dialog.Close();
        }
    }

    [AvaloniaFact]
    public void TryHandleDialogKeyDown_confirm_activates_focused_button()
    {
        var accepted = false;
        var yesButton = new Button { Content = "Yes", IsDefault = true, MinWidth = 80 };
        var noButton = new Button { Content = "No", MinWidth = 80 };
        var dialog = new Window
        {
            Width = 420,
            Height = 200,
            Content = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Children = { yesButton, noButton },
            },
        };
        yesButton.Click += (_, _) =>
        {
            accepted = true;
            dialog.Tag = true;
            dialog.Close();
        };
        var nav = GamepadModalDialogNavigation.Instance;
        var previousResolver = nav.ResolveKeyboardAction;
        var previousChromeCallback = nav.OnKeyboardNavigationActivated;

        try
        {
            nav.ResolveKeyboardAction = (key, modifiers) =>
                KeyboardBindingDefaults.FindAction(KeyboardBindingDefaults.Create(), key, modifiers);
            nav.OnKeyboardNavigationActivated = () =>
            {
                GamepadFocusChrome.SetKeyboardNavigationActive(true);
                GamepadFocusChrome.SetActive(true, dialog);
            };

            dialog.Show();
            GamepadModalDialogNavigation.Attach(dialog, value => dialog.Tag = value);
            nav.RefreshDialogButtons();

            nav.TryHandleDialogKeyDown(Key.Enter, KeyModifiers.None).Should().BeTrue();
            accepted.Should().BeTrue();
            dialog.Tag.Should().Be(true);
        }
        finally
        {
            nav.ResolveKeyboardAction = previousResolver;
            nav.OnKeyboardNavigationActivated = previousChromeCallback;
            nav.UnregisterModalDialog(dialog);
            GamepadFocusChrome.SetKeyboardNavigationActive(false);
            GamepadFocusChrome.SetActive(false);
            if (dialog.IsVisible)
                dialog.Close();
        }
    }
}
