using System.Diagnostics;
using FluentAssertions;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class LauncherForegroundControllerTests
{
    [Fact]
    public async Task Completion_of_previous_app_cannot_reclaim_input_from_new_app()
    {
        await using var session = new LauncherSession();
        using var music = new LauncherMusicService(action => action(), enabled: false);
        using var first = new Process();
        using var second = new Process();
        var firstExit = new TaskCompletionSource();
        var secondExit = new TaskCompletionSource();
        var restores = 0;
        using var controller = new LauncherForegroundController(session, music, () => null,
            () => new AppSettings(), () => true, () => { }, () => restores++, () => Task.CompletedTask,
            (_, _) => { }, action => { action(); return Task.CompletedTask; },
            (process, _) => ReferenceEquals(process, first) ? firstExit.Task : secondExit.Task);
        controller.GameStarted(first);
        controller.GameStarted(second);
        firstExit.SetResult();
        controller.LaunchedAppOwnsInput.Should().BeTrue();
        restores.Should().Be(0);
        secondExit.SetResult();
        await session.DisposeAsync();
        controller.LaunchedAppOwnsInput.Should().BeFalse();
        restores.Should().Be(1);
    }

    [Fact]
    public async Task Foreground_return_and_shutdown_cancel_waits_without_late_focus_changes()
    {
        await using var session = new LauncherSession();
        using var music = new LauncherMusicService(action => action(), enabled: false);
        using var process = new Process();
        var exited = new TaskCompletionSource();
        var restores = 0;
        CancellationToken waitingToken = default;
        using var controller = new LauncherForegroundController(session, music, () => null,
            () => new AppSettings(), () => true, () => { }, () => restores++, () => Task.CompletedTask,
            (_, _) => { }, action => { action(); return Task.CompletedTask; },
            (_, token) => { waitingToken = token; return exited.Task; });
        controller.GameStarted(process);
        controller.Activated();
        waitingToken.IsCancellationRequested.Should().BeTrue();
        controller.LaunchedAppOwnsInput.Should().BeFalse();
        restores.Should().Be(1);
        controller.Dispose();
        var shuttingDown = session.DisposeAsync().AsTask();
        shuttingDown.IsCompleted.Should().BeFalse();
        exited.SetResult();
        await shuttingDown;
        restores.Should().Be(1);
    }
}
