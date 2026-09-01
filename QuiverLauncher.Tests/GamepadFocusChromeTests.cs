using FluentAssertions;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class GamepadFocusChromeTests
{
    [Theory]
    [InlineData(true, true, false, true)]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, false, false)]
    [InlineData(true, false, true, true)]
    [InlineData(false, false, true, true)]
    [InlineData(true, true, true, true)]
    public void ShouldShowGamepadChrome_pad_when_enabled_or_keyboard_nav(
        bool enableGamepadInput,
        bool hasConnectedGamepad,
        bool keyboardNavActive,
        bool expected)
    {
        GamepadFocusChrome.ShouldShowGamepadChrome(
                enableGamepadInput,
                hasConnectedGamepad,
                keyboardNavActive)
            .Should().Be(expected);
    }

    [Theory]
    [InlineData(true, false, false, true, true)]
    [InlineData(false, false, false, true, false)]
    [InlineData(true, false, false, false, false)]
    [InlineData(true, true, false, true, true)]
    public void ShouldShowGamepadChrome_treat_as_present_covers_gaming_mode_without_sdl_pad(
        bool enableGamepadInput,
        bool hasConnectedGamepad,
        bool keyboardNavActive,
        bool treatAsGamepadPresent,
        bool expected)
    {
        GamepadFocusChrome.ShouldShowGamepadChrome(
                enableGamepadInput,
                hasConnectedGamepad,
                keyboardNavActive,
                treatAsGamepadPresent)
            .Should().Be(expected);
    }
}
