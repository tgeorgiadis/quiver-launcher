using Avalonia.Controls;
using Avalonia.Input;

namespace QuiverLauncher.Services;

/// <summary>
/// Parks keyboard focus off chrome after a library/catalog card is selected.
/// Cards are not Focusable; <see cref="FocusManager.Focus"/> of null makes
/// Avalonia's window handler treat the next arrow as Tab-to-first-stop (Continue).
/// </summary>
internal static class GamepadCardFocusSink
{
    public static void Configure(Control sink)
    {
        sink.Focusable = true;
        sink.IsHitTestVisible = false;
        sink.FocusAdorner = null;
        KeyboardNavigation.SetIsTabStop(sink, false);
        XYFocus.SetNavigationModes(sink, XYFocusNavigationModes.Disabled);
    }

    public static void Park(Control? sink)
    {
        GamepadTextInput.Reset();
        if (sink == null)
            return;

        Configure(sink);
        sink.Focus();
    }
}
