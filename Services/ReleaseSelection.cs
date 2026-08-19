using QuiverLauncher.Core.Models;

namespace QuiverLauncher.Services;

/// <summary>
/// Picks the release that should be treated as "latest" for update checks and installs.
/// </summary>
public static class ReleaseSelection
{
    public static IReadOnlyList<GitHubRelease> PreferDownloadable(IReadOnlyList<GitHubRelease>? releases)
    {
        if (releases == null || releases.Count == 0)
            return [];

        var withAssets = releases
            .Where(release => release.assets is { Length: > 0 })
            .ToList();
        return withAssets.Count > 0 ? withAssets : releases;
    }

    /// <summary>
    /// Preferred pin wins. If the install is already on a pre-release track, stay there and
    /// only move to a truly newer pre-release. Otherwise skip GitHub pre-releases (newest stable),
    /// falling back to the newest pre-release when the repo has no stable releases.
    /// </summary>
    public static GitHubRelease? SelectLatestRelease(
        IReadOnlyList<GitHubRelease>? releases,
        string? preferredVersion = null,
        string? installedVersion = null)
    {
        var pool = PreferDownloadable(releases);
        if (pool.Count == 0)
            return null;

        if (!string.IsNullOrWhiteSpace(preferredVersion))
        {
            var pinned = FindByTag(pool, preferredVersion);
            if (pinned != null)
                return pinned;
        }

        var trackHint = FirstNonEmpty(preferredVersion, installedVersion);
        if (IsPrereleaseTrack(trackHint, pool))
            return SelectNewerOnPrereleaseTrack(pool, installedVersion);

        return SelectNewestStableOrFallback(pool);
    }

    public static bool IsSameInstalledRelease(string? installedVersion, string? releaseTag) =>
        LauncherVersionService.AreVersionsEquivalent(installedVersion, releaseTag);

    private static GitHubRelease? SelectNewerOnPrereleaseTrack(
        IReadOnlyList<GitHubRelease> pool,
        string? installedVersion)
    {
        var prereleases = pool.Where(IsPrereleaseRelease).ToList();
        if (prereleases.Count == 0)
            return SelectNewestStableOrFallback(pool);

        if (string.IsNullOrWhiteSpace(installedVersion) ||
            IsUnknownInstalledVersion(installedVersion))
        {
            return PickHighestVersion(prereleases);
        }

        var newer = prereleases
            .Where(release => LauncherVersionService.IsNewerVersion(release.tag_name, installedVersion))
            .ToList();
        if (newer.Count > 0)
            return PickHighestVersion(newer);

        return FindByTag(pool, installedVersion) ?? PickHighestVersion(prereleases);
    }

    private static GitHubRelease? SelectNewestStableOrFallback(IReadOnlyList<GitHubRelease> pool)
    {
        var stables = pool.Where(release => !IsPrereleaseRelease(release)).ToList();
        if (stables.Count > 0)
            return PickHighestVersion(stables);

        return PickHighestVersion(pool);
    }

    private static GitHubRelease? PickHighestVersion(IReadOnlyList<GitHubRelease> pool)
    {
        GitHubRelease? best = null;
        foreach (var release in pool)
        {
            if (best == null || LauncherVersionService.IsNewerVersion(release.tag_name, best.tag_name))
                best = release;
        }

        return best;
    }

    private static bool IsPrereleaseTrack(string? trackHint, IReadOnlyList<GitHubRelease> pool)
    {
        if (string.IsNullOrWhiteSpace(trackHint) || IsUnknownInstalledVersion(trackHint))
            return false;

        var match = FindByTag(pool, trackHint);
        if (match != null)
            return IsPrereleaseRelease(match);

        return LauncherVersionService.LooksLikePrereleaseTag(trackHint);
    }

    private static bool IsPrereleaseRelease(GitHubRelease release) =>
        release.prerelease || LauncherVersionService.LooksLikePrereleaseTag(release.tag_name);

    private static GitHubRelease? FindByTag(IReadOnlyList<GitHubRelease> pool, string tag) =>
        pool.FirstOrDefault(release =>
            LauncherVersionService.AreVersionsEquivalent(release.tag_name, tag));

    private static bool IsUnknownInstalledVersion(string version) =>
        string.Equals(version, "Unknown", StringComparison.OrdinalIgnoreCase) ||
        LauncherVersionService.AreVersionsEquivalent(version, "0.0.0") ||
        LauncherVersionService.AreVersionsEquivalent(version, "v0.0.0");

    private static string? FirstNonEmpty(string? first, string? second) =>
        !string.IsNullOrWhiteSpace(first) ? first : second;
}
