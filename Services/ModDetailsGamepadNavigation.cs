namespace QuiverLauncher.Services;

public enum ModDetailsGamepadSlot
{
    OpenPage,
    Close,
    Details,
    Changelog,
}

public readonly record struct ModDetailsGamepadMove(
    ModDetailsGamepadSlot? Slot,
    GamepadNavigationZone? LeaveZone,
    bool ScrollBody);

/// <summary>
/// Explicit 2x2 graph for the Mod Details header/tabs.
/// Open Page may be hidden when the package has no page URL.
/// </summary>
public static class ModDetailsGamepadNavigation
{
    public static ModDetailsGamepadMove Move(
        ModDetailsGamepadSlot current,
        NavigationDirection direction,
        bool openPageVisible)
    {
        if (current == ModDetailsGamepadSlot.OpenPage && !openPageVisible)
            current = ModDetailsGamepadSlot.Close;

        return (current, direction) switch
        {
            (ModDetailsGamepadSlot.Details, NavigationDirection.Right) => Stay(ModDetailsGamepadSlot.Changelog),
            (ModDetailsGamepadSlot.Changelog, NavigationDirection.Left) => Stay(ModDetailsGamepadSlot.Details),
            (ModDetailsGamepadSlot.Details, NavigationDirection.Up) =>
                Stay(openPageVisible ? ModDetailsGamepadSlot.OpenPage : ModDetailsGamepadSlot.Close),
            (ModDetailsGamepadSlot.Changelog, NavigationDirection.Up) => Stay(ModDetailsGamepadSlot.Close),
            (ModDetailsGamepadSlot.OpenPage, NavigationDirection.Down) => Stay(ModDetailsGamepadSlot.Details),
            (ModDetailsGamepadSlot.Close, NavigationDirection.Down) => Stay(ModDetailsGamepadSlot.Changelog),
            (ModDetailsGamepadSlot.OpenPage, NavigationDirection.Right) => Stay(ModDetailsGamepadSlot.Close),
            (ModDetailsGamepadSlot.Close, NavigationDirection.Left) =>
                Stay(openPageVisible ? ModDetailsGamepadSlot.OpenPage : ModDetailsGamepadSlot.Details),
            (ModDetailsGamepadSlot.Details, NavigationDirection.Down) => Scroll(),
            (ModDetailsGamepadSlot.Changelog, NavigationDirection.Down) => Scroll(),
            (ModDetailsGamepadSlot.OpenPage, NavigationDirection.Up) => Leave(GamepadNavigationZone.TopBar),
            (ModDetailsGamepadSlot.Close, NavigationDirection.Up) => Leave(GamepadNavigationZone.TopBar),
            (ModDetailsGamepadSlot.OpenPage, NavigationDirection.Left) => Leave(GamepadNavigationZone.Sidebar),
            (ModDetailsGamepadSlot.Details, NavigationDirection.Left) => Leave(GamepadNavigationZone.Sidebar),
            _ => Stay(current),
        };
    }

    public static ModDetailsGamepadSlot SlotReturningFromChrome(
        GamepadNavigationZone fromZone,
        bool openPageVisible)
    {
        if (fromZone == GamepadNavigationZone.Sidebar)
            return openPageVisible ? ModDetailsGamepadSlot.OpenPage : ModDetailsGamepadSlot.Details;

        if (fromZone is GamepadNavigationZone.TopBar or GamepadNavigationZone.AnnouncementBanner)
            return openPageVisible ? ModDetailsGamepadSlot.OpenPage : ModDetailsGamepadSlot.Close;

        return ModDetailsGamepadSlot.Details;
    }

    private static ModDetailsGamepadMove Stay(ModDetailsGamepadSlot slot) =>
        new(slot, LeaveZone: null, ScrollBody: false);

    private static ModDetailsGamepadMove Leave(GamepadNavigationZone zone) =>
        new(Slot: null, zone, ScrollBody: false);

    private static ModDetailsGamepadMove Scroll() =>
        new(Slot: null, LeaveZone: null, ScrollBody: true);
}
