using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FluentAssertions;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class LauncherDialogLifetimeTests
{
    [AvaloniaFact]
    public async Task Replacing_host_settles_owned_dialog_and_preserves_other_sessions_dialog()
    {
        var closingSession = new LauncherSession();
        var replacementSession = new LauncherSession();
        using var oldDialogs = new LauncherDialogLifetime(closingSession.Token, action => Dispatcher.UIThread.Post(action));
        using var newDialogs = new LauncherDialogLifetime(replacementSession.Token, action => Dispatcher.UIThread.Post(action));
        var oldWindow = new Window();
        var newWindow = new Window();
        var result = new TaskCompletionSource();
        oldWindow.Closed += (_, _) => result.TrySetResult();
        oldDialogs.Track(oldWindow);
        newDialogs.Track(newWindow);
        oldWindow.Show();
        newWindow.Show();
        var prompt = closingSession.RunAsync(() => result.Task);
        var shutdown = closingSession.DisposeAsync().AsTask();
        Dispatcher.UIThread.RunJobs();
        await shutdown;
        await prompt;
        oldWindow.IsVisible.Should().BeFalse();
        newWindow.IsVisible.Should().BeTrue();
        var replacementShutdown = replacementSession.DisposeAsync().AsTask();
        Dispatcher.UIThread.RunJobs();
        await replacementShutdown;
        newWindow.IsVisible.Should().BeFalse();
    }
}
