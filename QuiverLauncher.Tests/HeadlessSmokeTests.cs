using Avalonia.Headless.XUnit;
using FluentAssertions;
using QuiverLauncher;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class HeadlessSmokeTests
{
    // Smoke test only: validates MainWindow visual tree and ctor wiring.
    // Does not exercise music playback or async icon loading.
    [AvaloniaFact]
    public async Task MainWindow_can_be_created_in_headless_mode()
    {
        var window = new MainWindow();
        try
        {
            window.Should().NotBeNull();
            window.Width.Should().BeGreaterThan(0);
        }
        finally
        {
            window.RequestExit();
            window.Close();
            await window.View.ShutdownAsync();
        }
    }
}
