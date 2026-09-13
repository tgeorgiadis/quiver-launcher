using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using QuiverLauncher.Services;

namespace QuiverLauncher.Views;

public partial class AndroidLauncherUpdateView : UserControl
{
    public static readonly StyledProperty<bool> IsBannerProperty = AvaloniaProperty.Register<AndroidLauncherUpdateView, bool>(nameof(IsBanner));
    public bool IsBanner { get => GetValue(IsBannerProperty); set => SetValue(IsBannerProperty, value); }
    public AndroidLauncherUpdateView() => InitializeComponent();
    private AndroidLauncherUpdater? Model => DataContext as AndroidLauncherUpdater;
    private async void Update_Click(object? sender, RoutedEventArgs e) { if (Model is {} model) await model.UpdateAsync(); }
    private async void Check_Click(object? sender, RoutedEventArgs e) { if (Model is {} model) await model.CheckAsync(true); }
    private void Cancel_Click(object? sender, RoutedEventArgs e) => Model?.CancelDownload();
    private void Dismiss_Click(object? sender, RoutedEventArgs e) => Model?.Dismiss();
    private async void Retry_Click(object? sender, RoutedEventArgs e)
    {
        if (Model is not {} model) return;
        if (model.HasUpdate && !model.LastFailureWasCheck) await model.UpdateAsync(); else await model.CheckAsync(true);
    }
    private async void Notes_Click(object? sender, RoutedEventArgs e)
    {
        if (Model?.NotesUrl is {} url && TopLevel.GetTopLevel(this) is {} top)
            await top.Launcher.LaunchUriAsync(new Uri(url));
    }
}
