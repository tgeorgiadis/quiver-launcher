using System.Text.RegularExpressions;
using QuiverLauncher.Core.Models;

namespace QuiverLauncher.Core.Services;

public sealed record DownloadAssetSelection(IReadOnlyList<GitHubAsset> Eligible,
    IReadOnlyList<GitHubAsset> Uncertain, string? EmptyReason)
{
    public GitHubAsset? Automatic => Eligible.Count == 1 ? Eligible[0] : null;
    public bool NeedsChoice => Eligible.Count > 1 || Eligible.Count == 0 && Uncertain.Count > 0;
}

public static class DownloadAssetPolicy
{
    public static bool IsAuxiliary(string name) =>
        name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
        Regex.IsMatch(name, @"(?:\.(?:sha(?:1|224|256|384|512)?|md5|sig|asc|minisig|signature|debug)(?:\.txt)?$)|(?:^|[._-])(?:checksums?|sha(?:1|224|256|384|512)?sums?|md5sums?|pdb|dsym|symbols|debugsymbols|debug[._-]symbols)(?:$|[._-])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static DownloadAssetSelection Select(GitHubRelease release, string platform, string? filter = null)
    {
        var all = GitHubReleaseService.GetDownloadableAssets(release);
        var filtered = GitHubReleaseService.GetDownloadableAssets(release, filter);
        var linux = platform.StartsWith("Linux", StringComparison.OrdinalIgnoreCase);
        var eligible = filtered.Where(a => PlatformAssetMatcher.MatchesPlatform(a.name, platform) ||
            linux && PlatformAssetMatcher.IsWindowsAsset(a.name))
            .OrderByDescending(a => PlatformAssetMatcher.MatchesPlatform(a.name, platform)).ToList();
        var uncertain = filtered.Where(a => !HasKnownPlatform(a.name)).ToList();
        var reason = eligible.Count > 0 ? null : all.Count == 0 ? "This release has no installable download files." :
            filtered.Count == 0 ? $"No download files match the release asset filter \"{filter}\"." :
            $"This release has no recognized download for {platform}.";
        return new(eligible, uncertain, reason);
    }

    private static bool HasKnownPlatform(string name) => PlatformAssetMatcher.IsIosAsset(name) ||
        PlatformAssetMatcher.IsDedicatedDeviceAsset(name) || PlatformAssetMatcher.IsWindowsAsset(name) ||
        PlatformAssetMatcher.MatchesPlatform(name, "macOS") ||
        Regex.IsMatch(name, @"(?:linux|appimage|flatpak|android|arm64-v8a|switch|\.apk$|\.deb$|\.rpm$|\.tar\.(?:gz|xz)$)", RegexOptions.IgnoreCase);
}
