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
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        QuiverLauncherPaths.AndroidFilesDirProvider = () => FilesDir?.AbsolutePath;
        AppInstallLaunch.Current = new AndroidAppInstallLaunchService(this);
        QuiverLauncherPaths.EnsureUserDataRootExists();
        base.OnCreate(savedInstanceState);
    }
}
