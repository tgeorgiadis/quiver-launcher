using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using SDL2;

namespace QuiverLauncher.Services;

public enum GamepadAction
{
    Confirm,
    Cancel,
    Options,
    NavUp,
    NavDown,
    NavLeft,
    NavRight,
}

public enum GamepadInputKind
{
    Button,
    AxisPositive,
    AxisNegative,
}

public sealed class GamepadBinding : IEquatable<GamepadBinding>
{
    public GamepadInputKind Kind { get; set; }
    public int SdlCode { get; set; }

    public GamepadBinding()
    {
    }

    public GamepadBinding(GamepadInputKind kind, int sdlCode)
    {
        Kind = kind;
        SdlCode = sdlCode;
    }

    public static GamepadBinding Button(SDL.SDL_GameControllerButton button) =>
        new(GamepadInputKind.Button, (int)button);

    public static GamepadBinding AxisPositive(SDL.SDL_GameControllerAxis axis) =>
        new(GamepadInputKind.AxisPositive, (int)axis);

    public static GamepadBinding AxisNegative(SDL.SDL_GameControllerAxis axis) =>
        new(GamepadInputKind.AxisNegative, (int)axis);

    public bool Equals(GamepadBinding? other) =>
        other != null && Kind == other.Kind && SdlCode == other.SdlCode;

    public override bool Equals(object? obj) => obj is GamepadBinding other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Kind, SdlCode);
}

public sealed class ConnectedGamepadInfo
{
    public int Index { get; init; }
    public string Name { get; init; } = "";
}

public static class GamepadBindingDefaults
{
    public static Dictionary<GamepadAction, List<GamepadBinding>> Create() => new()
    {
        [GamepadAction.Confirm] =
        [
            GamepadBinding.Button(SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_A),
        ],
        [GamepadAction.Cancel] =
        [
            GamepadBinding.Button(SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_B),
        ],
        [GamepadAction.Options] =
        [
            GamepadBinding.Button(SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_Y),
        ],
        [GamepadAction.NavUp] =
        [
            GamepadBinding.Button(SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_UP),
            GamepadBinding.AxisNegative(SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_LEFTY),
        ],
        [GamepadAction.NavDown] =
        [
            GamepadBinding.Button(SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_DOWN),
            GamepadBinding.AxisPositive(SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_LEFTY),
        ],
        [GamepadAction.NavLeft] =
        [
            GamepadBinding.Button(SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_LEFT),
            GamepadBinding.AxisNegative(SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_LEFTX),
        ],
        [GamepadAction.NavRight] =
        [
            GamepadBinding.Button(SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_RIGHT),
            GamepadBinding.AxisPositive(SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_LEFTX),
        ],
    };

    public static Dictionary<GamepadAction, List<GamepadBinding>> Clone(
        IReadOnlyDictionary<GamepadAction, List<GamepadBinding>> source)
    {
        var clone = new Dictionary<GamepadAction, List<GamepadBinding>>();
        foreach (var action in Enum.GetValues<GamepadAction>())
        {
            if (source.TryGetValue(action, out var list) && list != null)
            {
                clone[action] = list
                    .Select(b => new GamepadBinding(b.Kind, b.SdlCode))
                    .ToList();
            }
            else
            {
                clone[action] = Create()[action]
                    .Select(b => new GamepadBinding(b.Kind, b.SdlCode))
                    .ToList();
            }
        }

        return clone;
    }

    public static void EnsureComplete(Dictionary<GamepadAction, List<GamepadBinding>>? bindings)
    {
        if (bindings == null)
            return;

        var defaults = Create();
        foreach (var action in Enum.GetValues<GamepadAction>())
        {
            if (!bindings.TryGetValue(action, out var list) || list == null || list.Count == 0)
                bindings[action] = defaults[action]
                    .Select(b => new GamepadBinding(b.Kind, b.SdlCode))
                    .ToList();
        }
    }

