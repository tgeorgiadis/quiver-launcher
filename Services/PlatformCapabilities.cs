namespace QuiverLauncher.Services;

/// <summary>
/// Runtime feature switches for desktop vs Android.
/// </summary>
public static class PlatformCapabilities
{
    public static bool IsMobile => OperatingSystem.IsAndroid();

    public static bool IsDesktop => !IsMobile;

    public static bool SupportsTray => !IsMobile;

    public static bool SupportsVelopack => !IsMobile;

    public static bool SupportsFolderInstall => !IsMobile;

    public static bool SupportsWine => OperatingSystem.IsLinux() && !IsMobile;

    public static bool SupportsSteamShortcuts =>
        !IsMobile && (OperatingSystem.IsWindows() || OperatingSystem.IsLinux());

    public static bool SupportsModsFolder => !IsMobile;

    public static bool SupportsGamepadSdl => !IsMobile;
}
