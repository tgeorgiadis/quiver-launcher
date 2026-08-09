namespace QuiverLauncher.Services;

public static class SettingsStoreProvider
{
    private static ISettingsStore? _default;

    public static ISettingsStore Default
    {
        get => _default ??= new FileSettingsStore(QuiverLauncherPaths.SettingsJsonPath);
        set => _default = value;
    }
}
