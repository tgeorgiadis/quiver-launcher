using QuiverLauncher.Core.Models;

namespace QuiverLauncher.Services;

/// <summary>
/// Picks the release that should be treated as "latest" for update checks and installs.
/// </summary>
public static class ReleaseSelection
{
    /// <summary>
    /// Preferred pin wins. Otherwise first stable with assets (GitHub order), else first prerelease with assets.
    /// </summary>
    public static GitHubRelease? SelectLatestRelease(
        IReadOnlyList<GitHubRelease>? releases,
        string? preferredVersion = null,
        string? installedVersion = null)
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

        return FirstWithAssets(releases, prerelease: false)
            ?? FirstWithAssets(releases, prerelease: true);
    }

    public static bool IsSameInstalledRelease(string? installedVersion, string? releaseTag) =>
        LauncherVersionService.AreVersionsEquivalent(installedVersion, releaseTag);

    private static GitHubRelease? FirstWithAssets(IReadOnlyList<GitHubRelease> releases, bool prerelease) =>
        releases.FirstOrDefault(release => release.prerelease == prerelease && HasAssets(release));

    private static bool HasAssets(GitHubRelease release) =>
        release.assets is { Length: > 0 };

    private static GitHubRelease? FindByTag(IReadOnlyList<GitHubRelease> pool, string tag) =>
        pool.FirstOrDefault(release =>
            LauncherVersionService.AreVersionsEquivalent(release.tag_name, tag));
}
