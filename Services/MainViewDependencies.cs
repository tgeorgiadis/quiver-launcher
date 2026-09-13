namespace QuiverLauncher.Services;

/// <summary>Host composition options. Injected services remain owned by their caller.</summary>
public sealed class MainViewDependencies
{
    public ISettingsStore SettingsStore { get; init; } = SettingsStoreProvider.Default;
    public GameManager? GameManager { get; init; }
    public bool EnableInput { get; init; } = true;
    public bool EnableMusic { get; init; } = true;
    public bool InitializeOnOpen { get; init; } = true;
}
