using FluentAssertions;
using QuiverLauncher;
using QuiverLauncher.Services;
using SDL2;

namespace QuiverLauncher.Tests;

public class GamepadBindingsTests
{
    [Fact]
    public void Defaults_match_current_hardcoded_layout()
    {
        var defaults = GamepadBindingDefaults.Create();

        defaults[GamepadAction.Confirm].Should().BeEquivalentTo(
        [
            GamepadBinding.Button(SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_A),
        ]);
        defaults[GamepadAction.Cancel].Should().BeEquivalentTo(
        [
            GamepadBinding.Button(SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_B),
        ]);
        defaults[GamepadAction.Options].Should().BeEquivalentTo(
        [
            GamepadBinding.Button(SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_Y),
        ]);
        defaults[GamepadAction.NavUp].Should().Contain(
            GamepadBinding.Button(SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_UP));
        defaults[GamepadAction.NavUp].Should().Contain(
            GamepadBinding.AxisNegative(SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_LEFTY));
        defaults[GamepadAction.NavDown].Should().Contain(
            GamepadBinding.AxisPositive(SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_LEFTY));
        defaults[GamepadAction.NavLeft].Should().Contain(
            GamepadBinding.AxisNegative(SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_LEFTX));
        defaults[GamepadAction.NavRight].Should().Contain(
            GamepadBinding.AxisPositive(SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_LEFTX));
    }

    [Fact]
    public void Format_labels_buttons_and_axes()
    {
        GamepadBindingLabels.Format(
                GamepadBinding.Button(SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_A))
            .Should().Be("A");
        GamepadBindingLabels.Format(
                GamepadBinding.Button(SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_LEFTSHOULDER))
            .Should().Be("LB");
        GamepadBindingLabels.Format(
                GamepadBinding.AxisPositive(SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_TRIGGERLEFT))
            .Should().Be("LT");
        GamepadBindingLabels.Format(
                GamepadBinding.AxisNegative(SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_LEFTY))
            .Should().Be("LS↑");
    }

    [Fact]
    public void AssignExclusive_removes_binding_from_other_actions()
    {
        var bindings = GamepadBindingDefaults.Create();
        var aButton = GamepadBinding.Button(SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_A);

        GamepadBindingDefaults.AssignExclusive(bindings, GamepadAction.Cancel, aButton);

        bindings[GamepadAction.Cancel].Should().BeEquivalentTo([aButton]);
        bindings[GamepadAction.Confirm].Should().NotContain(aButton);
    }

    [Fact]
    public void FormatHints_updates_when_confirm_binding_changes()
    {
        var bindings = GamepadBindingDefaults.Create();
        var defaultHints = GamepadBindingLabels.FormatHints(bindings);
        defaultHints.Should().Contain("A (Select)");

        GamepadBindingDefaults.AssignExclusive(
            bindings,
            GamepadAction.Confirm,
            GamepadBinding.Button(SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_RIGHTSHOULDER));

        var updated = GamepadBindingLabels.FormatHints(bindings);
        updated.Should().Contain("RB (Select)");
        updated.Should().NotContain("A (Select)");
    }

    [Fact]
    public void EnsureInitialized_restores_defaults_for_null_or_empty_maps()
    {
        var settings = new AppSettings
        {
            GamepadBindings = null!,
        };
        settings.EnsureInitialized();
        settings.GamepadBindings.Should().ContainKey(GamepadAction.Confirm);
        settings.GamepadBindings[GamepadAction.Confirm].Should().NotBeEmpty();

        settings.GamepadBindings = new Dictionary<GamepadAction, List<GamepadBinding>>
        {
            [GamepadAction.Confirm] = [],
        };
        settings.EnsureInitialized();
        settings.GamepadBindings[GamepadAction.Confirm].Should().NotBeEmpty();
        settings.GamepadBindings.Should().ContainKey(GamepadAction.NavRight);
    }
}
