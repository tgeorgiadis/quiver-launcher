using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using QuiverLauncher.Core.Models;

namespace QuiverLauncher.Core.Services;

public enum ReleaseRateLimitKind { None, Primary, Secondary }

public sealed class ReleaseFetchException(GitHubReleaseFetchResult result)
    : HttpRequestException(result.ErrorMessage ?? "Release metadata is unavailable.", null, result.StatusCode)
{
    public GitHubReleaseFetchResult Result { get; } = result;
}

/// <summary>One coordinator per session HTTP client. Tokens are never persisted or included in diagnostics.</summary>
public sealed class ReleaseRequestCoordinator
{
    private static readonly ConditionalWeakTable<HttpClient, ReleaseRequestCoordinator> Instances = new();
    public static ReleaseRequestCoordinator For(HttpClient client) => Instances.GetValue(client, _ => new());
    public static void Configure(HttpClient client, string cacheDirectory) => For(client)._cache = new(cacheDirectory);
    public static string CredentialKey(string? token) => string.IsNullOrWhiteSpace(token) ? "anonymous"
        : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token.Trim())));

    private sealed class Flight
    {
        public readonly CancellationTokenSource Cancellation = new();
        public Task<GitHubReleaseFetchResult> Task = null!;
        public int Waiters;
    }
    private readonly object _gate = new();
    private readonly Dictionary<string, Flight> _flights = [];
    private readonly Dictionary<string, GitHubReleaseFetchResult> _cooldowns = [];
    private readonly Dictionary<string, int> _secondaryFailures = [];
    private readonly Dictionary<string, Uri> _redirects = [];
    private readonly SemaphoreSlim _github = new(1, 1);
    private readonly SemaphoreSlim _gitlab = new(1, 1);
    private ReleaseEndpointCache _cache = new();
    public TimeProvider Clock { get; private set; } = TimeProvider.System;
    public ReleaseRequestCoordinator(TimeProvider? clock = null) => Clock = clock ?? TimeProvider.System;
    internal void UseClock(TimeProvider clock) => Clock = clock;

    public async Task<GitHubReleaseFetchResult> FetchAsync(HttpClient client, Uri endpoint, string provider,
        string? token, Func<string, IReadOnlyList<GitHubRelease>> parse, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        token = token?.Trim();
        var context = provider + ":" + CredentialKey(token);
        var key = context + ":" + endpoint.AbsoluteUri;
        Flight flight;
        lock (_gate)
        {
            if (!_flights.TryGetValue(key, out flight!))
            {
                flight = new Flight();
                _flights[key] = flight;
                var captured = flight;
                flight.Task = Task.Run(() => FetchCoreAsync(client, endpoint, provider, token, parse, context, key, captured.Cancellation.Token));
            }
            flight.Waiters++;
        }
        try { return await flight.Task.WaitAsync(cancellationToken).ConfigureAwait(false); }
        finally
        {
            lock (_gate)
            {
                if (--flight.Waiters == 0)
                {
                    _flights.Remove(key);
                    if (!flight.Task.IsCompleted) flight.Cancellation.Cancel();
                    _ = flight.Task.ContinueWith(_ => flight.Cancellation.Dispose(), TaskScheduler.Default);
                }
            }
        }
    }

    private async Task<GitHubReleaseFetchResult> FetchCoreAsync(HttpClient client, Uri endpoint, string provider,
        string? token, Func<string, IReadOnlyList<GitHubRelease>> parse, string context, string key, CancellationToken ct)
    {
        var semaphore = provider == "github" ? _github : _gitlab;
        await semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            lock (_gate)
                if (_cooldowns.TryGetValue(context, out var paused) && paused.RetryAt > Clock.GetUtcNow()) return paused;
            var cached = _cache.Get(key);
            Uri url;
            lock (_gate) url = _redirects.GetValueOrDefault(key) ?? endpoint;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var redirects = 0;
            var unconditionalRetry = false;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                if (provider == "github" && !IsTrustedGitHubUri(url))
                    throw new HttpRequestException("Rejected an untrusted GitHub API redirect.");
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                if (!string.IsNullOrWhiteSpace(token))
                {
                    if (provider == "github") request.Headers.Authorization = new("Bearer", token);
                    else request.Headers.TryAddWithoutValidation("PRIVATE-TOKEN", token);
                }
                if (!unconditionalRetry && !string.IsNullOrEmpty(cached?.ETag))
                    request.Headers.TryAddWithoutValidation("If-None-Match", cached.ETag);
                using var response = await client.SendAsync(request, ct).ConfigureAwait(false);
                if (provider == "github" && response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Found
                    or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
                {
                    if (response.Headers.Location == null || ++redirects > 5 || !seen.Add(url.AbsoluteUri))
                        throw new HttpRequestException("Invalid or excessive GitHub API redirects.");
                    var destination = new Uri(url, response.Headers.Location);
                    if (!IsTrustedGitHubUri(destination) || seen.Contains(destination.AbsoluteUri))
                        throw new HttpRequestException("Rejected an unsafe or looping GitHub API redirect.");
                    if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.PermanentRedirect)
                        lock (_gate) _redirects[key] = destination;
                    url = destination;
                    continue;
                }
                // Protect callers that accidentally supply an automatically redirecting client.
                if (provider == "github" && response.RequestMessage?.RequestUri is { } final && final != url)
                    throw new HttpRequestException("GitHub API client followed a redirect without preserving authentication.");
                var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var result = Describe(response, provider, !string.IsNullOrWhiteSpace(token), body, context);
                if (result.IsRateLimited)
                {
                    lock (_gate) _cooldowns[context] = result;
                    return result;
                }
                if (response.StatusCode == HttpStatusCode.NotModified)
                {
                    if (cached == null)
                    {
                        if (unconditionalRetry) throw new HttpRequestException("Release endpoint returned 304 without a cached payload.");
                        unconditionalRetry = true;
                        continue;
                    }
                    body = cached.Body;
                    result = result with { StatusCode = HttpStatusCode.OK, WasNotModified = true };
                }
                if (result.StatusCode != HttpStatusCode.OK) return result;
                var releases = parse(body);
                var etag = response.Headers.ETag?.ToString() ?? (result.WasNotModified ? cached?.ETag : null);
                _cache.Set(key, new(body, etag, Clock.GetUtcNow()));
                lock (_gate) { _cooldowns.Remove(context); _secondaryFailures.Remove(context); }
                return result with { Releases = releases, ETag = etag };
            }
        }
        finally { semaphore.Release(); }
    }

    public static bool IsTrustedGitHubUri(Uri uri) => uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort
        && uri.Host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(uri.UserInfo);

    private GitHubReleaseFetchResult Describe(HttpResponseMessage response, string provider, bool authenticated, string body, string context)
    {
        static long? Number(HttpResponseHeaders headers, string name) => headers.TryGetValues(name, out var values)
            && long.TryParse(values.FirstOrDefault(), out var value) ? value : null;
        static DateTimeOffset? Timestamp(long? value) => value is >= 0 and <= 253402300799 ? DateTimeOffset.FromUnixTimeSeconds(value.Value) : null;
        var limit = Number(response.Headers, "X-RateLimit-Limit") ?? Number(response.Headers, "RateLimit-Limit");
        var remaining = Number(response.Headers, "X-RateLimit-Remaining") ?? Number(response.Headers, "RateLimit-Remaining");
        var reset = Timestamp(Number(response.Headers, "X-RateLimit-Reset") ?? Number(response.Headers, "RateLimit-Reset"));
        var limited = GitHubReleaseService.IsRateLimitResponse(response.StatusCode, response.Headers, body);
        var kind = !limited ? ReleaseRateLimitKind.None : remaining == 0 ? ReleaseRateLimitKind.Primary : ReleaseRateLimitKind.Secondary;
        DateTimeOffset? retry = null;
        if (limited)
        {
            var now = Clock.GetUtcNow();
            if (response.Headers.RetryAfter?.Delta is { } delta) retry = now + delta;
            else retry = response.Headers.RetryAfter?.Date;
            if (remaining == 0 && reset > retry.GetValueOrDefault(DateTimeOffset.MinValue)) retry = reset;
            if (retry == null || retry <= now)
            {
                int failures;
                lock (_gate) { failures = _secondaryFailures.GetValueOrDefault(context); _secondaryFailures[context] = failures + 1; }
                retry = now.AddSeconds(60 * Math.Pow(2, Math.Min(failures, 6)));
            }
        }
        string? message = null;
        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotModified)
        {
            // Do not expose raw response bodies, which can contain request data.
            message = limited ? $"{provider} API rate limit reached." : $"{provider} release request failed (HTTP {(int)response.StatusCode}).";
        }
        return new GitHubReleaseFetchResult { StatusCode = response.StatusCode, Provider = provider,
            IsAuthenticated = authenticated, RateLimitKind = kind, Limit = limit, Remaining = remaining,
            ResetAt = reset, RetryAt = retry, IsRateLimited = limited, ErrorMessage = message };
    }
}
