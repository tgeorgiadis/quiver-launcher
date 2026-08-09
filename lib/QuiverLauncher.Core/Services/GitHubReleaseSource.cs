namespace QuiverLauncher.Core.Services
{
    public sealed class GitHubReleaseSource : IReleaseSource
    {
        public string Id => RepositorySourceIds.GitHub;
        public string DisplayName => "GitHub";

        public Task<GitHubReleaseFetchResult> FetchReleasesAsync(
            HttpClient httpClient,
            string repository,
            string? token = null,
            string? etag = null) =>
            GitHubReleaseService.FetchReleasesAsync(httpClient, repository, token, etag);
    }
}
