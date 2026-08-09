using Avalonia.Input;
using FluentAssertions;
using QuiverLauncher;
using QuiverLauncher.Services;
using System.Text.Json;

namespace QuiverLauncher.Tests;

public class KeyboardBindingsTests
{
    [Fact]
    public void Defaults_cover_all_actions_with_expected_keys()
    {
        var defaults = KeyboardBindingDefaults.Create();

        defaults.Keys.Should().BeEquivalentTo(Enum.GetValues<GamepadAction>());

        defaults[GamepadAction.Confirm].Should().BeEquivalentTo(
        [
            KeyboardBinding.Of(Key.Enter),
        ]);
        defaults[GamepadAction.Cancel].Should().BeEquivalentTo(
        [
            KeyboardBinding.Of(Key.Escape),
        ]);
        defaults[GamepadAction.Options].Should().BeEquivalentTo(
        [
            KeyboardBinding.Of(Key.O),
        ]);
        defaults[GamepadAction.NavUp].Should().BeEquivalentTo([KeyboardBinding.Of(Key.Up)]);
        defaults[GamepadAction.NavDown].Should().BeEquivalentTo([KeyboardBinding.Of(Key.Down)]);
        defaults[GamepadAction.NavLeft].Should().BeEquivalentTo([KeyboardBinding.Of(Key.Left)]);
        defaults[GamepadAction.NavRight].Should().BeEquivalentTo([KeyboardBinding.Of(Key.Right)]);
    }

    [Fact]
    public void AssignExclusive_moves_key_from_confirm_to_cancel()
    {
        var bindings = KeyboardBindingDefaults.Create();
        var enter = KeyboardBinding.Of(Key.Enter);

        KeyboardBindingDefaults.AssignExclusive(bindings, GamepadAction.Cancel, enter);

        bindings[GamepadAction.Cancel].Should().BeEquivalentTo([enter]);
        bindings[GamepadAction.Confirm].Should().BeEmpty();
    }

    [Fact]
    public void FindAction_matches_default_keys()
    {
        var bindings = KeyboardBindingDefaults.Create();

        KeyboardBindingDefaults.FindAction(bindings, Key.Enter, KeyModifiers.None)
            .Should().Be(GamepadAction.Confirm);
        KeyboardBindingDefaults.FindAction(bindings, Key.Escape, KeyModifiers.None)
            .Should().Be(GamepadAction.Cancel);
        KeyboardBindingDefaults.FindAction(bindings, Key.O, KeyModifiers.None)
            .Should().Be(GamepadAction.Options);
        KeyboardBindingDefaults.FindAction(bindings, Key.O, KeyModifiers.Shift)
            .Should().BeNull();
        KeyboardBindingDefaults.FindAction(bindings, Key.Back, KeyModifiers.None)
            .Should().BeNull();
    }

    [Fact]
    public void Format_labels_escape_and_arrows()
    {
        KeyboardBindingLabels.Format(KeyboardBinding.Of(Key.Escape))
            .Should().Be("Esc");
        KeyboardBindingLabels.Format(KeyboardBinding.Of(Key.Up))
            .Should().Be("Up");
        KeyboardBindingLabels.Format(KeyboardBinding.Of(Key.O))
            .Should().Be("O");
        KeyboardBindingLabels.Format(KeyboardBinding.Of(Key.Back))
            .Should().Be("Backspace");
    }

    [Fact]
    public void FormatHints_includes_select_options_back_navigate()
    {
        var hints = KeyboardBindingLabels.FormatHints(KeyboardBindingDefaults.Create());
        hints.Should().Contain("Enter (Select)");
        hints.Should().Contain("O (Options)");
        hints.Should().Contain("Esc (Back)");
        hints.Should().Contain("Arrows (Navigate)");
    }

    [Fact]
    public void EnsureComplete_fills_missing_actions()
    {
        var bindings = new Dictionary<GamepadAction, List<KeyboardBinding>>
        {
            [GamepadAction.Confirm] = [KeyboardBinding.Of(Key.A)],
        };

        KeyboardBindingDefaults.EnsureComplete(bindings);

        bindings[GamepadAction.Confirm].Should().BeEquivalentTo([KeyboardBinding.Of(Key.A)]);
        bindings[GamepadAction.Cancel].Should().NotBeEmpty();
        bindings[GamepadAction.Options].Should().NotBeEmpty();
        bindings[GamepadAction.NavUp].Should().NotBeEmpty();
    }

    [Fact]
    public void AppSettings_EnsureInitialized_completes_keyboard_bindings()
    {
        var settings = new AppSettings
        {
            KeyboardBindings = new Dictionary<GamepadAction, List<KeyboardBinding>>(),
        };

        settings.EnsureInitialized();

        settings.KeyboardBindings.Keys.Should().BeEquivalentTo(Enum.GetValues<GamepadAction>());
        settings.KeyboardBindings[GamepadAction.Cancel].Should().Contain(KeyboardBinding.Of(Key.Escape));
    }

    [Fact]
    public void KeyboardBindings_json_round_trip_preserves_modifiers()
    {
        var settings = new AppSettings
        {
            KeyboardBindings = KeyboardBindingDefaults.Create(),
        };
        KeyboardBindingDefaults.AssignExclusive(
            settings.KeyboardBindings,
            GamepadAction.Options,
            KeyboardBinding.Of(Key.O, KeyModifiers.Control));

        var json = JsonSerializer.Serialize(settings);
        var loaded = JsonSerializer.Deserialize<AppSettings>(json);
        loaded.Should().NotBeNull();
        loaded!.EnsureInitialized();

        loaded.KeyboardBindings[GamepadAction.Options].Should().BeEquivalentTo(
        [
            KeyboardBinding.Of(Key.O, KeyModifiers.Control),
        ]);
        loaded.KeyboardBindings[GamepadAction.Confirm].Should().Contain(KeyboardBinding.Of(Key.Enter));
    }
}
