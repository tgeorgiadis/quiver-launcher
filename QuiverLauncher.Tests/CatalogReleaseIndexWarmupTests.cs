using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using FluentAssertions;
using QuiverLauncher.Core.Models;
using QuiverLauncher.Core.Services;
using QuiverLauncher.Models;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class CatalogReleaseIndexWarmupTests : IDisposable
{
    private readonly string _cacheDir;

    public CatalogReleaseIndexWarmupTests()
    {
        _cacheDir = Path.Combine(Path.GetTempPath(), "QuiverWarmup_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_cacheDir);
        GitHubApiCache.Initialize(_cacheDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_cacheDir))
                Directory.Delete(_cacheDir, recursive: true);
        }
        catch
        {
            // Best effort
        }
    }

    [Fact]
    public async Task Progress_counts_distinct_pending_repositories_and_hides_cached_only_batches()
    {
        var repo = UniqueRepo("progress");
        var cached = UniqueRepo("cached");
        SeedCache("github", cached, "v1", "etag", Release("v1", "app.zip"), persist: false);
        var reports = new List<CatalogReleaseWarmupProgress>();
        using var client = new HttpClient(LatestHandler(_ => LatestJson("app.zip")));
        Task Report(CatalogReleaseWarmupProgress p) { reports.Add(p); return Task.CompletedTask; }
        await CatalogReleaseIndexWarmup.WarmAsync(client, [Row(repo), Row(repo), Row(cached)], null, CancellationToken.None, onProgress: Report);
        reports.First().Completed.Should().Be(1);
        reports.Last().Should().Be(new CatalogReleaseWarmupProgress(2, 2, CatalogReleaseWarmupOutcome.Completed, Attempted: 1));
        reports.Clear();
        await CatalogReleaseIndexWarmup.WarmAsync(client, [Row(repo)], null, CancellationToken.None, onProgress: Report);
        reports.Should().Equal(new CatalogReleaseWarmupProgress(1, 1, CatalogReleaseWarmupOutcome.Completed));
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, CatalogReleaseWarmupOutcome.Failed)]
    [InlineData(HttpStatusCode.TooManyRequests, CatalogReleaseWarmupOutcome.RateLimited)]
    public async Task Progress_counts_failed_attempts_but_not_skipped_requests(HttpStatusCode status, CatalogReleaseWarmupOutcome outcome)
    {
        var reports = new List<CatalogReleaseWarmupProgress>();
        var requests = 0;
        using var client = new HttpClient(new RecordingHandler(_ =>
        {
            requests++;
            return new HttpResponseMessage(status) { Content = new StringContent("failure") };
        }));
        await CatalogReleaseIndexWarmup.WarmAsync(client, Enumerable.Range(0, 5).Select(_ => Row(UniqueRepo("failed"))), null,
            CancellationToken.None, onProgress: p => { reports.Add(p); return Task.CompletedTask; });
        reports.Last().Outcome.Should().Be(outcome);
        reports.Last().Total.Should().Be(5);
        reports.Last().Completed.Should().Be(0);
        reports.Last().Attempted.Should().Be(requests);
        if (outcome == CatalogReleaseWarmupOutcome.RateLimited) requests.Should().BeLessThan(5);
        else requests.Should().Be(5);
    }

    [Fact]
    public async Task Cancelled_batches_do_not_publish_a_completed_status()
    {
        using var cancellation = new CancellationTokenSource();
        var reports = new List<CatalogReleaseWarmupProgress>();
        using var client = new HttpClient(LatestHandler(_ => LatestJson("app.zip")));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CatalogReleaseIndexWarmup.WarmAsync(client, [Row(UniqueRepo("cancel"))], null, cancellation.Token,
            onProgress: p => { reports.Add(p); cancellation.Cancel(); return Task.CompletedTask; }));
        reports.Should().ContainSingle().Which.Outcome.Should().Be(CatalogReleaseWarmupOutcome.Running);
    }

    [Fact]
    public void SetCache_persist_false_does_not_write_until_flush()
    {
        var repo = UniqueRepo("persist");
        var cacheFile = Path.Combine(_cacheDir, "version_cache.json");

        SeedCache("github", repo, "v1", "etag", Release("v1", "app.apk"), persist: false);

        File.Exists(cacheFile).Should().BeFalse();
        GitHubApiCache.TryGetAssetNames("github", repo, out var names).Should().BeTrue();
        names.Should().Contain("app.apk");

        GitHubApiCache.Flush();
        File.Exists(cacheFile).Should().BeTrue();
        File.ReadAllText(cacheFile).Should().Contain(repo);
        File.ReadAllText(cacheFile).Should().Contain("app.apk");
    }

    [Fact]
    public async Task WarmAsync_coalesces_onUpdated_for_an_instant_burst()
    {
        var repos = Enumerable.Range(0, 8).Select(_ => UniqueRepo("burst")).ToList();
        var updates = 0;
        using var client = new HttpClient(LatestHandler(_ => LatestJson("app.apk")));

        await CatalogReleaseIndexWarmup.WarmAsync(
            client,
            repos.Select(repo => Row(repo)),
            getApiToken: null,
            CancellationToken.None,
            () =>
            {
                Interlocked.Increment(ref updates);
                return Task.CompletedTask;
            });

        updates.Should().Be(1);
        foreach (var repo in repos)
            IndexNames("github", repo, out _).Should().BeTrue();
    }

    [Fact]
    public async Task WarmAsync_does_not_await_ui_callback_per_repo()
    {
        var repos = Enumerable.Range(0, 6).Select(_ => UniqueRepo("gate")).ToList();
        using var client = new HttpClient(LatestHandler(_ => LatestJson("app.apk")));
        var updates = 0;
        var sw = Stopwatch.StartNew();

        await CatalogReleaseIndexWarmup.WarmAsync(
            client,
            repos.Select(repo => Row(repo)),
            getApiToken: null,
            CancellationToken.None,
            async () =>
            {
                Interlocked.Increment(ref updates);
                await Task.Delay(400);
            });

        sw.Stop();
        updates.Should().BeLessThan(repos.Count);
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task WarmAsync_uses_latest_only_when_preferred_version_is_empty()
    {
        var repo = UniqueRepo("latest-only");
        var paths = new ConcurrentBag<string>();
        using var client = new HttpClient(new RecordingHandler(request =>
        {
            paths.Add(request.RequestUri?.AbsolutePath ?? "");
            return JsonOk(LatestJson("game-android.apk"));
        }));

        await CatalogReleaseIndexWarmup.WarmAsync(
            client,
            [Row(repo)],
            getApiToken: null,
            CancellationToken.None);

        paths.Should().ContainSingle(path => path.EndsWith("/releases/latest", StringComparison.OrdinalIgnoreCase));
        paths.Should().NotContain(path => path.EndsWith("/releases", StringComparison.OrdinalIgnoreCase));
        IndexNames("github", repo, out var names).Should().BeTrue();
        names.Should().Contain("game-android.apk");
    }

    [Fact]
    public async Task WarmAsync_uses_full_release_list_when_preferred_version_is_set()
    {
        var repo = UniqueRepo("pinned");
        var paths = new ConcurrentBag<string>();
        using var client = new HttpClient(new RecordingHandler(request =>
        {
            paths.Add(request.RequestUri?.AbsolutePath ?? "");
            var path = request.RequestUri?.AbsolutePath ?? "";
            if (path.EndsWith("/releases/latest", StringComparison.OrdinalIgnoreCase))
                return JsonOk(LatestJson("game-android.apk", "v2.0.0"));

            return JsonOk(
                """[{"tag_name":"v1.2.0","prerelease":false,"assets":[{"name":"game-android.apk","browser_download_url":"https://example.com/app.apk"}]},{"tag_name":"v2.0.0","prerelease":false,"assets":[{"name":"game-android.apk","browser_download_url":"https://example.com/app2.apk"}]}]""");
        }));

        await CatalogReleaseIndexWarmup.WarmAsync(
            client,
            [Row(repo, preferredVersion: "v1.2.0")],
            getApiToken: null,
            CancellationToken.None);

        paths.Should().Contain(path => path.EndsWith("/releases", StringComparison.OrdinalIgnoreCase));
        CatalogPlatformIndex.TryGet("github", repo, "v1.2.0", null, out _).Should().BeTrue();
    }

    [Fact]
    public async Task WarmAsync_falls_back_to_full_list_when_latest_is_404()
    {
        var repo = UniqueRepo("prerelease");
        var paths = new ConcurrentBag<string>();
        using var client = new HttpClient(new RecordingHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath ?? "";
            paths.Add(path);
            if (path.EndsWith("/releases/latest", StringComparison.OrdinalIgnoreCase))
                return new HttpResponseMessage(HttpStatusCode.NotFound);

            return JsonOk(
                """[{"tag_name":"v1.0.0-rc1","prerelease":true,"assets":[{"name":"game-android.apk","browser_download_url":"https://example.com/rc.apk"}]}]""");
        }));

        await CatalogReleaseIndexWarmup.WarmAsync(
            client,
            [Row(repo)],
            getApiToken: null,
            CancellationToken.None);

        paths.Should().Contain(path => path.EndsWith("/releases/latest", StringComparison.OrdinalIgnoreCase));
        paths.Should().Contain(path => path.EndsWith("/releases", StringComparison.OrdinalIgnoreCase));
        IndexNames("github", repo, out var names).Should().BeTrue();
        names.Should().Contain("game-android.apk");
    }

    [Fact]
    public async Task WarmAsync_flushes_asset_index_to_disk()
    {
        var repo = UniqueRepo("disk");
        var cacheFile = Path.Combine(_cacheDir, "catalog_platform_index_v1.json");
        using var client = new HttpClient(LatestHandler(_ => LatestJson("app.apk")));

        await CatalogReleaseIndexWarmup.WarmAsync(
            client,
            [Row(repo)],
            getApiToken: null,
            CancellationToken.None);

        File.Exists(cacheFile).Should().BeTrue();
        File.ReadAllText(cacheFile).Should().Contain("app.apk");
    }

    [Fact]
    public async Task WarmAsync_records_empty_index_when_latest_has_no_usable_release()
    {
        var repo = UniqueRepo("empty-latest");
        using var client = new HttpClient(new RecordingHandler(request =>
            JsonOk(request.RequestUri!.AbsolutePath.EndsWith("/latest")
                ? """{"tag_name":"v1.0.0","prerelease":false,"assets":[]}"""
                : """[{"tag_name":"v1.0.0","prerelease":false,"assets":[]}]""")));

        await CatalogReleaseIndexWarmup.WarmAsync(
            client,
            [Row(repo)],
            getApiToken: null,
            CancellationToken.None);

        CatalogPlatformIndex.IsFresh("github", repo).Should().BeTrue();
        IndexNames("github", repo, out var names).Should().BeTrue();
        names.Should().BeEmpty();
        CatalogPlatformSupport.AppMatches("github", repo, null, ["Android"]).Should().BeFalse();
    }

    [Fact]
    public async Task WarmAsync_records_empty_index_when_latest_404_and_full_list_is_empty()
    {
        var repo = UniqueRepo("no-releases");
        using var client = new HttpClient(new RecordingHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath ?? "";
            if (path.EndsWith("/releases/latest", StringComparison.OrdinalIgnoreCase))
                return new HttpResponseMessage(HttpStatusCode.NotFound);

            return JsonOk("[]");
        }));

        await CatalogReleaseIndexWarmup.WarmAsync(
            client,
            [Row(repo)],
            getApiToken: null,
            CancellationToken.None);

        CatalogPlatformIndex.IsFresh("github", repo).Should().BeTrue();
        IndexNames("github", repo, out var names).Should().BeTrue();
        names.Should().BeEmpty();
        CatalogPlatformSupport.AppMatches("github", repo, null, ["Android"]).Should().BeFalse();
    }

    [Fact]
    public async Task WarmAsync_continues_after_one_403_and_caches_later_repos()
    {
        var denied = UniqueRepo("denied");
        var ok1 = UniqueRepo("ok1");
        var ok2 = UniqueRepo("ok2");
        using var client = new HttpClient(new RecordingHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath ?? "";
            if (path.Contains(denied, StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(HttpStatusCode.Forbidden)
                {
                    Content = new StringContent("""{"message":"Not Found"}""")
                };
            }

            return JsonOk(LatestJson("game-android.apk"));
        }));

        await CatalogReleaseIndexWarmup.WarmAsync(
            client,
            [Row(denied), Row(ok1), Row(ok2)],
            getApiToken: null,
            CancellationToken.None);

        IndexNames("github", denied, out var deniedNames).Should().BeFalse();
        deniedNames.Should().BeEmpty();
        CatalogPlatformSupport.AppMatches("github", denied, null, ["Android"]).Should().BeFalse();

        IndexNames("github", ok1, out var ok1Names).Should().BeTrue();
        ok1Names.Should().Contain("game-android.apk");
        IndexNames("github", ok2, out var ok2Names).Should().BeTrue();
        ok2Names.Should().Contain("game-android.apk");
    }

    [Fact]
    public async Task WarmAsync_does_not_empty_cache_a_rate_limited_repo()
    {
        var limited = UniqueRepo("limited");
        using var client = new HttpClient(new RecordingHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent("""{"message":"API rate limit exceeded for 1.2.3.4"}""")
            }));

        await CatalogReleaseIndexWarmup.WarmAsync(
            client,
            [Row(limited)],
            getApiToken: null,
            CancellationToken.None);

        CatalogPlatformIndex.IsFresh("github", limited).Should().BeFalse();
        IndexNames("github", limited, out _).Should().BeFalse();
        CatalogPlatformSupport.AppMatches("github", limited, null, ["Android"]).Should().BeFalse();
    }

    private static CatalogSyncRowItem Row(string repository, string? preferredVersion = null) =>
        new()
        {
            Repository = repository,
            External = new GameInfo
            {
                Repository = repository,
                Name = repository,
                FolderName = repository.Replace('/', '-'),
                PreferredVersion = preferredVersion,
            },
        };

    [Fact]
    public async Task GitHub_limit_does_not_stop_GitLab_platform_checks()
    {
        var github = Row(UniqueRepo("github-paused"));
        var gitlab = Row(UniqueRepo("gitlab-ok"));
        gitlab.External!.RepositorySource = "gitlab";
        var hosts = new List<string>();
        using var client = new HttpClient(new RecordingHandler(request =>
        {
            hosts.Add(request.RequestUri!.Host);
            return request.RequestUri.Host == "api.github.com"
                ? new HttpResponseMessage(HttpStatusCode.TooManyRequests) : JsonOk("[]");
        }));
        await CatalogReleaseIndexWarmup.WarmAsync(client, [github, gitlab], null, CancellationToken.None);
        hosts.Should().Equal("api.github.com", "gitlab.com");
        CatalogPlatformIndex.IsFresh("gitlab", gitlab.Repository).Should().BeTrue();
        CatalogPlatformIndex.IsFresh("github", github.Repository).Should().BeFalse();
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Failed_refresh_preserves_previously_verified_assets(HttpStatusCode status)
    {
        var repo = UniqueRepo("preserve");
        SeedCache("github", repo, "v1", "legacy", Release("v1", "app.apk"));
        using var client = new HttpClient(new RecordingHandler(_ => new HttpResponseMessage(status)));
        await Assert.ThrowsAsync<ReleaseFetchException>(() => CatalogReleaseIndexWarmup.WarmOneAsync(client,
            new("github", repo, null, () => "test"), "test", CancellationToken.None));
        IndexNames("github", repo, out var assets).Should().BeTrue();
        assets.Should().Equal("app.apk");
    }

    [Fact]
    public async Task Successful_empty_release_replaces_old_platform_support()
    {
        var repo = UniqueRepo("no-longer-android");
        SeedCache("github", repo, "v1", "legacy", Release("v1", "app.apk"));
        using var client = new HttpClient(new RecordingHandler(request => JsonOk(request.RequestUri!.AbsolutePath.EndsWith("/latest")
            ? """{"tag_name":"v2","assets":[]}""" : """[{"tag_name":"v2","assets":[]}]""")));
        await CatalogReleaseIndexWarmup.WarmOneAsync(client, new("github", repo, null, () => ""), null, CancellationToken.None);
        IndexNames("github", repo, out var assets).Should().BeTrue();
        assets.Should().BeEmpty();
        CatalogPlatformSupport.AppMatches("github", repo, null, ["Android"]).Should().BeFalse();
    }

    [Fact]
    public void Legacy_cache_keeps_stale_platforms_but_discards_unscoped_validators()
    {
        var repo = UniqueRepo("legacy-stale");
        File.WriteAllText(Path.Combine(_cacheDir, "version_cache.json"), System.Text.Json.JsonSerializer.Serialize(
            new Dictionary<string, GameVersionCache> { [repo] = new() { Version = "v1", ETag = "legacy-etag",
                LastChecked = DateTime.UtcNow.AddDays(-3), CachedRelease = Release("v1", "app.apk") } }));
        GitHubApiCache.Initialize(_cacheDir);
        GitHubApiCache.GetETag("github", repo).Should().BeEmpty();
        CatalogPlatformIndex.IsFresh("github", repo).Should().BeFalse();
        CatalogPlatformSupport.AppMatches("github", repo, null, ["Android"]).Should().BeTrue();
    }

    private static bool IndexNames(string provider, string repo, out IReadOnlyList<string> names)
    {
        var found = CatalogPlatformIndex.TryGet(provider, repo, null, null, out var entry);
        names = entry?.AssetNames ?? [];
        return found;
    }

    private static void SeedCache(string provider, string repo, string version, string etag, GitHubRelease? release = null, bool persist = true, bool replaceAssetNames = false)
    {
        GitHubApiCache.SetCache(provider, repo, version, etag, release, persist, replaceAssetNames);
        CatalogPlatformIndex.Set(provider, repo, null, null, release);
    }

    private static string UniqueRepo(string suffix) =>
        $"owner/{suffix}-{Guid.NewGuid():N}";

    private static GitHubRelease Release(string tag, params string[] assetNames) =>
        new()
        {
            tag_name = tag,
            assets = assetNames
                .Select(name => new GitHubAsset { name = name, browser_download_url = $"https://example.com/{name}" })
                .ToArray(),
        };

    private static string LatestJson(string assetName, string tag = "v1.0.0") =>
        $$"""{"tag_name":"{{tag}}","prerelease":false,"assets":[{"name":"{{assetName}}","browser_download_url":"https://example.com/{{assetName}}"}]}""";

    private static HttpResponseMessage JsonOk(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json) };

    private static RecordingHandler LatestHandler(Func<HttpRequestMessage, string> json) =>
        new(request => JsonOk(json(request)));

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) =>
            _handler = handler;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(_handler(request));
    }
}
