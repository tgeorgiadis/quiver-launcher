using System.Net;
using System.Text;
using FluentAssertions;
using QuiverLauncher.Core.Services;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class RepositoryReadmeServiceTests
{
    [Fact]
    public void BuildGitHubReadmeUrl_uses_repos_readme_endpoint()
    {
        RepositoryReadmeService.BuildGitHubReadmeUrl("owner/regaiden-recomp")
            .Should().Be("https://api.github.com/repos/owner/regaiden-recomp/readme");
    }

    [Fact]
    public void BuildGitLabReadmeUrl_encodes_project_and_file()
    {
        RepositoryReadmeService.BuildGitLabReadmeUrl("group/repo", "README.md")
            .Should().Be("https://gitlab.com/api/v4/projects/group%2Frepo/repository/files/README.md/raw?ref=HEAD");
    }

    [Fact]
    public void BuildRawRootUrl_uses_github_or_gitlab_raw_hosts()
    {
        RepositoryReadmeService.BuildRawRootUrl(null, "owner/repo")
            .Should().Be("https://raw.githubusercontent.com/owner/repo/HEAD/");
        RepositoryReadmeService.BuildRawRootUrl(RepositorySourceIds.GitLab, "owner/repo")
            .Should().Be("https://gitlab.com/owner/repo/-/raw/HEAD/");
    }

    [Fact]
    public void GetCachePath_sanitizes_repo_and_hosts_by_source()
    {
        var root = Path.Combine(Path.GetTempPath(), "quiver-readme-cache");
        RepositoryReadmeService.GetCachePath(root, null, "owner/repo")
            .Should().Be(Path.Combine(root, "Readmes", "github", "owner_repo.md"));
        RepositoryReadmeService.GetCachePath(root, RepositorySourceIds.GitLab, "owner/repo")
            .Should().Be(Path.Combine(root, "Readmes", "gitlab", "owner_repo.md"));
    }

    [Fact]
    public async Task GetReadmeAsync_returns_no_repository_without_network()
    {
        var service = new RepositoryReadmeService();
        using var http = new HttpClient(new StubReadmeHandler());
        var result = await service.GetReadmeAsync(
            http, null, repository: null, null, null, Path.GetTempPath(), DateTime.UtcNow);

        result.Status.Should().Be(RepositoryReadmeStatus.NoRepository);
    }

    [Fact]
    public async Task GetReadmeAsync_github_fetches_raw_markdown_and_caches()
    {
        var cache = CreateCacheRoot();
        var handler = new StubReadmeHandler();
        handler.Responses["https://api.github.com/repos/owner/repo/readme"] =
            (HttpStatusCode.OK, "# Hello\n\nWorld");

        try
        {
            var service = new RepositoryReadmeService();
            using var http = new HttpClient(handler);
            var now = DateTime.UtcNow;

            var first = await service.GetReadmeAsync(http, null, "owner/repo", "token", null, cache, now);
            first.Status.Should().Be(RepositoryReadmeStatus.Markdown);
            first.Markdown.Should().Contain("Hello");
            first.RawRootUrl.Should().Be("https://raw.githubusercontent.com/owner/repo/HEAD/");
            handler.RequestCount.Should().Be(1);
            handler.LastAccept.Should().Contain("application/vnd.github.raw");

            var second = await service.GetReadmeAsync(http, null, "owner/repo", "token", null, cache, now);
            second.Status.Should().Be(RepositoryReadmeStatus.Markdown);
            second.Markdown.Should().Contain("Hello");
            handler.RequestCount.Should().Be(1);
        }
        finally
        {
            DeleteCacheRoot(cache);
        }
    }

    [Fact]
    public async Task GetReadmeAsync_github_404_is_not_found_and_cached()
    {
        var cache = CreateCacheRoot();
        var handler = new StubReadmeHandler();
        handler.Responses["https://api.github.com/repos/owner/missing/readme"] =
            (HttpStatusCode.NotFound, "");

        try
        {
            var service = new RepositoryReadmeService();
            using var http = new HttpClient(handler);
            var now = DateTime.UtcNow;

            var first = await service.GetReadmeAsync(http, null, "owner/missing", null, null, cache, now);
            first.Status.Should().Be(RepositoryReadmeStatus.NotFound);
            handler.RequestCount.Should().Be(1);

            var second = await service.GetReadmeAsync(http, null, "owner/missing", null, null, cache, now);
            second.Status.Should().Be(RepositoryReadmeStatus.NotFound);
            handler.RequestCount.Should().Be(1);
        }
        finally
        {
            DeleteCacheRoot(cache);
        }
    }

    [Fact]
    public async Task GetReadmeAsync_expired_cache_refetches()
    {
        var cache = CreateCacheRoot();
        var handler = new StubReadmeHandler();
        handler.Responses["https://api.github.com/repos/owner/repo/readme"] =
            (HttpStatusCode.OK, "# Fresh");

        try
        {
            var path = RepositoryReadmeService.GetCachePath(cache, null, "owner/repo");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, "# Stale");
            var staleWrite = DateTime.UtcNow.AddHours(-13);
            File.SetLastWriteTimeUtc(path, staleWrite);

            var service = new RepositoryReadmeService();
            using var http = new HttpClient(handler);
            var result = await service.GetReadmeAsync(
                http, null, "owner/repo", null, null, cache, DateTime.UtcNow);

            result.Markdown.Should().Contain("Fresh");
            handler.RequestCount.Should().Be(1);
        }
        finally
        {
            DeleteCacheRoot(cache);
        }
    }

    [Fact]
    public async Task GetReadmeAsync_gitlab_tries_readme_names_then_succeeds()
    {
        var cache = CreateCacheRoot();
        var handler = new StubReadmeHandler();
        handler.Responses[RepositoryReadmeService.BuildGitLabReadmeUrl("group/repo", "README.md")] =
            (HttpStatusCode.NotFound, "");
        handler.Responses[RepositoryReadmeService.BuildGitLabReadmeUrl("group/repo", "readme.md")] =
            (HttpStatusCode.OK, "# GitLab");

        try
        {
            var service = new RepositoryReadmeService();
            using var http = new HttpClient(handler);
            var result = await service.GetReadmeAsync(
                http, RepositorySourceIds.GitLab, "group/repo", null, "gl-token", cache, DateTime.UtcNow);

            result.Status.Should().Be(RepositoryReadmeStatus.Markdown);
            result.Markdown.Should().Contain("GitLab");
            result.RawRootUrl.Should().Be("https://gitlab.com/group/repo/-/raw/HEAD/");
            handler.RequestCount.Should().Be(2);
        }
        finally
        {
            DeleteCacheRoot(cache);
        }
    }

    private static string CreateCacheRoot()
    {
        var cache = Path.Combine(Path.GetTempPath(), "quiver-readme-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(cache);
        return cache;
    }

    private static void DeleteCacheRoot(string cache)
    {
        if (Directory.Exists(cache))
            Directory.Delete(cache, recursive: true);
    }

    private sealed class StubReadmeHandler : HttpMessageHandler
    {
        public Dictionary<string, (HttpStatusCode Status, string Body)> Responses { get; } = new(StringComparer.Ordinal);
        public int RequestCount { get; private set; }
        public string? LastAccept { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            LastAccept = request.Headers.Accept.ToString();
            var url = request.RequestUri?.ToString() ?? string.Empty;
            if (Responses.TryGetValue(url, out var response))
            {
                return Task.FromResult(new HttpResponseMessage(response.Status)
                {
                    Content = new StringContent(response.Body, Encoding.UTF8, "text/plain"),
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
