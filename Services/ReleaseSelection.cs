using QuiverLauncher.Core.Models;
using QuiverLauncher.Core.Services;
namespace QuiverLauncher.Services;

/// <summary>Install, update and catalog publishing use the same Core selector.</summary>
public static class ReleaseSelection
{
    public static GitHubRelease? SelectLatestRelease(IReadOnlyList<GitHubRelease>? releases,
        string? preferredVersion = null, string? installedVersion = null, string? githubLatestTag = null) =>
        CatalogReleaseSelection.SelectLatestRelease(releases, preferredVersion, installedVersion, githubLatestTag);
    public static bool IsSameInstalledRelease(string? installedVersion, string? releaseTag) =>
        CatalogReleaseSelection.IsSameInstalledRelease(installedVersion, releaseTag);
}
