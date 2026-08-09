using Avalonia.Headless.XUnit;
using FluentAssertions;
using QuiverLauncher;

namespace QuiverLauncher.Tests;

public class HeadlessSmokeTests
{
    [AvaloniaFact]
    public void App_type_can_be_created_in_headless_mode()
    {
        var app = new App();

        app.Should().NotBeNull();
    }

    // Smoke test only: validates MainWindow visual tree and ctor wiring.
    // Does not exercise music playback, async icon loading, or catalog refresh.
    [AvaloniaFact]
    public void MainWindow_can_be_created_in_headless_mode()
    {
        var window = new MainWindow();

        window.Should().NotBeNull();
        window.Width.Should().BeGreaterThan(0);
    }
}
