using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace QuiverLauncher.Services;

/// <summary>
/// Maps a mouse click onto gamepad/keyboard highlight targets without stealing
/// the pointer's focus (Park would yank focus off Play/Options mid-click).
/// </summary>
internal static class GamepadPointerFocusSync
{
    public static bool Hit(GamepadNavigationService navigation, IReadOnlyList<Control> controls,
        GamepadNavigationZone zone, int currentIndex, Action<int> apply, object? source)
    {
        var index = GamepadControlActivation.IndexOfControlContainingFocus(controls, source);
        if (index < 0) return false;
        if (navigation.ActiveZone == zone && currentIndex == index) return true;
        if (controls[index] is not TextBox) GamepadTextInput.Reset();
        apply(index);
        return true;
    }

    public static bool Card<T>(GamepadNavigationService navigation, IReadOnlyList<T> items,
        GamepadNavigationZone zone, int currentIndex, Action<int> apply, object? source) where T : class
    {
        var index = IndexOfDataContext(items, source as Visual);
        if (index < 0) return false;
        if (navigation.ActiveZone == zone && currentIndex == index) return true;
        GamepadTextInput.Reset();
        apply(index);
        return true;
    }

    public static T? FindDataContextInAncestors<T>(Visual? source) where T : class
    {
        for (var visual = source; visual != null; visual = visual.GetVisualParent())
        {
            if (visual is Control { DataContext: T match })
                return match;
        }

        return null;
    }

    public static int IndexOfDataContext<T>(IReadOnlyList<T> items, Visual? source)
        where T : class
    {
        var match = FindDataContextInAncestors<T>(source);
        if (match == null || items.Count == 0)
            return -1;

        for (var i = 0; i < items.Count; i++)
        {
            if (ReferenceEquals(items[i], match))
                return i;
        }

        return -1;
    }

    /// <summary>
    /// Card Apply* methods park keyboard focus on a sink. Pointer sync must skip
    /// that so nested buttons still receive the click.
    /// </summary>
    public static void ApplyCardSelectionFocus(Control? sink, bool stealFocus)
    {
        if (stealFocus)
            GamepadCardFocusSink.Park(sink);
    }
}
