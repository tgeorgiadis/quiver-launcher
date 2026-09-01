namespace QuiverLauncher.Services;

/// <summary>
/// Short gamepad navigation traces on Steam Deck (or when QUIVER_GAMEPAD_DEBUG is set).
/// Written beside user data as <c>gamepad-debug.log</c> for SSH tail.
/// </summary>
internal static class GamepadDebugLog
{
    public const string FileName = "gamepad-debug.log";

    public static string LogPath => Path.Combine(QuiverLauncherPaths.UserDataRoot, FileName);

    public static bool IsEnabled() =>
        IsEnabled(OperatingSystem.IsLinux(), Environment.GetEnvironmentVariable);

    public static bool IsEnabled(bool isLinux, Func<string, string?> getEnvironmentVariable)
    {
        if (!string.IsNullOrWhiteSpace(getEnvironmentVariable("QUIVER_GAMEPAD_DEBUG")))
            return true;

        return isLinux && !string.IsNullOrWhiteSpace(getEnvironmentVariable("SteamDeck"));
    }

    public static void Write(string message)
    {
        if (!IsEnabled())
            return;

        Write(message, IsEnabled(), appendAllText: null);
    }

    public static string FormatEvent(
        string eventName,
        string zone,
        int top,
        string focused,
        bool editing,
        bool gaming,
        bool skipFocus,
        bool chrome,
        int pads,
        string extra = "")
    {
        var line =
            $"{eventName} zone={zone} top={top} focused={focused} editing={editing} " +
            $"gaming={gaming} skipFocus={skipFocus} chrome={chrome} pads={pads}";
        if (!string.IsNullOrWhiteSpace(extra))
            line += $" {extra.Trim()}";
        return line;
    }

    public static void Write(string message, bool enabled, Action<string>? appendAllText)
    {
        if (!enabled)
            return;

        var line = $"[{DateTime.UtcNow:O}] {message}{Environment.NewLine}";
        try
        {
            if (appendAllText != null)
            {
                appendAllText(line);
                return;
            }

            QuiverLauncherPaths.EnsureUserDataRootExists();
            File.AppendAllText(LogPath, line);
        }
        catch
        {
            // Best-effort tracing only.
        }
    }
}
