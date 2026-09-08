using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using QuiverLauncher.Core.Models;

namespace QuiverLauncher.Core.Services
{
    public sealed class GitHubReleaseFetchResult
    {
        public HttpStatusCode StatusCode { get; init; }
        public IReadOnlyList<GitHubRelease> Releases { get; init; } = [];
        public string? ETag { get; init; }
        public string? LatestTag { get; init; }
        public string? ErrorMessage { get; init; }
        public bool IsRateLimited { get; init; }
        public bool IsNotModified => StatusCode == HttpStatusCode.NotModified;
    }

    public static class GitHubReleaseService
    {
        public static async Task<GitHubReleaseFetchResult> FetchReleasesAsync(
            HttpClient httpClient,
            string repository,
            string? token = null,
            string? etag = null)
        {
            if (string.IsNullOrWhiteSpace(repository))
            {
                return new GitHubReleaseFetchResult
                {
                    StatusCode = HttpStatusCode.BadRequest
                };
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repository}/releases");

            if (!string.IsNullOrWhiteSpace(etag))
            {
                request.Headers.TryAddWithoutValidation("If-None-Match", etag);
            }

            if (!string.IsNullOrWhiteSpace(token))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            using var response = await httpClient.SendAsync(request).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                return new GitHubReleaseFetchResult
                {
                    StatusCode = response.StatusCode,
                    ETag = response.Headers.ETag?.Tag
                };
            }

            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var releases = JsonSerializer.Deserialize<List<GitHubRelease>>(responseContent) ?? [];
            var latest = await FetchLatestReleaseAsync(httpClient, repository, token).ConfigureAwait(false);
            var merged = MergeLatestRelease(releases, latest);

            return new GitHubReleaseFetchResult
            {
                StatusCode = response.StatusCode,
                Releases = merged,
                ETag = response.Headers.ETag?.Tag,
                LatestTag = string.IsNullOrWhiteSpace(latest?.tag_name) ? null : latest.tag_name
            };
        }

        /// <summary>
        /// Latest GitHub release for catalog platform indexing. One HTTP call; does not
        /// download the full <c>/releases</c> list. 404 means prerelease-only (no GitHub Latest).
        /// </summary>
        public static async Task<GitHubReleaseFetchResult> FetchLatestReleaseIndexAsync(
            HttpClient httpClient,
            string repository,
            string? token = null,
            string? etag = null)
        {
            if (string.IsNullOrWhiteSpace(repository))
            {
                return new GitHubReleaseFetchResult
                {
                    StatusCode = HttpStatusCode.BadRequest
                };
            }

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"https://api.github.com/repos/{repository}/releases/latest");

            if (!string.IsNullOrWhiteSpace(etag))
                request.Headers.TryAddWithoutValidation("If-None-Match", etag);

            if (!string.IsNullOrWhiteSpace(token))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await httpClient.SendAsync(request).ConfigureAwait(false);
            var responseEtag = response.Headers.ETag?.Tag;

            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                return new GitHubReleaseFetchResult
                {
                    StatusCode = response.StatusCode,
                    ETag = responseEtag
                };
            }

            if (response.StatusCode is HttpStatusCode.NotFound
                or HttpStatusCode.Forbidden
                or HttpStatusCode.Unauthorized
                or HttpStatusCode.TooManyRequests
                || !response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                return new GitHubReleaseFetchResult
                {
                    StatusCode = response.StatusCode,
                    ETag = responseEtag,
                    ErrorMessage = string.IsNullOrWhiteSpace(errorBody) ? null : errorBody,
                    IsRateLimited = IsRateLimitResponse(response.StatusCode, response.Headers, errorBody)
                };
            }

            var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var latest = JsonSerializer.Deserialize<GitHubRelease>(content);

            return new GitHubReleaseFetchResult
            {
                StatusCode = response.StatusCode,
                Releases = latest == null ? [] : [latest],
                ETag = responseEtag,
                LatestTag = string.IsNullOrWhiteSpace(latest?.tag_name) ? null : latest.tag_name
            };
        }

        /// <summary>
        /// GitHub's Latest release, or null on 404 / failure (prerelease-only repos).
        /// </summary>
        public static async Task<GitHubRelease?> FetchLatestReleaseAsync(
            HttpClient httpClient,
            string repository,
            string? token = null,
            string? etag = null)
        {
            if (string.IsNullOrWhiteSpace(repository))
                return null;

            try
            {
                using var request = new HttpRequestMessage(
                    HttpMethod.Get,
                    $"https://api.github.com/repos/{repository}/releases/latest");

                if (!string.IsNullOrWhiteSpace(etag))
                    request.Headers.TryAddWithoutValidation("If-None-Match", etag);

                if (!string.IsNullOrWhiteSpace(token))
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                using var response = await httpClient.SendAsync(request).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.NotModified ||
                    response.StatusCode == HttpStatusCode.NotFound ||
                    !response.IsSuccessStatusCode)
                {
                    return null;
                }

                var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                return JsonSerializer.Deserialize<GitHubRelease>(content);
            }
            catch (Exception)
            {
                return null;
            }
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
            string? token = null)
        {
            var result = await FetchReleasesAsync(httpClient, repository, token).ConfigureAwait(false);
            return new GitHubReleaseFetchResult
            {
                StatusCode = result.StatusCode,
                Releases = result.Releases
                    .Where(release => release.assets != null && release.assets.Length > 0)
                    .ToList(),
                ETag = result.ETag,
                LatestTag = result.LatestTag
            };
        }

        public static List<GitHubAsset> GetDownloadableAssets(GitHubRelease release, string? releaseAssetFilter = null)
        {
            var assets = (release.assets ?? [])
                .Where(asset => !asset.name.Contains("flatpak", StringComparison.OrdinalIgnoreCase));

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
