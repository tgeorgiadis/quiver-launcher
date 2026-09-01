using FluentAssertions;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class SteamDeckSdlHintsTests
{
    [Fact]
    public void GetHidapiSteamHintValue_null_when_not_linux()
    {
        SteamDeckSdlHints.GetHidapiSteamHintValue(
            isLinux: false,
            _ => "1",
            (_, _) => false).Should().BeNull();
    }

    [Fact]
    public void GetHidapiSteamHintValue_null_when_SteamDeck_unset()
    {
        SteamDeckSdlHints.GetHidapiSteamHintValue(
            isLinux: true,
            _ => null,
            (_, _) => false).Should().BeNull();
    }

    [Fact]
    public void GetHidapiSteamHintValue_disables_hidapi_on_desktop_mode()
    {
        SteamDeckSdlHints.GetHidapiSteamHintValue(
            isLinux: true,
            name => name == "SteamDeck" ? "1" : null,
            (_, _) => false).Should().Be(SteamDeckSdlHints.DisableHidapiSteamValue);
    }

    [Fact]
    public void GetHidapiSteamHintValue_null_in_gaming_mode()
    {
        SteamDeckSdlHints.GetHidapiSteamHintValue(
            isLinux: true,
            name => name == "SteamDeck" ? "1" : null,
            (_, _) => true).Should().BeNull();
    }

    [Fact]
    public void ApplyBeforeInit_sets_hint_when_value_present()
    {
        string? setName = null;
        string? setValue = null;

        var applied = SteamDeckSdlHints.ApplyBeforeInit(
            SteamDeckSdlHints.DisableHidapiSteamValue,
            (name, value) =>
            {
                setName = name;
                setValue = value;
            });

        applied.Should().BeTrue();
        setName.Should().Be(SteamDeckSdlHints.JoystickHidapiSteamHintName);
        setValue.Should().Be(SteamDeckSdlHints.DisableHidapiSteamValue);
    }

    [Fact]
    public void ApplyBeforeInit_noop_when_no_override()
    {
        var called = false;

        var applied = SteamDeckSdlHints.ApplyBeforeInit(
            hidapiSteamHintValue: null,
            (_, _) => called = true);

        applied.Should().BeFalse();
        called.Should().BeFalse();
    }

    [Fact]
    public void GetAllowSteamVirtualGamepadHintValue_null_when_not_linux()
    {
        SteamDeckSdlHints.GetAllowSteamVirtualGamepadHintValue(
            isLinux: false,
            _ => "1",
            (_, _) => true).Should().BeNull();
    }

    [Fact]
    public void GetAllowSteamVirtualGamepadHintValue_null_on_desktop_mode()
    {
        SteamDeckSdlHints.GetAllowSteamVirtualGamepadHintValue(
            isLinux: true,
            name => name == "SteamDeck" ? "1" : null,
            (_, _) => false).Should().BeNull();
    }

    [Fact]
    public void GetAllowSteamVirtualGamepadHintValue_allows_virtual_pad_in_gaming_mode()
    {
        SteamDeckSdlHints.GetAllowSteamVirtualGamepadHintValue(
            isLinux: true,
            name => name == "SteamDeck" ? "1" : null,
            (_, _) => true).Should().Be(SteamDeckSdlHints.AllowSteamVirtualGamepadValue);
    }

    [Fact]
    public void GetAllowSteamVirtualGamepadHintValue_uses_real_gaming_mode_detector_for_gamescope()
    {
        SteamDeckSdlHints.GetAllowSteamVirtualGamepadHintValue(
            isLinux: true,
            name => name switch
            {
                "SteamDeck" => "1",
                "XDG_CURRENT_DESKTOP" => "gamescope",
                _ => null
            },
            SteamDeckEnvironment.IsGamingMode).Should().Be(SteamDeckSdlHints.AllowSteamVirtualGamepadValue);
    }

    [Fact]
    public void GetAllowSteamVirtualGamepadHintValue_uses_real_gaming_mode_detector_for_desktop()
    {
        SteamDeckSdlHints.GetAllowSteamVirtualGamepadHintValue(
            isLinux: true,
            name => name switch
            {
                "SteamDeck" => "1",
                "XDG_CURRENT_DESKTOP" => "KDE",
                _ => null
            },
            SteamDeckEnvironment.IsGamingMode).Should().BeNull();
    }

    [Fact]
    public void ApplyBeforeInit_sets_virtual_gamepad_hint()
    {
        string? setName = null;
        string? setValue = null;

        var applied = SteamDeckSdlHints.ApplyBeforeInit(
            hidapiSteamHintValue: null,
            SteamDeckSdlHints.AllowSteamVirtualGamepadValue,
            (name, value) =>
            {
                setName = name;
                setValue = value;
            });

        applied.Should().BeTrue();
        setName.Should().Be(SteamDeckSdlHints.AllowSteamVirtualGamepadHintName);
        setValue.Should().Be(SteamDeckSdlHints.AllowSteamVirtualGamepadValue);
    }

    [Fact]
    public void GetHidapiSteamHintValue_uses_real_gaming_mode_detector_for_desktop()
    {
        SteamDeckSdlHints.GetHidapiSteamHintValue(
            isLinux: true,
            name => name switch
            {
                "SteamDeck" => "1",
                "XDG_CURRENT_DESKTOP" => "KDE",
                _ => null
            },
            SteamDeckEnvironment.IsGamingMode).Should().Be(SteamDeckSdlHints.DisableHidapiSteamValue);
    }

    [Fact]
    public void GetHidapiSteamHintValue_uses_real_gaming_mode_detector_for_gamescope()
    {
        SteamDeckSdlHints.GetHidapiSteamHintValue(
            isLinux: true,
            name => name switch
            {
                "SteamDeck" => "1",
                "XDG_CURRENT_DESKTOP" => "gamescope",
                _ => null
            },
            SteamDeckEnvironment.IsGamingMode).Should().BeNull();
    }
}
