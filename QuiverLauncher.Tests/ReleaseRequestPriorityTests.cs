using System.Net;
using QuiverLauncher.Core.Services;

namespace QuiverLauncher.Tests;

public class ReleaseRequestPriorityTests
{
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token);
    }
    [Fact]
    public async Task Interactive_request_overtakes_queued_background_work_but_never_runs_concurrently()
    {
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var paths = new List<string>();
        var active = 0;
        using var client = new HttpClient(new Handler(async (r, ct) =>
        {
            Assert.Equal(1, Interlocked.Increment(ref active));
            paths.Add(r.RequestUri!.AbsolutePath);
            if (paths.Count == 1) { firstStarted.SetResult(); await releaseFirst.Task.WaitAsync(ct); }
            Interlocked.Decrement(ref active);
            return new(HttpStatusCode.OK) { Content = new StringContent("[]") };
        }));
        var coordinator = ReleaseRequestCoordinator.For(client);
        Task Fetch(string path) => coordinator.FetchAsync(client, new Uri("https://api.github.com/" + path), "github", null, _ => [], TestContext.Current.CancellationToken);
        var first = Fetch("first"); await firstStarted.Task;
        var background = Fetch("background");
        Task foreground;
        using (ReleaseRequestCoordinator.PrioritizeInteractiveChecks()) foreground = Fetch("foreground");
        await Task.Delay(100, TestContext.Current.CancellationToken);
        releaseFirst.SetResult();
        await Task.WhenAll(first, background, foreground);
        Assert.Equal(new[] { "/first", "/foreground", "/background" }, paths);
    }
}
