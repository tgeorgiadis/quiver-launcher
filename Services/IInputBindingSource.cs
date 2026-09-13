namespace QuiverLauncher.Services;

public interface IInputBindingSource
{
    event Action<GamepadBinding>? OnRawInput;
    void SetCaptureMode(bool enabled);
    void SetGamepadEnabled(bool enabled);
    void ApplyBindings(IReadOnlyDictionary<GamepadAction, List<GamepadBinding>>? bindings);
    IReadOnlyList<ConnectedGamepadInfo> GetConnectedGamepads();
}
