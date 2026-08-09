using System.Runtime.InteropServices;

namespace QuiverLauncher.Services;

public static class InstallationErrorMessages
{
    private const string WindowsDefenderGuidance =
        "\n\nIf Windows Defender blocked this download, open Windows Security → Protection history, find the blocked item, and choose Allow if you believe it is safe.";

    public static string FormatInstallationError(string gameName, string errorMessage)
    {
        var message = $"Error installing {gameName}: {errorMessage}";
        if (ShouldIncludeWindowsDefenderGuidance(errorMessage))
            message += WindowsDefenderGuidance;

        return message;
    }

    internal static bool IsLikelyWindowsDefenderBlock(string errorMessage) =>
        errorMessage.Contains("virus", StringComparison.OrdinalIgnoreCase)
        || errorMessage.Contains("potentially unwanted", StringComparison.OrdinalIgnoreCase)
        || errorMessage.Contains("Windows Defender", StringComparison.OrdinalIgnoreCase);

    private static bool ShouldIncludeWindowsDefenderGuidance(string errorMessage) =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        && IsLikelyWindowsDefenderBlock(errorMessage);
}
