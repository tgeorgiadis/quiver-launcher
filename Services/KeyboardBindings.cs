using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Input;

namespace QuiverLauncher.Services;

public sealed class KeyboardBinding : IEquatable<KeyboardBinding>
{
    public Key Key { get; set; }
    public KeyModifiers Modifiers { get; set; } = KeyModifiers.None;

    public KeyboardBinding()
    {
    }

    public KeyboardBinding(Key key, KeyModifiers modifiers = KeyModifiers.None)
    {
        Key = key;
        Modifiers = NormalizeModifiers(modifiers);
    }

    public static KeyboardBinding Of(Key key, KeyModifiers modifiers = KeyModifiers.None) =>
        new(key, modifiers);

    public static KeyModifiers NormalizeModifiers(KeyModifiers modifiers) =>
        modifiers & (KeyModifiers.Shift | KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Meta);

    public bool Matches(Key key, KeyModifiers modifiers) =>
        Key == key && Modifiers == NormalizeModifiers(modifiers);

    public bool Equals(KeyboardBinding? other) =>
        other != null && Key == other.Key && Modifiers == other.Modifiers;

    public override bool Equals(object? obj) => obj is KeyboardBinding other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Key, Modifiers);
}

public static class KeyboardBindingDefaults
{
    public static Dictionary<GamepadAction, List<KeyboardBinding>> Create() => new()
    {
        [GamepadAction.Confirm] =
        [
            KeyboardBinding.Of(Key.Enter),
        ],
        [GamepadAction.Cancel] =
        [
            KeyboardBinding.Of(Key.Escape),
        ],
        [GamepadAction.Options] =
        [
            KeyboardBinding.Of(Key.O),
        ],
        [GamepadAction.NavUp] =
        [
            KeyboardBinding.Of(Key.Up),
        ],
        [GamepadAction.NavDown] =
        [
            KeyboardBinding.Of(Key.Down),
        ],
        [GamepadAction.NavLeft] =
        [
            KeyboardBinding.Of(Key.Left),
        ],
        [GamepadAction.NavRight] =
        [
            KeyboardBinding.Of(Key.Right),
        ],
    };

    public static Dictionary<GamepadAction, List<KeyboardBinding>> Clone(
        IReadOnlyDictionary<GamepadAction, List<KeyboardBinding>> source)
    {
        var clone = new Dictionary<GamepadAction, List<KeyboardBinding>>();
        foreach (var action in Enum.GetValues<GamepadAction>())
        {
            if (source.TryGetValue(action, out var list) && list != null)
            {
                clone[action] = list
                    .Select(b => new KeyboardBinding(b.Key, b.Modifiers))
                    .ToList();
            }
            else
            {
                clone[action] = Create()[action]
                    .Select(b => new KeyboardBinding(b.Key, b.Modifiers))
                    .ToList();
            }
        }

        return clone;
    }

    public static void EnsureComplete(Dictionary<GamepadAction, List<KeyboardBinding>>? bindings)
    {
        if (bindings == null)
            return;

        var defaults = Create();
        foreach (var action in Enum.GetValues<GamepadAction>())
        {
            if (!bindings.TryGetValue(action, out var list) || list == null || list.Count == 0)
            {
                bindings[action] = defaults[action]
                    .Select(b => new KeyboardBinding(b.Key, b.Modifiers))
                    .ToList();
            }
        }
    }

    /// <summary>
    /// Assigns <paramref name="binding"/> to <paramref name="action"/> (replacing that action's
    /// bindings with a single entry) and removes the same binding from every other action.
    /// </summary>
    public static void AssignExclusive(
        Dictionary<GamepadAction, List<KeyboardBinding>> bindings,
        GamepadAction action,
        KeyboardBinding binding)
    {
        foreach (var other in Enum.GetValues<GamepadAction>())
        {
            if (other == action)
                continue;

            if (!bindings.TryGetValue(other, out var list) || list == null)
                continue;

            list.RemoveAll(b => b.Equals(binding));
        }

        bindings[action] = [new KeyboardBinding(binding.Key, binding.Modifiers)];
    }