    /// <summary>
    /// Assigns <paramref name="binding"/> to <paramref name="action"/> (replacing that action's
    /// bindings with a single entry) and removes the same binding from every other action.
    /// </summary>
    public static void AssignExclusive(
        Dictionary<GamepadAction, List<GamepadBinding>> bindings,
        GamepadAction action,
        GamepadBinding binding)
    {
        foreach (var other in Enum.GetValues<GamepadAction>())
        {
            if (other == action)
                continue;

            if (!bindings.TryGetValue(other, out var list) || list == null)
                continue;

            list.RemoveAll(b => b.Equals(binding));
        }

        bindings[action] = [new GamepadBinding(binding.Kind, binding.SdlCode)];
    }
}

public static class GamepadBindingLabels
{
    public static string Format(GamepadBinding binding) => binding.Kind switch
    {
        GamepadInputKind.Button => FormatButton((SDL.SDL_GameControllerButton)binding.SdlCode),
        GamepadInputKind.AxisPositive => FormatAxis((SDL.SDL_GameControllerAxis)binding.SdlCode, positive: true),
        GamepadInputKind.AxisNegative => FormatAxis((SDL.SDL_GameControllerAxis)binding.SdlCode, positive: false),
        _ => "?",
    };

    public static string FormatActionBindings(
        IReadOnlyDictionary<GamepadAction, List<GamepadBinding>> bindings,
        GamepadAction action)
    {
        if (!bindings.TryGetValue(action, out var list) || list == null || list.Count == 0)
            return "—";

        return string.Join(" / ", list.Select(Format));
    }

    public static string FormatHints(IReadOnlyDictionary<GamepadAction, List<GamepadBinding>> bindings)
    {
        var confirm = FormatActionBindings(bindings, GamepadAction.Confirm);
        var options = FormatActionBindings(bindings, GamepadAction.Options);
        var cancel = FormatActionBindings(bindings, GamepadAction.Cancel);
        var nav = FormatNavigationHint(bindings);
        return $"{confirm} (Select) · {options} (Options) · {cancel} (Back) · {nav} (Navigate)";
    }

    private static string FormatNavigationHint(
        IReadOnlyDictionary<GamepadAction, List<GamepadBinding>> bindings)
    {
        var labels = new[]
            {
                GamepadAction.NavUp,
                GamepadAction.NavDown,
                GamepadAction.NavLeft,
                GamepadAction.NavRight,
            }
            .SelectMany(a => bindings.TryGetValue(a, out var list) && list != null
                ? list.Select(Format)
                : Enumerable.Empty<string>())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (labels.Count == 0)
            return "D-pad";

        // Prefer a short summary when defaults are present.
        var hasDpad = labels.Any(l => l is "↑" or "↓" or "←" or "→");
        var hasStick = labels.Any(l => l.StartsWith("LS", StringComparison.Ordinal));
        if (hasDpad && hasStick)
            return "D-pad";
        if (hasDpad)
            return "D-pad";
        if (hasStick)
            return "LS";

        return string.Join("/", labels.Take(4));
    }

    private static string FormatButton(SDL.SDL_GameControllerButton button) => button switch
    {
        SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_A => "A",
        SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_B => "B",
        SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_X => "X",
        SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_Y => "Y",
        SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_BACK => "Back",
        SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_GUIDE => "Guide",
        SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_START => "Start",
        SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_LEFTSTICK => "L3",
        SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_RIGHTSTICK => "R3",
        SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_LEFTSHOULDER => "LB",
        SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_RIGHTSHOULDER => "RB",
        SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_UP => "↑",
        SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_DOWN => "↓",
        SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_LEFT => "←",
        SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_RIGHT => "→",
        SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_MISC1 => "Misc",
        SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_PADDLE1 => "P1",
        SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_PADDLE2 => "P2",
        SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_PADDLE3 => "P3",
        SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_PADDLE4 => "P4",
        SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_TOUCHPAD => "Touch",
        _ => $"Btn{(int)button}",
    };

    private static string FormatAxis(SDL.SDL_GameControllerAxis axis, bool positive) => axis switch
    {
        SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_LEFTX => positive ? "LS→" : "LS←",
        SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_LEFTY => positive ? "LS↓" : "LS↑",
        SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_RIGHTX => positive ? "RS→" : "RS←",
        SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_RIGHTY => positive ? "RS↓" : "RS↑",
        SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_TRIGGERLEFT => "LT",
        SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_TRIGGERRIGHT => "RT",
        _ => $"Axis{(int)axis}{(positive ? "+" : "-")}",
    };
}
