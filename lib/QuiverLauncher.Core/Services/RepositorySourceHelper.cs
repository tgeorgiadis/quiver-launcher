namespace QuiverLauncher.Core.Services
{
    public static class RepositorySourceHelper
    {
        /// <summary>
        /// Normalizes a repository source id. Unknown/empty values become GitHub.
        /// </summary>
        public static string Normalize(string? repositorySource, out bool wasUnsupported)
        {
            wasUnsupported = false;
            if (string.IsNullOrWhiteSpace(repositorySource))
                return RepositorySourceIds.GitHub;

            var trimmed = repositorySource.Trim();
            if (string.Equals(trimmed, RepositorySourceIds.GitHub, StringComparison.OrdinalIgnoreCase))
                return RepositorySourceIds.GitHub;

            if (string.Equals(trimmed, RepositorySourceIds.GitLab, StringComparison.OrdinalIgnoreCase))
                return RepositorySourceIds.GitLab;

            if (string.Equals(trimmed, RepositorySourceIds.Ipfs, StringComparison.OrdinalIgnoreCase))
                return RepositorySourceIds.Ipfs;

            wasUnsupported = true;
            return RepositorySourceIds.GitHub;
        }

        public static string Normalize(string? repositorySource) =>
            Normalize(repositorySource, out _);

        public static bool IsGitHub(string? repositorySource) =>
            string.Equals(Normalize(repositorySource), RepositorySourceIds.GitHub, StringComparison.OrdinalIgnoreCase);

        public static bool IsGitLab(string? repositorySource) =>
            string.Equals(Normalize(repositorySource), RepositorySourceIds.GitLab, StringComparison.OrdinalIgnoreCase);

        public static bool IsIpfs(string? repositorySource) =>
            string.Equals(Normalize(repositorySource), RepositorySourceIds.Ipfs, StringComparison.OrdinalIgnoreCase);

        public static bool IsManuallyManaged(string? repository) =>
            string.IsNullOrWhiteSpace(repository);

        public static string GetManualIdentityKey(string? folderName)
        {
            var folder = folderName?.Trim() ?? string.Empty;
            return $"{RepositorySourceIds.Manual}:{folder}";
        }

        /// <summary>
        /// Repository identity / API cache key: "{source}:{repository}". Missing source is treated as GitHub.
        /// Apps with no repository use <c>manual:{folderName}</c>.
        /// Multiple library tiles may share this key when they use the same hosted repository.
        /// </summary>
        public static string GetIdentityKey(string? repositorySource, string? repository, string? folderName = null)
        {
            if (IsManuallyManaged(repository))
                return GetManualIdentityKey(folderName);

            var source = Normalize(repositorySource);
            var repo = repository!.Trim();
            return $"{source}:{repo}";
        }

        /// <summary>
        /// Unique library-tile key: "{source}:{repository}:{folderName}" for hosted apps,
        /// or <c>manual:{folderName}</c> when there is no repository.
        /// </summary>
        public static string GetInstanceKey(string? repositorySource, string? repository, string? folderName)
        {
            if (IsManuallyManaged(repository))
                return GetManualIdentityKey(folderName);

            var source = Normalize(repositorySource);
            var repo = repository!.Trim();
            var folder = folderName?.Trim() ?? string.Empty;
            return $"{source}:{repo}:{folder}";
        }

        /// <summary>
        /// Case-insensitive substring match for a per-app release asset filter.
        /// Empty/whitespace filters match every asset.
        /// </summary>
        public static bool AssetNameMatchesFilter(string? assetName, string? releaseAssetFilter)
        {
            var filter = NormalizeReleaseAssetFilter(releaseAssetFilter);
            if (filter == null)
                return true;

            return (assetName ?? string.Empty).Contains(filter, StringComparison.OrdinalIgnoreCase);
        }

        public static string? NormalizeReleaseAssetFilter(string? releaseAssetFilter)
        {
            var trimmed = releaseAssetFilter?.Trim();
            return string.IsNullOrEmpty(trimmed) ? null : trimmed;
        }

        public static string GetRepositoryPageUrl(string? repositorySource, string? repository)
        {
            var repo = repository?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(repo))
                return string.Empty;

            if (IsIpfs(repositorySource))
                return $"https://ipfs.io/ipfs/{repo}";

            return IsGitLab(repositorySource)
                ? $"https://gitlab.com/{repo}"
                : $"https://github.com/{repo}";
        }

        public static string DisplayName(string? repositorySource) =>
            IsIpfs(repositorySource) ? "IPFS" :
            IsGitLab(repositorySource) ? "GitLab" : "GitHub";
    }
}
