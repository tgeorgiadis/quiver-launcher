using System.Diagnostics;

namespace QuiverLauncher.Services;

/// <summary>
/// Opens Steam's on-screen keyboard on Steam Deck / Gamescope.
/// Avalonia X11 TextBoxes do not participate in Steam's text-input path, so the OSK
/// must be requested explicitly (see Valve gamescope#668).
/// </summary>
internal static class SteamOnScreenKeyboard
{
    public const string OpenKeyboardUri = "steam://open/keyboard";

    /// <summary>
    /// Starts a process. Returns exit code when waited; null when fire-and-forget without wait.
    /// Throws on launch failure.
    /// </summary>
    internal delegate int? ProcessStarter(string fileName, string argument);

    public static bool ShouldOffer() =>
        ShouldOffer(OperatingSystem.IsLinux(), Environment.GetEnvironmentVariable);

    public static bool ShouldOffer(bool isLinux, Func<string, string?> getEnvironmentVariable)
    {
        if (!isLinux)
            return false;

        if (!string.IsNullOrWhiteSpace(getEnvironmentVariable("SteamGameId")) ||
            !string.IsNullOrWhiteSpace(getEnvironmentVariable("SteamAppId")) ||
            !string.IsNullOrWhiteSpace(getEnvironmentVariable("SteamOverlayGameId")) ||
            !string.IsNullOrWhiteSpace(getEnvironmentVariable("SteamDeck")))
        {
            return true;
        }

        var desktop = getEnvironmentVariable("XDG_CURRENT_DESKTOP");
        return string.Equals(desktop, "gamescope", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Requests Steam's OSK when running under Steam/Gamescope on Linux.
    /// Returns true if an open was attempted.
    /// </summary>
    public static bool TryOpen(Action<string>? openUri = null) =>
        TryOpen(ShouldOffer(), openUri);

    public static bool TryOpen(bool shouldOffer, Action<string>? openUri)
    {
        if (!shouldOffer)
            return false;

        try
        {
            if (openUri != null)
                openUri(OpenKeyboardUri);
            else
                OpenUriDefault(OpenKeyboardUri);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Steam OSK open failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Opens a URI with Linux-friendly tools first (xdg-open, then steam CLI),
    /// falling back to UseShellExecute elsewhere.
    /// </summary>
    internal static void OpenUriDefault(string uri, ProcessStarter? startProcess = null)
    {
        if (OperatingSystem.IsLinux())
        {
            OpenUriOnLinux(uri, startProcess ?? StartProcessAndWait);
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = uri,
            UseShellExecute = true,
        });
    }

    internal static void OpenUriOnLinux(string uri, ProcessStarter startProcess)
    {
        try
        {
            var exitCode = startProcess("xdg-open", uri);
            if (exitCode is null or 0)
                return;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"xdg-open Steam OSK failed: {ex.Message}");
        }

        startProcess("steam", uri);
    }

    private static int? StartProcessAndWait(string fileName, string argument)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = fileName,
            ArgumentList = { argument },
            UseShellExecute = false,
        });

        if (process == null)
            throw new InvalidOperationException($"Failed to start '{fileName}'.");

        // xdg-open / steam usually exit quickly after handing off to the handler.
        if (!process.WaitForExit(3000))
            return null;

        return process.ExitCode;
    }
}
