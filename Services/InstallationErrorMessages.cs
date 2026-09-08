using System.Runtime.InteropServices;

namespace QuiverLauncher.Services;

public static class InstallationErrorMessages
{
    private const string WindowsDefenderGuidance =
        "\n\nIf Windows Defender blocked this download, open Windows Security → Protection history, find the blocked item, and choose Allow if you believe it is safe.";

    private const string FileLockGuidance =
        "\n\nWindows may still be scanning the downloaded archive. Wait a few seconds and try installing again.";

    public static string FormatInstallationError(string gameName, string errorMessage)
    {
        var message = $"Error installing {gameName}: {errorMessage}";
        if (ShouldIncludeWindowsDefenderGuidance(errorMessage))
            message += WindowsDefenderGuidance;
        else if (ShouldIncludeFileLockGuidance(errorMessage))
            message += FileLockGuidance;

        return message;
    }

    internal static bool IsLikelyWindowsDefenderBlock(string errorMessage) =>
        errorMessage.Contains("virus", StringComparison.OrdinalIgnoreCase)
        || errorMessage.Contains("potentially unwanted", StringComparison.OrdinalIgnoreCase)
        || errorMessage.Contains("Windows Defender", StringComparison.OrdinalIgnoreCase);

    internal static bool IsLikelyFileLock(string errorMessage) =>
        errorMessage.Contains("being used by another process", StringComparison.OrdinalIgnoreCase)
        || errorMessage.Contains("still locked by another process", StringComparison.OrdinalIgnoreCase)
        || errorMessage.Contains("lock violation", StringComparison.OrdinalIgnoreCase);

    private static bool ShouldIncludeWindowsDefenderGuidance(string errorMessage) =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        && IsLikelyWindowsDefenderBlock(errorMessage);

    private static bool ShouldIncludeFileLockGuidance(string errorMessage) =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        && IsLikelyFileLock(errorMessage);
}
