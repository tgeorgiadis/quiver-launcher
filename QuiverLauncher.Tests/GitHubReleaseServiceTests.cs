using System.Net;
using FluentAssertions;
using QuiverLauncher.Core.Models;
using QuiverLauncher.Core.Services;

namespace QuiverLauncher.Tests;

public class GitHubReleaseServiceTests
{
    [Fact]
    public void MergeLatestRelease_prepends_latest_when_missing_from_list()
    {
        var rc5 = Release("1.1-rc5");
        var latest = Release("1.1.0");

        var merged = GitHubReleaseService.MergeLatestRelease([rc5], latest);

        merged.Should().Equal(latest, rc5);
    }

    [Fact]
    public void MergeLatestRelease_does_not_add_duplicate_tag()
    {
        var latest = Release("1.1.0");
        var rc5 = Release("1.1-rc5");
        var existing = Release("1.1.0");

        var merged = GitHubReleaseService.MergeLatestRelease([rc5, existing], latest);

        merged.Should().HaveCount(2);
        merged[0].Should().BeSameAs(rc5);
        merged[1].Should().BeSameAs(existing);
    }

    [Fact]
    public void MergeLatestRelease_skips_latest_without_assets()
    {
        var rc5 = Release("1.1-rc5");
        var latest = new GitHubRelease { tag_name = "1.1.0", assets = [] };

        var merged = GitHubReleaseService.MergeLatestRelease([rc5], latest);

        merged.Should().Equal(rc5);
    }

