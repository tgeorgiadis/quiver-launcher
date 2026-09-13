using QuiverLauncher.Core.Models;

namespace QuiverLauncher.Core.Services;

/// <summary>
/// Picks the release that should be treated as "latest" for update checks and installs.
/// </summary>
public static class CatalogReleaseSelection
{
    public static async Task<GitHubRelease?> FetchSelectedAsync(HttpClient client, string? provider, string repository,
        string? preferredRelease, string? token, CancellationToken cancellationToken = default)
    {
        var latestOnly = RepositorySourceHelper.IsGitHub(provider) && string.IsNullOrWhiteSpace(preferredRelease);
        var result = latestOnly
            ? await GitHubReleaseService.FetchLatestReleaseIndexAsync(client, repository, token, cancellationToken: cancellationToken).ConfigureAwait(false)
            : await ReleaseSourceRegistry.Default.FetchReleasesAsync(client, provider, repository, token, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (latestOnly && (result.StatusCode == System.Net.HttpStatusCode.NotFound ||
            result.StatusCode == System.Net.HttpStatusCode.OK && SelectLatestRelease(result.Releases, preferredRelease, githubLatestTag: result.LatestTag) == null))
            result = await ReleaseSourceRegistry.Default.FetchReleasesAsync(client, provider, repository, token, cancellationToken: cancellationToken).ConfigureAwait(false);
        result.EnsureSuccess();
        if (result.StatusCode != System.Net.HttpStatusCode.OK) throw new HttpRequestException("Release metadata was not returned.");
        return SelectLatestRelease(result.Releases, preferredRelease, githubLatestTag: result.LatestTag);
    }

    /// <summary>
    /// Preferred pin wins. Then GitHub's Latest tag when present with assets.
    /// Otherwise first stable with assets (GitHub list order), else first prerelease with assets.
    /// </summary>
    public static GitHubRelease? SelectLatestRelease(
        IReadOnlyList<GitHubRelease>? releases,
        string? preferredVersion = null,
        string? installedVersion = null,
        string? githubLatestTag = null)
    {
        _ = installedVersion;

        if (releases == null || releases.Count == 0)
            return null;

        if (!string.IsNullOrWhiteSpace(preferredVersion))
        {
            var pinned = FindByTag(releases, preferredVersion);
            if (pinned != null)
                return pinned;
        }

        if (!string.IsNullOrWhiteSpace(githubLatestTag))
        {
            var flagged = FindByTag(releases, githubLatestTag);
            if (flagged != null && HasAssets(flagged))
                return flagged;
        }

        return FirstWithAssets(releases, prerelease: false)
            ?? FirstWithAssets(releases, prerelease: true);
    }

    public static bool IsSameInstalledRelease(string? installedVersion, string? releaseTag) =>
        ReleaseVersionIdentity.AreVersionsEquivalent(installedVersion, releaseTag);

    private static GitHubRelease? FirstWithAssets(IReadOnlyList<GitHubRelease> releases, bool prerelease) =>
        releases.FirstOrDefault(release => release.prerelease == prerelease && HasAssets(release));

    private static bool HasAssets(GitHubRelease release) =>
        QuiverLauncher.Core.Services.GitHubReleaseService.GetDownloadableAssets(release).Count > 0;

    private static GitHubRelease? FindByTag(IReadOnlyList<GitHubRelease> pool, string tag) =>
        pool.FirstOrDefault(release =>
            ReleaseVersionIdentity.AreVersionsEquivalent(release.tag_name, tag));
}
