using QuiverLauncher.Core.Models;

namespace QuiverLauncher.Core.Services
{
    public interface IReleaseSource
    {
        string Id { get; }
        string DisplayName { get; }

        Task<GitHubReleaseFetchResult> FetchReleasesAsync(
            HttpClient httpClient,
            string repository,
            string? token = null,
            string? etag = null);
    }
}
