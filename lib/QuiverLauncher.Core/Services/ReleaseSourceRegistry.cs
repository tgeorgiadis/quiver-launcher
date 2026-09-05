using QuiverLauncher.Core.Models;

namespace QuiverLauncher.Core.Services
{
    public sealed class ReleaseSourceRegistry
    {
        private readonly Dictionary<string, IReleaseSource> _sources;

        public ReleaseSourceRegistry(IEnumerable<IReleaseSource>? sources = null)
        {
            var list = sources?.ToList() ??
            [
                new GitHubReleaseSource(),
                new GitLabReleaseSource()
            ];

            _sources = list.ToDictionary(s => s.Id, StringComparer.OrdinalIgnoreCase);
        }

        public static ReleaseSourceRegistry Default { get; } = new();

        public IReadOnlyCollection<IReleaseSource> All => _sources.Values;

        public IReleaseSource Get(string? repositorySource)
        {
            var normalized = RepositorySourceHelper.Normalize(repositorySource, out var wasUnsupported);
            if (wasUnsupported)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Unsupported repositorySource '{repositorySource}'; defaulting to GitHub.");
            }

            if (_sources.TryGetValue(normalized, out var source))
                return source;

            return _sources[RepositorySourceIds.GitHub];
        }

        public async Task<GitHubReleaseFetchResult> FetchReleasesAsync(
            HttpClient httpClient,
            string? repositorySource,
            string repository,
            string? token = null,
            string? etag = null)
        {
            var source = Get(repositorySource);
            return await source.FetchReleasesAsync(httpClient, repository, token, etag)
                .ConfigureAwait(false);
        }

        public async Task<GitHubReleaseFetchResult> FetchReleasesWithAssetsAsync(
            HttpClient httpClient,
            string? repositorySource,
            string repository,
            string? token = null)
        {
            var result = await FetchReleasesAsync(httpClient, repositorySource, repository, token)
                .ConfigureAwait(false);
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
    }
}
