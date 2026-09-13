using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using FluentAssertions;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class ModalInputOwnershipTests
{
    [AvaloniaFact]
    public void Dialog_confirm_does_not_end_editing_in_another_window()
    {
        var text = new TextBox();
        GamepadTextInput.SetEngageOnConfirm(text, true);
        var parent = new Window { Content = text };
        var accept = new Button { Content = "Yes", IsDefault = true };
        var dialog = new Window { Content = accept };
        var accepted = false;
        accept.Click += (_, _) => accepted = true;
        var navigation = GamepadModalDialogNavigation.Instance;
        var previous = navigation.ResolveKeyboardAction;
        try
        {
            parent.Show();
            dialog.Show();
            GamepadModalDialogNavigation.Attach(dialog);
            GamepadTextInput.BeginEdit(text);
            navigation.ResolveKeyboardAction = (_, _) => GamepadAction.Confirm;
            navigation.RefreshDialogButtons();
            navigation.TryHandleDialogKeyDown(Key.Enter, KeyModifiers.None).Should().BeTrue();
            accepted.Should().BeTrue();
        }
        finally
        {
            navigation.ResolveKeyboardAction = previous;
            navigation.UnregisterModalDialog(dialog);
            dialog.Close(); parent.Close();
            GamepadTextInput.Reset();
            GamepadFocusChrome.SetKeyboardNavigationActive(false);
            GamepadFocusChrome.SetActive(false);
        }
    }

    [AvaloniaFact]
    public void Dialog_confirm_does_not_consume_another_windows_dropdown()
    {
        var combo = new ComboBox { Items = { new ComboBoxItem { Content = "One" }, new ComboBoxItem { Content = "Two" } } };
        var parent = new Window { Content = combo };
        var accept = new Button { Content = "Yes", IsDefault = true };
        var dialog = new Window { Content = accept };
        var accepted = false;
        accept.Click += (_, _) => accepted = true;
        var navigation = GamepadModalDialogNavigation.Instance;
        var previous = navigation.ResolveKeyboardAction;
        try
        {
            parent.Show();
            GamepadComboBoxNavigation.Instance.RegisterOpen(combo, openIfNeeded: false);
            dialog.Show();
            GamepadModalDialogNavigation.Attach(dialog);
            navigation.ResolveKeyboardAction = (_, _) => GamepadAction.Confirm;
            navigation.RefreshDialogButtons();
            navigation.TryHandleDialogKeyDown(Key.Enter, KeyModifiers.None).Should().BeTrue();
            accepted.Should().BeTrue();
            GamepadComboBoxNavigation.Instance.IsActiveFor(parent).Should().BeTrue();
        }
        finally
        {
            navigation.ResolveKeyboardAction = previous;
            GamepadComboBoxNavigation.Instance.Close(combo);
            navigation.UnregisterModalDialog(dialog);
            dialog.Close(); parent.Close();
            GamepadTextInput.Reset();
            GamepadFocusChrome.SetKeyboardNavigationActive(false);
            GamepadFocusChrome.SetActive(false);
        }
    }
}
