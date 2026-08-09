using FluentAssertions;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class SteamDeckEnvironmentTests
{
    [Fact]
    public void IsGamingMode_false_when_not_linux()
    {
        SteamDeckEnvironment.IsGamingMode(
            isLinux: false,
            _ => "gamescope").Should().BeFalse();
    }

    [Theory]
    [InlineData("SteamGamepadUI")]
    [InlineData("SteamOS")]
    public void IsGamingMode_false_when_only_steam_env_set(string envName)
    {
        SteamDeckEnvironment.IsGamingMode(
            isLinux: true,
            name => name == envName ? "1" : null).Should().BeFalse();
    }

    [Fact]
    public void IsGamingMode_false_when_steam_os_with_kde_desktop()
    {
        SteamDeckEnvironment.IsGamingMode(
            isLinux: true,
            name => name switch
            {
                "SteamOS" => "1",
                "XDG_CURRENT_DESKTOP" => "KDE",
                _ => null
            }).Should().BeFalse();
    }

    [Fact]
    public void IsGamingMode_true_when_gamescope_desktop()
    {
        SteamDeckEnvironment.IsGamingMode(
            isLinux: true,
            name => name == "XDG_CURRENT_DESKTOP" ? "gamescope" : null).Should().BeTrue();
    }

    [Fact]
    public void IsGamingMode_true_when_session_desktop_gamescope()
    {
        SteamDeckEnvironment.IsGamingMode(
            isLinux: true,
            name => name == "XDG_SESSION_DESKTOP" ? "gamescope" : null).Should().BeTrue();
    }

    [Fact]
    public void IsGamingMode_true_when_gamescope_wayland_display_set()
    {
        SteamDeckEnvironment.IsGamingMode(
            isLinux: true,
            name => name == "GAMESCOPE_WAYLAND_DISPLAY" ? "gamescope-0" : null).Should().BeTrue();
    }

    [Fact]
    public void IsGamingMode_false_when_only_SteamDeck_set()
    {
        SteamDeckEnvironment.IsGamingMode(
            isLinux: true,
            name => name == "SteamDeck" ? "1" : null).Should().BeFalse();
    }

    [Fact]
    public void IsGamingMode_false_when_linux_without_gaming_mode_env()
    {
        SteamDeckEnvironment.IsGamingMode(
            isLinux: true,
            _ => null).Should().BeFalse();
    }
}
