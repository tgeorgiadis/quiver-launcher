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
    public void SetCache_persist_false_does_not_write_until_flush()
    {
        var repo = UniqueRepo("persist");
        var cacheFile = Path.Combine(_cacheDir, "version_cache.json");

        GitHubApiCache.SetCache("github", repo, "v1", "etag", Release("v1", "app.apk"), persist: false);

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
            GitHubApiCache.TryGetAssetNames("github", repo, out _).Should().BeTrue();
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
        GitHubApiCache.TryGetAssetNames("github", repo, out var names).Should().BeTrue();
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
        GitHubApiCache.TryGetAssetNames("github", repo, out _).Should().BeTrue();
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
        GitHubApiCache.TryGetAssetNames("github", repo, out var names).Should().BeTrue();
        names.Should().Contain("game-android.apk");
    }

    [Fact]
    public async Task WarmAsync_flushes_asset_index_to_disk()
    {
        var repo = UniqueRepo("disk");
        var cacheFile = Path.Combine(_cacheDir, "version_cache.json");
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
        using var client = new HttpClient(new RecordingHandler(_ =>
            JsonOk("""{"tag_name":"v1.0.0","prerelease":false,"assets":[]}""")));

        await CatalogReleaseIndexWarmup.WarmAsync(
            client,
            [Row(repo)],
            getApiToken: null,
            CancellationToken.None);

        GitHubApiCache.HasFreshAssetIndex("github", repo).Should().BeTrue();
        GitHubApiCache.TryGetAssetNames("github", repo, out var names).Should().BeTrue();
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

        GitHubApiCache.HasFreshAssetIndex("github", repo).Should().BeTrue();
        GitHubApiCache.TryGetAssetNames("github", repo, out var names).Should().BeTrue();
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

        GitHubApiCache.TryGetAssetNames("github", denied, out var deniedNames).Should().BeTrue();
        deniedNames.Should().BeEmpty();
        CatalogPlatformSupport.AppMatches("github", denied, null, ["Android"]).Should().BeFalse();

        GitHubApiCache.TryGetAssetNames("github", ok1, out var ok1Names).Should().BeTrue();
        ok1Names.Should().Contain("game-android.apk");
        GitHubApiCache.TryGetAssetNames("github", ok2, out var ok2Names).Should().BeTrue();
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

        GitHubApiCache.HasFreshAssetIndex("github", limited).Should().BeFalse();
        GitHubApiCache.TryGetAssetNames("github", limited, out _).Should().BeFalse();
        CatalogPlatformSupport.AppMatches("github", limited, null, ["Android"]).Should().BeTrue();
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
