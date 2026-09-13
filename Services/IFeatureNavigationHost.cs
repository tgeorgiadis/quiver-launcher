using Avalonia.Controls;

namespace QuiverLauncher.Services;

/// <summary>Shell operations available to local feature navigation without exposing shell controls.</summary>
public interface IFeatureNavigationHost
{
    GamepadNavigationService Navigation { get; }
    GamepadNavigationZone MainContentZone { get; }
    bool IsFocusActive { get; }
    bool ApplyTransition(GamepadZoneTransition transition);
    void ClearFocus();
    void ClearSidebarFocus();
    void FocusCard(bool stealFocus);
}
