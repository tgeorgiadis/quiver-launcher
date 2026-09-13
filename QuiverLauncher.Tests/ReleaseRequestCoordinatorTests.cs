using System.Net;
using System.Net.Http.Headers;
using QuiverLauncher.Core.Models;
using QuiverLauncher.Core.Services;

namespace QuiverLauncher.Tests;

public class ReleaseRequestCoordinatorTests
{
    private static readonly Uri Endpoint = new("https://api.github.com/repos/owner/repo/releases/latest");
    private static HttpResponseMessage Ok(string asset = "game.apk") => new(HttpStatusCode.OK)
    { Content = new StringContent($$"""{"tag_name":"v1","assets":[{"name":"{{asset}}"}]}""") };
    private static IReadOnlyList<GitHubRelease> Parse(string body) =>
        [System.Text.Json.JsonSerializer.Deserialize<GitHubRelease>(body)!];
    private static HttpResponseMessage Redirect(string url, HttpStatusCode status = HttpStatusCode.MovedPermanently) =>
        new(status) { Headers = { Location = new Uri(url, UriKind.RelativeOrAbsolute) } };
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => send(request, ct);
    }
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 9, 11, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public async Task Moved_repository_keeps_trimmed_token_and_remembers_destination()
    {
        var urls = new List<string>();
        using var client = new HttpClient(new Handler((request, _) =>
        {
            Assert.Equal("test-token", request.Headers.Authorization?.Parameter);
            urls.Add(request.RequestUri!.AbsolutePath);
            return Task.FromResult(request.RequestUri == Endpoint
                ? Redirect("https://api.github.com/repositories/123/releases/latest") : Ok());
        }));
        var coordinator = new ReleaseRequestCoordinator();
        for (var i = 0; i < 2; i++)
        {
            var result = await coordinator.FetchAsync(client, Endpoint, "github", "  test-token  ", Parse);
            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            Assert.True(result.IsAuthenticated);
            Assert.Single(result.Releases);
        }
        Assert.Equal(new[] { Endpoint.AbsolutePath, "/repositories/123/releases/latest", "/repositories/123/releases/latest" }, urls);
    }

    [Theory]
    [InlineData("https://example.com/releases")]
    [InlineData("http://api.github.com/releases")]
    [InlineData("https://api.github.com:444/releases")]
    [InlineData("https://user@api.github.com/releases")]
    [InlineData("https://api.github.com/repos/owner/repo/releases/latest")]
    public async Task Unsafe_or_looping_redirect_never_sends_a_second_request(string destination)
    {
        var calls = 0;
        using var client = new HttpClient(new Handler((_, _) => { calls++; return Task.FromResult(Redirect(destination)); }));
        await Assert.ThrowsAsync<HttpRequestException>(() => new ReleaseRequestCoordinator().FetchAsync(client, Endpoint, "github", "test-token", Parse));
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(5, true)]
    [InlineData(6, false)]
    public async Task Follows_at_most_five_redirects(int hops, bool succeeds)
    {
        var calls = 0;
        using var client = new HttpClient(new Handler((_, _) => Task.FromResult(++calls <= hops ? Redirect($"https://api.github.com/hop/{calls}") : Ok())));
        var task = new ReleaseRequestCoordinator().FetchAsync(client, Endpoint, "github", "test", Parse);
        if (succeeds) Assert.Equal(HttpStatusCode.OK, (await task).StatusCode);
        else await Assert.ThrowsAsync<HttpRequestException>(() => task);
        Assert.Equal(6, calls);
    }

    [Fact]
    public async Task Coalesces_duplicates_and_canceling_one_waiter_does_not_cancel_another()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var client = new HttpClient(new Handler(async (_, ct) =>
        { Interlocked.Increment(ref calls); entered.SetResult(); await release.Task.WaitAsync(ct); return Ok(); }));
        var coordinator = new ReleaseRequestCoordinator();
        using var canceled = new CancellationTokenSource();
        var first = coordinator.FetchAsync(client, Endpoint, "github", "test", Parse, canceled.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = coordinator.FetchAsync(client, Endpoint, "github", "test", Parse);
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        release.SetResult();
        Assert.Equal(HttpStatusCode.OK, (await second).StatusCode);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Canceling_last_waiter_cancels_the_http_request()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var client = new HttpClient(new Handler(async (_, ct) =>
        {
            entered.SetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); return Ok(); }
            finally { ended.SetResult(); }
        }));
        using var cancellation = new CancellationTokenSource();
        var task = new ReleaseRequestCoordinator().FetchAsync(client, Endpoint, "github", "test", Parse, cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        await ended.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Serializes_different_github_requests()
    {
        var active = 0; var maximum = 0;
        using var client = new HttpClient(new Handler(async (_, ct) =>
        {
            maximum = Math.Max(maximum, Interlocked.Increment(ref active));
            await Task.Delay(10, ct);
            Interlocked.Decrement(ref active); return Ok();
        }));
        var coordinator = new ReleaseRequestCoordinator();
        await Task.WhenAll(Enumerable.Range(0, 8).Select(i => coordinator.FetchAsync(client, new Uri(Endpoint + "?id=" + i), "github", "test", Parse)));
        Assert.Equal(1, maximum);
    }

    [Fact]
    public async Task Primary_cooldown_is_shared_across_endpoints_but_not_tokens_or_providers()
    {
        var clock = new Clock(); var calls = 0;
        using var client = new HttpClient(new Handler((request, _) =>
        {
            calls++;
            if (request.Headers.Authorization?.Parameter != "limited") return Task.FromResult(Ok());
            var response = new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("rate limit exceeded") };
            response.Headers.Add("X-RateLimit-Limit", "5000");
            response.Headers.Add("X-RateLimit-Remaining", "0");
            response.Headers.Add("X-RateLimit-Reset", clock.Now.AddMinutes(10).ToUnixTimeSeconds().ToString());
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
            return Task.FromResult(response);
        }));
        var coordinator = new ReleaseRequestCoordinator(clock);
        var first = await coordinator.FetchAsync(client, Endpoint, "github", "limited", Parse);
        Assert.Equal(ReleaseRateLimitKind.Primary, first.RateLimitKind);
        Assert.Equal(clock.Now.AddMinutes(10), first.RetryAt);
        var second = await coordinator.FetchAsync(client, new Uri(Endpoint + "?other"), "github", "limited", Parse);
        Assert.Equal(first, second); Assert.Equal(1, calls);
        Assert.Equal(HttpStatusCode.OK, (await coordinator.FetchAsync(client, Endpoint, "github", "new-token", Parse)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await coordinator.FetchAsync(client, new Uri("https://gitlab.com/api/v4/projects/1/releases"), "gitlab", "limited", Parse)).StatusCode);
        Assert.Equal(3, calls);
        clock.Now = first.RetryAt!.Value;
        await coordinator.FetchAsync(client, Endpoint, "github", "limited", Parse);
        Assert.Equal(4, calls);
    }

    [Fact]
    public async Task Secondary_cooldown_backs_off_and_preserves_provider_metadata()
    {
        var clock = new Clock(); var calls = 0;
        using var client = new HttpClient(new Handler((_, _) =>
        {
            calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        }));
        var coordinator = new ReleaseRequestCoordinator(clock);
        var first = await coordinator.FetchAsync(client, Endpoint, "github", null, Parse);
        Assert.False(first.IsAuthenticated); Assert.Equal("github", first.Provider);
        Assert.Equal(ReleaseRateLimitKind.Secondary, first.RateLimitKind);
        Assert.Equal(clock.Now.AddSeconds(60), first.RetryAt);
        clock.Now = first.RetryAt!.Value;
        var second = await coordinator.FetchAsync(client, Endpoint, "github", null, Parse);
        Assert.Equal(clock.Now.AddSeconds(120), second.RetryAt);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Validators_are_endpoint_and_credential_specific_and_304_reuses_stale_payload()
    {
        var clock = new Clock();
        var seen = new Dictionary<string, int>();
        using var client = new HttpClient(new Handler((request, _) =>
        {
            var identity = request.RequestUri!.AbsoluteUri + request.Headers.Authorization?.Parameter;
            var count = seen.GetValueOrDefault(identity); seen[identity] = count + 1;
            if (count == 0)
            {
                Assert.Empty(request.Headers.IfNoneMatch);
                var response = Ok(); response.Headers.ETag = new EntityTagHeaderValue("\"etag\"", isWeak: true); return Task.FromResult(response);
            }
            Assert.Equal("W/\"etag\"", request.Headers.IfNoneMatch.Single().ToString());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified));
        }));
        var coordinator = new ReleaseRequestCoordinator(clock);
        await coordinator.FetchAsync(client, Endpoint, "github", "one", Parse);
        await coordinator.FetchAsync(client, new Uri(Endpoint + "/other"), "github", "one", Parse);
        await coordinator.FetchAsync(client, Endpoint, "github", "two", Parse);
        clock.Now = clock.Now.AddDays(3);
        var result = await coordinator.FetchAsync(client, Endpoint, "github", "one", Parse);
        Assert.True(result.WasNotModified); Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal("game.apk", result.Releases.Single().assets.Single().name);
    }

    [Fact]
    public async Task Failed_access_does_not_replace_cached_response()
    {
        var calls = 0;
        using var client = new HttpClient(new Handler((request, _) =>
        {
            calls++;
            if (calls == 1) { var response = Ok(); response.Headers.ETag = new("\"good\""); return Task.FromResult(response); }
            Assert.Equal("\"good\"", request.Headers.IfNoneMatch.Single().ToString());
            return Task.FromResult(new HttpResponseMessage(calls == 2 ? HttpStatusCode.Unauthorized : HttpStatusCode.NotModified));
        }));
        var coordinator = new ReleaseRequestCoordinator();
        await coordinator.FetchAsync(client, Endpoint, "github", "test", Parse);
        Assert.Equal(HttpStatusCode.Unauthorized, (await coordinator.FetchAsync(client, Endpoint, "github", "test", Parse)).StatusCode);
        Assert.Single((await coordinator.FetchAsync(client, Endpoint, "github", "test", Parse)).Releases);
    }

    [Fact]
    public void Endpoint_cache_round_trips_body_and_validation_time()
    {
        var path = Path.Combine(Path.GetTempPath(), "QuiverReleaseCacheTests", Guid.NewGuid().ToString("N"));
        try
        {
            var entry = new ReleaseEndpointCache.Entry("[]", "W/\"release\"", DateTimeOffset.UtcNow.AddDays(-3));
            new ReleaseEndpointCache(path).Set("github:anonymous:https://api.github.com/repos/a/b/releases", entry);
            Assert.Equal(entry, new ReleaseEndpointCache(path).Get("github:anonymous:https://api.github.com/repos/a/b/releases"));
        }
        finally { if (Directory.Exists(path)) Directory.Delete(path, true); }
    }
}
