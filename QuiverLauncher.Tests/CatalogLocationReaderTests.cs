using FluentAssertions;
using QuiverLauncher.Services;
using System.Net;

namespace QuiverLauncher.Tests;

public class CatalogLocationReaderTests
{
    [Fact]
    public async Task ReadAsync_reads_local_file()
    {
        var reader = new CatalogLocationReader();
        var json = await reader.ReadAsync(new HttpClient(), TestFixtures.CommunityIndexPath);

        json.Should().Contain("Nintendo-64");
    }

    [Fact]
    public async Task ReadAsync_fetches_remote_content_via_http_handler()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"apps\":[]}"),
        });
        var client = new HttpClient(handler);
        var reader = new CatalogLocationReader();

        var json = await reader.ReadAsync(client, "https://example.com/apps.json");

        json.Should().Be("{\"apps\":[]}");
    }

    [Fact]
    public async Task ReadAsync_throws_when_local_file_missing()
    {
        var reader = new CatalogLocationReader();
        var act = () => reader.ReadAsync(new HttpClient(), "missing/catalog.json");

        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(_handler(request));
    }
}
