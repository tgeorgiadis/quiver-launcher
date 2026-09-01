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

        if (View._settings.StartFullscreen)
            WindowState = SteamDeckEnvironment.DesktopFullscreenWindowState();

        Opened += (_, _) => View.HandleOpened();
        Closing += (_, e) => View.HandleClosing(e);
        Closed += (_, _) => View.HandleClosed();
        Activated += (_, _) => View.HandleActivated();
        Deactivated += (_, _) => View.HandleDeactivated();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property != WindowStateProperty)
            return;

        View.HandleHostWindowStateChanged(
            change.GetOldValue<WindowState>(),
            change.GetNewValue<WindowState>());
    }

    public void RestoreFromTray() => View.RestoreFromTray();

    public void RequestExit() => View.RequestExit();

    public void ApplyTraySettingsFromApp() => View.ApplyTraySettingsFromApp();

    public Task RunUpdateCheckAsync(bool promptForReview, bool isManualCheck)
        => View.RunUpdateCheckAsync(promptForReview, isManualCheck);

    public void OpenGitHubApiTokenSettings() => View.OpenGitHubApiTokenSettings();

    public void OpenGitLabApiTokenSettings() => View.OpenGitLabApiTokenSettings();

    internal static int GetDefaultCatalogSourceCardActionIndex(IReadOnlyList<Control> controls)
        => MainView.GetDefaultCatalogSourceCardActionIndex(controls);

    internal static string FormatModsLoadedStatus(
        int loaded,
        bool isSearch,
        bool canLoadMore,
        int? totalCountHint)
        => MainView.FormatModsLoadedStatus(loaded, isSearch, canLoadMore, totalCountHint);

    internal static bool ShouldShowModsListLoading(bool isLoading, int rowCount)
        => MainView.ShouldShowModsListLoading(isLoading, rowCount);

    internal static bool ShouldShowCatalogReviewGrid(bool useGridView)
        => MainView.ShouldShowCatalogReviewGrid(useGridView);

    internal static bool ShouldShowCatalogReviewList(bool useGridView)
        => MainView.ShouldShowCatalogReviewList(useGridView);

    internal static bool ShouldShowCatalogReviewHelpLines(bool useGridView)
        => MainView.ShouldShowCatalogReviewHelpLines(useGridView);

    internal static bool ShouldShowCatalogReviewOpenRepo(string? repository)
        => MainView.ShouldShowCatalogReviewOpenRepo(repository);
}
