namespace QuiverLauncher.Services;

public interface IFeatureNavigationHandler
{
    bool Navigate(NavigationDirection direction);
    bool Confirm();
    bool Cancel();
    bool Options();
    void RestoreFocus();
    bool SynchronizePointer(object? source) => false;
    bool EnterZone(GamepadZoneTransition transition) => false;
    void LeaveZone(GamepadNavigationZone nextZone) { }
}
