using FluentAssertions;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class LauncherSessionTests
{
    [Fact]
    public async Task Synchronous_shutdown_from_work_waits_for_that_work_to_finish()
    {
        var session = new LauncherSession();
        var disposed = false;
        var release = new TaskCompletionSource();
        session.OnShutdown(() => disposed = true);
        Task? closing = null;
        var work = session.RunAsync(() =>
        {
            closing = session.DisposeAsync().AsTask();
            disposed.Should().BeFalse();
            return release.Task;
        });
        closing!.IsCompleted.Should().BeFalse();
        release.SetResult();
        await work;
        await closing;
        disposed.Should().BeTrue();
    }

    [Fact]
    public async Task Cancellation_callback_can_reenter_shutdown_without_duplicate_disposal()
    {
        var session = new LauncherSession();
        var count = 0;
        Task? reentered = null;
        session.OnShutdown(() => count++);
        using var registration = session.Token.Register(() => reentered = session.DisposeAsync().AsTask());
        var closing = session.DisposeAsync().AsTask();
        await closing;
        reentered.Should().BeSameAs(closing);
        count.Should().Be(1);
    }
    [Fact]
    public async Task Initializes_once_and_rejects_work_after_shutdown()
    {
        var session = new LauncherSession();
        session.TryInitialize().Should().BeTrue();
        session.TryInitialize().Should().BeFalse();
        await session.DisposeAsync();
        session.TryInitialize().Should().BeFalse();
        var started = false;
        await session.RunAsync(() => { started = true; return Task.CompletedTask; });
        started.Should().BeFalse();
    }

    [Fact]
    public async Task Cancels_then_drains_work_before_disposing_dependencies_once()
    {
        var session = new LauncherSession();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposalCount = 0;
        session.OnShutdown(() => disposalCount++);
        var operation = session.RunAsync(() => release.Task);
        var closing = session.DisposeAsync().AsTask();
        session.Token.IsCancellationRequested.Should().BeTrue();
        closing.IsCompleted.Should().BeFalse();
        disposalCount.Should().Be(0);
        release.SetResult();
        await operation;
        await closing;
        await session.DisposeAsync();
        disposalCount.Should().Be(1);
    }

    [Fact]
    public async Task Failed_work_does_not_skip_cleanup()
    {
        var session = new LauncherSession();
        var disposed = false;
        session.OnShutdown(() => disposed = true);
        var work = session.RunAsync(() => Task.FromException(new InvalidOperationException("load failed")));
        await FluentActions.Awaiting(() => work).Should().ThrowAsync<InvalidOperationException>();
        await session.DisposeAsync();
        disposed.Should().BeTrue();
    }
}
