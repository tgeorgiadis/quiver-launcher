using QuiverLauncher.Core.Models;

namespace QuiverLauncher.Core.Services;

/// <summary>
/// Picks the release that should be treated as "latest" for update checks and installs.
/// </summary>
public static class CatalogReleaseSelection
{
    public static async Task<GitHubRelease?> FetchSelectedAsync(HttpClient client, string? provider, string repository,
        string? preferredRelease, string? token, CancellationToken cancellationToken = default, bool includePrereleases = false)
    {
        // GitHub's Latest alone answers, unless a pin or the newest pre-release is wanted from the release list.
        var latestOnly = RepositorySourceHelper.IsGitHub(provider) && string.IsNullOrWhiteSpace(preferredRelease) && !includePrereleases;
        var result = latestOnly
            ? await GitHubReleaseService.FetchLatestReleaseIndexAsync(client, repository, token, cancellationToken: cancellationToken).ConfigureAwait(false)
            : await ReleaseSourceRegistry.Default.FetchReleasesAsync(client, provider, repository, token, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (latestOnly && (result.StatusCode == System.Net.HttpStatusCode.NotFound ||
            result.StatusCode == System.Net.HttpStatusCode.OK && SelectLatestRelease(result.Releases, preferredRelease, githubLatestTag: result.LatestTag) == null))
            result = await ReleaseSourceRegistry.Default.FetchReleasesAsync(client, provider, repository, token, cancellationToken: cancellationToken).ConfigureAwait(false);
        result.EnsureSuccess();
        if (result.StatusCode != System.Net.HttpStatusCode.OK) throw new HttpRequestException("Release metadata was not returned.");
        return SelectLatestRelease(result.Releases, preferredRelease, githubLatestTag: result.LatestTag, includePrereleases: includePrereleases);
    }

    /// <summary>
    /// Preferred pin wins. Then, when <paramref name="includePrereleases"/>, the newest release with assets, pre-release
    /// or not. Then GitHub's Latest tag when present with assets.
    /// Otherwise first stable with assets (GitHub list order), else first prerelease with assets.
    /// </summary>
    public static GitHubRelease? SelectLatestRelease(
        IReadOnlyList<GitHubRelease>? releases,
        string? preferredVersion = null,
        string? installedVersion = null,
        string? githubLatestTag = null,
        bool includePrereleases = false)
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

        if (includePrereleases && NewestWithAssets(releases) is { } newest)
            return newest;

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

    /// <summary>
    /// The release whose files were uploaded last: a nightly refilled under an old release counts as new. In list order
    /// when files carry no upload time (GitHub lists the newest release first).
    /// </summary>
    private static GitHubRelease? NewestWithAssets(IReadOnlyList<GitHubRelease> releases) =>
        releases.Select((release, index) => (Release: release, Index: index,
                Uploaded: GitHubReleaseService.GetDownloadableAssets(release).Max(a => a.updated_at)))
            .Where(r => HasAssets(r.Release))
            .OrderByDescending(r => r.Uploaded ?? DateTimeOffset.MinValue).ThenBy(r => r.Index)
            .Select(r => r.Release).FirstOrDefault();

    private static GitHubRelease? FirstWithAssets(IReadOnlyList<GitHubRelease> releases, bool prerelease) =>
        releases.FirstOrDefault(release => release.prerelease == prerelease && HasAssets(release));

    private static bool HasAssets(GitHubRelease release) =>
        QuiverLauncher.Core.Services.GitHubReleaseService.GetDownloadableAssets(release).Count > 0;

    private static GitHubRelease? FindByTag(IReadOnlyList<GitHubRelease> pool, string tag) =>
        pool.FirstOrDefault(release => string.Equals(release.tag_name, tag.Trim(), StringComparison.OrdinalIgnoreCase)) ??
        pool.FirstOrDefault(release =>
            ReleaseVersionIdentity.AreVersionsEquivalent(release.tag_name, tag));
}
