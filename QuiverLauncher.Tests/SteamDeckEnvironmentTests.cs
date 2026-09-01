using Avalonia.Controls;
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

    [Fact]
    public void IsDesktopMode_true_when_steam_deck_and_kde()
    {
        SteamDeckEnvironment.IsDesktopMode(
            isLinux: true,
            name => name switch
            {
                "SteamDeck" => "1",
                "XDG_CURRENT_DESKTOP" => "KDE",
                _ => null
            }).Should().BeTrue();
    }

    [Fact]
    public void IsDesktopMode_false_when_gamescope()
    {
        SteamDeckEnvironment.IsDesktopMode(
            isLinux: true,
            name => name switch
            {
                "SteamDeck" => "1",
                "XDG_CURRENT_DESKTOP" => "gamescope",
                _ => null
            }).Should().BeFalse();
    }

    [Fact]
    public void IsDesktopMode_false_when_not_linux()
    {
        SteamDeckEnvironment.IsDesktopMode(
            isLinux: false,
            name => name == "SteamDeck" ? "1" : null).Should().BeFalse();
    }

    [Fact]
    public void DisallowsExclusiveFullscreen_true_on_deck_kde_desktop()
    {
        SteamDeckEnvironment.DisallowsExclusiveFullscreen(
            isLinux: true,
            name => name switch
            {
                "SteamDeck" => "1",
                "XDG_CURRENT_DESKTOP" => "KDE",
                _ => null
            }).Should().BeTrue();
    }

    [Fact]
    public void DisallowsExclusiveFullscreen_false_on_deck_gamescope()
    {
        SteamDeckEnvironment.DisallowsExclusiveFullscreen(
            isLinux: true,
            name => name switch
            {
                "SteamDeck" => "1",
                "XDG_CURRENT_DESKTOP" => "gamescope",
                _ => null
            }).Should().BeFalse();
    }

    [Fact]
    public void DesktopFullscreenWindowState_maximized_on_deck_kde_desktop()
    {
        SteamDeckEnvironment.DesktopFullscreenWindowState(
            isLinux: true,
            name => name switch
            {
                "SteamDeck" => "1",
                "XDG_CURRENT_DESKTOP" => "KDE",
                _ => null
            }).Should().Be(WindowState.Maximized);
    }

    [Fact]
    public void DesktopFullscreenWindowState_fullscreen_on_deck_gamescope()
    {
        SteamDeckEnvironment.DesktopFullscreenWindowState(
            isLinux: true,
            name => name switch
            {
                "SteamDeck" => "1",
                "XDG_CURRENT_DESKTOP" => "gamescope",
                _ => null
            }).Should().Be(WindowState.FullScreen);
    }

    [Fact]
    public void DesktopFullscreenWindowState_fullscreen_when_not_linux()
    {
        SteamDeckEnvironment.DesktopFullscreenWindowState(
            isLinux: false,
            name => name == "SteamDeck" ? "1" : null).Should().Be(WindowState.FullScreen);
    }

    [Fact]
    public void DesktopFullscreenWindowState_fullscreen_on_linux_without_steam_deck()
    {
        SteamDeckEnvironment.DesktopFullscreenWindowState(
            isLinux: true,
            name => name == "XDG_CURRENT_DESKTOP" ? "KDE" : null).Should().Be(WindowState.FullScreen);
    }
}
