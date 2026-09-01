using FluentAssertions;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class GamepadDebugLogTests
{
    [Fact]
    public void IsEnabled_true_when_QUIVER_GAMEPAD_DEBUG_set()
    {
        GamepadDebugLog.IsEnabled(
            isLinux: false,
            name => name == "QUIVER_GAMEPAD_DEBUG" ? "1" : null).Should().BeTrue();
    }

    [Fact]
    public void IsEnabled_true_on_linux_steam_deck()
    {
        GamepadDebugLog.IsEnabled(
            isLinux: true,
            name => name == "SteamDeck" ? "1" : null).Should().BeTrue();
    }

    [Fact]
    public void IsEnabled_false_on_linux_without_steam_deck_or_override()
    {
        GamepadDebugLog.IsEnabled(isLinux: true, _ => null).Should().BeFalse();
    }

    [Fact]
    public void Write_noop_when_disabled()
    {
        var written = false;
        GamepadDebugLog.Write("hello", enabled: false, _ => written = true);
        written.Should().BeFalse();
    }

    [Fact]
    public void Write_appends_when_enabled()
    {
        string? line = null;
        GamepadDebugLog.Write("nav dir=Right", enabled: true, text => line = text);
        line.Should().Contain("nav dir=Right");
    }

    [Fact]
    public void FormatEvent_includes_chrome_and_pads()
    {
        var line = GamepadDebugLog.FormatEvent(
            "nav",
            zone: "Sidebar",
            top: -1,
            focused: "LibraryNavButton",
            editing: false,
            gaming: true,
            skipFocus: true,
            chrome: true,
            pads: 1,
            extra: "dir=Down");

        line.Should().Be(
            "nav zone=Sidebar top=-1 focused=LibraryNavButton editing=False " +
            "gaming=True skipFocus=True chrome=True pads=1 dir=Down");
    }
}
