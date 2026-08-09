using Avalonia;
using Avalonia.Controls;

namespace QuiverLauncher.Services;

/// <summary>
/// Global gate for orange gamepad/keyboard focus rings. Styles only paint when the
/// hosting <see cref="Window"/> has class <c>gamepad-chrome</c>.
/// </summary>
internal static class GamepadFocusChrome
{
    public const string WindowClassName = "gamepad-chrome";
    public const string FocusedClassName = "gamepad-focused";

    public static bool IsActive { get; private set; }

    public static bool KeyboardNavigationActive { get; private set; }

    public static bool ShouldShowGamepadChrome(
        bool enableGamepadInput,
        bool hasConnectedGamepad,
        bool keyboardNavActive = false) =>
        (enableGamepadInput && hasConnectedGamepad) || keyboardNavActive;

    public static void SetKeyboardNavigationActive(bool active) =>
        KeyboardNavigationActive = active;

    public static void SetActive(bool active, params Window?[] windows)
    {
        IsActive = active;
        foreach (var window in windows)
        {
            if (window != null)
                window.Classes.Set(WindowClassName, active);
        }
    }

    public static void ApplyToWindow(Window window, bool active) =>
        window.Classes.Set(WindowClassName, active);

    /// <summary>
    /// Sets <c>gamepad-focused</c>, forcing off when chrome is inactive.
    /// </summary>
    public static void SetFocused(StyledElement element, bool focused) =>
        element.Classes.Set(FocusedClassName, IsActive && focused);
}
