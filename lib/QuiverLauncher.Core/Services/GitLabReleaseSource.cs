using System.Net;
using System.Text.Json;
using QuiverLauncher.Core.Models;

namespace QuiverLauncher.Core.Services
{
    public sealed class GitLabReleaseSource : IReleaseSource
    {
        public const string ApiBaseUrl = "https://gitlab.com/api/v4";

        public string Id => RepositorySourceIds.GitLab;
        public string DisplayName => "GitLab";

        public async Task<GitHubReleaseFetchResult> FetchReleasesAsync(
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

            var encodedProject = Uri.EscapeDataString(repository.Trim());
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{ApiBaseUrl}/projects/{encodedProject}/releases");

            if (!string.IsNullOrWhiteSpace(etag))
            {
                request.Headers.TryAddWithoutValidation("If-None-Match", etag);
            }

            if (!string.IsNullOrWhiteSpace(token))
            {
                request.Headers.TryAddWithoutValidation("PRIVATE-TOKEN", token);
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
            var releases = MapReleasesFromJson(responseContent);

            return new GitHubReleaseFetchResult
            {
                StatusCode = response.StatusCode,
                Releases = releases,
                ETag = response.Headers.ETag?.Tag
            };
        }

        public static List<GitHubRelease> MapReleasesFromJson(string json)
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return [];

            var releases = new List<GitHubRelease>();
            foreach (var element in document.RootElement.EnumerateArray())
            {
                var mapped = MapRelease(element);
                if (mapped != null)
                    releases.Add(mapped);
            }

            return releases;
        }

        public static GitHubRelease? MapRelease(JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.Object)
                return null;

            var tagName = element.TryGetProperty("tag_name", out var tagElement)
                ? tagElement.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(tagName))
                return null;

            var upcoming = element.TryGetProperty("upcoming_release", out var upcomingElement) &&
                           upcomingElement.ValueKind == JsonValueKind.True;

            var assets = MapAssets(element);

            return new GitHubRelease
            {
                tag_name = tagName,
                prerelease = upcoming,
                assets = assets.ToArray()
            };
        }

        public static List<GitHubAsset> MapAssets(JsonElement releaseElement)
        {
            if (!releaseElement.TryGetProperty("assets", out var assetsElement) ||
                assetsElement.ValueKind != JsonValueKind.Object)
            {
                return [];
            }

            if (!assetsElement.TryGetProperty("links", out var linksElement) ||
                linksElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var assets = new List<GitHubAsset>();
            foreach (var link in linksElement.EnumerateArray())
            {
                if (link.ValueKind != JsonValueKind.Object)
                    continue;

                var name = link.TryGetProperty("name", out var nameElement)
                    ? nameElement.GetString()
                    : null;
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                var directUrl = link.TryGetProperty("direct_asset_url", out var directElement)
                    ? directElement.GetString()
                    : null;
                var url = link.TryGetProperty("url", out var urlElement)
                    ? urlElement.GetString()
                    : null;

                var downloadUrl = !string.IsNullOrWhiteSpace(directUrl) ? directUrl : url;
                if (string.IsNullOrWhiteSpace(downloadUrl) || !IsGitLabHostedUrl(downloadUrl))
                    continue;

                assets.Add(new GitHubAsset
                {
                    name = name,
                    browser_download_url = downloadUrl
                });
            }

            return assets;
        }

        public static bool IsGitLabHostedUrl(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return false;

            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                return false;

            var host = uri.Host;
            return string.Equals(host, "gitlab.com", StringComparison.OrdinalIgnoreCase) ||
                   host.EndsWith(".gitlab.com", StringComparison.OrdinalIgnoreCase);
        }
    }
}
