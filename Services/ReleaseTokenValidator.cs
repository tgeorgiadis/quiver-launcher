using System.Net;
using System.Net.Http.Headers;
using QuiverLauncher.Core.Services;

namespace QuiverLauncher.Services;

public sealed record ReleaseTokenValidationResult(bool IsValid, string? ErrorMessage = null)
{
    public static readonly ReleaseTokenValidationResult Valid = new(true);
}

public static class ReleaseTokenValidator
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    public static async Task<ReleaseTokenValidationResult> ValidateAsync(
        HttpClient client, string provider, string token, CancellationToken cancellationToken)
    {
        var endpoint = provider switch
        {
            "github" => "https://api.github.com/user",
            "gitlab" => "https://gitlab.com/api/v4/user",
            "codeberg" => "https://codeberg.org/api/v1/user",
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unsupported release provider."),
        };

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        if (provider == "github")
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        else if (provider == "codeberg")
            request.Headers.Authorization = new AuthenticationHeaderValue("token", token);
        else
            request.Headers.TryAddWithoutValidation("PRIVATE-TOKEN", token);

        try
        {
            using var response = await client.SendAsync(request, timeout.Token).ConfigureAwait(false);
            static long? Header(HttpResponseMessage response, string name) => response.Headers.TryGetValues(name, out var values)
                && long.TryParse(values.FirstOrDefault(), out var value) ? value : null;
            var resetSeconds = Header(response, "X-RateLimit-Reset") ?? Header(response, "RateLimit-Reset");
            DateTimeOffset? resetAt = resetSeconds is >= 0 and <= 253402300799
                ? DateTimeOffset.FromUnixTimeSeconds(resetSeconds.Value) : null;
            ReleaseRequestCoordinator.For(client).RecordRateLimit(provider, token,
                Header(response, "X-RateLimit-Limit") ?? Header(response, "RateLimit-Limit"),
                Header(response, "X-RateLimit-Remaining") ?? Header(response, "RateLimit-Remaining"), resetAt);
            if (response.IsSuccessStatusCode)
            {
                ReleaseRequestCoordinator.For(client).RecordValidatedToken(provider, token);
                return ReleaseTokenValidationResult.Valid;
            }

            var reason = response.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "The token was rejected or does not have access.",
                _ => $"The provider returned {(int)response.StatusCode} ({response.ReasonPhrase}).",
            };
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                ReleaseRequestCoordinator.For(client).InvalidateValidatedToken(provider, token);
            return new(false, reason);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(false, "The validation request timed out.");
        }
        catch (HttpRequestException ex)
        {
            return new(false, $"Could not reach the provider: {ex.Message}");
        }
    }
}
