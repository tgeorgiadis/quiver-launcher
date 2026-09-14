using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace QuiverLauncher.Views;

public partial class UpdateCheckStatusView : UserControl
{
    public event EventHandler? CancelRequested;
    public event EventHandler? RetryRequested;
    public event EventHandler? DismissRequested;
    public UpdateCheckStatusView() => AvaloniaXamlLoader.Load(this);
    private void Cancel_Click(object? sender, RoutedEventArgs args) => CancelRequested?.Invoke(this, EventArgs.Empty);
    private void Retry_Click(object? sender, RoutedEventArgs args) => RetryRequested?.Invoke(this, EventArgs.Empty);
    private void Dismiss_Click(object? sender, RoutedEventArgs args) => DismissRequested?.Invoke(this, EventArgs.Empty);
}
