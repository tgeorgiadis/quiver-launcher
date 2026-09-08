using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace QuiverLauncher.Services;

internal static class GamepadControlActivation
{
    public static void ActivateButton(Button button)
    {
        if (!button.IsEnabled || !button.IsVisible)
            return;

        button.Focus();

        // ClickEvent does not run Button.OnClick, so attached flyouts never open.
        if (button.Flyout != null)
        {
            GamepadMenuFlyoutNavigation.Toggle(button);
            return;
        }

        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, RoutingStrategies.Bubble) { Source = button });
    }

    /// <summary>
    /// Toggles a checkbox. Bound Select/Confirm is not Space, so Avalonia's native
    /// Space handler never runs — call this from Confirm instead.
    /// </summary>
    public static void ActivateCheckBox(CheckBox checkBox)
    {
        if (!checkBox.IsEnabled || !checkBox.IsVisible)
            return;

        checkBox.IsChecked = checkBox.IsChecked != true;
    }

    /// <summary>
    /// Index of the navigable host that is the focused element, or that contains it
    /// (CheckBox template parts). -1 when focus is outside the list.
    /// </summary>
    public static int IndexOfControlContainingFocus(IReadOnlyList<Control> controls, object? focused)
    {
        if (focused is not Visual focusedVisual || controls.Count == 0)
            return -1;

        for (var i = 0; i < controls.Count; i++)
        {
            var control = controls[i];
            if (ReferenceEquals(control, focused))
                return i;

            if (control is Visual host && host.IsVisualAncestorOf(focusedVisual))
                return i;
        }

        return -1;
    }

    /// <summary>
    /// Enters text-edit mode (caret + Steam OSK). Highlight-only navigation uses
    /// <see cref="GamepadTextInput.Highlight"/> instead.
    /// </summary>
    public static void ActivateTextBox(TextBox textBox) =>
        GamepadTextInput.BeginEdit(textBox);

    /// <summary>
    /// Applies Avalonia keyboard focus so XYFocus / :focus chrome can follow the cursor.
    /// TextBoxes stay highlight-only until Confirm.
    /// </summary>
    public static void ApplyGamepadHighlightFocus(Control control)
    {
        if (!control.IsEnabled)
        {
            TopLevel.GetTopLevel(control)?.FocusManager?.Focus(null);
            return;
        }

        if (control is TextBox textBox)
        {
            GamepadTextInput.Highlight(textBox);
            return;
        }

        control.Focus();
    }

    /// <summary>
    /// True when gamepad highlight should move Avalonia keyboard focus onto the control.
    /// </summary>
    public static bool ShouldKeyboardFocusOnGamepadHighlight(Control control) =>
        control.IsEnabled;

    public static void MoveCaretToEnd(TextBox textBox)
    {
        var length = textBox.Text?.Length ?? 0;
        textBox.CaretIndex = length;
        textBox.SelectionStart = length;
        textBox.SelectionEnd = length;
    }

    public static void ActivateDialogButton(Button button, Window? closeFallbackDialog = null)
    {
        if (!button.IsEnabled || !button.IsVisible)
            return;

        button.Focus();

        RaiseClickEvent(button, Button.ClickEvent);

        if (button.Command?.CanExecute(null) == true)
            button.Command.Execute(null);

        SimulateActivationKeys(button);

        if (closeFallbackDialog != null && closeFallbackDialog.IsVisible)
            closeFallbackDialog.Close();
    }

    public static void ActivateMenuItem(MenuItem item)
    {
        if (!item.IsEnabled || !item.IsVisible)
            return;

        item.Focus();
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, RoutingStrategies.Bubble) { Source = item });

        if (item.Command?.CanExecute(null) == true)
            item.Command.Execute(null);

        CloseParentMenuIfOpen(item);
    }

    private static void RaiseClickEvent(Interactive control, RoutedEvent clickEvent)
    {
        control.RaiseEvent(new RoutedEventArgs(clickEvent) { Source = control });
        control.RaiseEvent(new RoutedEventArgs(clickEvent, RoutingStrategies.Bubble) { Source = control });
        control.RaiseEvent(new RoutedEventArgs(clickEvent, RoutingStrategies.Tunnel) { Source = control });
    }

    private static void SimulateActivationKeys(InputElement control)
    {
        SimulateKey(control, Key.Enter);
        SimulateKey(control, Key.Space);
    }

    private static void SimulateKey(InputElement control, Key key)
    {
        control.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = key,
            KeyModifiers = KeyModifiers.None,
            Source = control,
        });

        control.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyUpEvent,
            Key = key,
            KeyModifiers = KeyModifiers.None,
            Source = control,
        });
    }

    private static void CloseParentMenuIfOpen(MenuItem item)
    {
        // Nested items (e.g. Customize → Edit Tags) have a MenuItem parent, not ContextMenu.
        for (Control? current = item.Parent as Control; current != null; current = current.Parent as Control)
        {
            if (current is ContextMenu menu)
            {
                if (menu.IsOpen)
                    menu.Close();
                return;
            }
        }

        var visualMenu = item.GetVisualAncestors().OfType<ContextMenu>().FirstOrDefault();
        if (visualMenu != null && visualMenu.IsOpen)
            visualMenu.Close();
    }
}
