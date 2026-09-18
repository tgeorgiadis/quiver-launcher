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
        public IReadOnlyList<AppCheckResult>? Targets;
        public LibraryCheckResult AppResults = LibraryCheckResult.Empty;
        public bool LauncherSucceeds = true;
        public List<IReadOnlySet<string>?> Selections { get; } = [];
        public List<bool> Retries { get; } = [];
        public async Task<ManualLauncherCheckResult> CheckLauncherAsync(bool manual, CancellationToken token)
        { Calls.Add("launcher"); await LauncherGate.WaitAsync(token); return new() { CheckSucceeded = LauncherSucceeds }; }
        public async Task<LibraryCheckResult> CheckAppsAsync(bool manual, IProgress<AppCheckProgress> progress, CancellationToken token,
            IReadOnlySet<string>? appKeys = null)
        {
            Selections.Add(appKeys);
            Calls.Add("apps"); progress.Report(new(0, Targets?.Count ?? 20, Targets: Targets));
            await AppsGate.WaitAsync(token);
            return AppResults;
        }
        public void ApplyCheckResult(UpdateCheckResult result, DateTime time) { Result = result; Calls.Add("result"); }
        public void QueuePostCheckWork(UpdateCheckResult result, bool retry = false) { Retries.Add(retry); Calls.Add("background"); }
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

    [Fact]
    public async Task Retry_only_checks_unsuccessful_apps_and_shrinks_on_each_attempt()
    {
        var workflow = new Workflow { AppResults = new([
            new("ok", AppCheckOutcome.Successful), new("failed", AppCheckOutcome.Failed),
            new("limited", AppCheckOutcome.RateLimited), new("unfinished", AppCheckOutcome.Cancelled)]) };
        var check = new UpdateCheckCoordinator(workflow);
        await check.CheckAsync(false, true, TestContext.Current.CancellationToken);
        workflow.Selections.Single().Should().BeNull();
        workflow.AppResults = new([new("failed", AppCheckOutcome.Successful),
            new("limited", AppCheckOutcome.Failed), new("unfinished", AppCheckOutcome.Successful)]);
        await check.RetryAsync(false, true, TestContext.Current.CancellationToken);
        workflow.Selections.Last().Should().BeEquivalentTo(new[] { "failed", "limited", "unfinished" });
        workflow.Result!.Apps.Successful.Should().Be(3);
        workflow.Result.Apps.Failed.Should().Be(1);
        workflow.Calls.Count(c => c == "launcher").Should().Be(1);
        workflow.Retries.Should().Equal(false, true);
        workflow.AppResults = new([new("limited", AppCheckOutcome.Successful)]);
        await check.RetryAsync(false, true, TestContext.Current.CancellationToken);
        workflow.Selections.Last().Should().BeEquivalentTo(new[] { "limited" });
        workflow.Result!.Complete.Should().BeTrue();
        workflow.Result.Apps.Successful.Should().Be(4);
        workflow.Result.Details.Should().BeEmpty();
        await check.CheckAsync(false, true, TestContext.Current.CancellationToken);
        workflow.Selections.Last().Should().BeNull();
        workflow.Calls.Count(c => c == "launcher").Should().Be(2);
    }

    [Fact]
    public async Task Launcher_only_failure_does_not_recheck_apps()
    {
        var workflow = new Workflow { LauncherSucceeds = false, AppResults = new([new("ok", AppCheckOutcome.Successful)]) };
        var check = new UpdateCheckCoordinator(workflow);
        await check.CheckAsync(false, true, TestContext.Current.CancellationToken);
        workflow.LauncherSucceeds = true;
        await check.RetryAsync(false, true, TestContext.Current.CancellationToken);
        workflow.Calls.Count(c => c == "launcher").Should().Be(2);
        workflow.Selections.Should().ContainSingle();
        workflow.Result!.Complete.Should().BeTrue();
        workflow.Result.Apps.Successful.Should().Be(1);
    }

    [Fact]
    public async Task Retry_drops_failures_for_apps_that_are_no_longer_eligible()
    {
        var workflow = new Workflow { AppResults = new([new("ok", AppCheckOutcome.Successful), new("removed", AppCheckOutcome.Failed)]) };
        var check = new UpdateCheckCoordinator(workflow);
        await check.CheckAsync(false, true, TestContext.Current.CancellationToken);
        workflow.AppResults = LibraryCheckResult.Empty;
        await check.RetryAsync(false, true, TestContext.Current.CancellationToken);
        workflow.Selections.Last().Should().BeEquivalentTo(new[] { "removed" });
        workflow.Result!.Complete.Should().BeTrue();
        workflow.Result.Apps.Apps.Should().ContainSingle().Which.IdentityKey.Should().Be("ok");
    }

    [Fact]
    public async Task Cancelled_retry_keeps_failed_targets_and_successful_results_for_next_retry()
    {
        var workflow = new Workflow { AppResults = new([new("ok", AppCheckOutcome.Successful), new("failed", AppCheckOutcome.Failed)]) };
        var check = new UpdateCheckCoordinator(workflow);
        await check.CheckAsync(false, true, TestContext.Current.CancellationToken);
        workflow.AppsGate = Task.Delay(Timeout.Infinite, TestContext.Current.CancellationToken);
        var retry = check.RetryAsync(false, true, TestContext.Current.CancellationToken);
        check.RetryAsync(false, true, TestContext.Current.CancellationToken).Should().BeSameAs(retry);
        check.Cancel();
        await retry;
        workflow.Result!.Apps.Apps.Select(a => a.IdentityKey).Should().BeEquivalentTo(new[] { "ok", "failed" });
        workflow.Result.Interrupted.Should().BeTrue();
        workflow.AppsGate = Task.CompletedTask;
        workflow.AppResults = new([new("failed", AppCheckOutcome.Successful)]);
        await check.RetryAsync(false, true, TestContext.Current.CancellationToken);
        workflow.Selections.Last().Should().BeEquivalentTo(new[] { "failed" });
        workflow.Result!.Complete.Should().BeTrue();
    }

    [Fact]
    public async Task Interrupted_check_preserves_names_for_unfinished_apps()
    {
        var workflow = new Workflow
        {
            AppsGate = Task.Delay(Timeout.Infinite, TestContext.Current.CancellationToken),
            Targets = [new("one", AppCheckOutcome.Cancelled, "Animal Crossing", "Check did not finish.")],
        };
        var check = new UpdateCheckCoordinator(workflow);
        var running = check.CheckAsync(false, true, TestContext.Current.CancellationToken);
        check.Cancel();
        await running;
        workflow.Result!.Details.Should().Contain("Animal Crossing: Check did not finish.");
        workflow.Result.Details.Should().NotContain("unchecked-");
    }

    [Fact]
    public void Details_show_only_unsuccessful_apps_and_clear_on_retry()
    {
        var result = new UpdateCheckResult(new() { CheckSucceeded = true }, new([
            new("one", AppCheckOutcome.Failed, "Ace Combat", "Access denied (HTTP 403)."),
            new("two", AppCheckOutcome.Successful, "Successful app"),
            new("three", AppCheckOutcome.RateLimited, "Another app", "Rate limit reached."),
        ]));
        result.Details.Should().Contain("Ace Combat: Access denied (HTTP 403).").And.Contain("Another app: Rate limit reached.");
        result.Details.Should().NotContain("Successful app");
        var shell = new QuiverLauncher.ViewModels.ShellViewModel { UpdateCheckDetails = result.Details };
        shell.HasUpdateCheckDetails.Should().BeTrue();
        shell.IsCheckingUpdates = true;
        shell.UpdateCheckDetails.Should().BeEmpty();
        shell.HasUpdateCheckDetails.Should().BeFalse();
    }
}
