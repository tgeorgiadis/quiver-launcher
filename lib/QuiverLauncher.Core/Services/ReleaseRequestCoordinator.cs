using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using QuiverLauncher.Core.Models;

namespace QuiverLauncher.Core.Services;

public enum ReleaseRateLimitKind { None, Primary, Secondary }
public sealed record ProviderRateLimitSnapshot(string Provider, long? Limit, long Remaining, DateTimeOffset? ResetAt);

public sealed class ReleaseFetchException(GitHubReleaseFetchResult result)
    : HttpRequestException(result.ErrorMessage ?? "Release metadata is unavailable.", null, result.StatusCode)
{
    public GitHubReleaseFetchResult Result { get; } = result;
}

/// <summary>One coordinator per session HTTP client. Tokens are never persisted or included in diagnostics.</summary>
public sealed class ReleaseRequestCoordinator
{
    private static readonly AsyncLocal<bool> Foreground = new();
    private static readonly AsyncLocal<TimeSpan?> CacheAge = new();
    public static IDisposable AllowCachedMetadata(TimeSpan age)
    {
        var previous = CacheAge.Value;
        CacheAge.Value = age;
        return new CacheScope(previous);
    }
    private sealed class CacheScope(TimeSpan? previous) : IDisposable
    {
        public void Dispose() => CacheAge.Value = previous;
    }
    public static IDisposable PrioritizeInteractiveChecks()
    {
        var previous = Foreground.Value;
        Foreground.Value = true;
        return new PriorityScope(previous);
    }
    private sealed class PriorityScope(bool previous) : IDisposable
    {
        public void Dispose() => Foreground.Value = previous;
    }
    public TimeSpan MetadataTimeout { get; set; } = TimeSpan.FromSeconds(15);
    private long _requestCount;
    private readonly HashSet<string> _validatedTokens = [];
    public long RequestCount => Interlocked.Read(ref _requestCount);
    private static readonly ConditionalWeakTable<HttpClient, ReleaseRequestCoordinator> Instances = new();
    public static ReleaseRequestCoordinator For(HttpClient client) => Instances.GetValue(client, _ => new());
    public static void Configure(HttpClient client, string cacheDirectory)
    {
        var coordinator = For(client);
        coordinator._cache = new(cacheDirectory);
    }
    private static readonly ConditionalWeakTable<string, StrongBox<string>> CredentialKeys = new();
    public static string CredentialKey(string? token) => string.IsNullOrWhiteSpace(token) ? "anonymous"
        : CredentialKeys.GetValue(token, value => new(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim()))))).Value!;

    public ProviderRateLimitSnapshot? GetRateLimitSnapshot(string provider, string? token)
    {
        var entry = _cache.GetRateLimit(provider + ":" + CredentialKey(token));
        if (entry == null || entry.ResetAt is { } reset && reset <= Clock.GetUtcNow()) return null;
        return new(provider, entry.Limit, entry.Remaining, entry.ResetAt);
    }

    public void RecordRateLimit(string provider, string? token, long? limit, long? remaining, DateTimeOffset? resetAt)
    {
        if (remaining == null) return;
        UpdateRateLimit(provider + ":" + CredentialKey(token), new()
        {
            Provider = provider,
            Limit = limit,
            Remaining = remaining,
            ResetAt = resetAt,
        });
    }

    public bool HasValidatedToken(string provider, string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;
        lock (_gate) return _validatedTokens.Contains(provider + ":" + CredentialKey(token));
    }

    public void RecordValidatedToken(string provider, string? token)
    {
        if (!string.IsNullOrWhiteSpace(token))
            lock (_gate) _validatedTokens.Add(provider + ":" + CredentialKey(token));
    }

    public void InvalidateValidatedToken(string provider, string? token)
    {
        if (!string.IsNullOrWhiteSpace(token))
            lock (_gate) _validatedTokens.Remove(provider + ":" + CredentialKey(token));
    }

    private sealed class Flight
    {
        public readonly CancellationTokenSource Cancellation = new();
        public Task<GitHubReleaseFetchResult> Task = null!;
        public int Waiters;
        public volatile bool Priority;
    }
    private readonly object _gate = new();
    private readonly Dictionary<string, Flight> _flights = [];
    private readonly Dictionary<string, GitHubReleaseFetchResult> _cooldowns = [];
    private readonly Dictionary<string, int> _secondaryFailures = [];
    private readonly Dictionary<string, Uri> _redirects = [];
    private readonly ReleaseRequestQueue _github = new();
    private readonly ReleaseRequestQueue _gitlab = new();
    private readonly ReleaseRequestQueue _codeberg = new();
    private ReleaseEndpointCache _cache = new();
    public event Action<GitHubReleaseFetchResult>? RequestCompleted;
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
        if (CacheAge.Value is { } age && _cache.ValidatedAt(key) is { } validated &&
            Clock.GetUtcNow() - validated < age && _cache.Get(key) is { } cached)
        {
            try { return new() { StatusCode = HttpStatusCode.OK, Releases = parse(cached.Body), ETag = cached.ETag, Provider = provider }; }
            catch (JsonException) { /* Revalidate an unreadable payload. */ }
        }
        Flight flight;
        lock (_gate)
        {
            if (!_flights.TryGetValue(key, out flight!))
            {
                flight = new Flight { Priority = Foreground.Value };
                _flights[key] = flight;
                var captured = flight;
                flight.Task = Task.Run(() => FetchCoreAsync(client, endpoint, provider, token, parse, context, key, captured));
            }
            if (Foreground.Value) flight.Priority = true;
            flight.Waiters++;
        }
        try
        {
            var result = await flight.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            RequestCompleted?.Invoke(result);
            return result;
        }
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
        string? token, Func<string, IReadOnlyList<GitHubRelease>> parse, string context, string key, Flight flight)
    {
        var semaphore = provider switch { "github" => _github, "codeberg" => _codeberg, _ => _gitlab };
        await semaphore.EnterAsync(() => flight.Priority, flight.Cancellation.Token).ConfigureAwait(false);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(flight.Cancellation.Token);
        deadline.CancelAfter(MetadataTimeout);
        var ct = deadline.Token;
        try
        {
            if (ReserveKnownQuota(context, provider, !string.IsNullOrWhiteSpace(token)) is { } quotaPaused)
                return quotaPaused;
            lock (_gate)
                if (_cooldowns.TryGetValue(context, out var paused) && paused.RetryAt > Clock.GetUtcNow()) return paused;
            // The payload itself is read only if the server says it is still current.
            var cachedETag = _cache.ETag(key);
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
                    else if (provider == "codeberg") request.Headers.Authorization = new("token", token);
                    else request.Headers.TryAddWithoutValidation("PRIVATE-TOKEN", token);
                }
                if (!unconditionalRetry && !string.IsNullOrEmpty(cachedETag))
                    request.Headers.TryAddWithoutValidation("If-None-Match", cachedETag);
                Interlocked.Increment(ref _requestCount);
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
                UpdateRateLimit(context, result);
                if (result.IsAuthenticated && response.IsSuccessStatusCode)
                    RecordValidatedToken(provider, token);
                if (result.IsRateLimited)
                {
                    lock (_gate) _cooldowns[context] = result;
                    return result;
                }
                if (response.StatusCode == HttpStatusCode.NotModified)
                {
                    var cached = unconditionalRetry ? null : _cache.Get(key);
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
                var etag = response.Headers.ETag?.ToString() ?? (result.WasNotModified ? cachedETag : null);
                if (result.WasNotModified) _cache.Revalidate(key, etag, Clock.GetUtcNow());
                else _cache.Set(key, new(body, etag, Clock.GetUtcNow()));
                lock (_gate) { _cooldowns.Remove(context); _secondaryFailures.Remove(context); }
                return result with { Releases = releases, ETag = etag };
            }
        }
        finally { semaphore.Release(); }
    }

    private GitHubReleaseFetchResult? ReserveKnownQuota(string context, string provider, bool authenticated)
    {
        var entry = _cache.GetRateLimit(context);
        var now = Clock.GetUtcNow();
        if (entry == null || entry.ResetAt is { } reset && reset <= now)
            return null;
        if (entry.Remaining > 0)
        {
            _cache.SetRateLimit(context, entry with { Remaining = entry.Remaining - 1, ObservedAt = now });
            return null;
        }

        var retryAt = entry.ResetAt ?? now.AddMinutes(1);
        return new()
        {
            StatusCode = HttpStatusCode.TooManyRequests,
            Provider = provider,
            IsAuthenticated = authenticated,
            IsRateLimited = true,
            RateLimitKind = ReleaseRateLimitKind.Primary,
            Limit = entry.Limit,
            Remaining = 0,
            ResetAt = entry.ResetAt,
            RetryAt = retryAt,
            ErrorMessage = $"{provider} API rate limit reached.",
        };
    }

    private void UpdateRateLimit(string context, GitHubReleaseFetchResult result)
    {
        if (result.Remaining is not { } remaining)
            return;

        var now = Clock.GetUtcNow();
        _cache.UpdateRateLimit(context, existing =>
        {
            // Concurrent API calls can reply out of order. Clamp our cached remaining # to the lowest we've seen in a response until the next reset
            if (existing?.ResetAt is { } reset && reset > now && existing.Remaining < remaining)
                remaining = existing.Remaining;
            return new(result.Limit ?? existing?.Limit, remaining, result.ResetAt ?? existing?.ResetAt, now);
        });
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
