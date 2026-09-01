using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(QuiverLauncher.Tests.HeadlessTestApp))]

namespace QuiverLauncher.Tests;

public static class HeadlessTestApp
{
    public static AppBuilder BuildAvaloniaApp()
    {
        App.SuppressDesktopHost = true;
        return AppBuilder.Configure<App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
    }
}
