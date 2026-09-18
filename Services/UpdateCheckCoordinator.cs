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

    public string Details => string.Join("\n\n", Apps.Apps
        .Where(app => app.Outcome != AppCheckOutcome.Successful)
        .Select(app => $"{app.AppName ?? app.IdentityKey}: {app.Reason ?? (app.Outcome == AppCheckOutcome.Cancelled ? "Check did not finish." : app.Outcome == AppCheckOutcome.RateLimited ? "Release service rate limit reached." : "Could not read release information.")}")
        .Concat(Launcher.CheckSucceeded ? [] : new[] { "Quiver Launcher: Its update check did not complete successfully. Try again." }));
}

public interface IUpdateCheckWorkflow
{
    bool CanPresentResults { get; }
    Task<ManualLauncherCheckResult> CheckLauncherAsync(bool manual, CancellationToken token);
    Task<LibraryCheckResult> CheckAppsAsync(bool manual, IProgress<AppCheckProgress> progress, CancellationToken token,
        IReadOnlySet<string>? appKeys = null);
    void ApplyCheckResult(UpdateCheckResult result, DateTime checkedAt);
    void QueuePostCheckWork(UpdateCheckResult result, bool retry = false);
    Task PresentCheckResultAsync(ManualLauncherCheckResult result, int autoUpdated, bool manual);
    Task ReportCheckFailureAsync(Exception error, bool present);
}

/// <summary>One foreground pass; downloads and secondary refreshes never extend its busy state.</summary>
public sealed class UpdateCheckCoordinator(IUpdateCheckWorkflow workflow, TimeSpan? timeout = null)
{
    private readonly object _gate = new();
    private Task? _active;
    private CancellationTokenSource? _cancel;
    private UpdateCheckResult? _lastResult;
    public bool IsChecking { get; private set; }
    public AppCheckProgress Progress { get; private set; } = new(0, 0);
    public event Action? CheckingChanged;
    public event Action? ProgressChanged;
    public void Cancel() { lock (_gate) _cancel?.Cancel(); }

    public Task RetryAsync(bool promptForReview, bool manual, CancellationToken cancellationToken) =>
        CheckAsync(promptForReview, manual, cancellationToken, retry: true);

    public Task CheckAsync(bool promptForReview, bool manual, CancellationToken cancellationToken, bool retry = false)
    {
        lock (_gate)
        {
            if (_active is { IsCompleted: false }) return _active;
            if (cancellationToken.IsCancellationRequested) return Task.CompletedTask;
            return _active = RunAsync(promptForReview, manual, cancellationToken, retry ? _lastResult : null);
        }
    }

    private async Task RunAsync(bool prompt, bool manual, CancellationToken lifetime, UpdateCheckResult? previous)
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
        var launcher = previous?.Launcher ?? new ManualLauncherCheckResult { CheckSucceeded = false };
        var apps = LibraryCheckResult.Empty;
        var interrupted = false;
        var completedApps = new List<AppCheckResult>();
        IReadOnlyList<AppCheckResult>? targets = previous?.Apps.Apps.Where(a => a.Outcome != AppCheckOutcome.Successful).ToArray();
        var appKeys = targets?.Select(a => a.IdentityKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        try
        {
            var launcherTask = previous?.Launcher.CheckSucceeded == true
                ? Task.FromResult(previous.Launcher) : workflow.CheckLauncherAsync(manual, token);
            var appsTask = appKeys is { Count: 0 } ? Task.FromResult(LibraryCheckResult.Empty) : workflow.CheckAppsAsync(manual, new InlineProgress(value =>
            {
                if (token.IsCancellationRequested || !IsChecking) return;
                Progress = value;
                if (value.Targets != null) targets = value.Targets;
                if (value.Result != null) completedApps.Add(value.Result);
                ProgressChanged?.Invoke();
            }), token, appKeys);
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
            apps = new(completedApps.Concat(targets?.Skip(completedApps.Count) ??
                Enumerable.Range(completedApps.Count, Math.Max(0, Progress.Total - completedApps.Count))
                    .Select(i => new AppCheckResult($"unchecked-{i}", AppCheckOutcome.Cancelled, "Unchecked app"))).ToArray());
        if (previous != null)
            apps = new(previous.Apps.Apps.Where(a => a.Outcome == AppCheckOutcome.Successful).Concat(apps.Apps).ToArray());
        var result = new UpdateCheckResult(launcher, apps, interrupted);
        _lastResult = result;
        workflow.ApplyCheckResult(result, DateTime.Now);
        priority.Dispose();
        if (!interrupted) workflow.QueuePostCheckWork(result, retry: previous != null);
        if (prompt && !interrupted && workflow.CanPresentResults)
            await workflow.PresentCheckResultAsync(launcher, 0, manual);
    }

    private sealed class InlineProgress(Action<AppCheckProgress> report) : IProgress<AppCheckProgress>
    {
        public void Report(AppCheckProgress value) => report(value);
    }
}
