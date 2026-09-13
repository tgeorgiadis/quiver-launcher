using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using QuiverLauncher.Services;

namespace QuiverLauncher;

public partial class MainWindow : Window
{
    public MainView View => RootView;

    public App _app
    {
        get => View._app;
        set => View._app = value;
    }

    public MainWindow()
    {
        InitializeComponent();
        DataContext = View;

        _ = new DesktopHostController(this, View);
    }

    public void RestoreFromTray() => View.RestoreFromTray();

    public void RequestExit() => View.RequestExit();

    public void ApplyTraySettingsFromApp() => View.ApplyTraySettingsFromApp();

    public Task RunUpdateCheckAsync(bool promptForReview, bool isManualCheck)
        => View.RunUpdateCheckAsync(promptForReview, isManualCheck);

    public void OpenGitHubApiTokenSettings() => View.OpenGitHubApiTokenSettings();

    public void OpenGitLabApiTokenSettings() => View.OpenGitLabApiTokenSettings();

}
