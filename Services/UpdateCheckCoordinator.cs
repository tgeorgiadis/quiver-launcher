using System.Diagnostics;
using QuiverLauncher.Core.Services;

namespace QuiverLauncher.Services;

public sealed record UpdateCheckResult(ManualLauncherCheckResult Launcher, LibraryCheckResult Apps, bool Interrupted = false)
{
    public bool Complete => !Interrupted && Launcher.CheckSucceeded && Apps.Complete;
    public string? Note => Complete ? null : Interrupted ? "Update check incomplete · Some checks did not finish"
        : Apps.RateLimited > 0 ? "Update check incomplete · Release checks are rate limited"
        : !Apps.Complete ? $"Update check incomplete · {Apps.Failed + Apps.Cancelled} apps could not be checked"
        : "Could not check Quiver Launcher";
}

public interface IUpdateCheckWorkflow
{
    bool CanPresentResults { get; }
    Task<ManualLauncherCheckResult> CheckLauncherAsync(bool manual, CancellationToken token);
    Task<LibraryCheckResult> CheckAppsAsync(bool manual, IProgress<AppCheckProgress> progress, CancellationToken token);
    void ApplyCheckResult(UpdateCheckResult result, DateTime checkedAt);
    void QueuePostCheckWork(UpdateCheckResult result);
    Task PresentCheckResultAsync(ManualLauncherCheckResult result, int autoUpdated, bool manual);
    Task ReportCheckFailureAsync(Exception error, bool present);
}

/// <summary>One foreground pass; downloads and secondary refreshes never extend its busy state.</summary>
public sealed class UpdateCheckCoordinator(IUpdateCheckWorkflow workflow, TimeSpan? timeout = null)
{
    private readonly object _gate = new();
    private Task? _active;
    private CancellationTokenSource? _cancel;
    public bool IsChecking { get; private set; }
    public AppCheckProgress Progress { get; private set; } = new(0, 0);
    public event Action? CheckingChanged;
    public event Action? ProgressChanged;
    public void Cancel() { lock (_gate) _cancel?.Cancel(); }

    public Task CheckAsync(bool promptForReview, bool manual, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_active is { IsCompleted: false }) return _active;
            if (cancellationToken.IsCancellationRequested) return Task.CompletedTask;
            return _active = RunAsync(promptForReview, manual, cancellationToken);
        }
    }

    private async Task RunAsync(bool prompt, bool manual, CancellationToken lifetime)
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        _cancel = cancel;
        cancel.CancelAfter(timeout ?? TimeSpan.FromSeconds(60));
        var token = cancel.Token;
        using var priority = ReleaseRequestCoordinator.PrioritizeInteractiveChecks();
        var watch = Stopwatch.StartNew();
        IsChecking = true;
        Progress = new(0, 0);
        CheckingChanged?.Invoke();
        ProgressChanged?.Invoke();
        var launcher = new ManualLauncherCheckResult { CheckSucceeded = false };
        var apps = LibraryCheckResult.Empty;
        var interrupted = false;
        var completedApps = new List<AppCheckResult>();
        try
        {
            var launcherTask = workflow.CheckLauncherAsync(manual, token);
            var appsTask = workflow.CheckAppsAsync(manual, new InlineProgress(value =>
            {
                if (token.IsCancellationRequested || !IsChecking) return;
                Progress = value;
                if (value.Result != null) completedApps.Add(value.Result);
                ProgressChanged?.Invoke();
            }), token);
            try { await Task.WhenAll(launcherTask, appsTask).WaitAsync(token); }
            finally
            {
                if (launcherTask.IsCompletedSuccessfully) launcher = launcherTask.Result;
                if (appsTask.IsCompletedSuccessfully) apps = appsTask.Result;
                _ = launcherTask.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                _ = appsTask.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { interrupted = true; }
        catch (Exception error)
        {
            interrupted = true;
            cancel.Cancel();
            Trace.WriteLine($"Update check failed: {error.GetType().Name}");
        }
        finally
        {
            lock (_gate) _cancel = null;
            IsChecking = false;
            CheckingChanged?.Invoke();
            Trace.WriteLine($"Foreground update check: elapsedMs={watch.ElapsedMilliseconds}, completed={Progress.Completed}, total={Progress.Total}");
        }
        if (lifetime.IsCancellationRequested) return;
        if (interrupted && apps.Apps.Count == 0)
            apps = new(completedApps.Concat(Enumerable.Range(completedApps.Count, Math.Max(0, Progress.Total - completedApps.Count))
                .Select(i => new AppCheckResult($"unchecked-{i}", AppCheckOutcome.Cancelled))).ToArray());
        var result = new UpdateCheckResult(launcher, apps, interrupted);
        workflow.ApplyCheckResult(result, DateTime.Now);
        priority.Dispose();
        if (!interrupted) workflow.QueuePostCheckWork(result);
        if (prompt && !interrupted && workflow.CanPresentResults)
            await workflow.PresentCheckResultAsync(launcher, 0, manual);
    }

    private sealed class InlineProgress(Action<AppCheckProgress> report) : IProgress<AppCheckProgress>
    {
        public void Report(AppCheckProgress value) => report(value);
    }
}
