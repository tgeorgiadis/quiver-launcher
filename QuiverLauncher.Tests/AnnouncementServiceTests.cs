using System.Net;
using System.Net.Http;
using System.Text;
using FluentAssertions;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class AnnouncementServiceTests
{
    [Fact]
    public void TryParse_returns_payload_when_valid()
    {
        var payload = AnnouncementService.TryParse(
            """{"id":"notice-1","enabled":true,"message":"Hello from Quiver"}""");

        payload.Should().NotBeNull();
        payload!.Id.Should().Be("notice-1");
        payload.Message.Should().Be("Hello from Quiver");
        payload.Enabled.Should().BeTrue();
    }

    [Fact]
    public void TryParse_returns_null_when_disabled_or_incomplete()
    {
        AnnouncementService.TryParse(
            """{"id":"notice-1","enabled":false,"message":"Hidden"}""").Should().BeNull();

        AnnouncementService.TryParse(
            """{"id":"","enabled":true,"message":"No id"}""").Should().BeNull();

        AnnouncementService.TryParse(
            """{"id":"notice-1","enabled":true,"message":"  "}""").Should().BeNull();

        AnnouncementService.TryParse("not-json").Should().BeNull();
        AnnouncementService.TryParse(null).Should().BeNull();
    }

    [Fact]
    public void ShouldShow_respects_dismissed_ids()
    {
        var payload = new AnnouncementPayload
        {
            Id = "notice-1",
            Enabled = true,
            Message = "Hello",
        };

        AnnouncementService.ShouldShow(payload, null).Should().BeTrue();
        AnnouncementService.ShouldShow(payload, []).Should().BeTrue();
        AnnouncementService.ShouldShow(payload, ["other"]).Should().BeTrue();
        AnnouncementService.ShouldShow(payload, ["notice-1"]).Should().BeFalse();
        AnnouncementService.ShouldShow(payload, ["NOTICE-1"]).Should().BeFalse();
    }

    [Fact]
    public async Task TryFetchAsync_returns_parsed_payload_on_success()
    {
        using var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"id":"remote-1","enabled":true,"message":"Remote hello"}""",
                    Encoding.UTF8,
                    "application/json"),
            });
        using var client = new HttpClient(handler);

        var payload = await AnnouncementService.TryFetchAsync(client, "https://example.com/announcement.json");

        payload.Should().NotBeNull();
        payload!.Id.Should().Be("remote-1");
        payload.Message.Should().Be("Remote hello");
    }

    [Fact]
    public async Task TryFetchAsync_returns_null_on_http_failure()
    {
        using var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.NotFound));
        using var client = new HttpClient(handler);

        var payload = await AnnouncementService.TryFetchAsync(client, "https://example.com/missing.json");
        payload.Should().BeNull();
    }

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
