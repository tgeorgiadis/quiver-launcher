using FluentAssertions;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class UpdateCheckCoordinatorTests
{
    private sealed class Workflow : IUpdateCheckWorkflow
    {
        public List<string> Calls { get; } = [];
        public Task LauncherGate { get; set; } = Task.CompletedTask;
        public Task AppsGate { get; set; } = Task.CompletedTask;
        public bool CanPresentResults => true;
        public UpdateCheckResult? Result;
        public async Task<ManualLauncherCheckResult> CheckLauncherAsync(bool manual, CancellationToken token)
        { Calls.Add("launcher"); await LauncherGate.WaitAsync(token); return new() { CheckSucceeded = true }; }
        public async Task<LibraryCheckResult> CheckAppsAsync(bool manual, IProgress<AppCheckProgress> progress, CancellationToken token)
        {
            Calls.Add("apps"); progress.Report(new(0, 20));
            await AppsGate.WaitAsync(token);
            return LibraryCheckResult.Empty;
        }
        public void ApplyCheckResult(UpdateCheckResult result, DateTime time) { Result = result; Calls.Add("result"); }
        public void QueuePostCheckWork(UpdateCheckResult result) => Calls.Add("background");
        public Task PresentCheckResultAsync(ManualLauncherCheckResult result, int updated, bool manual) { Calls.Add("prompt"); return Task.CompletedTask; }
        public Task ReportCheckFailureAsync(Exception error, bool present) => Task.CompletedTask;
    }

    [Fact]
    public async Task Launcher_and_apps_start_together_and_result_precedes_background_work()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var workflow = new Workflow { LauncherGate = gate.Task };
        var check = new UpdateCheckCoordinator(workflow);
        var running = check.CheckAsync(true, true, TestContext.Current.CancellationToken);
        workflow.Calls.Should().Equal("launcher", "apps");
        check.IsChecking.Should().BeTrue();
        gate.SetResult(); await running;
        workflow.Calls.Should().Equal("launcher", "apps", "result", "background", "prompt");
        check.IsChecking.Should().BeFalse();
    }

    [Fact]
    public async Task Repeated_clicks_share_work_and_cancel_leaves_an_incomplete_result()
    {
        var workflow = new Workflow { AppsGate = Task.Delay(Timeout.Infinite, TestContext.Current.CancellationToken) };
        var check = new UpdateCheckCoordinator(workflow);
        var first = check.CheckAsync(true, true, TestContext.Current.CancellationToken);
        var second = check.CheckAsync(true, true, TestContext.Current.CancellationToken);
        second.Should().BeSameAs(first);
        check.Cancel(); await first.WaitAsync(TimeSpan.FromSeconds(2));
        workflow.Result!.Complete.Should().BeFalse();
        workflow.Result.Apps.Cancelled.Should().Be(20);
        workflow.Calls.Should().NotContain("prompt").And.NotContain("background");
        check.IsChecking.Should().BeFalse();
        workflow.AppsGate = Task.CompletedTask;
        await check.CheckAsync(false, false, TestContext.Current.CancellationToken);
        workflow.Result.Complete.Should().BeTrue();
    }

    [Fact]
    public async Task Deadline_finishes_without_waiting_for_stalled_requests()
    {
        var workflow = new Workflow { LauncherGate = Task.Delay(Timeout.Infinite, TestContext.Current.CancellationToken) };
        var check = new UpdateCheckCoordinator(workflow, TimeSpan.FromMilliseconds(40));
        await check.CheckAsync(false, true, TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(3));
        workflow.Result!.Interrupted.Should().BeTrue();
        check.IsChecking.Should().BeFalse();
    }

    [Fact]
    public async Task Shutdown_cancels_work_without_publishing_or_starting_background_tasks()
    {
        using var lifetime = new CancellationTokenSource();
        var workflow = new Workflow { AppsGate = Task.Delay(Timeout.Infinite, TestContext.Current.CancellationToken) };
        var check = new UpdateCheckCoordinator(workflow);
        var running = check.CheckAsync(false, false, lifetime.Token);
        lifetime.Cancel(); await running;
        workflow.Result.Should().BeNull();
        workflow.Calls.Should().Equal("launcher", "apps");
    }
}
