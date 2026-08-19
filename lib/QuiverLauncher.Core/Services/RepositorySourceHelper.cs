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

            wasUnsupported = true;
            return RepositorySourceIds.GitHub;
        }

        public static string Normalize(string? repositorySource) =>
            Normalize(repositorySource, out _);

        public static bool IsGitHub(string? repositorySource) =>
            string.Equals(Normalize(repositorySource), RepositorySourceIds.GitHub, StringComparison.OrdinalIgnoreCase);

        public static bool IsGitLab(string? repositorySource) =>
            string.Equals(Normalize(repositorySource), RepositorySourceIds.GitLab, StringComparison.OrdinalIgnoreCase);

        public static bool IsManuallyManaged(string? repository) =>
            string.IsNullOrWhiteSpace(repository);

        public static string GetManualIdentityKey(string? folderName)
        {
            var folder = folderName?.Trim() ?? string.Empty;
            return $"{RepositorySourceIds.Manual}:{folder}";
        }

        /// <summary>
        /// Identity / cache key: "{source}:{repository}". Missing source is treated as GitHub.
        /// Apps with no repository use <c>manual:{folderName}</c>.
        /// </summary>
        public static string GetIdentityKey(string? repositorySource, string? repository, string? folderName = null)
        {
            if (IsManuallyManaged(repository))
                return GetManualIdentityKey(folderName);

            var source = Normalize(repositorySource);
            var repo = repository!.Trim();
            return $"{source}:{repo}";
        }

        public static string GetRepositoryPageUrl(string? repositorySource, string? repository)
        {
            var repo = repository?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(repo))
                return string.Empty;

            return IsGitLab(repositorySource)
                ? $"https://gitlab.com/{repo}"
                : $"https://github.com/{repo}";
        }

        public static string DisplayName(string? repositorySource) =>
            IsGitLab(repositorySource) ? "GitLab" : "GitHub";
    }
}
