using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FluentAssertions;
using QuiverLauncher.Models;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public sealed class FlatpakLaunchThreadTests
{
    [AvaloniaFact]
    public async Task Flatpak_launch_returns_to_UI_thread_before_notifying_process_and_library()
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Skip("Requires Linux to run the disposable Flatpak command fixture.");
            return;
        }

        var root = Path.Combine(Path.GetTempPath(), "quiver-flatpak-thread-" + Guid.NewGuid().ToString("N"));
        var gamePath = Path.Combine(root, "game");
        var originalPath = Environment.GetEnvironmentVariable("PATH");
        var originalDataRoot = QuiverLauncherPaths.OverrideUserDataRoot;
        Directory.CreateDirectory(gamePath);
        Process? started = null;
        try
        {
            QuiverLauncherPaths.OverrideUserDataRoot = root;
            var receipt = new FlatpakReceipt("io.github.quiver.ThreadTest", "x86_64", "stable", new string('a', 64), "v1");
            FlatpakService.WriteReceipt(Path.Combine(gamePath, FlatpakService.ReceiptFileName), receipt);
            // Force asynchronous state checks without requiring Flatpak or installing an app.
            var command = Path.Combine(root, "flatpak");
            File.WriteAllText(command, "#!/bin/sh\n" +
                "case \"$1\" in\n" +
                $"list) sleep 0.05; printf '%s\\n' '{receipt.Reference[4..]}' ;;\n" +
                $"info) sleep 0.05; printf '%s\\n' '{receipt.Commit}' ;;\n" +
                "run) exit 0 ;;\n" +
                "*) exit 1 ;;\nesac\n");
            File.SetUnixFileMode(command, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Environment.SetEnvironmentVariable("PATH", root + Path.PathSeparator + originalPath);

            using var manager = new GameManager(new FileSettingsStore(Path.Combine(root, "settings.json")));
            var game = new GameInfo { Name = "Thread test", FolderName = "game", IsFlatpak = true, GameManager = manager };
            var label = new TextBlock();
            var processOnUiThread = false;
            var libraryOnUiThread = false;
            game.GameProcessStarted += process =>
            {
                started = process;
                processOnUiThread = Dispatcher.UIThread.CheckAccess();
            };
            manager.PropertyChanged += (_, change) =>
            {
                if (change.PropertyName != nameof(GameManager.Games)) return;
                libraryOnUiThread = Dispatcher.UIThread.CheckAccess();
                // Reproduce a UI subscriber refreshing a control after process start.
                label.Text = "Running";
            };

            var launched = await GameLaunchService.LaunchAsync(game, root);
            started.Should().NotBeNull("the Flatpak process was successfully started");
            launched.Should().BeTrue("a successful launch must not become a UI thread access error");
            processOnUiThread.Should().BeTrue();
            libraryOnUiThread.Should().BeTrue();
            label.Text.Should().Be("Running");
            File.Exists(Path.Combine(gamePath, "LastPlayed.txt")).Should().BeTrue();
            await started!.WaitForExitAsync(TestContext.Current.CancellationToken);
        }
        finally
        {
            if (started != null)
            {
                await started.WaitForExitAsync(TestContext.Current.CancellationToken);
                started.Dispose();
            }
            Environment.SetEnvironmentVariable("PATH", originalPath);
            QuiverLauncherPaths.OverrideUserDataRoot = originalDataRoot;
            Directory.Delete(root, true);
        }
    }
}
