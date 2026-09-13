using Avalonia.Controls;
using Avalonia.Interactivity;
using QuiverLauncher.ViewModels;

namespace QuiverLauncher.Views;
public partial class InputBindingsView : UserControl
{
    public InputBindingsView() => InitializeComponent();
    private InputBindingsViewModel? Model => DataContext as InputBindingsViewModel;

    private void RefreshConnectedGamepads_Click(object? sender, RoutedEventArgs e) => Model?.RefreshControllers();
    private void ResetGamepadBindings_Click(object? sender, RoutedEventArgs e) => Model?.Reset();
    private void GamepadRebindButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: InputBindingRow row })
            Model?.ListenForGamepad(row.Action);
    }

    private void KeyboardRebindButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: InputBindingRow row })
            Model?.ListenForKeyboard(row.Action);
    }
}
