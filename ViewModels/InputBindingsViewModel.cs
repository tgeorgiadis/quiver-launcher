using System.Collections.ObjectModel;
using QuiverLauncher.Services;

namespace QuiverLauncher.ViewModels;

public sealed class InputBindingRow(GamepadAction action, string name, string gamepadLabel, string keyboardLabel,
    bool gamepadListening, bool keyboardListening, bool canRebindGamepad, bool canRebindKeyboard) : ObservableViewModel
{
    public GamepadAction Action { get; } = action;
    public string Name { get; } = name;
    public string GamepadLabel { get; private set; } = gamepadLabel;
    public string KeyboardLabel { get; private set; } = keyboardLabel;
    public bool GamepadListening { get; private set; } = gamepadListening;
    public bool KeyboardListening { get; private set; } = keyboardListening;
    public bool CanRebindGamepad { get; private set; } = canRebindGamepad;
    public bool CanRebindKeyboard { get; private set; } = canRebindKeyboard;
    public string GamepadButtonLabel => GamepadListening ? "Listening…" : "Rebind";
    public string KeyboardButtonLabel => KeyboardListening ? "Listening…" : "Rebind";
    public void Update(InputBindingRow row)
    {
        GamepadLabel = row.GamepadLabel; KeyboardLabel = row.KeyboardLabel;
        GamepadListening = row.GamepadListening; KeyboardListening = row.KeyboardListening;
        CanRebindGamepad = row.CanRebindGamepad; CanRebindKeyboard = row.CanRebindKeyboard;
        Notify(null);
    }
}

/// <summary>Owns capture subscriptions and binding changes for the settings Controls tab.</summary>
public sealed class InputBindingsViewModel : ObservableViewModel, IDisposable
{
    private readonly Func<IInputBindingSource?> _input;
    private readonly Action<Action> _dispatch;
    private Action<GamepadBinding>? _capture;
    private IInputBindingSource? _captureSource;
    private int _captureGeneration;
    private bool _disposed;
    public SettingsViewModel Settings { get; }
    public ObservableCollection<InputBindingRow> Rows { get; } = [];
    public GamepadAction? ListeningGamepad { get; private set; }
    public GamepadAction? ListeningKeyboard { get; private set; }
    public event Action? BindingsChanged;
    public event Action<string>? Error;
    public string Controllers { get; private set; } = "No controllers detected";
    public bool IsListening => ListeningGamepad.HasValue || ListeningKeyboard.HasValue;
    public string Status => ListeningGamepad is { } pad
        ? $"Listening for {DisplayName(pad)} (gamepad). Press a control, or Esc to cancel."
        : ListeningKeyboard is { } key
            ? $"Listening for {DisplayName(key)} (keyboard). Press a key, or Esc to cancel."
            : string.Empty;

    public InputBindingsViewModel(SettingsViewModel settings, Func<IInputBindingSource?> input, Action<Action> dispatch)
    {
        Settings = settings;
        _input = input;
        _dispatch = dispatch;
        Refresh();
    }

    public void RefreshControllers()
    {
        if (_disposed) return;
        var controllers = _input()?.GetConnectedGamepads() ?? [];
        Controllers = controllers.Count == 0 ? "No controllers detected"
            : string.Join(Environment.NewLine, controllers.Select(p => $"{p.Index + 1}. {p.Name}"));
        Notify(nameof(Controllers));
    }

    public void Refresh()
    {
        if (_disposed) return;
        Settings.Current.EnsureInitialized();
        foreach (var action in Enum.GetValues<GamepadAction>())
        {
            var updated = new InputBindingRow(action, DisplayName(action),
                ListeningGamepad == action ? "Press a control…" : GamepadBindingLabels.FormatActionBindings(Settings.Current.GamepadBindings, action),
                ListeningKeyboard == action ? "Press a key…" : KeyboardBindingLabels.FormatActionBindings(Settings.Current.KeyboardBindings, action),
                ListeningGamepad == action, ListeningKeyboard == action,
                !IsListening || ListeningGamepad == action, !IsListening || ListeningKeyboard == action);
            var existing = Rows.FirstOrDefault(r => r.Action == action);
            if (existing == null) Rows.Add(updated);
            else existing.Update(updated);
        }
        Notify(nameof(IsListening));
        Notify(nameof(Status));
    }

    public void ListenForGamepad(GamepadAction action)
    {
        if (_disposed) return;
        if (ListeningGamepad == action) { Cancel(); return; }
        var source = _input();
        if (source == null) return;
        if (!Settings.EnableGamepadInput)
        {
            Error?.Invoke("Enable Gamepad Input before rebinding gamepad controls.");
            return;
        }
        Cancel();
        ListeningGamepad = action;
        var generation = ++_captureGeneration;
        _captureSource = source;
        _capture = binding => _dispatch(() =>
        {
            if (_disposed || generation != _captureGeneration || ListeningGamepad != action) return;
            Cancel();
            GamepadBindingDefaults.AssignExclusive(Settings.Current.GamepadBindings, action, binding);
            source.ApplyBindings(Settings.Current.GamepadBindings);
            Save();
        });
        source.SetCaptureMode(true);
        source.SetGamepadEnabled(true);
        source.OnRawInput += _capture;
        Refresh();
    }

    public void ListenForKeyboard(GamepadAction action)
    {
        if (_disposed) return;
        if (ListeningKeyboard == action) { Cancel(); return; }
        Cancel();
        ListeningKeyboard = action;
        Refresh();
    }

    public void CaptureKeyboard(KeyboardBinding binding)
    {
        if (_disposed || ListeningKeyboard is not { } action) return;
        Cancel();
        KeyboardBindingDefaults.AssignExclusive(Settings.Current.KeyboardBindings, action, binding);
        Save();
    }

    public void Reset()
    {
        if (_disposed) return;
        Cancel();
        Settings.Current.GamepadBindings = GamepadBindingDefaults.Create();
        Settings.Current.KeyboardBindings = KeyboardBindingDefaults.Create();
        _input()?.ApplyBindings(Settings.Current.GamepadBindings);
        Save();
    }

    private void Save()
    {
        try { Settings.Save(Settings.Current); }
        catch (Exception ex) { Error?.Invoke($"Failed to save settings: {ex.Message}"); }
        Refresh();
        BindingsChanged?.Invoke();
    }

    public void Cancel()
    {
        ++_captureGeneration;
        if (_captureSource != null)
        {
            if (_capture != null) _captureSource.OnRawInput -= _capture;
            _captureSource.SetCaptureMode(false);
            _captureSource.SetGamepadEnabled(Settings.EnableGamepadInput);
        }
        _capture = null;
        _captureSource = null;
        var wasListening = IsListening;
        ListeningGamepad = ListeningKeyboard = null;
        if (wasListening) Refresh();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Cancel();
    }

    private static string DisplayName(GamepadAction action) => action switch
    {
        GamepadAction.Confirm => "Confirm / Select", GamepadAction.Cancel => "Cancel / Back",
        GamepadAction.NavUp => "Navigate Up", GamepadAction.NavDown => "Navigate Down",
        GamepadAction.NavLeft => "Navigate Left", GamepadAction.NavRight => "Navigate Right",
        _ => action.ToString(),
    };
}
