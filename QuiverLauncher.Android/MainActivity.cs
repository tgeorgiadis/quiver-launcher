using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Avalonia.Android;
using QuiverLauncher.Services;

namespace QuiverLauncher.Android;

[Activity(
    Label = "Quiver Launcher",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@mipmap/ic_launcher",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTop,
    WindowSoftInputMode = SoftInput.AdjustResize,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity
{
    private AndroidLauncherInstaller? _launcherInstaller;
    private SidebarGestureExclusion? _sidebarGestureExclusion;
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        var filesDirectory = FilesDir?.AbsolutePath;
        QuiverLauncherPaths.AndroidFilesDirProvider = () => filesDirectory;
        AppInstallLaunch.Current = new AndroidAppInstallLaunchService(this);
        _launcherInstaller = new AndroidLauncherInstaller(this);
        if (AndroidLauncherUpdater.Current == null)
            AndroidLauncherUpdater.Current = new AndroidLauncherUpdater(new HttpClient { Timeout = TimeSpan.FromMinutes(30) },
                Path.Combine(filesDirectory!, "launcher-updates"), _launcherInstaller, () => AppSettings.Load(),
                action => Avalonia.Threading.Dispatcher.UIThread.Post(action));
        else AndroidLauncherUpdater.Current.Installer = _launcherInstaller;
        QuiverLauncherPaths.EnsureUserDataRootExists();
        base.OnCreate(savedInstanceState);
        if (OperatingSystem.IsAndroidVersionAtLeast(29) && Window?.DecorView is { } decor)
        {
            _sidebarGestureExclusion = new SidebarGestureExclusion(decor,
                () => Content is MainView view && view.CanOpenMobileNavigation);
            decor.ViewTreeObserver?.AddOnPreDrawListener(_sidebarGestureExclusion);
            if (Content is MainView mainView)
                mainView.LayoutUpdated += UpdateSidebarGestureExclusion;
        }
    }

    private void UpdateSidebarGestureExclusion(object? sender, EventArgs e)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(29))
            _sidebarGestureExclusion?.OnPreDraw();
    }

    protected override void OnDestroy()
    {
        if (Content is MainView mainView)
            mainView.LayoutUpdated -= UpdateSidebarGestureExclusion;
        if (OperatingSystem.IsAndroidVersionAtLeast(29) && _sidebarGestureExclusion != null)
        {
            Window?.DecorView?.ViewTreeObserver?.RemoveOnPreDrawListener(_sidebarGestureExclusion);
            _sidebarGestureExclusion.Dispose();
            _sidebarGestureExclusion = null;
        }
        _launcherInstaller?.Detach();
        // Dispose only this activity's view; a replacement activity may already exist.
        if (Content is MainView view)
            view.HandleClosed();
        base.OnDestroy();
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, global::Android.Content.Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        _launcherInstaller?.ActivityResult(requestCode);
        if (requestCode == AndroidAppInstallLaunchService.InstallRequestCode)
            AndroidAppInstallLaunchService.CompleteInstall(resultCode == Result.Ok);
        else if (requestCode == AndroidAppInstallLaunchService.UninstallRequestCode)
            AndroidAppInstallLaunchService.CompleteUninstall(resultCode == Result.Ok);
    }

    protected override void OnResume()
    {
        base.OnResume();
        if (!VelopackUpdateService.ShouldSkipAutomaticSelfUpdate())
            _ = AndroidLauncherUpdater.Current?.CheckAsync();
    }
}
