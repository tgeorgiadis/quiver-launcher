using System.Runtime.InteropServices;

namespace QuiverLauncher.Core.Services
{
    [Flags]
    public enum CatalogPlatformFlags
    {
        None = 0,
        Windows = 1,
        Linux = 2,
        Mac = 4,
        Android = 8,
    }

    /// <summary>
    /// Catalog-review platform filter helpers. Linux means any Linux arch (x64 or ARM).
    /// </summary>
    public static class CatalogPlatformSupport
    {
        public const string Windows = "Windows";
        public const string Linux = "Linux";
        public const string Mac = "Mac";
        public const string Android = "Android";
        public const string All = "All";

        public static readonly string[] KnownPlatforms = [Windows, Linux, Mac, Android];

        public static CatalogPlatformFlags FromAssetNames(IEnumerable<string>? assetNames)
        {
            var flags = CatalogPlatformFlags.None;
            if (assetNames == null)
                return flags;

            foreach (var name in assetNames)
            {
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                if (PlatformAssetMatcher.MatchesPlatform(name, "Windows"))
                    flags |= CatalogPlatformFlags.Windows;
                if (IsAnyLinuxAsset(name))
                    flags |= CatalogPlatformFlags.Linux;
                if (PlatformAssetMatcher.MatchesPlatform(name, "macOS"))
                    flags |= CatalogPlatformFlags.Mac;
                if (PlatformAssetMatcher.MatchesPlatform(name, "Android"))
                    flags |= CatalogPlatformFlags.Android;
            }

            return flags;
        }

        public static bool IsAnyLinuxAsset(string assetName) =>
            PlatformAssetMatcher.MatchesPlatform(assetName, "Linux-X64") ||
            PlatformAssetMatcher.MatchesPlatform(assetName, "Linux-ARM64");

        public static IReadOnlyList<string> FilterAssetNames(
            IEnumerable<string>? assetNames,
            string? releaseAssetFilter)
        {
            if (assetNames == null)
                return [];

            var filter = RepositorySourceHelper.NormalizeReleaseAssetFilter(releaseAssetFilter);
            var names = assetNames.Where(name => !string.IsNullOrWhiteSpace(name));
            if (filter == null)
                return names.ToList();

            return names
                .Where(name => RepositorySourceHelper.AssetNameMatchesFilter(name, filter))
                .ToList();
        }

        public static CatalogPlatformFlags ParseFilters(IEnumerable<string>? filters)
        {
            var flags = CatalogPlatformFlags.None;
            foreach (var filter in Normalize(filters))
                flags |= ToFlag(filter);
            return flags;
        }

        public static bool IsAll(IEnumerable<string>? filters)
        {
            var normalized = Normalize(filters);
            return normalized.Count == 0;
        }

        public static bool Matches(CatalogPlatformFlags supported, CatalogPlatformFlags selected)
        {
            if (selected == CatalogPlatformFlags.None)
                return true;
            return (supported & selected) != 0;
        }

        /// <summary>
        /// Unknown apps (no repository or no cached asset names) stay visible.
        /// Known latest-release assets must intersect the selected platforms.
        /// </summary>
        public static bool AppMatches(
            string? repositorySource,
            string? repository,
            string? releaseAssetFilter,
            IEnumerable<string>? selectedFilters)
        {
            if (IsAll(selectedFilters))
                return true;

            if (string.IsNullOrWhiteSpace(repository))
                return true;

            if (!GitHubApiCache.TryGetAssetNames(repositorySource, repository, out var assetNames))
                return true;

            var filtered = FilterAssetNames(assetNames, releaseAssetFilter);
            var supported = FromAssetNames(filtered);
            return Matches(supported, ParseFilters(selectedFilters));
        }

        public static string DetectRuntimePlatform()
        {
            if (OperatingSystem.IsAndroid())
                return Android;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return Windows;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return Mac;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return Linux;
            return Windows;
        }

        public static IReadOnlyList<string> Normalize(IEnumerable<string>? filters)
        {
            if (filters == null)
                return [];

            var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var filter in filters)
            {
                var canonical = Canonical(filter);
                if (canonical != null)
                    selected.Add(canonical);
            }

            return KnownPlatforms.Where(selected.Contains).ToList();
        }

        public static List<string> Toggle(IEnumerable<string>? current, string? platform)
        {
            if (IsAllToken(platform))
                return [];

            var canonical = Canonical(platform);
            if (canonical == null)
                return Normalize(current).ToList();

            var set = new HashSet<string>(Normalize(current), StringComparer.OrdinalIgnoreCase);
            if (!set.Add(canonical))
                set.Remove(canonical);

            return Normalize(set).ToList();
        }

        public static string FormatLabel(IEnumerable<string>? filters)
        {
            var normalized = Normalize(filters);
            if (normalized.Count == 0)
                return "All platforms";
            if (normalized.Count == 1)
                return normalized[0];
            return string.Join(" + ", normalized);
        }

        public static bool IsSelected(IEnumerable<string>? filters, string? platform)
        {
            if (IsAllToken(platform))
                return IsAll(filters);

            var canonical = Canonical(platform);
            if (canonical == null)
                return false;

            return Normalize(filters).Contains(canonical, StringComparer.OrdinalIgnoreCase);
        }

        public static string? Canonical(string? platform)
        {
            if (string.IsNullOrWhiteSpace(platform))
                return null;

            var trimmed = platform.Trim();
            if (trimmed.StartsWith("platform:", StringComparison.OrdinalIgnoreCase))
                trimmed = trimmed["platform:".Length..];

            if (IsAllToken(trimmed))
                return null;

            foreach (var known in KnownPlatforms)
            {
                if (known.Equals(trimmed, StringComparison.OrdinalIgnoreCase))
                    return known;
            }

            if (trimmed.Equals("macOS", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("OSX", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("darwin", StringComparison.OrdinalIgnoreCase))
            {
                return Mac;
            }

            return null;
        }

        private static bool IsAllToken(string? platform)
        {
            if (string.IsNullOrWhiteSpace(platform))
                return false;

            var trimmed = platform.Trim();
            if (trimmed.StartsWith("platform:", StringComparison.OrdinalIgnoreCase))
                trimmed = trimmed["platform:".Length..];

            return trimmed.Equals(All, StringComparison.OrdinalIgnoreCase) ||
                   trimmed.Equals("All platforms", StringComparison.OrdinalIgnoreCase);
        }

        private static CatalogPlatformFlags ToFlag(string platform) =>
            platform switch
            {
                Windows => CatalogPlatformFlags.Windows,
                Linux => CatalogPlatformFlags.Linux,
                Mac => CatalogPlatformFlags.Mac,
                Android => CatalogPlatformFlags.Android,
                _ => CatalogPlatformFlags.None,
            };
    }
}
