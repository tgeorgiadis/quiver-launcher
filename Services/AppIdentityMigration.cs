using QuiverLauncher.Core.Services;
using AppSettings = QuiverLauncher.AppSettings;

namespace QuiverLauncher.Services;

/// <summary>
/// Remaps settings and cache when an app's repository identity changes on edit.
/// </summary>
public static class AppIdentityMigration
{
        public static bool HasIdentityChanged(
        string? oldRepositorySource,
        string? oldRepository,
        string? newRepositorySource,
        string? newRepository,
        string? oldFolderName = null,
        string? newFolderName = null)
    {
        var oldKey = RepositorySourceHelper.GetIdentityKey(oldRepositorySource, oldRepository, oldFolderName);
        var newKey = RepositorySourceHelper.GetIdentityKey(newRepositorySource, newRepository, newFolderName);
        return !string.Equals(oldKey, newKey, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Migrates user tags and display names, and clears the old version cache.
    /// Returns true if settings were modified.
    /// </summary>
    public static bool MigrateIdentity(
        AppSettings settings,
        string? oldRepositorySource,
        string? oldRepository,
        string? newRepositorySource,
        string? newRepository,
        string? oldFolderName = null,
        string? newFolderName = null)
    {
        if (!HasIdentityChanged(
                oldRepositorySource,
                oldRepository,
                newRepositorySource,
                newRepository,
                oldFolderName,
                newFolderName))
            return false;

        settings.EnsureInitialized();
        var settingsChanged = false;

        var oldRepo = oldRepository?.Trim() ?? string.Empty;
        var newRepo = newRepository?.Trim() ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(oldRepo) &&
            !string.IsNullOrWhiteSpace(newRepo) &&
            !string.Equals(oldRepo, newRepo, StringComparison.OrdinalIgnoreCase))
        {
            settingsChanged |= MigrateUserAppTags(settings, oldRepo, newRepo);
            settingsChanged |= MigrateUserAppDisplayNames(settings, oldRepo, newRepo);
        }

        if (!string.IsNullOrWhiteSpace(oldRepo))
            GitHubApiCache.RemoveCache(oldRepositorySource, oldRepo);

        return settingsChanged;
    }

    internal static bool MigrateUserAppTags(AppSettings settings, string oldRepository, string newRepository)
    {
        settings.UserAppTags ??= new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        if (!settings.UserAppTags.TryGetValue(oldRepository, out var tags))
            return false;

        settings.UserAppTags.Remove(oldRepository);

        if (!settings.UserAppTags.ContainsKey(newRepository))
            settings.UserAppTags[newRepository] = tags;

        return true;
    }

    internal static bool MigrateUserAppDisplayNames(AppSettings settings, string oldRepository, string newRepository)
    {
        settings.UserAppDisplayNames ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (!settings.UserAppDisplayNames.TryGetValue(oldRepository, out var displayName))
            return false;

        settings.UserAppDisplayNames.Remove(oldRepository);

        if (!settings.UserAppDisplayNames.ContainsKey(newRepository))
            settings.UserAppDisplayNames[newRepository] = displayName;

        return true;
    }
}
