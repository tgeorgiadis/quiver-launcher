using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using FluentAssertions;
using QuiverLauncher.Services;
using QuiverLauncher.ViewModels;

namespace QuiverLauncher.Tests;

public class LauncherInputControllerTests : IDisposable
{
    // Other UI fixtures can leave the process-wide text editor active. This test
    // exercises callback ownership with normal navigation, not Enter-to-end-edit.
    public LauncherInputControllerTests() => GamepadTextInput.Reset();
    public void Dispose() => GamepadTextInput.Reset();

    private sealed class Store : ISettingsStore
    {
        public AppSettings Current { get; } = new();
        public AppSettings Load() => Current;
        public void Save(AppSettings settings) { }
    }
    [AvaloniaFact]
    public void Closing_replaced_input_owner_preserves_new_callbacks_and_unsubscribes_keys()
    {
        var settings = new SettingsViewModel(new Store());
        using var bindings = new InputBindingsViewModel(settings, () => null, action => action());
        var oldView = new UserControl();
        var newView = new UserControl();
        var calls = 0;
        LauncherInputActions Actions() => new(_ => false, () => calls++, () => { }, () => { }, _ => { }, () => { });
        using var first = new LauncherInputController(oldView, () => settings.Current, bindings, () => true, () => false, () => { }, Actions(), false);
        using var second = new LauncherInputController(newView, () => settings.Current, bindings, () => true, () => false, () => { }, Actions(), false);
        var resolver = GamepadModalDialogNavigation.Instance.ResolveKeyboardAction;
        first.Dispose(); first.Dispose();
        GamepadModalDialogNavigation.Instance.ResolveKeyboardAction.Should().BeSameAs(resolver);
        oldView.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        calls.Should().Be(0);
        newView.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        calls.Should().Be(1);
        second.Dispose();
        GamepadModalDialogNavigation.Instance.ResolveKeyboardAction.Should().BeNull();
    }
}
