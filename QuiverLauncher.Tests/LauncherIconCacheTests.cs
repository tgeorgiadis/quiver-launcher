using System.Net;
using System.Net.Http;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class LauncherIconCacheTests
{
    [Theory]
    [InlineData("https://github.com/owner/repo/blob/main/icon.png", "raw.githubusercontent.com", true)]
    [InlineData("https://api.github.com/repos/owner/repo/icon", "api.github.com", true)]
    [InlineData("https://example.com/icon.png", "example.com", false)]
    [InlineData("https://github.com.example.com/icon.png", "github.com.example.com", false)]
    [InlineData("http://github.com/icon.png", "github.com", false)]
    public async Task Failed_requests_are_retryable_and_credentials_are_host_scoped(string url, string host, bool authenticated)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png");
        var requests = 0;
        using var client = new HttpClient(new Handler(request =>
        {
            requests++;
            Assert.Equal(host, request.RequestUri!.Host);
            Assert.Equal(authenticated ? "dummy-token" : null, request.Headers.Authorization?.Parameter);
            Assert.DoesNotContain("/blob/", request.RequestUri.AbsolutePath);
            return new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("rate limited") };
        }));
        for (var i = 0; i < 2; i++)
        {
            await Assert.ThrowsAsync<HttpRequestException>(() => LauncherIconCache.FetchAsync(client, url, path, "dummy-token", TestContext.Current.CancellationToken));
            Assert.False(File.Exists(path));
        }
        Assert.Equal(2, requests);
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
}
