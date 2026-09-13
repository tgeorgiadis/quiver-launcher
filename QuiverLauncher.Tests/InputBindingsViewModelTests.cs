using FluentAssertions;
using Avalonia.Controls;
using QuiverLauncher.Services;
using QuiverLauncher.ViewModels;
using SDL2;

namespace QuiverLauncher.Tests;

public class InputBindingsViewModelTests
{
    [Fact]
    public void Swapping_confirm_and_cancel_preserves_exclusive_bindings_through_save_and_reload()
    {
        var store = new Store();
        var input = new Input();
        using var model = new InputBindingsViewModel(new SettingsViewModel(store), () => input, action => action());
        model.ListenForGamepad(GamepadAction.Confirm);
        input.Send(GamepadBinding.Button(SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_B));
        store.Current.GamepadBindings[GamepadAction.Cancel].Should().BeEmpty();
        model.Rows.Single(r => r.Action == GamepadAction.Cancel).GamepadLabel.Should().Be("—");
        var restored = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(System.Text.Json.JsonSerializer.Serialize(store.Current))!;
        restored.EnsureInitialized();
        GamepadBindingDefaults.Clone(restored.GamepadBindings)[GamepadAction.Cancel].Should().BeEmpty();
        model.ListenForGamepad(GamepadAction.Cancel);
        input.Send(GamepadBinding.Button(SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_A));
        model.Rows.Single(r => r.Action == GamepadAction.Confirm).GamepadLabel.Should().Be("B");
        model.Rows.Single(r => r.Action == GamepadAction.Cancel).GamepadLabel.Should().Be("A");

        model.ListenForKeyboard(GamepadAction.Cancel);
        model.CaptureKeyboard(new KeyboardBinding(Avalonia.Input.Key.Enter));
        store.Current.KeyboardBindings[GamepadAction.Confirm].Should().BeEmpty();
        restored = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(System.Text.Json.JsonSerializer.Serialize(store.Current))!;
        restored.EnsureInitialized();
        restored.KeyboardBindings[GamepadAction.Confirm].Should().BeEmpty();
        KeyboardBindingDefaults.FindAction(restored.KeyboardBindings, Avalonia.Input.Key.Enter, Avalonia.Input.KeyModifiers.None)
            .Should().Be(GamepadAction.Cancel);
    }

    [Avalonia.Headless.XUnit.AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Rebinding_keeps_the_initiating_button_and_focus(bool gamepad)
    {
        var settings = new SettingsViewModel(new Store());
        var input = new Input();
        using var model = new InputBindingsViewModel(settings, () => input, action => action());
        var view = new QuiverLauncher.Views.SettingsView { DataContext = settings };
        var bindingsView = view.FindControl<QuiverLauncher.Views.InputBindingsView>("BindingsView")!;
        bindingsView.DataContext = model;
        var navigation = new QuiverLauncher.Views.SettingsNavigationController(view, new GamepadNavigationService());
        var window = new Avalonia.Controls.Window { Content = view, Width = 600, Height = 600 };
        try
        {
            window.Show();
            view.FindControl<Avalonia.Controls.TabControl>("SettingsTabControl")!.SelectedIndex = 1;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var row = model.Rows.Single(r => r.Action == GamepadAction.NavUp);
            var buttons = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(bindingsView)
                .OfType<Avalonia.Controls.Button>().Where(b => ReferenceEquals(b.DataContext, row)).ToArray();
            var button = buttons[gamepad ? 0 : 1];
            navigation.ApplySettingsGamepadSelection(navigation.CollectSettingsFocusableControls().IndexOf(button));
            var rows = model.Rows.ToArray();
            var resets = 0;
            model.Rows.CollectionChanged += (_, _) => resets++;
            navigation.ActivateSettingsGamepadSelection();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            model.IsListening.Should().BeTrue();
            button.Content.Should().Be("Listening…");
            button.IsFocused.Should().BeTrue();
            navigation.CollectSettingsFocusableControls()[navigation.FocusIndex].Should().BeSameAs(button);
            if (gamepad) input.Send(GamepadBinding.Button(SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_X));
            else model.CaptureKeyboard(new KeyboardBinding(Avalonia.Input.Key.F8));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            button.Content.Should().Be("Rebind");
            button.IsFocused.Should().BeTrue();
            navigation.GetSettingsFocusedControl(navigation.CollectSettingsFocusableControls()).Should().BeSameAs(button);
            model.Rows.Should().Equal(rows);
            resets.Should().Be(0);
        }
        finally { window.Close(); }
    }

    private sealed class Store : ISettingsStore
    {
        public AppSettings Current { get; } = new() { EnableGamepadInput = true };
        public int Saves { get; private set; }
        public AppSettings Load() => Current;
        public void Save(AppSettings settings) => Saves++;
    }
    private sealed class Input : IInputBindingSource
    {
        public event Action<GamepadBinding>? OnRawInput;
        public bool Capturing { get; private set; }
        public void Send(GamepadBinding binding) => OnRawInput?.Invoke(binding);
        public void SetCaptureMode(bool enabled) => Capturing = enabled;
        public void SetGamepadEnabled(bool enabled) { }
        public void ApplyBindings(IReadOnlyDictionary<GamepadAction, List<GamepadBinding>>? bindings) { }
        public IReadOnlyList<ConnectedGamepadInfo> GetConnectedGamepads() => [];
    }

    [Fact]
    public void Queued_capture_cannot_change_a_new_listening_session()
    {
        var store = new Store();
        var input = new Input();
        var queued = new Queue<Action>();
        using var model = new InputBindingsViewModel(new SettingsViewModel(store), () => input, queued.Enqueue);
        model.ListenForGamepad(GamepadAction.Confirm);
        input.Send(GamepadBinding.Button(SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_X));
        model.Cancel();
        model.ListenForGamepad(GamepadAction.Options);
        queued.Dequeue()();
        store.Saves.Should().Be(0);
        model.ListeningGamepad.Should().Be(GamepadAction.Options);
        input.Send(GamepadBinding.Button(SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_X));
        queued.Dequeue()();
        store.Saves.Should().Be(1);
        model.IsListening.Should().BeFalse();
        input.Capturing.Should().BeFalse();
    }

    [Fact]
    public void Disposal_unsubscribes_and_ignores_queued_input()
    {
        var store = new Store();
        var input = new Input();
        var queued = new Queue<Action>();
        var model = new InputBindingsViewModel(new SettingsViewModel(store), () => input, queued.Enqueue);
        model.ListenForGamepad(GamepadAction.Confirm);
        input.Send(GamepadBinding.Button(SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_X));
        model.Dispose();
        model.Dispose();
        queued.Dequeue()();
        input.Send(GamepadBinding.Button(SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_Y));
        queued.Should().BeEmpty();
        store.Saves.Should().Be(0);
        input.Capturing.Should().BeFalse();
    }
}
