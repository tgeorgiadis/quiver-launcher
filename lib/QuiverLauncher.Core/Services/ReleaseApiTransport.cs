namespace QuiverLauncher.Core.Services;

/// <summary>Release API redirects are handled explicitly; downloads retain normal redirect behavior.</summary>
public sealed class ReleaseApiTransport : HttpMessageHandler
{
    private readonly HttpMessageInvoker _api = new(new HttpClientHandler { AllowAutoRedirect = false });
    private readonly HttpMessageInvoker _other = new(new HttpClientHandler());

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        (request.RequestUri?.Host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase) == true
            ? _api : _other).SendAsync(request, cancellationToken);

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _api.Dispose(); _other.Dispose(); }
        base.Dispose(disposing);
    }
}
