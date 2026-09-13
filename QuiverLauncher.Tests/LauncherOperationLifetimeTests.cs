using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FluentAssertions;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class LauncherOperationLifetimeTests
{
    private sealed class Store : ISettingsStore
    {
        public AppSettings Current { get; } = new() { AppsPath = Path.Combine(Path.GetTempPath(), "quiver-operation-tests", Guid.NewGuid().ToString("N")) };
        public AppSettings Load() => Current;
        public void Save(AppSettings settings) { }
    }
    [Fact]
    public async Task Shared_manager_skips_closed_callback_owners_when_last_view_closes()
    {
        using var manager = new GameManager(new Store());
        await using var first = new LauncherSession();
        await using var second = new LauncherSession();
        var calls = 0;
        Func<Action, Task> original = action => { action(); return Task.CompletedTask; };
        manager.UiThreadInvoker = original;
        using var lease1 = new LauncherUiDispatch(manager, first, original);
        using var lease2 = new LauncherUiDispatch(manager, second, original);
        var current = manager.UiThreadInvoker;
        await first.DisposeAsync();
        lease1.Dispose();
        manager.UiThreadInvoker.Should().BeSameAs(current);
        await manager.UiThreadInvoker!(() => calls++);
        calls.Should().Be(1);
        await second.DisposeAsync();
        lease2.Dispose();
        manager.UiThreadInvoker.Should().BeSameAs(original);
    }

    [AvaloniaFact]
    public async Task Closing_session_settles_service_dialog_before_cleanup()
    {
        var session = new LauncherSession();
        var dialog = new Window();
        var closed = false;
        dialog.Closed += (_, _) => closed = true;
        var closedBeforeCleanup = false;
        session.OnShutdown(() => closedBeforeCleanup = closed);
        var pending = session.RunAsync(() => GameDialogService.ShowWindowAsync(dialog));
        dialog.IsVisible.Should().BeTrue();
        await session.DisposeAsync();
        await pending;
        closedBeforeCleanup.Should().BeTrue();
    }

    [AvaloniaFact]
    public async Task Delayed_service_work_cannot_open_a_dialog_after_host_replacement()
    {
        var session = new LauncherSession();
        var dialog = new Window();
        var release = new TaskCompletionSource();
        var pending = session.RunAsync(async () =>
        {
            await release.Task;
            await GameDialogService.ShowWindowAsync(dialog);
        });
        var shutdown = session.DisposeAsync().AsTask();
        release.SetResult();
        await pending;
        await shutdown;
        dialog.IsVisible.Should().BeFalse();
    }
}