    [Fact]
    public async Task FetchLatestReleaseAsync_returns_null_on_404()
    {
        using var client = new HttpClient(new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.NotFound)));

        var latest = await GitHubReleaseService.FetchLatestReleaseAsync(client, "owner/repo");

        latest.Should().BeNull();
    }

    [Fact]
    public async Task FetchReleasesAsync_merges_latest_once_and_sets_tag()
    {
        using var client = new HttpClient(new StubHttpMessageHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath ?? "";
            if (path.EndsWith("/releases/latest", StringComparison.OrdinalIgnoreCase))
            {
                return JsonOk("""{"tag_name":"1.1.0","prerelease":false,"assets":[{"name":"app.zip","browser_download_url":"https://example.com/1.1.0.zip"}]}""");
            }

            if (path.EndsWith("/releases", StringComparison.OrdinalIgnoreCase))
            {
                return JsonOk("""[{"tag_name":"1.1-rc5","prerelease":false,"assets":[{"name":"app.zip","browser_download_url":"https://example.com/rc5.zip"}]}]""");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }));

        var result = await GitHubReleaseService.FetchReleasesAsync(client, "owner/smb");

        result.LatestTag.Should().Be("1.1.0");
        result.Releases.Should().HaveCount(2);
        result.Releases[0].tag_name.Should().Be("1.1.0");
        result.Releases[1].tag_name.Should().Be("1.1-rc5");
    }

    [Fact]
    public async Task FetchReleasesAsync_does_not_duplicate_latest_already_in_list()
    {
        using var client = new HttpClient(new StubHttpMessageHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath ?? "";
            if (path.EndsWith("/releases/latest", StringComparison.OrdinalIgnoreCase))
            {
                return JsonOk("""{"tag_name":"1.1.0","prerelease":false,"assets":[{"name":"app.zip","browser_download_url":"https://example.com/1.1.0.zip"}]}""");
            }

            if (path.EndsWith("/releases", StringComparison.OrdinalIgnoreCase))
            {
                return JsonOk("""[{"tag_name":"1.1-rc5","prerelease":false,"assets":[{"name":"app.zip","browser_download_url":"https://example.com/rc5.zip"}]},{"tag_name":"1.1.0","prerelease":false,"assets":[{"name":"app.zip","browser_download_url":"https://example.com/1.1.0.zip"}]}]""");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }));

        var result = await GitHubReleaseService.FetchReleasesAsync(client, "owner/smb");

        result.LatestTag.Should().Be("1.1.0");
        result.Releases.Should().HaveCount(2);
        result.Releases.Count(release => release.tag_name == "1.1.0").Should().Be(1);
        result.Releases[0].tag_name.Should().Be("1.1-rc5");
    }

    [Fact]
    public async Task FetchReleasesAsync_keeps_list_when_latest_is_404()
    {
        using var client = new HttpClient(new StubHttpMessageHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath ?? "";
            if (path.EndsWith("/releases/latest", StringComparison.OrdinalIgnoreCase))
                return new HttpResponseMessage(HttpStatusCode.NotFound);

            return JsonOk("""[{"tag_name":"github-v1.0.9","prerelease":false,"assets":[{"name":"app.zip","browser_download_url":"https://example.com/relive.zip"}]}]""");
        }));

        var result = await GitHubReleaseService.FetchReleasesAsync(client, "owner/relive");

        result.LatestTag.Should().BeNull();
        result.Releases.Should().ContainSingle(release => release.tag_name == "github-v1.0.9");
    }

    [Fact]
    public async Task FetchLatestReleaseIndexAsync_returns_single_release()
    {
        using var client = new HttpClient(new StubHttpMessageHandler(_ =>
            JsonOk("""{"tag_name":"1.2.0","prerelease":false,"assets":[{"name":"app.apk","browser_download_url":"https://example.com/app.apk"}]}""")));

        var result = await GitHubReleaseService.FetchLatestReleaseIndexAsync(client, "owner/repo");

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        result.LatestTag.Should().Be("1.2.0");
        result.Releases.Should().ContainSingle(release => release.tag_name == "1.2.0");
    }

    [Fact]
    public async Task FetchLatestReleaseIndexAsync_revalidates_its_own_cached_payload()
    {
        var calls = 0;
        using var client = new HttpClient(new StubHttpMessageHandler(request =>
        {
            if (++calls == 1)
            {
                request.Headers.IfNoneMatch.Should().BeEmpty("legacy ETags have unknown endpoint identity");
                var response = JsonOk("""{"tag_name":"v1","assets":[]}""");
                response.Headers.ETag = new("\"abc\"");
                return response;
            }
            request.Headers.IfNoneMatch.Single().Tag.Should().Be("\"abc\"");
            return new HttpResponseMessage(HttpStatusCode.NotModified);
        }));
        await GitHubReleaseService.FetchLatestReleaseIndexAsync(client, "owner/repo", etag: "\"legacy\"");
        var result = await GitHubReleaseService.FetchLatestReleaseIndexAsync(client, "owner/repo");
        result.WasNotModified.Should().BeTrue();
        result.StatusCode.Should().Be(HttpStatusCode.OK);
        result.Releases.Should().ContainSingle();
    }

    [Fact]
    public async Task FetchLatestReleaseIndexAsync_returns_not_found_without_throwing()
    {
        using var client = new HttpClient(new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.NotFound)));

        var result = await GitHubReleaseService.FetchLatestReleaseIndexAsync(client, "owner/repo");

        result.StatusCode.Should().Be(HttpStatusCode.NotFound);
        result.Releases.Should().BeEmpty();
    }

    [Fact]
    public async Task FetchLatestReleaseIndexAsync_surfaces_rate_limit_status()
    {
        using var client = new HttpClient(new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent("""{"message":"API rate limit exceeded"}""")
            }));

        var result = await GitHubReleaseService.FetchLatestReleaseIndexAsync(client, "owner/repo");

        result.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        result.IsRateLimited.Should().BeTrue();
    }

    [Fact]
    public async Task FetchLatestReleaseIndexAsync_treats_plain_403_as_not_rate_limited()
    {
        using var client = new HttpClient(new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent("""{"message":"Not Found"}""")
            }));

        var result = await GitHubReleaseService.FetchLatestReleaseIndexAsync(client, "owner/repo");

        result.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        result.IsRateLimited.Should().BeFalse();
    }

    private static GitHubRelease Release(string tag) =>
        new()
        {
            tag_name = tag,
            assets = [new GitHubAsset { name = "app.zip", browser_download_url = "https://example.com/app.zip" }],
        };

    private static HttpResponseMessage JsonOk(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json),
        };

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) =>
            _handler = handler;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(_handler(request));
    }
}
