using Avalonia.Controls;

namespace QuiverLauncher.Services;

/// <summary>
/// Detects Steam Deck / SteamOS Gaming Mode (Gamescope) sessions.
/// Distinct from <see cref="SteamOnScreenKeyboard.ShouldOffer"/>, which also
/// matches Desktop Mode where <c>SteamDeck=1</c> is set.
/// </summary>
/// <remarks>
/// Do not treat <c>SteamOS</c> or <c>SteamGamepadUI</c> alone as Gaming Mode —
/// those can be set in Desktop Mode (KDE) as well. Rely on Gamescope session
/// markers only.
/// </remarks>
internal static class SteamDeckEnvironment
{
    public static bool IsGamingMode() =>
        IsGamingMode(OperatingSystem.IsLinux(), Environment.GetEnvironmentVariable);

    public static bool IsGamingMode(bool isLinux, Func<string, string?> getEnvironmentVariable)
    {
        if (!isLinux)
            return false;

        if (LooksLikeGamescopeDesktop(getEnvironmentVariable("XDG_CURRENT_DESKTOP")) ||
            LooksLikeGamescopeDesktop(getEnvironmentVariable("XDG_SESSION_DESKTOP")))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(getEnvironmentVariable("GAMESCOPE_WAYLAND_DISPLAY"));
    }

    /// <summary>
    /// True on Steam Deck KDE Desktop Mode. Exclusive <see cref="WindowState.FullScreen"/>
    /// covers the display so Steam's on-screen keyboard appears behind the window.
    /// </summary>
    public static bool IsDesktopMode() =>
        IsDesktopMode(OperatingSystem.IsLinux(), Environment.GetEnvironmentVariable);

    public static bool IsDesktopMode(bool isLinux, Func<string, string?> getEnvironmentVariable)
    {
        if (!isLinux)
            return false;

        if (string.IsNullOrWhiteSpace(getEnvironmentVariable("SteamDeck")))
            return false;

        return !IsGamingMode(isLinux, getEnvironmentVariable);
    }

    /// <summary>
    /// Steam Deck Desktop cannot use exclusive <see cref="WindowState.FullScreen"/>
    /// (the compositor then hides the on-screen keyboard behind the window).
    /// </summary>
    public static bool DisallowsExclusiveFullscreen() =>
        DisallowsExclusiveFullscreen(OperatingSystem.IsLinux(), Environment.GetEnvironmentVariable);

    public static bool DisallowsExclusiveFullscreen(
        bool isLinux,
        Func<string, string?> getEnvironmentVariable) =>
        IsDesktopMode(isLinux, getEnvironmentVariable);

    /// <summary>
    /// Window state to apply when Start in Fullscreen is requested.
    /// Deck Desktop uses <see cref="WindowState.Maximized"/> so the KDE taskbar
    /// stays visible and Steam's on-screen keyboard can appear above the window.
    /// Other platforms use exclusive <see cref="WindowState.FullScreen"/>.
    /// </summary>
    public static WindowState DesktopFullscreenWindowState() =>
        DesktopFullscreenWindowState(OperatingSystem.IsLinux(), Environment.GetEnvironmentVariable);

    public static WindowState DesktopFullscreenWindowState(
        bool isLinux,
        Func<string, string?> getEnvironmentVariable) =>
        DisallowsExclusiveFullscreen(isLinux, getEnvironmentVariable)
            ? WindowState.Maximized
            : WindowState.FullScreen;

    private static bool LooksLikeGamescopeDesktop(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Contains("gamescope", StringComparison.OrdinalIgnoreCase);
}
