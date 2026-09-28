namespace QuiverLauncher.Services;

/// <summary>
/// Runtime feature switches for desktop vs Android.
/// </summary>
public static class PlatformCapabilities
{
    public static bool IsMobile => OperatingSystem.IsAndroid();

    public static bool IsDesktop => !IsMobile;

    public static string InstalledAppRemovalLabel => IsMobile ? "Uninstall" : "Delete";

    public static bool SupportsTray => !IsMobile;

    public static bool SupportsVelopack => !IsMobile;

    public static bool SupportsFolderInstall => !IsMobile;

    /// <summary>Windows-only apps can run through Wine/Proton (Linux) or Wine/CrossOver (macOS).</summary>
    public static bool SupportsWine => (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) && !IsMobile;

    public static bool SupportsSteamShortcuts =>
        !IsMobile && (OperatingSystem.IsWindows() || OperatingSystem.IsLinux());

    public static bool SupportsModsFolder => !IsMobile;

    public static bool SupportsGamepadSdl => !IsMobile;
}
