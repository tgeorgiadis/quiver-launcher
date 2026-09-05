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

    [Fact]
    public async Task ReadAsync_fetches_ipfs_content_via_local_node_cat_endpoint()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"apps\":[]}"),
            };
        });
        var client = new HttpClient(handler);
        var reader = new CatalogLocationReader();

        var json = await reader.ReadAsync(client, "ipfs://bafybeigdyrzt5sfp7udm7hu76uh7y26nf3efuylqabf3oclgtqy55fbzdi");

        json.Should().Be("{\"apps\":[]}");
        capturedRequest.Should().NotBeNull();
        capturedRequest!.Method.Should().Be(HttpMethod.Post);
        capturedRequest.RequestUri!.ToString().Should().Contain("/api/v0/cat");
        capturedRequest.RequestUri!.ToString().Should().Contain("bafybeigdyrzt5sfp7udm7hu76uh7y26nf3efuylqabf3oclgtqy55fbzdi");
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