    public static GamepadAction? FindAction(
        IReadOnlyDictionary<GamepadAction, List<KeyboardBinding>> bindings,
        Key key,
        KeyModifiers modifiers)
    {
        foreach (var action in Enum.GetValues<GamepadAction>())
        {
            if (!bindings.TryGetValue(action, out var list) || list == null)
                continue;

            if (list.Any(b => b.Matches(key, modifiers)))
                return action;
        }

        return null;
    }

    public static bool IsModifierOnlyKey(Key key) =>
        key is Key.LeftShift or Key.RightShift
            or Key.LeftCtrl or Key.RightCtrl
            or Key.LeftAlt or Key.RightAlt
            or Key.LWin or Key.RWin;
}

public static class KeyboardBindingLabels
{
    public static string Format(KeyboardBinding binding)
    {
        var keyLabel = FormatKey(binding.Key);
        if (binding.Modifiers == KeyModifiers.None)
            return keyLabel;

        var parts = new List<string>();
        if (binding.Modifiers.HasFlag(KeyModifiers.Control))
            parts.Add("Ctrl");
        if (binding.Modifiers.HasFlag(KeyModifiers.Alt))
            parts.Add("Alt");
        if (binding.Modifiers.HasFlag(KeyModifiers.Shift))
            parts.Add("Shift");
        if (binding.Modifiers.HasFlag(KeyModifiers.Meta))
            parts.Add("Meta");
        parts.Add(keyLabel);
        return string.Join("+", parts);
    }

    public static string FormatActionBindings(
        IReadOnlyDictionary<GamepadAction, List<KeyboardBinding>> bindings,
        GamepadAction action)
    {
        if (!bindings.TryGetValue(action, out var list) || list == null || list.Count == 0)
            return "—";

        return string.Join(", ", list.Select(Format));
    }

    public static string FormatHints(IReadOnlyDictionary<GamepadAction, List<KeyboardBinding>> bindings)
    {
        var confirm = FormatActionBindings(bindings, GamepadAction.Confirm);
        var options = FormatActionBindings(bindings, GamepadAction.Options);
        var cancel = FormatActionBindings(bindings, GamepadAction.Cancel);
        var nav = FormatNavigationHint(bindings);
        return $"{confirm} (Select) · {options} (Options) · {cancel} (Back) · {nav} (Navigate)";
    }

    private static string FormatNavigationHint(
        IReadOnlyDictionary<GamepadAction, List<KeyboardBinding>> bindings)
    {
        var hasArrows =
            HasKey(bindings, GamepadAction.NavUp, Key.Up) &&
            HasKey(bindings, GamepadAction.NavDown, Key.Down) &&
            HasKey(bindings, GamepadAction.NavLeft, Key.Left) &&
            HasKey(bindings, GamepadAction.NavRight, Key.Right);

        if (hasArrows)
            return "Arrows";

        var labels = new[]
            {
                GamepadAction.NavUp,
                GamepadAction.NavDown,
                GamepadAction.NavLeft,
                GamepadAction.NavRight,
            }
            .Select(a => FormatActionBindings(bindings, a))
            .Where(s => s != "—")
            .ToList();

        return labels.Count == 0 ? "—" : string.Join("/", labels);
    }

    private static bool HasKey(
        IReadOnlyDictionary<GamepadAction, List<KeyboardBinding>> bindings,
        GamepadAction action,
        Key key) =>
        bindings.TryGetValue(action, out var list) &&
        list != null &&
        list.Any(b => b.Key == key && b.Modifiers == KeyModifiers.None);

    private static string FormatKey(Key key) => key switch
    {
        Key.Up => "Up",
        Key.Down => "Down",
        Key.Left => "Left",
        Key.Right => "Right",
        Key.Enter => "Enter",
        Key.Space => "Space",
        Key.Back => "Backspace",
        Key.Escape => "Esc",
        Key.Apps => "Menu",
        Key.F10 => "F10",
        _ => key.ToString(),
    };
}
