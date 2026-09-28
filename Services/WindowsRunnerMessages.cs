namespace QuiverLauncher.Services;

/// <summary>User-facing guidance for running Windows-only apps on Linux and macOS.</summary>
public static class WindowsRunnerMessages
{
    private const string LaunchOptionsHint =
        "open this app’s menu (⋯) → Launch Options → Windows Runner to pick a runner or custom command.";

    public static string InstallHint => OperatingSystem.IsMacOS()
        ? "Install CrossOver or Wine (for example Homebrew’s wine-stable)"
        : "Install Wine/Proton";

    public static string NotFound =>
        "The selected Windows executable needs a Windows runner, but none is configured or detected.\n\n" +
        $"{InstallHint}, or {LaunchOptionsHint}";

    public static string MissingBeforeInstall =>
        "This game requires a Windows runner to launch, but none was detected.\n\n" +
        $"{InstallHint}, or after install {LaunchOptionsHint}";

    public static string AutoDescription => OperatingSystem.IsMacOS()
        ? $"Auto (Wine, then CrossOver in a “{WindowsRunnerService.CrossOverBottleName}” bottle)"
        : "Auto (prefer Proton, then Wine)";

    /// <summary>Example shown in the custom command boxes.</summary>
    public static string CustomCommandExample => OperatingSystem.IsMacOS()
        ? "Example: \"/Applications/CrossOver.app/Contents/SharedSupport/CrossOver/bin/wine\" --bottle Games {exe}"
        : "Example: flatpak run com.usebottles.bottles -e {exe}";
}
