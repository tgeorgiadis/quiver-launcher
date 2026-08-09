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

    private static bool LooksLikeGamescopeDesktop(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Contains("gamescope", StringComparison.OrdinalIgnoreCase);
}
