using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;

namespace QuiverLauncher.Android;

[Application(Name = "com.quiverlauncher.app.AndroidApp")]
public class AndroidApp : AvaloniaAndroidApplication<App>
{
    protected AndroidApp(nint javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer)
    {
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        return base.CustomizeAppBuilder(builder)
            .WithInterFont();
    }
}
