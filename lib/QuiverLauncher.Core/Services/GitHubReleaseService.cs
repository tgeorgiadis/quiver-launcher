using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using QuiverLauncher.Core.Models;

namespace QuiverLauncher.Core.Services
{
    public sealed record GitHubReleaseFetchResult
    {
        public HttpStatusCode StatusCode { get; init; }
        public IReadOnlyList<GitHubRelease> Releases { get; init; } = [];
        public string? ETag { get; init; }
        public string? LatestTag { get; init; }
        public string? ErrorMessage { get; init; }
        public bool IsRateLimited { get; init; }
        public string Provider { get; init; } = "github";
        public bool IsAuthenticated { get; init; }
        public ReleaseRateLimitKind RateLimitKind { get; init; }
        public long? Limit { get; init; }
        public long? Remaining { get; init; }
        public DateTimeOffset? ResetAt { get; init; }
        public DateTimeOffset? RetryAt { get; init; }
        public bool WasNotModified { get; init; }
        public void EnsureSuccess()
        {
            if ((int)StatusCode >= 400) throw new ReleaseFetchException(this);
        }
        public bool IsNotModified => StatusCode == HttpStatusCode.NotModified;
    }

    public static class GitHubReleaseService
    {
        public static async Task<GitHubReleaseFetchResult> FetchReleasesAsync(
            HttpClient httpClient, string repository, string? token = null, string? etag = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(repository)) return new() { StatusCode = HttpStatusCode.BadRequest };
            var result = await FetchReleaseListAsync(httpClient, repository, token, cancellationToken).ConfigureAwait(false);
            if (result.StatusCode != HttpStatusCode.OK) return result;
            var latest = await FetchLatestReleaseIndexAsync(httpClient, repository, token,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (latest.IsRateLimited || latest.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return latest;
            return result with
            {
                Releases = MergeLatestRelease(result.Releases, latest.Releases.FirstOrDefault()),
                LatestTag = latest.LatestTag
            };
        }

        public static Task<GitHubReleaseFetchResult> FetchReleaseListAsync(HttpClient client, string repository,
            string? token = null, CancellationToken cancellationToken = default) =>
            ReleaseRequestCoordinator.For(client).FetchAsync(client,
                new Uri($"https://api.github.com/repos/{repository}/releases"), "github", token,
                body => JsonSerializer.Deserialize<List<GitHubRelease>>(body) ?? [], cancellationToken);

        /// <summary>Legacy ETags are ignored; endpoint validators always travel with their own payload.</summary>
        public static async Task<GitHubReleaseFetchResult> FetchLatestReleaseIndexAsync(
            HttpClient httpClient, string repository, string? token = null, string? etag = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(repository)) return new() { StatusCode = HttpStatusCode.BadRequest };
            var result = await ReleaseRequestCoordinator.For(httpClient).FetchAsync(httpClient,
                new Uri($"https://api.github.com/repos/{repository}/releases/latest"), "github", token,
                body => JsonSerializer.Deserialize<GitHubRelease>(body) is { } release ? [release] : [],
                cancellationToken).ConfigureAwait(false);
            return result with { LatestTag = result.Releases.FirstOrDefault()?.tag_name };
        }

        public static async Task<GitHubRelease?> FetchLatestReleaseAsync(
            HttpClient httpClient, string repository, string? token = null, string? etag = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(repository)) return null;
            var result = await FetchLatestReleaseIndexAsync(httpClient, repository, token, etag, cancellationToken).ConfigureAwait(false);
            if (result.StatusCode == HttpStatusCode.NotFound) return null;
            result.EnsureSuccess();
            return result.Releases.FirstOrDefault();
        }

        /// <summary>
        /// Prepends GitHub's Latest when it has assets and is missing from the list page.
        /// </summary>
        internal static List<GitHubRelease> MergeLatestRelease(
            IReadOnlyList<GitHubRelease> releases,
            GitHubRelease? latest)
        {
            var merged = releases.ToList();
            if (latest == null || !HasAssets(latest) || string.IsNullOrWhiteSpace(latest.tag_name))
                return merged;

            if (merged.Any(release =>
                    string.Equals(release.tag_name, latest.tag_name, StringComparison.OrdinalIgnoreCase)))
            {
                return merged;
            }

            merged.Insert(0, latest);
            return merged;
        }

        public static async Task<GitHubReleaseFetchResult> FetchReleasesWithAssetsAsync(
            HttpClient httpClient,
            string repository,
            string? token = null, CancellationToken cancellationToken = default)
        {
            var result = await FetchReleasesAsync(httpClient, repository, token, cancellationToken: cancellationToken).ConfigureAwait(false);
            result.EnsureSuccess();
            return result with { Releases = result.Releases.Where(release => release.assets is { Length: > 0 }).ToList() };
        }

        public static List<GitHubAsset> GetDownloadableAssets(GitHubRelease release, string? releaseAssetFilter = null)
        {
            var assets = (release.assets ?? [])
                .Where(asset => (!asset.name.Contains("flatpak", StringComparison.OrdinalIgnoreCase) ||
                    GameInstallationService.IsFlatpakAsset(asset.name)) &&
                    !DownloadAssetPolicy.IsAuxiliary(asset.name));

            var filter = RepositorySourceHelper.NormalizeReleaseAssetFilter(releaseAssetFilter);
            if (filter != null)
                assets = assets.Where(asset => RepositorySourceHelper.AssetNameMatchesFilter(asset.name, filter));

            return assets.ToList();
        }

        private static bool HasAssets(GitHubRelease release) =>
            release.assets is { Length: > 0 };

        public static bool IsRateLimitResponse(
            HttpStatusCode status,
            HttpResponseHeaders? headers,
            string? body)
        {
            if (status == HttpStatusCode.TooManyRequests)
                return true;

            if (status != HttpStatusCode.Forbidden)
                return false;

            if (headers != null &&
                headers.TryGetValues("X-RateLimit-Remaining", out var remainingValues))
            {
                foreach (var value in remainingValues)
                {
                    if (int.TryParse(value, out var remaining) && remaining <= 0)
                        return true;
                }
            }

            return LooksLikeRateLimitMessage(body);
        }

        public static bool LooksLikeRateLimitMessage(string? message) =>
            !string.IsNullOrWhiteSpace(message) &&
            (message.Contains("rate limit", StringComparison.OrdinalIgnoreCase) ||
             message.Contains("rate-limit", StringComparison.OrdinalIgnoreCase));
    }
}
