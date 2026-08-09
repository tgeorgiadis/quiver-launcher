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

            var response = await httpClient.SendAsync(request).ConfigureAwait(false);

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

            return new GitHubReleaseFetchResult
            {
                StatusCode = response.StatusCode,
                Releases = releases,
                ETag = response.Headers.ETag?.Tag
            };
        }

        public static async Task<List<GitHubRelease>> FetchReleasesWithAssetsAsync(
            HttpClient httpClient,
            string repository,
            string? token = null)
        {
            var result = await FetchReleasesAsync(httpClient, repository, token).ConfigureAwait(false);
            return result.Releases
                .Where(release => release.assets != null && release.assets.Length > 0)
                .ToList();
        }

        public static List<GitHubAsset> GetDownloadableAssets(GitHubRelease release)
        {
            return (release.assets ?? [])
                .Where(asset => !asset.name.Contains("flatpak", StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
    }
}
