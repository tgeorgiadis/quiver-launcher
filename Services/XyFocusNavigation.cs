using Avalonia.Controls;
using Avalonia.Input;
using AvaloniaNav = Avalonia.Input.NavigationDirection;

namespace QuiverLauncher.Services;

/// <summary>
/// Bridges SDL/keyboard <see cref="NavigationDirection"/> into Avalonia 12 XYFocus.
/// </summary>
internal static class XyFocusNavigation
{
    public static AvaloniaNav ToAvalonia(NavigationDirection direction) =>
        direction switch
        {
            NavigationDirection.Up => AvaloniaNav.Up,
            NavigationDirection.Down => AvaloniaNav.Down,
            NavigationDirection.Left => AvaloniaNav.Left,
            NavigationDirection.Right => AvaloniaNav.Right,
            _ => AvaloniaNav.Down,
        };

    public static FindNextElementOptions CreateOptions(
        InputElement? searchRoot,
        IInputElement? from = null) =>
        new()
        {
            SearchRoot = searchRoot,
            FocusedElement = from,
            NavigationStrategyOverride = XYFocusNavigationStrategy.Projection,
        };

    /// <summary>
    /// Moves keyboard focus in <paramref name="direction"/> within <paramref name="searchRoot"/>.
    /// </summary>
    public static bool TryMove(
        Control host,
        NavigationDirection direction,
        InputElement? searchRoot = null)
    {
        var focusManager = TopLevel.GetTopLevel(host)?.FocusManager;
        if (focusManager == null)
            return false;

        return focusManager.TryMoveFocus(
            ToAvalonia(direction),
            CreateOptions(searchRoot ?? host));
    }

    public static IInputElement? FindNext(
        Control host,
        NavigationDirection direction,
        InputElement? searchRoot = null,
        IInputElement? from = null)
    {
        var focusManager = TopLevel.GetTopLevel(host)?.FocusManager;
        return focusManager?.FindNextElement(
            ToAvalonia(direction),
            CreateOptions(searchRoot ?? host, from));
    }

    /// <summary>
    /// Shell chrome: gamepad/remote only. Keyboard arrows stay with the zone interceptor
    /// so Avalonia does not walk to Continue while cards hold <c>IsGamepadFocused</c>.
    /// </summary>
    public static void EnableOn(InputElement element) =>
        EnableOn(element, XYFocusNavigationModes.Gamepad | XYFocusNavigationModes.Remote);

    /// <summary>
    /// Modal dialogs own their KeyDown tunnel and still want Avalonia keyboard XYFocus.
    /// </summary>
    public static void EnableOnForDialog(InputElement element) =>
        EnableOn(element, XYFocusNavigationModes.Enabled);

    private static void EnableOn(InputElement element, XYFocusNavigationModes modes)
    {
        XYFocus.SetNavigationModes(element, modes);
        XYFocus.SetUpNavigationStrategy(element, XYFocusNavigationStrategy.Projection);
        XYFocus.SetDownNavigationStrategy(element, XYFocusNavigationStrategy.Projection);
        XYFocus.SetLeftNavigationStrategy(element, XYFocusNavigationStrategy.Projection);
        XYFocus.SetRightNavigationStrategy(element, XYFocusNavigationStrategy.Projection);
    }
}
