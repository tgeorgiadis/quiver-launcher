using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(QuiverLauncher.Tests.HeadlessTestApp))]

namespace QuiverLauncher.Tests;

public static class HeadlessTestApp
{
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<QuiverLauncher.App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
