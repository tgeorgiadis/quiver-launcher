using System.Diagnostics;
using FluentAssertions;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class MacAppBundleLaunchTests
{
    [Fact]
    public void App_bundle_start_waits_for_the_app_to_quit()
    {
        var startInfo = new ProcessStartInfo();

        GameLaunchService.ConfigureMacAppBundleStart(startInfo, "/Games/My \"Game\".app", "/Games");

        startInfo.FileName.Should().Be("open");
        startInfo.ArgumentList.Should().Equal("-W", "/Games/My \"Game\".app");
        startInfo.Arguments.Should().BeEmpty();
        startInfo.UseShellExecute.Should().BeFalse();
        startInfo.WorkingDirectory.Should().Be("/Games");
    }

    [Fact]
    public async Task Open_process_lives_until_the_app_bundle_quits()
    {
        if (!OperatingSystem.IsMacOS())
            Assert.Skip("Requires macOS LaunchServices.");

        var root = Path.Combine(Path.GetTempPath(), "quiver-open-wait-" + Guid.NewGuid().ToString("N"));
        var app = Path.Combine(root, "Waiter.app");
        var macOS = Directory.CreateDirectory(Path.Combine(app, "Contents", "MacOS")).FullName;
        try
        {
            File.WriteAllText(Path.Combine(app, "Contents", "Info.plist"), $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <plist version="1.0"><dict>
                <key>CFBundleIdentifier</key><string>test.quiver.waiter.{Guid.NewGuid():N}</string>
                <key>CFBundleExecutable</key><string>Waiter</string>
                <key>CFBundlePackageType</key><string>APPL</string>
                <key>LSUIElement</key><true/>
                </dict></plist>
                """);
            var executable = Path.Combine(macOS, "Waiter");
            File.WriteAllText(executable, "#!/bin/sh\nsleep 3\n");
            File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            var startInfo = new ProcessStartInfo();
            GameLaunchService.ConfigureMacAppBundleStart(startInfo, app, root);
            var stopwatch = Stopwatch.StartNew();
            using var process = Process.Start(startInfo)!;
            await process.WaitForExitAsync(TestContext.Current.CancellationToken);

            process.ExitCode.Should().Be(0);
            stopwatch.Elapsed.Should().BeGreaterThan(TimeSpan.FromSeconds(2),
                "the launcher treats this process as the running game");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
