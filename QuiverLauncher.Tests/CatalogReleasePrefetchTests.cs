using QuiverLauncher.Models;
using QuiverLauncher.Services;
using QuiverLauncher.ViewModels;
using QuiverLauncher.Core.Services;
using System.Net;

namespace QuiverLauncher.Tests;

public class CatalogReleasePrefetchTests
{
    [Fact]
    public async Task Rate_limit_resumes_twice_then_requires_explicit_retry()
    {
        var store = new Store();
        var clock = new AdvancingClock();
        var calls = 0;
        using var client = new HttpClient(new LimitedHandler(() => Interlocked.Increment(ref calls)));
        ReleaseRequestCoordinator.For(client).UseClock(clock);
        using var manager = new GameManager(store, client);
        var model = new CatalogSyncViewModel();
        var settings = new SettingsViewModel(store);
        using var workspace = new CatalogReviewWorkspace(model, settings, new ReviewService(),
            (_, _, _) => Task.FromResult(true), () => Task.CompletedTask);
        await using var session = new LauncherSession();
        await workspace.OpenAsync(new() { Id = Guid.NewGuid().ToString("N") }, CatalogReviewFilter.All, session.Token);
        using var prefetch = new CatalogReleasePrefetch(session, manager, settings, model, workspace,
            () => true, action => { action(); return Task.CompletedTask; }, () => { }, clock);
        prefetch.Start();
        await prefetch.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(3, calls);
        Assert.True(model.ShowPlatformRetry);
        Assert.Contains("Select Retry", model.PlatformCheckExplanation);
        Assert.Equal(3, clock.Delays.Count + 1);
        Assert.Equal(TimeSpan.FromSeconds(60), clock.Delays[0]);
        Assert.Equal(TimeSpan.FromSeconds(120), clock.Delays[1]);
        var completedWork = prefetch.Completion;
        prefetch.Start(); // Updating review rows must not launch another platform batch.
        Assert.Same(completedWork, prefetch.Completion);
        Assert.Equal(3, calls);
        // Starting another catalog operation still respects the existing coordinator cooldown.
        var result = await GitHubReleaseService.FetchLatestReleaseIndexAsync(client, "owner/different");
        Assert.True(result.IsRateLimited);
        Assert.Equal(3, calls);
        var source = workspace.ActiveSource!;
        prefetch.Cancel();
        await workspace.OpenAsync(source, CatalogReviewFilter.All, session.Token);
        prefetch.Start();
        await prefetch.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(3, calls); // Navigation does not grant two more automatic retries.
        Assert.True(model.ShowPlatformFailure);
        Assert.Contains("Select Retry", model.PlatformCheckExplanation);
    }

    private sealed class LimitedHandler(Action sent) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { sent(); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)); }
    }
    private sealed class AdvancingClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 9, 11, 0, 0, 0, TimeSpan.Zero);
        public List<TimeSpan> Delays { get; } = [];
        public override DateTimeOffset GetUtcNow() => _now;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Delays.Add(dueTime);
            _now += dueTime;
            return new Timer(callback, state, TimeSpan.FromMilliseconds(1), Timeout.InfiniteTimeSpan);
        }
    }

    [Fact]
    public async Task Cancel_and_source_replacement_reject_queued_progress_and_shutdown_drains_it()
    {
        var store = new Store();
        using var manager = new GameManager(store);
        var model = new CatalogSyncViewModel();
        var settings = new SettingsViewModel(store);
        using var workspace = new CatalogReviewWorkspace(model, settings, new ReviewService(),
            (_, _, _) => Task.FromResult(true), () => Task.CompletedTask);
        await using var session = new LauncherSession();
        await workspace.OpenAsync(new() { Id = Guid.NewGuid().ToString("N") }, CatalogReviewFilter.All, session.Token);
        var queued = new System.Collections.Concurrent.ConcurrentQueue<(Action Action, TaskCompletionSource Done)>();
        Task Dispatch(Action action)
        {
            var done = new TaskCompletionSource(); queued.Enqueue((action, done)); return done.Task;
        }
        using var prefetch = new CatalogReleasePrefetch(session, manager, settings, model, workspace, () => true, Dispatch, () => { });
        session.OnShutdown(prefetch.Cancel);
        prefetch.Start(); prefetch.Start();
        Assert.Single(queued); // Repeated start schedules a follow-up, not another active batch.
        Assert.True(queued.TryDequeue(out var old));
        prefetch.Cancel();
        Assert.True(model.ShowPlatformCheck); // Cancelling does not verify unresolved targets.
        await workspace.OpenAsync(new() { Id = Guid.NewGuid().ToString("N") }, CatalogReviewFilter.All, session.Token);
        model.SetPlatformCheck(new(3, 8, CatalogReleaseWarmupOutcome.Running));
        old.Action(); old.Done.SetResult();
        Assert.Contains("5 apps", model.PlatformCheckText);
        var shutdown = session.DisposeAsync().AsTask();
        // Drain the old operation's guarded cleanup callback.
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!shutdown.IsCompleted)
        {
            if (queued.TryDequeue(out var next)) { next.Action(); next.Done.SetResult(); }
            else await Task.Delay(1, deadline.Token);
        }
        await shutdown;
        Assert.True(model.ShowPlatformCheck); // Missing metadata remains explicit when the batch stops.
    }

    private sealed class Store : ISettingsStore
    {
        public AppSettings Current { get; } = new() { FirstStartup = false, AppsPath = Path.GetTempPath() };
        public AppSettings Load() => Current;
        public void Save(AppSettings settings) { }
    }
    private sealed class ReviewService : ICatalogReviewService
    {
        public Task<List<GameInfo>> LoadLocalAsync() => Task.FromResult(new List<GameInfo>());
        public Task<List<GameInfo>> LoadCachedAsync(string id) => Task.FromResult(new List<GameInfo> { new() { Name = id, Repository = "owner/" + id, FolderName = id } });
        public Task FetchAsync(AppCatalogSource source) => Task.CompletedTask;
        public Task SaveLocalAsync(List<GameInfo> apps, AppCatalogSource source, CancellationToken token) => Task.CompletedTask;
        public void RefreshAvailability(AppCatalogSource source, List<GameInfo> local, List<GameInfo> external) { }
        public void Acknowledge(AppCatalogSource source) { }
    }
}
