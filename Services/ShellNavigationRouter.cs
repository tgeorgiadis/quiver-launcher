using QuiverLauncher.ViewModels;

namespace QuiverLauncher.Services;

/// <summary>Routes shell input to feature-owned handlers, preserving overlay and chrome precedence.</summary>
public sealed class ShellNavigationRouter(ShellViewModel shell, GamepadNavigationService navigation,
    IReadOnlyDictionary<GamepadNavigationZone, Func<IFeatureNavigationHandler>> handlers,
    Func<bool> displayFilterOpen, Action clearChromeFocus, Action restoreMainFocus)
{
    private IFeatureNavigationHandler? For(GamepadNavigationZone zone) => handlers.TryGetValue(zone, out var handler) ? handler() : null;
    private static bool IsChrome(GamepadNavigationZone zone) => zone is GamepadNavigationZone.Sidebar or GamepadNavigationZone.TopBar or GamepadNavigationZone.AnnouncementBanner;
    public GamepadNavigationZone MainZone => shell.ModDetailsOpen ? GamepadNavigationZone.ModsDetailsOverlay
        : shell.BrowseDetailsOpen ? GamepadNavigationZone.BrowseDetailsOverlay
        : shell.Mode == MainViewMode.Library ? shell.ModsOpen ? GamepadNavigationZone.ModsOverlayList
            : shell.AppUpdatesOpen ? GamepadNavigationZone.AppUpdatesReviewList : GamepadNavigationZone.Library
        : GamepadNavigationZone.BrowseGrid;
    private IFeatureNavigationHandler? Enter(GamepadNavigationZone zone)
    {
        navigation.ActiveZone = zone;
        return For(zone);
    }
    public bool ApplyTransition(GamepadZoneTransition transition)
    {
        For(navigation.ActiveZone)?.LeaveZone(transition.Zone);
        return For(transition.Zone)?.EnterZone(transition) ?? false;
    }
    public void RestoreCurrentFocus(bool bringIntoView = true)
    {
        var zone = navigation.ActiveZone;
        if (!bringIntoView)
        {
            var overlay = displayFilterOpen() ? GamepadNavigationZone.DisplayFilterOverlay
                : shell.EntryEditorOpen ? GamepadNavigationZone.EntryFormOverlay
                : shell.TagEditorOpen ? GamepadNavigationZone.TagEditOverlay
                : shell.SettingsOpen ? GamepadNavigationZone.Settings : (GamepadNavigationZone?)null;
            if (overlay.HasValue)
            {
                For(overlay.Value)?.RestoreFocus(bringIntoView: false);
                return;
            }
        }
        // Activation restores selection without re-entering or scrolling the list.
        if (!bringIntoView && !IsChrome(zone) &&
            MainZone is GamepadNavigationZone.Library or GamepadNavigationZone.BrowseGrid)
        {
            For(MainZone)?.RestoreFocus(bringIntoView: false);
            return;
        }
        if (IsChrome(zone) || zone is GamepadNavigationZone.ModsOverlayToolbar or GamepadNavigationZone.ModsOverlayFilters
            or GamepadNavigationZone.ModsOverlaySourceFilters or GamepadNavigationZone.ModsOverlayList
            or GamepadNavigationZone.ModsOverlayRowActions or GamepadNavigationZone.ModsDetailsOverlay
            or GamepadNavigationZone.BrowseDetailsOverlay) For(zone)?.RestoreFocus();
        else restoreMainFocus();
    }
    public bool Navigate(NavigationDirection direction)
    {
        IFeatureNavigationHandler? feature;
        if (displayFilterOpen()) feature = Enter(GamepadNavigationZone.DisplayFilterOverlay);
        else if (shell.ModDetailsOpen || shell.BrowseDetailsOpen) feature = IsChrome(navigation.ActiveZone) ? For(navigation.ActiveZone) : Enter(MainZone);
        else if (shell.EntryEditorOpen) feature = Enter(GamepadNavigationZone.EntryFormOverlay);
        else if (shell.TagEditorOpen) feature = Enter(GamepadNavigationZone.TagEditOverlay);
        else if (shell.SettingsOpen) feature = Enter(GamepadNavigationZone.Settings);
        else feature = IsChrome(navigation.ActiveZone) ? For(navigation.ActiveZone) : For(MainZone);
        return feature?.Navigate(direction) ?? false;
    }
    public bool ConfirmPriorityDetails()
    {
        var zone = navigation.ActiveZone;
        // Details opened with the mouse leave the zone behind; Confirm still belongs to the details.
        if (shell.BrowseDetailsOpen && zone != GamepadNavigationZone.BrowseDetailsOverlay && !IsChrome(zone))
            return Enter(GamepadNavigationZone.BrowseDetailsOverlay)?.Confirm() ?? false;
        if ((shell.ModDetailsOpen && zone == GamepadNavigationZone.ModsDetailsOverlay)
            || (shell.BrowseDetailsOpen && zone == GamepadNavigationZone.BrowseDetailsOverlay))
            return For(zone)?.Confirm() ?? false;
        return false;
    }
    public bool OptionsPriorityDetails()
    {
        var zone = shell.ModDetailsOpen ? GamepadNavigationZone.ModsDetailsOverlay
            : shell.BrowseDetailsOpen ? GamepadNavigationZone.BrowseDetailsOverlay : (GamepadNavigationZone?)null;
        return zone.HasValue && For(zone.Value)?.Options() == true;
    }
    public bool OptionsFeature() => For(navigation.ActiveZone)?.Options() ?? false;
    public bool SynchronizePointer(object? source)
    {
        GamepadNavigationZone? overlay = displayFilterOpen() ? GamepadNavigationZone.DisplayFilterOverlay
            : shell.BrowseDetailsOpen ? GamepadNavigationZone.BrowseDetailsOverlay
            : shell.ModDetailsOpen ? GamepadNavigationZone.ModsDetailsOverlay
            : shell.EntryEditorOpen ? GamepadNavigationZone.EntryFormOverlay
            : shell.TagEditorOpen ? GamepadNavigationZone.TagEditOverlay
            : shell.SettingsOpen ? GamepadNavigationZone.Settings : null;
        if (overlay is { } zone) return For(zone)?.SynchronizePointer(source) ?? false;
        if (MainZone != GamepadNavigationZone.Library && For(MainZone)?.SynchronizePointer(source) == true) return true;
        if (For(GamepadNavigationZone.Sidebar)?.SynchronizePointer(source) == true) return true;
        return MainZone == GamepadNavigationZone.Library && For(MainZone)?.SynchronizePointer(source) == true;
    }
    public bool ConfirmFeature(bool allowChrome, object? focusedControl = null)
    {
        if (shell.SettingsOpen) return Enter(GamepadNavigationZone.Settings)?.Confirm() ?? false;
        if (displayFilterOpen()) return Enter(GamepadNavigationZone.DisplayFilterOverlay)?.Confirm() ?? false;
        if (shell.EntryEditorOpen) return Enter(GamepadNavigationZone.EntryFormOverlay)?.Confirm() ?? false;
        if (shell.TagEditorOpen) return Enter(GamepadNavigationZone.TagEditOverlay)?.Confirm() ?? false;
        // Tab/native keyboard focus can reach the banner without entering its
        // controller zone. Confirm the focused banner action, not the stale zone.
        if (allowChrome && !shell.ModDetailsOpen && !shell.BrowseDetailsOpen &&
            focusedControl != null && For(GamepadNavigationZone.AnnouncementBanner)?.SynchronizePointer(focusedControl) == true)
            return For(GamepadNavigationZone.AnnouncementBanner)?.Confirm() ?? false;
        return allowChrome && (For(navigation.ActiveZone)?.Confirm() ?? false);
    }
    public bool CancelFeature(bool allowChrome)
    {
        if (displayFilterOpen()) return For(GamepadNavigationZone.DisplayFilterOverlay)?.Cancel() ?? false;
        if (shell.EntryEditorOpen) return For(GamepadNavigationZone.EntryFormOverlay)?.Cancel() ?? false;
        if (shell.TagEditorOpen) return For(GamepadNavigationZone.TagEditOverlay)?.Cancel() ?? false;
        if (shell.ModsOpen) return For(GamepadNavigationZone.ModsOverlayList)?.Cancel() ?? false;
        if (shell.SettingsOpen) return For(GamepadNavigationZone.Settings)?.Cancel() ?? false;
        if (!allowChrome) return false;
        if (IsChrome(navigation.ActiveZone))
        {
            clearChromeFocus();
            navigation.ActiveZone = MainZone;
            restoreMainFocus();
            return true;
        }
        return For(navigation.ActiveZone)?.Cancel() ?? false;
    }
}
